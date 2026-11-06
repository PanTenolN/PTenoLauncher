using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Modpacks;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Mods;

public sealed record WorkingMod(ModFileState File, string? Id, string? Version, bool Enabled);
public sealed record WorkingConfiguration(Guid Id, Guid ProfileId, DateTimeOffset LaunchedAt, DateTimeOffset? SavedAt,
    InstallationReceipt Receipt, IReadOnlyList<WorkingMod> Mods, string QuiltOverridesHash, int Schema = 1);
public sealed record WorkingRestorePreview(WorkingConfiguration Configuration, ModUpdatePlan Plan,
    IReadOnlyList<WorkingMod> MissingMods, IReadOnlyList<string> MissingGameFiles, IReadOnlyList<WorkingMod> ReplacedMods, InstallationReceipt? CurrentReceipt)
{
    public bool CanApply => Plan.Blockers.Count == 0 && (Plan.Changes.Count > 0 || Plan.Before.Profile != Plan.After.Profile ||
        !(Plan.Before.Receipt ?? []).AsSpan().SequenceEqual(Plan.After.Receipt ?? []));
}

internal sealed class WorkingLaunchCapture(WorkingConfiguration configuration, IReadOnlyList<FileStream> handles) : IDisposable
{
    public WorkingConfiguration Configuration { get; } = configuration;
    public void Dispose()
    {
        foreach (var handle in handles) handle.Dispose();
    }
}

public sealed partial class ModUpdateManager
{
    private string WorkingPath(Guid id) => SafePath.Join(paths.Profile(id), "working-configuration.json");
    private string WorkingBlob(ModFileState file) => SafePath.Join(paths.Cache, $"working-mods/{file.Sha512.ToLowerInvariant()}.jar");
    private async Task<string> QuiltOverridesHash(Guid id, CancellationToken ct) =>
        Hash(await ReadBytes(SafePath.Join(paths.Game(id), "config/quilt-loader-overrides.json"), ct));

    private InstallationReceipt ParseWorkingReceipt(byte[]? bytes)
    {
        if (bytes is null) throw Error("UpdatesIntegrity");
        var receipt = JsonSerializer.Deserialize<InstallationReceipt>(bytes, AtomicFile.JsonOptions) ?? throw Error("UpdatesIntegrity");
        ReceiptValidator.Validate(paths, receipt);
        return receipt;
    }

    private static GameProfile WithWorkingTarget(GameProfile profile, InstallationReceipt receipt) => profile with
    {
        MinecraftVersion = receipt.Version,
        Loader = receipt.Loader,
        PreferredLoaderVersion = receipt.Loader == ModLoader.Vanilla ? null : receipt.LoaderVersion
    };

    private void ValidateWorking(Guid profile, WorkingConfiguration snapshot)
    {
        if (snapshot.Schema != 1 || snapshot.ProfileId != profile || snapshot.Id == Guid.Empty || snapshot.LaunchedAt == default ||
            snapshot.SavedAt is { } saved && saved < snapshot.LaunchedAt || snapshot.Receipt is null || snapshot.Mods is null ||
            snapshot.Mods.Count > 128 || snapshot.Mods.Any(m => m is null || m.File is null) ||
            snapshot.Mods.Select(m => m.File.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.Mods.Count ||
            snapshot.QuiltOverridesHash is null || snapshot.QuiltOverridesHash != "absent" &&
            (snapshot.QuiltOverridesHash.Length != 64 || !snapshot.QuiltOverridesHash.All(Uri.IsHexDigit)))
            throw Error("UpdatesIntegrity");
        ReceiptValidator.Validate(paths, snapshot.Receipt);
        foreach (var mod in snapshot.Mods)
        {
            ValidateFile(mod.File, working: true);
            if (mod.Enabled == IsDisabled(mod.File.Path) || mod.Id?.Length > 512 || mod.Version?.Length > 512)
                throw Error("UpdatesIntegrity", mod.File.Path);
        }
        if (snapshot.Mods.Sum(m => m.File.Size) > 8L * 1024 * 1024 * 1024) throw Error("UpdatesIntegrity");
    }

    public async Task<WorkingConfiguration?> ReadWorkingAsync(Guid profile, CancellationToken ct = default)
    {
        var snapshot = await AtomicFile.ReadJsonAsync<WorkingConfiguration>(WorkingPath(profile), ct);
        if (snapshot is null) return null;
        ValidateWorking(profile, snapshot);
        if (snapshot.SavedAt is null) throw Error("UpdatesIntegrity");
        return snapshot;
    }

    internal async Task<WorkingLaunchCapture> CaptureWorkingLaunchUnderLeaseAsync(GameProfile profile, InstallationReceipt receipt, CancellationToken ct)
    {
        RequireNoPending(paths, profile.Id);
        var state = await Capture(profile, ct, partial: true, working: true);
        if (!Same(ParseWorkingReceipt(state.Receipt), receipt)) throw Error("UpdatesStale");
        var mods = new List<WorkingMod>();
        var handles = new List<FileStream>();
        try
        {
            foreach (var file in state.Files)
            {
                var fullPath = SafePath.Join(paths.Game(profile.Id), file.Path);
                handles.Add(File.OpenRead(fullPath)); // Keep the captured bytes read-only throughout the launch.
                mods.Add(await InspectWorkingMod(profile.Id, file, ct));
            }
            var snapshot = new WorkingConfiguration(Guid.NewGuid(), profile.Id, DateTimeOffset.UtcNow, null, receipt, mods,
                await QuiltOverridesHash(profile.Id, ct));
            ValidateWorking(profile.Id, snapshot);
            return new(snapshot, handles);
        }
        catch
        {
            foreach (var handle in handles) handle.Dispose();
            throw;
        }
    }

    private async Task<WorkingMod> InspectWorkingMod(Guid profile, ModFileState file, CancellationToken ct)
    {
        var name = Path.GetFileName(file.Path);
        var enabled = !IsDisabled(name);
        var inspection = await local.InspectPayloadAsync(SafePath.Join(paths.Game(profile), file.Path),
            enabled ? name : name[..^".disabled".Length], ct);
        if (inspection.Sha256 != file.Sha256 || inspection.Size != file.Size) throw Error("UpdatesStale", file.Path);
        return new(file, inspection.Id, inspection.Version, enabled);
    }

    public async Task MarkWorkingAsync(GameProfile profile, WorkingConfiguration launch, bool confirmed, CancellationToken ct = default)
    {
        if (!confirmed) throw Error("UpdatesConfirm");
        using var lease = new ProfileUsage(paths).Acquire(profile.Id);
        new ProfileUsage(paths).CheckWorldLocks(profile.Id);
        RequireNoPending(paths, profile.Id);
        ValidateWorking(profile.Id, launch);
        var current = await Capture(profile, ct, partial: true, working: true);
        if (launch.SavedAt is not null || profile.MinecraftVersion != launch.Receipt.Version || profile.Loader != launch.Receipt.Loader ||
            (profile.PreferredLoaderVersion is { } loader && loader != launch.Receipt.LoaderVersion) ||
            !Same(ParseWorkingReceipt(current.Receipt), launch.Receipt) ||
            !current.Files.SequenceEqual(launch.Mods.Select(m => m.File)) ||
            await QuiltOverridesHash(profile.Id, ct) != launch.QuiltOverridesHash)
            throw Error("UpdatesStale");
        foreach (var mod in launch.Mods)
            await StoreWorkingBlob(SafePath.Join(paths.Game(profile.Id), mod.File.Path), mod.File, ct);
        if (Fingerprint(await Capture(profile, ct, partial: true, working: true)) != Fingerprint(current))
            throw Error("UpdatesStale");
        await AtomicFile.WriteJsonAsync(WorkingPath(profile.Id), launch with { Id = Guid.NewGuid(), SavedAt = DateTimeOffset.UtcNow }, ct);
    }

    private async Task<bool> HasWorkingBlob(ModFileState file, CancellationToken ct)
    {
        var path = WorkingBlob(file);
        return await VerifiedDownloader.VerifyAsync(path, file.Sha512, "SHA512", file.Size, ct) &&
            await VerifiedDownloader.VerifyAsync(path, file.Sha256, "SHA256", file.Size, ct);
    }

    private async Task StoreWorkingBlob(string source, ModFileState file, CancellationToken ct)
    {
        var destination = WorkingBlob(file);
        if (await HasWorkingBlob(file, ct)) return;
        var temporary = destination + ".pending-" + Guid.NewGuid().ToString("N");
        try
        {
            await CopyVerified(source, temporary, file, ct);
            ct.ThrowIfCancellationRequested();
            SafePath.RejectLinks(destination);
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static ModFileState? ExactLocalFile(ModSetState before, ModFileState wanted) => before.Files.FirstOrDefault(
        file => file.Sha256 == wanted.Sha256 && file.Sha512 == wanted.Sha512 && file.Size == wanted.Size);

    private async Task<IReadOnlyList<string>> MissingWorkingGameFiles(Guid profile, InstallationReceipt receipt, CancellationToken ct)
    {
        var missing = new List<string>();
        foreach (var file in receipt.Files)
            if (!await VerifiedDownloader.VerifyAsync(SafePath.Join(paths.Cache, file.RelativePath), file.Hash, file.Algorithm, file.Size, ct))
                missing.Add(file.RelativePath);
        if (missing.Count == 0)
            missing.AddRange((await AssetManager.CheckAsync(paths, profile, receipt, ct)).Select(i => i.Message));
        return missing;
    }

    public async Task<WorkingRestorePreview> PreviewWorkingAsync(GameProfile profile, CancellationToken ct = default)
    {
        using var lease = new ProfileUsage(paths).Acquire(profile.Id);
        new ProfileUsage(paths).CheckWorldLocks(profile.Id);
        RequireNoPending(paths, profile.Id);
        var snapshot = await ReadWorkingAsync(profile.Id, ct) ?? throw Error("WorkingNone");
        var before = await Capture(profile, ct, partial: true, working: true);
        var prior = before.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var desired = snapshot.Mods.ToDictionary(m => m.File.Path, m => m.File, StringComparer.OrdinalIgnoreCase);
        var blockers = new List<string>();
        var missing = new List<WorkingMod>();
        var replaced = new List<WorkingMod>();
        var changes = new List<ModUpdateChange>();
        foreach (var path in prior.Keys.Union(desired.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            prior.TryGetValue(path, out var old);
            desired.TryGetValue(path, out var next);
            if (old == next) continue;
            if (old is not null && next is not null && old.Path != next.Path) blockers.Add("WorkingCase: " + path);
            changes.Add(new("Restore", old, next, null));
            if (old is not null)
            {
                try { replaced.Add(await InspectWorkingMod(profile.Id, old, ct)); }
                catch (Exception error) when (error is InvalidDataException or JsonException or FormatException)
                { blockers.Add("WorkingInvalidJar: " + old.Path); }
            }
            if (next is not null && ExactLocalFile(before, next) is null && !await HasWorkingBlob(next, ct))
                missing.Add(snapshot.Mods.Single(m => m.File == next));
        }
        var target = WithWorkingTarget(profile, snapshot.Receipt);
        var missingGame = await MissingWorkingGameFiles(profile.Id, snapshot.Receipt, ct);
        if (missingGame.Count != 0) blockers.Add("WorkingGameMissing");
        if (missing.Count != 0) blockers.Add("WorkingModsMissing");
        if (await QuiltOverridesHash(profile.Id, ct) != snapshot.QuiltOverridesHash) blockers.Add("WorkingOverrides");
        foreach (var pin in ReadPins(before).Pins)
            if (!desired.TryGetValue(pin.File.Path, out var file) || file != pin.File) blockers.Add("UpdatesPinConflict: " + pin.File.Path);
        blockers.AddRange((await MrpackRequirements.CheckAsync(paths, target, snapshot.Receipt, ct)).Select(i => i.Message));
        var existing = before.Inventory is null ? null : JsonSerializer.Deserialize<InstalledModInventory>(before.Inventory, AtomicFile.JsonOptions);
        var inventory = new InstalledModInventory(profile.Id, snapshot.Mods.Where(m => m.Enabled).Select(m =>
            new InstalledModFile(m.File.Path, m.File.Sha256, m.File.Size,
                existing?.Files.FirstOrDefault(f => f.Size == m.File.Size && f.Sha256.Equals(m.File.Sha256, StringComparison.OrdinalIgnoreCase))?.Origin)).ToArray());
        var after = before with
        {
            Profile = target, Files = snapshot.Mods.Select(m => m.File).ToArray(),
            Receipt = JsonSerializer.SerializeToUtf8Bytes(snapshot.Receipt, AtomicFile.JsonOptions),
            Inventory = JsonSerializer.SerializeToUtf8Bytes(inventory, AtomicFile.JsonOptions),
            Epoch = JsonSerializer.SerializeToUtf8Bytes(Guid.NewGuid(), AtomicFile.JsonOptions)
        };
        var plan = new ModUpdatePlan(Guid.NewGuid(), profile.Id, DateTimeOffset.UtcNow, "Working", before, after, changes, blockers, [], snapshot.Id);
        var preview = new WorkingRestorePreview(snapshot, plan, missing, missingGame, replaced,
            before.Receipt is null ? null : ParseWorkingReceipt(before.Receipt));
        if (preview.CanApply)
        {
            ValidatePlan(profile, plan);
            await AtomicFile.WriteJsonAsync(PlanPath(profile.Id, plan.Id), plan, ct);
        }
        return preview;
    }

    public async Task SupplyWorkingFileAsync(Guid profile, Guid snapshotId, string relative, string? localFile, CancellationToken ct = default)
    {
        using var lease = new ProfileUsage(paths).Acquire(profile);
        new ProfileUsage(paths).CheckWorldLocks(profile);
        RequireNoPending(paths, profile);
        var snapshot = await ReadWorkingAsync(profile, ct);
        if (snapshot?.Id != snapshotId) throw Error("UpdatesStale");
        var file = snapshot.Mods.SingleOrDefault(m => m.File.Path == relative)?.File ?? throw Error("UpdatesStale");
        if (await HasWorkingBlob(file, ct)) return;
        if (localFile is null)
        {
            var version = await Identity(file, ct) ?? throw Error("WorkingExactUnavailable", relative);
            localFile = await catalog.DownloadContentAsync(version, null, ct);
        }
        await StoreWorkingBlob(localFile, file, ct);
    }

    public async Task SupplyWorkingGameFileAsync(Guid profile, Guid snapshotId, string relative, string localFile, CancellationToken ct = default)
    {
        using var lease = new ProfileUsage(paths).Acquire(profile);
        new ProfileUsage(paths).CheckWorldLocks(profile);
        RequireNoPending(paths, profile);
        var snapshot = await ReadWorkingAsync(profile, ct);
        if (snapshot?.Id != snapshotId) throw Error("UpdatesStale");
        var file = snapshot.Receipt.Files.SingleOrDefault(entry => entry.RelativePath == relative) ?? throw Error("UpdatesStale");
        var destination = SafePath.Join(paths.Cache, file.RelativePath);
        if (await VerifiedDownloader.VerifyAsync(destination, file.Hash, file.Algorithm, file.Size, ct)) return;
        // Cache locations are shared by profiles. Only fill a vacant location with the exact bytes.
        if (File.Exists(destination)) throw Error("WorkingGameMissing", file.RelativePath);
        if (!await VerifiedDownloader.VerifyAsync(localFile, file.Hash, file.Algorithm, file.Size, ct))
            throw Error("UpdatesIntegrity", file.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = SafePath.Join(paths.Cache, file.RelativePath + ".pending-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var input = File.OpenRead(localFile))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await input.CopyToAsync(output, ct);
                await output.FlushAsync(ct);
                output.Flush(true);
            }
            if (!await VerifiedDownloader.VerifyAsync(temporary, file.Hash, file.Algorithm, file.Size, ct))
                throw Error("UpdatesIntegrity", file.RelativePath);
            ct.ThrowIfCancellationRequested();
            SafePath.RejectLinks(destination);
            File.Move(temporary, destination, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task RecoverWorkingAsync(Guid profile, CancellationToken ct = default)
    {
        if (!File.Exists(WorkingPendingPath(paths, profile))) return;
        using var lease = new ProfileUsage(paths).Acquire(profile, allowWorkingRecovery: true);
        new ProfileUsage(paths).CheckWorldLocks(profile);
        var op = await AtomicFile.ReadJsonAsync<ModUpdateOperation>(WorkingPendingPath(paths, profile), ct);
        if (op is null) return;
        if (op.Plan.Kind != "Working") throw Error("UpdatesIntegrity");
        var current = (await store.GetProfilesAsync(ct)).SingleOrDefault(p => p.Id == profile) ?? throw Error("UpdatesStale");
        await RecoverOperation(current, op, ct);
    }

    public bool HasPendingWorking(Guid profile) => File.Exists(WorkingPendingPath(paths, profile));
}
