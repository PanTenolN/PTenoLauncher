using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.Modpacks;

public sealed partial class MrpackImporter(LauncherPaths paths, SqliteLauncherStore store, VerifiedDownloader downloads, NetworkPolicy network)
{
    public const string ReceiptName = "mrpack-import.json";
    public MrpackReader Reader { get; } = new(paths, network);
    public ZipPackReader ZipReader { get; } = new(paths, store, new(paths, network));
    public InstancePackReader InstanceReader { get; } = new(paths, store);
    public MinecraftLauncherReader MinecraftReader { get; } = new(paths, store);
    public ModrinthAppReader ModrinthAppReader { get; } = new(paths, store);
    public CurseForgeAppReader CurseForgeAppReader { get; } = new(paths, store);
    internal static string IndexName(string kind) => kind switch
    {
        "local-zip" => "zip-import-target.json",
        "local-prism-multimc" => "instance-import-target.json",
        "local-minecraft-launcher" => "minecraft-launcher-target.json",
        ModrinthAppReader.SourceKind => "modrinth-app-target.json",
        CurseForgeAppReader.SourceKind => "curseforge-app-target.json",
        ReconciledSourceKind => "reconciled-mrpack-plan.json",
        _ => "mrpack-index.json"
    };
    internal Action<string>? Fault { get; set; }
    private string Operations => SafePath.Join(paths.Root, "modpack-imports");
    public string OperationRoot(Guid id) => SafePath.Join(Operations, id.ToString("N"));
    private string Journal(Guid id) => SafePath.Join(OperationRoot(id), "journal.json");
    private LauncherPaths Staging(Guid id) => new(SafePath.Join(OperationRoot(id), "staging"));
    private IDisposable Lease()
    { Directory.CreateDirectory(Operations); try { return new FileStream(SafePath.Join(Operations, "import.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); } catch (IOException e) { throw new MrpackException("PackBusy", "", e); } }
    public async Task<MrpackImportResult> ImportAsync(MrpackPreview preview, bool confirmed, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        if (!confirmed) throw new MrpackException("PackConsent");
        using var operation = Lease();
        if ((await PendingAsync(ct)).Count != 0) throw new MrpackException("PackRecovery");
        var fresh = await Reader.PreviewAsync(preview.Archive, preview.ProfileName, preview.Optional, preview.PinMods, ct);
        if (fresh.Fingerprint != preview.Fingerprint) throw new MrpackException("PackChanged");
        return await ImportPreparedAsync(fresh, progress, ct);
    }
    public async Task<MrpackImportResult> ImportZipAsync(ZipPackPreview preview, bool confirmed, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        if (preview.IsMrpack) return await ImportAsync(preview.Plan, confirmed, progress, ct);
        if (!confirmed) throw new MrpackException("PackConsent");
        using var operation = Lease();
        if ((await PendingAsync(ct)).Count != 0) throw new MrpackException("PackRecovery");
        var fresh = await ZipReader.PreviewAsync(preview.Plan.Archive, preview.Request, ct);
        if (fresh.IsMrpack || fresh.Plan.Fingerprint != preview.Plan.Fingerprint) throw new MrpackException("PackChanged");
        return await ImportPreparedAsync(fresh.Plan, progress, ct);
    }
    public async Task<MrpackImportResult> ImportInstanceAsync(InstancePackPreview preview, bool confirmed, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        if (!confirmed) throw new MrpackException("PackConsent");
        using var operation = Lease();
        if ((await PendingAsync(ct)).Count != 0) throw new MrpackException("PackRecovery");
        var fresh = await InstanceReader.PreviewAsync(preview.Plan.Archive, preview.Request, ct);
        if (fresh.Plan.Fingerprint != preview.Plan.Fingerprint) throw new MrpackException("PackChanged");
        return await ImportPreparedAsync(fresh.Plan, progress, ct, fresh);
    }
    public async Task<MrpackImportResult> ImportMinecraftAsync(MinecraftLauncherPreview preview, bool confirmed, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        if (!confirmed) throw new MrpackException("PackConsent"); using var operation = Lease();
        if ((await PendingAsync(ct)).Count != 0) throw new MrpackException("PackRecovery");
        var fresh = await MinecraftReader.PreviewAsync(preview.ProfilesFile, preview.Request, ct);
        if (fresh.Plan.Fingerprint != preview.Plan.Fingerprint) throw new MrpackException("PackChanged");
        async Task VerifySource(CancellationToken token) { if ((await MinecraftReader.PreviewAsync(fresh.ProfilesFile,fresh.Request,token)).Plan.Fingerprint != fresh.Plan.Fingerprint) throw new MrpackException("PackChanged"); }
        var metadata = JsonSerializer.SerializeToUtf8Bytes(new InstanceImportTarget(1,"minecraft-launcher-profile",fresh.Plan.Minecraft,fresh.Plan.Loader,fresh.Plan.LoaderVersion,"content",fresh.Snapshot.MetadataSha256,fresh.Snapshot.Components),AtomicFile.JsonOptions);
        return await ImportPreparedAsync(fresh.Plan,progress,ct,fresh.Snapshot,true,VerifySource,metadata);
    }
    public async Task<MrpackImportResult> ImportModrinthAppAsync(ModrinthAppPreview preview, bool confirmed,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        if (!confirmed) throw new MrpackException("PackConsent");
        using var operation = Lease();
        if ((await PendingAsync(ct)).Count != 0) throw new MrpackException("PackRecovery");
        var fresh = await ModrinthAppReader.PreviewAsync(preview.Database, preview.Request, ct);
        if (fresh.Plan.Fingerprint != preview.Plan.Fingerprint) throw new MrpackException("PackChanged");
        async Task VerifySource(CancellationToken token)
        {
            var latest = await ModrinthAppReader.PreviewAsync(fresh.Database, fresh.Request, token);
            if (!latest.Plan.CanImport || latest.Plan.Fingerprint != fresh.Plan.Fingerprint) throw new MrpackException("PackChanged");
        }
        var metadata = JsonSerializer.SerializeToUtf8Bytes(new InstanceImportTarget(1, ModrinthAppReader.Origin,
            fresh.Plan.Minecraft, fresh.Plan.Loader, fresh.Plan.LoaderVersion, "content", fresh.Snapshot.MetadataSha256,
            fresh.Snapshot.Components, fresh.Request.InstanceId), AtomicFile.JsonOptions);
        return await ImportPreparedAsync(fresh.Plan, progress, ct, fresh.Snapshot, true, VerifySource, metadata);
    }
    public async Task<MrpackImportResult> ImportCurseForgeAppAsync(InstancePackPreview preview, bool confirmed,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        if (!confirmed) throw new MrpackException("PackConsent");
        using var operation = Lease();
        if ((await PendingAsync(ct)).Count != 0) throw new MrpackException("PackRecovery");
        var fresh = await CurseForgeAppReader.PreviewAsync(preview.Plan.Archive, preview.Request, ct);
        if (fresh.Plan.Fingerprint != preview.Plan.Fingerprint) throw new MrpackException("PackChanged");
        async Task VerifySource(CancellationToken token)
        {
            var latest = await CurseForgeAppReader.PreviewAsync(fresh.Plan.Archive, fresh.Request, token);
            if (!latest.Plan.CanImport || latest.Plan.Fingerprint != fresh.Plan.Fingerprint) throw new MrpackException("PackChanged");
        }
        var metadata = JsonSerializer.SerializeToUtf8Bytes(new InstanceImportTarget(1, CurseForgeAppReader.Origin,
            fresh.Plan.Minecraft, fresh.Plan.Loader, fresh.Plan.LoaderVersion, "content", fresh.MetadataSha256,
            fresh.Components), AtomicFile.JsonOptions);
        return await ImportPreparedAsync(fresh.Plan, progress, ct, fresh, true, VerifySource, metadata);
    }
    private async Task<MrpackImportResult> ImportPreparedAsync(MrpackPreview fresh, IProgress<DownloadProgress>? progress, CancellationToken ct, InstancePackPreview? instance = null,
        bool gameContent = false, Func<CancellationToken, Task>? verifyInstance = null, byte[]? instanceMetadata = null,
        Func<string, CancellationToken, Task>? copyPrepared = null, Guid? profileId = null)
    {
        if (!fresh.CanImport) throw new MrpackException(fresh.Blockers[0].Code, fresh.Blockers[0].Detail);
        var p = new GameProfile(profileId ?? Guid.NewGuid(), fresh.ProfileName, fresh.Minecraft, fresh.Loader, 4096, null, DateTimeOffset.UtcNow);
        using var profileLease = new ProfileUsage(paths).Acquire(p.Id);
        if (Directory.Exists(paths.Profile(p.Id)) || (await store.GetProfilesAsync(ct)).Any(x => x.Id == p.Id)) throw new MrpackException("PackConflict");
        if (new DriveInfo(Path.GetPathRoot(paths.Root)!).AvailableFreeSpace < fresh.Files.Sum(f => f.Size) * 3 + 64L * 1024 * 1024) throw new MrpackException("PackBudget");
        var journal = new MrpackJournal(1, p.Id, "Preparing", p, []);
        await AtomicFile.WriteJsonAsync(Journal(p.Id), journal, ct); Fault?.Invoke("preparing");
        var staged = Staging(p.Id); staged.CreateProfile(p);
        byte[] indexBytes;
        foreach (var f in fresh.Files.Where(f => f.Download is not null))
            await VerifyDownloadAsync(f.Download!, progress, ct);
        if (copyPrepared is not null)
        {
            indexBytes = instanceMetadata!;
            await copyPrepared(staged.Game(p.Id), ct);
        }
        else if (instance is not null)
        {
            indexBytes = instanceMetadata ?? InstancePackReader.TargetBytes(instance);
            using var input = new InstanceSource(fresh.Archive, gameContent);
            foreach (var file in instance.Content)
            {
                ct.ThrowIfCancellationRequested(); var target = SafePath.Join(staged.Game(p.Id), file.Path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await InstancePackReader.Copy(input, file, target, progress, ct);
            }
        }
        else using (var zip = ZipFile.OpenRead(fresh.Archive))
        {
            indexBytes = fresh.SourceKind == "local-zip" ? ZipPackReader.TargetBytes(fresh) : await MrpackReader.EntryBytes(zip.GetEntry("modrinth.index.json") ?? throw new MrpackException("PackChanged"), MrpackReader.MaxIndexBytes, ct);
            foreach (var f in fresh.Files)
            {
                ct.ThrowIfCancellationRequested(); var target = SafePath.Join(staged.Game(p.Id), f.Path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = f.Entry is { } entry ? (zip.GetEntry(entry) ?? throw new MrpackException("PackChanged")).Open() : File.OpenRead(Reader.CachePath(f.Download!));
                await CopyPayloadAsync(input, target, f.Path, f.Size, progress, ct);
                if (f.Download is { } d && (!await VerifiedDownloader.VerifyAsync(target, d.Sha512, "SHA512", d.Size, ct) || !await VerifiedDownloader.VerifyAsync(target, d.Sha1, "SHA1", d.Size, ct))) throw new MrpackException("PackIntegrity", f.Path);
            }
        }
        if (instance is null && copyPrepared is null && await MrpackReader.HashFile(fresh.Archive, ct) != fresh.ArchiveSha256) throw new MrpackException("PackChanged");
        var gameFiles = await Inventory(staged.Game(p.Id), ct);
        var inspector = new LocalModManager(staged, store); var jars = new List<ModCartItem>();
        foreach (var file in gameFiles.Where(f => MrpackReader.RootJar(f.RelativePath)))
            jars.Add(new(SafePath.Join(staged.Game(p.Id), file.RelativePath), await inspector.InspectAsync(SafePath.Join(staged.Game(p.Id), file.RelativePath), ct)));
        if (jars.Any(j => j.Inspection.Environment == "server")) throw new MrpackException("PackCompatibility", string.Join(", ", jars.Where(j => j.Inspection.Environment == "server").Select(j => j.Inspection.FileName)));
        ModCartReview? review = jars.Count > 0 ? await inspector.ReviewResultSetAsync(p, jars, ct) : null;
        if (review?.Blockers.Count > 0) throw new MrpackException("PackCompatibility", string.Join(Environment.NewLine, review.Blockers));
        await AtomicFile.WriteJsonAsync(SafePath.Join(staged.Profile(p.Id), "installed-mods.json"), new InstalledModInventory(p.Id, jars.Select(j => new InstalledModFile("mods/" + j.Inspection.FileName, j.Inspection.Sha256, j.Inspection.Size)).ToArray()), ct);
        if (fresh.PinMods)
        {
            var pins = new List<ModVersionPin>();
            foreach (var jar in jars)
            { await using var stream = File.OpenRead(jar.SourcePath); pins.Add(new(new("mods/" + jar.Inspection.FileName, jar.Inspection.Sha256, Convert.ToHexString(await SHA512.HashDataAsync(stream, ct)).ToLowerInvariant(), jar.Inspection.Size), null)); }
            await AtomicFile.WriteJsonAsync(SafePath.Join(staged.Profile(p.Id), "mod-pins.json"), new ModPinSet(p.Id, pins), ct);
        }
        await AtomicFile.WriteAsync(SafePath.Join(staged.Profile(p.Id), IndexName(fresh.SourceKind)), indexBytes, ct);
        var receipt = new MrpackImportReceipt(1, p.Id, fresh.Name, fresh.Version, fresh.ArchiveSha256, p, fresh.LoaderVersion, gameFiles, fresh.Optional, fresh.PinMods, review) { SourceKind = fresh.SourceKind, ContentRoot = fresh.ContentRoot, Excluded = fresh.Excluded, ArchiveFileName = Path.GetFileName(fresh.Archive), IndexSha256 = MrpackReader.Hash(indexBytes), IndexSize = indexBytes.Length };
        var receiptBytes = JsonSerializer.SerializeToUtf8Bytes(receipt, AtomicFile.JsonOptions); if (receiptBytes.Length > MrpackReader.MaxIndexBytes) throw new MrpackException("PackBudget");
        await AtomicFile.WriteAsync(SafePath.Join(staged.Profile(p.Id), ReceiptName), receiptBytes, ct);
        if (verifyInstance is not null) await verifyInstance(ct);
        else if (instance is not null && (await InstanceReader.PreviewAsync(fresh.Archive, instance.Request, ct)).Plan.Fingerprint != fresh.Fingerprint) throw new MrpackException("PackChanged");
        journal = journal with { State = "Ready", Files = await Inventory(staged.Profile(p.Id), ct, p.Id) };
        ct.ThrowIfCancellationRequested();
        await AtomicFile.WriteJsonAsync(Journal(p.Id), journal, ct); Fault?.Invoke("ready");
        await Commit(journal, copyPrepared is null ? ct : CancellationToken.None); return new(p, receipt);
    }
    private async Task VerifyDownloadAsync(MrpackDownload file, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        if (await Reader.CachedAsync(file, ct)) return;
        if (network.Offline) throw new MrpackException("PackOfflineMissing", file.Path);
        foreach (var url in file.Downloads)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                await downloads.DownloadAsync(url, Reader.CachePath(file), file.Sha512, file.Size, "SHA512", progress, deadline.Token, DownloadSource.Mrpack);
                if (!await Reader.CachedAsync(file, ct)) throw new MrpackException("PackIntegrity", file.Path);
                return;
            }
            catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException && !ct.IsCancellationRequested) { }
            catch (UntrustedMrpackSourceException e) { throw new MrpackException("PackOrigin", file.Path + " [" + e.Host + "]", e); }
            catch (InvalidDataException e) { throw new MrpackException("PackIntegrity", file.Path, e); }
        }
        throw new MrpackException("PackDownload", file.Path);
    }
    private static async Task CopyPayloadAsync(Stream input, string target, string path, long size,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await CopyPayloadAsync(input, output, path, size, progress, ct);
    }
    private static async Task CopyPayloadAsync(Stream input, FileStream output, string path, long size,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        long copied = 0;
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0)
        {
            if ((copied += count) > size) throw new MrpackException("PackIntegrity", path);
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
            progress?.Report(new(path, copied, size));
        }
        if (copied != size) throw new MrpackException("PackIntegrity", path);
        await output.FlushAsync(ct);
        output.Flush(true);
    }
    private static async Task<IReadOnlyList<InstalledFile>> Inventory(string root, CancellationToken ct, Guid? profile = null)
    {
        var result = new List<InstalledFile>();
        async Task Walk(string folder, int depth)
        {
            SafePath.RejectLinks(folder); if (depth > 26) throw new MrpackException("PackBudget");
            foreach (var f in Directory.EnumerateFileSystemEntries(folder))
            {
                ct.ThrowIfCancellationRequested(); SafePath.RejectLinks(f);
                if (depth == 0 && profile is { } id && Path.GetFileName(f) == LauncherPaths.IdentityFile(id) &&
                    LauncherPaths.HasIdentity(root, id)) continue;
                if (Directory.Exists(f)) await Walk(f, depth + 1);
                else { if (result.Count >= MrpackReader.MaxFiles + 4) throw new MrpackException("PackBudget"); result.Add(new(Path.GetRelativePath(root, f).Replace('\\', '/'), await MrpackReader.HashFile(f, ct), "SHA256", new FileInfo(f).Length)); }
            }
        }
        await Walk(root, 0); return result.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToArray();
    }
    private static async Task Verify(string root, Guid id, IReadOnlyList<InstalledFile> expected, CancellationToken ct)
    {
        if (expected is null || expected.Count > MrpackReader.MaxFiles + 4) throw new MrpackException("PackInvalid");
        MrpackReader.PathsRule(expected.Select(f => (f.RelativePath, false)));
        var actual = await Inventory(root, ct, id);
        if (JsonSerializer.Serialize(actual, AtomicFile.JsonOptions) != JsonSerializer.Serialize(expected, AtomicFile.JsonOptions)) throw new MrpackException("PackConflict");
    }
    private async Task Commit(MrpackJournal journal, CancellationToken ct)
    {
        Validate(journal); var stage = Staging(journal.Id).Profile(journal.Id);
        if (Directory.Exists(stage))
        {
            var target = paths.Profile(journal.Id);
            if (Directory.Exists(target) || File.Exists(target)) throw new MrpackException("PackConflict");
            await Verify(stage, journal.Id, journal.Files, ct); ct.ThrowIfCancellationRequested();
            try { paths.PlaceProfileDirectory(journal.Profile, stage); }
            catch (IOException error) when (error.Message == "ContentCollision") { throw new MrpackException("PackConflict", "", error); }
            Fault?.Invoke("moved");
        }
        await Verify(paths.Profile(journal.Id), journal.Id, journal.Files, ct);
        var receipt = await MrpackRequirements.ReadAsync(paths, journal.Profile, ct) ?? throw new MrpackException("PackInvalid");
        if (receipt.Id != journal.Id || receipt.Profile != journal.Profile) throw new MrpackException("PackConflict");
        await AtomicFile.WriteJsonAsync(Journal(journal.Id), journal with { State = "Moved" }, ct); Fault?.Invoke("before-database");
        await store.CommitImportedProfileAsync(journal.Profile, ct); Fault?.Invoke("database");
        await AtomicFile.WriteJsonAsync(Journal(journal.Id), journal with { State = "Committed" }, CancellationToken.None);
    }
    private static void Validate(MrpackJournal j)
    {
        if (j.Schema != 1 || j.Id == Guid.Empty || j.Profile is null || j.Profile.Id != j.Id || j.Profile.JavaPath is not null || j.Profile.Archived || j.State is not ("Preparing" or "Ready" or "Moved" or "Committed" or "Aborted")) throw new MrpackException("PackInvalid");
        ProfileRules.Validate(j.Profile);
        if (j.Files is null || j.Files.Count > MrpackReader.MaxFiles + 4 || j.Files.Any(f => f is null || f.Algorithm != "SHA256" || f.Size is < 0 or > MrpackReader.MaxArchiveBytes || f.Hash is null || f.Hash.Length != 64 || !f.Hash.All(Uri.IsHexDigit)) || j.Files.Select(f => f.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != j.Files.Count) throw new MrpackException("PackInvalid");
        if (j.State != "Preparing" && j.State != "Aborted" && !j.Files.Any(f => f.RelativePath == ReceiptName)) throw new MrpackException("PackInvalid");
    }
    public async Task<IReadOnlyList<Guid>> PendingAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(Operations)) return [];
        var dirs = Directory.EnumerateDirectories(Operations).Take(129).ToArray(); if (dirs.Length > 128) throw new MrpackException("PackBudget");
        var result = new List<Guid>();
        foreach (var dir in dirs)
        {
            SafePath.RejectLinks(dir); if (!Guid.TryParseExact(Path.GetFileName(dir), "N", out var id)) throw new MrpackException("PackInvalid");
            var j = await ReadJournal(id, ct); Validate(j);
            if (j.Id != id) throw new MrpackException("PackInvalid"); if (j.State is not ("Committed" or "Aborted")) result.Add(id);
        }
        return result;
    }
    public async Task RecoverAsync(bool confirmed, CancellationToken ct = default)
    {
        if (!confirmed) throw new MrpackException("PackConsent"); using var lease = Lease();
        foreach (var id in await PendingAsync(ct))
        {
            using var profile = new ProfileUsage(paths).Acquire(id); new ProfileUsage(paths).CheckWorldLocks(id);
            var j = await ReadJournal(id, ct);
            if (j.State == "Preparing")
                await DiscardPreparingAsync(id);
            else await Commit(j, ct);
        }
    }
    private async Task<MrpackJournal> ReadJournal(Guid id, CancellationToken ct)
    {
        using var doc = MrpackReader.Json(await AtomicFile.ReadBytesAsync(Journal(id), MrpackReader.MaxIndexBytes, ct));
        return doc.Deserialize<MrpackJournal>(AtomicFile.JsonOptions) ?? throw new MrpackException("PackInvalid");
    }
}
