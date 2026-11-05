using System.IO.Compression;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Security;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Updates;

public sealed record LauncherUpdateTransaction(Guid Id, string ApplicationRoot, string DataRoot, LauncherUpdateOffer Offer,
    ManagedLauncherInstallation Before, byte[] BeforeManifest, string State, string HealthToken, InstalledUpdateSnapshot? Installed = null);

public sealed partial class LauncherUpdater
{
    internal Action<string>? Fault { get; set; }
    internal InnoUpdateAdapter? InstalledAdapterOverride { get; set; }
    private InnoUpdateAdapter InstalledAdapter => InstalledAdapterOverride ?? InnoUpdateAdapter.Default;
    public const string InstallationManifest = "launcher-installation.json";
    public string TransactionRoot(Guid id) => SafePath.Join(paths.Root, "launcher-updates/operations/" + id.ToString("N"));
    private string Journal(Guid id) => SafePath.Join(TransactionRoot(id), "transaction.json");
    private string Area(Guid id, string area, string file) => SafePath.Join(TransactionRoot(id), area + "/" + file);
    private async Task VerifyFiles(string root, IReadOnlyList<LauncherUpdateFile> files, CancellationToken ct)
    {
        foreach (var f in files)
        {
            LauncherUpdateTrust.ValidateFile(f);
            if (!await VerifiedDownloader.VerifyAsync(SafePath.Join(root, f.Path), f.Sha256, "SHA256", f.Size, ct)) throw new LauncherUpdateException("UpdaterIntegrity");
        }
    }
    private void ValidateTransaction(LauncherUpdateTransaction t, bool allowExpired = true)
    {
        if (t is null || t.Offer is null || t.Before is null || t.BeforeManifest is null || t.ApplicationRoot is null || t.DataRoot is null || t.HealthToken is null) throw new LauncherUpdateException("UpdaterInvalid");
        VerifyOffer(t.Offer, allowExpired);
        var app = Path.GetFullPath(t.ApplicationRoot);
        if (t.Id == Guid.Empty || t.DataRoot != paths.Root || t.ApplicationRoot != app || t.Before.Build != current || t.Before.Schema != 1 || t.HealthToken.Length != 64 ||
            t.State is not ("Prepared" or "Applying" or "AwaitingHealth" or "Committed" or "RolledBack") ||
            !JsonSerializer.SerializeToUtf8Bytes(t.Before, AtomicFile.JsonOptions).SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(JsonSerializer.Deserialize<ManagedLauncherInstallation>(t.BeforeManifest, AtomicFile.JsonOptions), AtomicFile.JsonOptions))) throw new LauncherUpdateException("UpdaterInvalid");
        if (Path.GetPathRoot(app)!.TrimEnd('\\', '/') == app.TrimEnd('\\', '/')) throw new LauncherUpdateException("UpdaterPath");
        SafePath.RejectLinks(app); SafePath.RejectLinks(paths.Root);
        LauncherUpdateTrust.ValidatePackage(t.Offer.Package with { Files = t.Before.Files });
        LauncherUpdateTrust.ValidatePathUnion(t.Before.Files.Concat(t.Offer.Package.Files).Select(f => f.Path));
        if (t.Offer.Package.Mode == "Portable")
        { if (t.Installed is not null || !File.Exists(SafePath.Join(app, "portable.flag"))) throw new LauncherUpdateException("UpdaterInstalledBlocked"); }
        else
        {
            if (File.Exists(SafePath.Join(app, "portable.flag")) || t.Installed is null || t.Offer.Package.InstallerUrl is null) throw new LauncherUpdateException("UpdaterInstalledBlocked");
            InnoUpdateAdapter.ValidateSnapshot(t.Installed, app, paths.Root);
        }
        foreach (var f in t.Before.Files.Concat(t.Offer.Package.Files))
        {
            var target = SafePath.Join(app, f.Path);
            if (target.Equals(paths.Root, StringComparison.OrdinalIgnoreCase) || target.StartsWith(paths.Root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new LauncherUpdateException("UpdaterPath");
        }
    }
    private async Task Save(LauncherUpdateTransaction t, CancellationToken ct) => await AtomicFile.WriteJsonAsync(Journal(t.Id), t, ct);
    private async Task CopyFile(string source, string target, LauncherUpdateFile f, CancellationToken ct)
    {
        await VerifyFiles(Path.GetDirectoryName(source)!, [f with { Path = Path.GetFileName(source) }], ct);
        SafePath.RejectLinks(target); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        // Every copy uses a new path. Interrupted copies are preserved for diagnosis, never treated as verified files.
        await using (var input = File.OpenRead(source))
        await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
        { await input.CopyToAsync(output, ct); await output.FlushAsync(ct); output.Flush(true); }
        if (!await VerifiedDownloader.VerifyAsync(target, f.Sha256, "SHA256", f.Size, ct)) throw new LauncherUpdateException("UpdaterIntegrity");
    }
    public async Task<LauncherUpdateTransaction> PrepareAsync(LauncherUpdateOffer offer, string appRoot, bool confirmed, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        if (!confirmed) throw new LauncherUpdateException("UpdaterConsent");
        VerifyOffer(offer);
        if (offer.Package.Mode == "Installed" && offer.Package.InstallerUrl is null) throw new LauncherUpdateException("UpdaterInstalledBlocked");
        var root = Path.GetFullPath(appRoot).TrimEnd(Path.DirectorySeparatorChar);
        var inventoryPath = SafePath.Join(root, InstallationManifest);
        if (!File.Exists(inventoryPath) || new FileInfo(inventoryPath).Length > 2 * 1024 * 1024) throw new LauncherUpdateException("UpdaterBuild");
        var beforeBytes = await File.ReadAllBytesAsync(inventoryPath, ct);
        var before = JsonSerializer.Deserialize<ManagedLauncherInstallation>(beforeBytes, AtomicFile.JsonOptions) ?? throw new LauncherUpdateException("UpdaterInvalid");
        var installed = offer.Package.Mode == "Installed" ? await InstalledAdapter.CaptureAsync(root, paths.Root, ct) : null;
        var t = new LauncherUpdateTransaction(Guid.NewGuid(), root, paths.Root, offer, before, beforeBytes, "Prepared", Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)), installed);
        ValidateTransaction(t, allowExpired: false); await VerifyFiles(root, before.Files, ct);
        foreach (var f in offer.Package.Files.Where(f => !before.Files.Any(old => old.Path.Equals(f.Path, StringComparison.OrdinalIgnoreCase))))
            if (File.Exists(SafePath.Join(root, f.Path)) || Directory.Exists(SafePath.Join(root, f.Path))) throw new LauncherUpdateException("UpdaterConflict");
        var required = checked(offer.Package.Size + offer.Package.InstallerSize + offer.Package.Files.Sum(f => f.Size) * 2 + before.Files.Sum(f => f.Size) * 3 + (installed?.UninstallerFiles.Sum(f => f.Size) ?? 0) * 2 + 64L * 1024 * 1024);
        if (new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace < required || new DriveInfo(Path.GetPathRoot(paths.Root)!).AvailableFreeSpace < required) throw new LauncherUpdateException("UpdaterSpace");
        var probe = SafePath.Join(root, ".pteno-write-" + t.Id.ToString("N"));
        await File.WriteAllBytesAsync(probe, [1], ct); File.Delete(probe);
        var package = SafePath.Join(paths.Cache, "launcher-updates/" + offer.Package.Sha256 + ".zip");
        await downloads.DownloadAsync(offer.Package.Url, package, offer.Package.Sha256, offer.Package.Size, "SHA256", progress, ct, DownloadSource.LauncherUpdate);
        using (var zip = ZipFile.OpenRead(package))
        {
            SafeArchive.Validate(zip, TransactionRoot(t.Id), maxExpanded: 2L * 1024 * 1024 * 1024);
            if (zip.Entries.Count != offer.Package.Files.Count || zip.Entries.Any(e => !offer.Package.Files.Any(f => f.Path == e.FullName))) throw new LauncherUpdateException("UpdaterInvalid");
            foreach (var f in offer.Package.Files)
            {
                var e = zip.GetEntry(f.Path)!; if (e.Length != f.Size) throw new LauncherUpdateException("UpdaterIntegrity");
                var target = Area(t.Id, "new", f.Path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = e.Open();
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough);
                await input.CopyToAsync(output, ct); await output.FlushAsync(ct); output.Flush(true);
            }
        }
        await VerifyFiles(Path.Combine(TransactionRoot(t.Id), "new"), offer.Package.Files, ct);
        foreach (var f in before.Files) await CopyFile(SafePath.Join(root, f.Path), Area(t.Id, "backup", f.Path), f, ct);
        if (installed is not null)
        {
            foreach (var f in installed.UninstallerFiles)
            {
                var target = Area(t.Id, "backup-uninstaller", f.Path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(SafePath.Join(root, f.Path), target, false);
                if (!await VerifiedDownloader.VerifyAsync(target, f.Sha256, "SHA256", f.Size, ct)) throw new LauncherUpdateException("UpdaterIntegrity");
            }
            await InstalledAdapter.VerifyBeforeAsync(installed, root, paths.Root, ct);
            await downloads.DownloadAsync(offer.Package.InstallerUrl!, Area(t.Id, "installer", "setup.exe"), offer.Package.InstallerSha256!, offer.Package.InstallerSize, "SHA256", progress, ct, DownloadSource.LauncherUpdate);
        }
        await VerifyFiles(root, before.Files, ct);
        if (!(await File.ReadAllBytesAsync(inventoryPath, ct)).SequenceEqual(beforeBytes)) throw new LauncherUpdateException("UpdaterConflict");
        await Save(t, ct); return t;
    }
    public async Task<LauncherUpdateTransaction> ReadTransactionAsync(Guid id, CancellationToken ct = default)
    {
        var file = Journal(id); if (!File.Exists(file) || new FileInfo(file).Length > 4 * 1024 * 1024) throw new LauncherUpdateException("UpdaterInvalid");
        var t = LauncherUpdateTrust.Parse<LauncherUpdateTransaction>(await AtomicFile.ReadBytesAsync(file, 4 * 1024 * 1024, ct));
        if (t.Id != id) throw new LauncherUpdateException("UpdaterInvalid"); ValidateTransaction(t); return t;
    }
    private async Task VerifyPartial(LauncherUpdateTransaction t, CancellationToken ct)
    {
        await VerifyFiles(Path.Combine(TransactionRoot(t.Id), "backup"), t.Before.Files, ct);
        foreach (var file in t.Before.Files.Concat(t.Offer.Package.Files).Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var target = SafePath.Join(t.ApplicationRoot, file); if (!File.Exists(target)) continue;
            var allowed = t.Before.Files.Concat(t.Offer.Package.Files).Where(f => f.Path.Equals(file, StringComparison.OrdinalIgnoreCase)); var match = false;
            foreach (var f in allowed) match |= await VerifiedDownloader.VerifyAsync(target, f.Sha256, "SHA256", f.Size, ct);
            if (!match) throw new LauncherUpdateException("UpdaterConflict");
        }
        var manifest = SafePath.Join(t.ApplicationRoot, InstallationManifest);
        var currentBytes = await File.ReadAllBytesAsync(manifest, ct);
        var after = JsonSerializer.SerializeToUtf8Bytes(new ManagedLauncherInstallation(t.Offer.Release.Build, t.Offer.Package.Files), AtomicFile.JsonOptions);
        if (!currentBytes.SequenceEqual(t.BeforeManifest) && !currentBytes.SequenceEqual(after)) throw new LauncherUpdateException("UpdaterConflict");
    }
    internal async Task ApplyPreparedAsync(Guid id, CancellationToken ct = default)
    {
        var t = await ReadTransactionAsync(id, ct); if (t.State != "Prepared") throw new LauncherUpdateException("UpdaterRecovery");
        VerifyOffer(t.Offer);
        await VerifyFiles(t.ApplicationRoot, t.Before.Files, ct); await VerifyPartial(t, ct);
        await VerifyFiles(Path.Combine(TransactionRoot(id), "new"), t.Offer.Package.Files, ct);
        foreach (var f in t.Offer.Package.Files.Where(f => !t.Before.Files.Any(old => old.Path.Equals(f.Path, StringComparison.OrdinalIgnoreCase))))
            if (File.Exists(SafePath.Join(t.ApplicationRoot, f.Path)) || Directory.Exists(SafePath.Join(t.ApplicationRoot, f.Path))) throw new LauncherUpdateException("UpdaterConflict");
        t = t with { State = "Applying" }; await Save(t, ct); Fault?.Invoke("applying");
        if (t.Installed is not null)
        {
            await InstalledAdapter.ApplyAsync(t, Area(id, "installer", "setup.exe"), TransactionRoot(id), ct);
            // Inno must have installed the signed payload's managed inventory as part of its own file set.
            var expected = new ManagedLauncherInstallation(t.Offer.Release.Build, t.Offer.Package.Files);
            var actual = await AtomicFile.ReadJsonAsync<ManagedLauncherInstallation>(SafePath.Join(t.ApplicationRoot, InstallationManifest), ct);
            if (JsonSerializer.Serialize(actual, AtomicFile.JsonOptions) != JsonSerializer.Serialize(expected, AtomicFile.JsonOptions)) throw new LauncherUpdateException("UpdaterIntegrity");
            await Save(t with { State = "AwaitingHealth" }, ct); return;
        }
        foreach (var f in t.Before.Files)
        {
            await VerifyFiles(t.ApplicationRoot, [f], ct);
            var held = Area(id, "retired", f.Path); Directory.CreateDirectory(Path.GetDirectoryName(held)!);
            SafePath.RejectLinks(held); File.Move(SafePath.Join(t.ApplicationRoot, f.Path), held, false); Fault?.Invoke("old:" + f.Path);
        }
        foreach (var f in t.Offer.Package.Files)
        {
            await VerifyFiles(Path.Combine(TransactionRoot(id), "new"), [f], ct);
            var target = SafePath.Join(t.ApplicationRoot, f.Path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); SafePath.RejectLinks(target);
            File.Move(Area(id, "new", f.Path), target, false); Fault?.Invoke("new:" + f.Path);
        }
        var manifest = SafePath.Join(t.ApplicationRoot, InstallationManifest);
        if (!(await File.ReadAllBytesAsync(manifest, ct)).SequenceEqual(t.BeforeManifest)) throw new LauncherUpdateException("UpdaterConflict");
        await AtomicFile.WriteJsonAsync(manifest, new ManagedLauncherInstallation(t.Offer.Release.Build, t.Offer.Package.Files), ct);
        await VerifyFiles(t.ApplicationRoot, t.Offer.Package.Files, ct);
        await Save(t with { State = "AwaitingHealth" }, ct); Fault?.Invoke("awaiting-health");
    }
    internal async Task RollbackAsync(Guid id, CancellationToken ct = default)
    {
        var t = await ReadTransactionAsync(id, ct);
        if (t.State is not ("Prepared" or "Applying" or "AwaitingHealth")) throw new LauncherUpdateException("UpdaterRecovery");
        await VerifyPartial(t, ct);
        if (t.Installed is not null) await InstalledAdapter.VerifyRecoveryAsync(t, TransactionRoot(id), ct);
        // Unknown files are never scanned for deletion. Only the transaction's exact signed/baselined paths participate.
        foreach (var f in t.Before.Files.Concat(t.Offer.Package.Files).DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
        {
            var target = SafePath.Join(t.ApplicationRoot, f.Path); if (!File.Exists(target)) continue;
            var old = t.Before.Files.FirstOrDefault(x => x.Path == f.Path);
            if (old is not null && await VerifiedDownloader.VerifyAsync(target, old.Sha256, "SHA256", old.Size, ct)) continue;
            var held = Area(id, "rejected-" + Guid.NewGuid().ToString("N"), f.Path); Directory.CreateDirectory(Path.GetDirectoryName(held)!); SafePath.RejectLinks(held); File.Move(target, held, false);
        }
        foreach (var f in t.Before.Files)
        {
            var target = SafePath.Join(t.ApplicationRoot, f.Path); if (File.Exists(target)) continue;
            var temporary = Area(id, "restore-" + Guid.NewGuid().ToString("N"), f.Path);
            await CopyFile(Area(id, "backup", f.Path), temporary, f, ct); Directory.CreateDirectory(Path.GetDirectoryName(target)!); SafePath.RejectLinks(target); File.Move(temporary, target, false);
        }
        await AtomicFile.WriteAsync(SafePath.Join(t.ApplicationRoot, InstallationManifest), t.BeforeManifest, ct);
        if (t.Installed is not null) await InstalledAdapter.RestoreAsync(t, TransactionRoot(id), ct);
        await VerifyFiles(t.ApplicationRoot, t.Before.Files, ct); await Save(t with { State = "RolledBack" }, ct);
    }
    internal async Task ConfirmHealthAsync(Guid id, string token, LauncherBuild running, CancellationToken ct = default)
    {
        var t = await ReadTransactionAsync(id, ct);
        if (t.State != "AwaitingHealth" || t.HealthToken != token || t.Offer.Release.Build != running) throw new LauncherUpdateException("UpdaterRecovery");
        await VerifyFiles(t.ApplicationRoot, t.Offer.Package.Files, ct);
        await Save(t with { State = "Committed" }, ct); // Keep the previous verified backup; no automatic deletion.
    }
}
