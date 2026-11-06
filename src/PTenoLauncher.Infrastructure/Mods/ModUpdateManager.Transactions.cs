using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Mods;

public sealed partial class ModUpdateManager
{
    private string BackupPath(Guid profile, Guid op) => SafePath.Join(paths.Backups, $"{profile:N}-mods-{op:N}.zip");
    private string Stage(ModUpdatePlan plan, string area, string relative) => SafePath.Join(OperationRoot(plan.ProfileId, plan.Id), area + "/" + relative);
    private static bool Same<T>(T a, T b) => JsonSerializer.Serialize(a, AtomicFile.JsonOptions) == JsonSerializer.Serialize(b, AtomicFile.JsonOptions);
    private void ValidateState(Guid id, ModSetState s, bool working = false, bool prior = false)
    {
        if (s.Profile.Id != id || s.Files.Count > 128 || s.Files.Sum(f => f.Size) > 8L * 1024 * 1024 * 1024 ||
            s.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != s.Files.Count) throw Error("UpdatesIntegrity");
        ProfileRules.Validate(s.Profile);
        foreach (var f in s.Files) ValidateFile(f, working);
        if (s.Inventory is { } bytes)
        {
            var inventory = JsonSerializer.Deserialize<InstalledModInventory>(bytes, AtomicFile.JsonOptions) ?? throw Error("UpdatesIntegrity");
            new ModInventoryStore(paths).Validate(id, inventory);
            foreach (var f in inventory.Files)
                if (!(working && prior) && !s.Files.Any(x => x.Path == f.RelativePath && x.Sha256.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase) && x.Size == f.Size)) throw Error("UpdatesIntegrity");
        }
        foreach (var pin in ReadPins(s).Pins) if (!(working && prior) && !s.Files.Contains(pin.File)) throw Error("UpdatesPinConflict", pin.File.Path);
        if (s.Epoch is not null) _ = JsonSerializer.Deserialize<Guid>(s.Epoch, AtomicFile.JsonOptions);
    }
    private void ValidatePlan(GameProfile p, ModUpdatePlan plan)
    {
        var working = plan.Kind == "Working";
        if (plan.ProfileId != p.Id || plan.Id == Guid.Empty || plan.Kind is not ("Update" or "Rollback" or "Working") ||
            (working ? p != plan.Before.Profile && p != plan.After.Profile : plan.Before.Profile != p || plan.After.Profile != p) ||
            plan.Before.ContextHash != plan.After.ContextHash || (!working && plan.Changes.Count == 0) || plan.Changes.Count > 256 ||
            plan.Changes.Any(c => c.Before is null && c.After is null) ||
            plan.Changes.Select(c => c.Before?.Path ?? c.After!.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Changes.Count)
            throw Error("UpdatesIntegrity");
        ValidateState(p.Id, plan.Before, working, prior: true); ValidateState(p.Id, plan.After, working);
        if (working)
        {
            var receipt = ParseWorkingReceipt(plan.After.Receipt);
            if (WithWorkingTarget(plan.Before.Profile, receipt) != plan.After.Profile || plan.Reverts is null ||
                Hash(plan.Before.Pins) != Hash(plan.After.Pins)) throw Error("UpdatesIntegrity");
        }
        else if (plan.Before.Receipt is not null || plan.After.Receipt is not null) throw Error("UpdatesIntegrity");
        var expected = plan.Before.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var c in plan.Changes)
        {
            if (c.Before is null && c.After is null || c.Before is not null && c.After is not null && c.Before.Path != c.After.Path) throw Error("UpdatesIntegrity");
            if (c.Before is { } old) { if (!expected.Remove(old.Path, out var actual) || actual != old) throw Error("UpdatesIntegrity"); }
            if (c.After is { } next) { if (!expected.TryAdd(next.Path, next)) throw Error("UpdatesIntegrity"); }
            if (c.Version is { } v) { ValidateVersion(v); if (c.After is null || c.After.Sha512 != v.File.Sha512 || c.After.Size != v.File.Size) throw Error("UpdatesIntegrity"); }
            if (plan.Kind == "Update" && c.After is not null && c.Version is null) throw Error("UpdatesIntegrity");
        }
        if (expected.Count != plan.After.Files.Count || plan.After.Files.Any(f => !expected.TryGetValue(f.Path, out var actual) || actual != f)) throw Error("UpdatesIntegrity");
        // Update operations cannot alter persistent pins; only explicit pin commands and rollback do so.
        if (plan.Kind == "Update" && Hash(plan.Before.Pins) != Hash(plan.After.Pins)) throw Error("UpdatesPinConflict");
    }
    private async Task ValidateSavedPlan(GameProfile p, ModUpdatePlan plan, CancellationToken ct)
    {
        ValidatePlan(p, plan);
        var saved = await AtomicFile.ReadJsonAsync<ModUpdatePlan>(PlanPath(p.Id, plan.Id), ct);
        if (!Same(saved, plan)) throw Error("UpdatesStale");
    }
    private async Task<ModUpdateOperation> ReadOperation(GameProfile p, Guid id, CancellationToken ct)
    {
        var op = await AtomicFile.ReadJsonAsync<ModUpdateOperation>(OperationPath(p.Id, id), ct) ?? throw Error("UpdatesIntegrity");
        ValidateOperation(p, op);
        return op;
    }
    private void ValidateOperation(GameProfile p, ModUpdateOperation op)
    {
        ValidatePlan(p, op.Plan);
        if (op.Schema != 1 || op.State is not ("Prepared" or "Committed" or "Recovered") || op.Backup != Path.GetRelativePath(paths.Root, BackupPath(p.Id, op.Plan.Id)).Replace('\\', '/') ||
            op.BackupSha256.Length != 64 || !op.BackupSha256.All(Uri.IsHexDigit)) throw Error("UpdatesIntegrity");
    }
    private async Task<string> CreateBackup(ModUpdatePlan plan, CancellationToken ct)
    {
        var file = BackupPath(plan.ProfileId, plan.Id); SafePath.RejectLinks(file);
        Directory.CreateDirectory(paths.Backups);
        // A unique file is never overwritten, even after a failed attempt. No user data is cleaned up implicitly.
        if (File.Exists(file)) throw Error("UpdatesRecovery", file);
        await using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.WriteThrough))
        {
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                await using (var state = zip.CreateEntry("state.json", CompressionLevel.NoCompression).Open())
                    await state.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(plan.Before, AtomicFile.JsonOptions), ct);
                foreach (var f in plan.Before.Files)
                {
                    var source = SafePath.Join(paths.Game(plan.ProfileId), f.Path);
                    if (await FileState(source, f.Path, ct) != f) throw Error("UpdatesStale", f.Path);
                    await using var input = File.OpenRead(source);
                    await using var entry = zip.CreateEntry(f.Path, CompressionLevel.NoCompression).Open();
                    await input.CopyToAsync(entry, ct);
                }
            }
            await output.FlushAsync(ct); output.Flush(true);
        }
        await using var s = File.OpenRead(file);
        return Convert.ToHexString(await SHA256.HashDataAsync(s, ct)).ToLowerInvariant();
    }
    private async Task VerifyBackup(ModUpdateOperation op, CancellationToken ct)
    {
        var file = BackupPath(op.Plan.ProfileId, op.Plan.Id);
        if (!File.Exists(file) || !await VerifiedDownloader.VerifyAsync(file, op.BackupSha256, "SHA256", new FileInfo(file).Length, ct)) throw Error("UpdatesBackup");
        using var zip = ZipFile.OpenRead(file);
        var expected = op.Plan.Before.Files;
        if (zip.Entries.Count != expected.Count + 1 || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count) throw Error("UpdatesBackup");
        var state = zip.GetEntry("state.json") ?? throw Error("UpdatesBackup");
        if (state.Length > 16 * 1024 * 1024) throw Error("UpdatesBackup");
        await using (var s = state.Open())
            if (!Same(await JsonSerializer.DeserializeAsync<ModSetState>(s, AtomicFile.JsonOptions, ct), op.Plan.Before)) throw Error("UpdatesBackup");
        foreach (var f in expected)
        {
            var e = zip.GetEntry(f.Path) ?? throw Error("UpdatesBackup", f.Path);
            if (e.Length != f.Size || e.CompressedLength > f.Size + 1024) throw Error("UpdatesBackup", f.Path);
            await using var stream = e.Open();
            var hash = Convert.ToHexString(await SHA512.HashDataAsync(stream, ct)).ToLowerInvariant();
            if (hash != f.Sha512) throw Error("UpdatesBackup", f.Path);
        }
    }
    private async Task CopyVerified(string source, string target, ModFileState f, CancellationToken ct)
    {
        if (await FileState(source, f.Path, ct) != f) throw Error("UpdatesIntegrity", f.Path);
        SafePath.RejectLinks(target); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target))
        { if (await FileState(target, f.Path, ct) != f) throw Error("UpdatesIntegrity", target); return; }
        await using (var input = File.OpenRead(source))
        await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
        { await input.CopyToAsync(output, ct); await output.FlushAsync(ct); output.Flush(true); }
        if (await FileState(target, f.Path, ct) != f) throw Error("UpdatesIntegrity", target);
    }
    private async Task RestoreEntry(ModUpdateOperation op, ModFileState f, string target, CancellationToken ct)
    {
        SafePath.RejectLinks(target); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target)) { if (await FileState(target, f.Path, ct) != f) throw Error("UpdatesIntegrity", target); return; }
        using var zip = ZipFile.OpenRead(BackupPath(op.Plan.ProfileId, op.Plan.Id));
        await using var input = zip.GetEntry(f.Path)!.Open();
        // AtomicFile avoids leaving a partial JAR in the profile if extraction is interrupted.
        var temporary = Stage(op.Plan, "restore", f.Path + ".pending-" + Guid.NewGuid().ToString("N"));
        {
            Directory.CreateDirectory(Path.GetDirectoryName(temporary)!);
            await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough);
            await input.CopyToAsync(output, ct); await output.FlushAsync(ct); output.Flush(true);
        }
        if (await FileState(temporary, f.Path, ct) != f) throw Error("UpdatesBackup", temporary);
        SafePath.RejectLinks(target); File.Move(temporary, target, false);
    }
    private async Task WriteState(ModSetState s, ModSetState other, CancellationToken ct, bool working = false)
    {
        var files = new List<(string, byte[]?, byte[]?)> { (new ModInventoryStore(paths).PathFor(s.Profile.Id), s.Inventory, other.Inventory),
            (PinsPath(s.Profile.Id), s.Pins, other.Pins), (EpochPath(s.Profile.Id), s.Epoch, other.Epoch) };
        if (working) files.Add((paths.Receipt(s.Profile.Id), s.Receipt, other.Receipt));
        foreach (var (path, bytes, allowed) in files)
        {
            SafePath.RejectLinks(path);
            var current = Hash(await ReadBytes(path, ct));
            if (current != Hash(bytes) && current != Hash(allowed)) throw Error("UpdatesStale", path);
            if (bytes is null) { if (File.Exists(path)) File.Delete(path); }
            else await AtomicFile.WriteAsync(path, bytes, ct);
            Fault?.Invoke("metadata:" + Path.GetFileName(path));
        }
        if (working && s.Profile != other.Profile)
        {
            var current = (await store.GetProfilesAsync(ct)).SingleOrDefault(p => p.Id == s.Profile.Id);
            if (current != s.Profile && current != other.Profile) throw Error("UpdatesStale");
            if (current != s.Profile) ((SqliteLauncherStore)store).SaveProfileUnderLease(s.Profile, ct);
            Fault?.Invoke("working:profile");
        }
    }
    public async Task ApplyAsync(GameProfile p, ModUpdatePlan plan, bool confirmed, CancellationToken ct = default)
    {
        if (!confirmed) throw Error("UpdatesConfirm");
        using var lease = new ProfileUsage(paths).Acquire(p.Id); new ProfileUsage(paths).CheckWorldLocks(p.Id); RequireNoPending(paths, p.Id);
        await ValidateSavedPlan(p, plan, ct);
        var working = plan.Kind == "Working";
        if (p != plan.Before.Profile || plan.Blockers.Count != 0 || (!working && !plan.CanApply) ||
            Fingerprint(await Capture(p, ct, partial: working, working: working)) != Fingerprint(plan.Before)) throw Error("UpdatesStale");
        if (working)
        {
            var snapshot = await ReadWorkingAsync(p.Id, ct);
            if (snapshot is null || snapshot.Id != plan.Reverts || !Same(snapshot.Receipt, ParseWorkingReceipt(plan.After.Receipt)) ||
                !snapshot.Mods.Select(mod => mod.File).SequenceEqual(plan.After.Files) ||
                await QuiltOverridesHash(p.Id, ct) != snapshot.QuiltOverridesHash) throw Error("UpdatesStale");
            var missing = await MissingWorkingGameFiles(p.Id, snapshot.Receipt, ct);
            if (missing.Count != 0) throw Error("WorkingGameMissing", string.Join(Environment.NewLine, missing));
        }
        ModUpdateOperation? original = null;
        if (plan.Kind == "Rollback") { original = await ReadOperation(p, plan.Reverts ?? throw Error("UpdatesIntegrity"), ct); await VerifyBackup(original, ct); }
        foreach (var c in plan.Changes.Where(c => c.After is not null))
        {
            var f = c.After!; var stage = Stage(plan, "new", f.Path);
            if (original is not null) await RestoreEntry(original, f, stage, ct);
            else if (working)
            {
                var source = ExactLocalFile(plan.Before, f);
                var file = source is null ? WorkingBlob(f) : SafePath.Join(paths.Game(p.Id), source.Path);
                if (!File.Exists(file)) throw Error("WorkingExactUnavailable", f.Path);
                await CopyVerified(file, stage, f, ct);
            }
            else
            {
                var v = c.Version ?? throw Error("UpdatesIntegrity");
                await downloads.DownloadAsync(v.File.Url, CacheFile(v), v.File.Sha512, v.File.Size, "SHA512", ct: ct, source: DownloadSource.Modrinth);
                await CopyVerified(CacheFile(v), stage, f, ct);
            }
        }
        if (plan.Kind == "Update")
        {
            var blockers = new List<string>(); await ReviewResult(p, plan.Before, plan.After, plan.Changes, blockers, [], ct);
            if (blockers.Count > 0) throw Error("UpdatesCompatibility", string.Join(Environment.NewLine, blockers));
        }
        if (Fingerprint(await Capture(p, ct, partial: working, working: working)) != Fingerprint(plan.Before)) throw Error("UpdatesStale");
        var op = new ModUpdateOperation(plan, "Prepared", Path.GetRelativePath(paths.Root, BackupPath(p.Id, plan.Id)).Replace('\\', '/'), await CreateBackup(plan, ct));
        await VerifyBackup(op, ct);
        await store.RegisterArtifactAsync(new(p.Id, op.Backup, "backup"), ct);
        if (Fingerprint(await Capture(p, ct, partial: working, working: working)) != Fingerprint(plan.Before)) throw Error("UpdatesStale");
        await AtomicFile.WriteJsonAsync(PendingPath(plan), op, ct);
        await AtomicFile.WriteJsonAsync(OperationPath(p.Id, plan.Id), op, ct);
        Fault?.Invoke("prepared");
        foreach (var c in plan.Changes)
        {
            ct.ThrowIfCancellationRequested();
            if (c.Before is { } old)
            {
                var target = SafePath.Join(paths.Game(p.Id), old.Path); var held = Stage(plan, "old", old.Path);
                if (await FileState(target, old.Path, ct) != old) throw Error("UpdatesStale", old.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(held)!); SafePath.RejectLinks(held); File.Move(target, held, false);
                if (await FileState(held, old.Path, ct) != old) throw Error("UpdatesStale", old.Path);
                Fault?.Invoke("old:" + old.Path);
            }
            if (c.After is { } next)
            {
                var stage = Stage(plan, "new", next.Path); var target = SafePath.Join(paths.Game(p.Id), next.Path);
                if (await FileState(stage, next.Path, ct) != next) throw Error("UpdatesIntegrity", next.Path);
                SafePath.RejectLinks(target); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Move(stage, target, false);
                if (await FileState(target, next.Path, ct) != next) throw Error("UpdatesIntegrity", next.Path);
                Fault?.Invoke("new:" + next.Path);
            }
        }
        await WriteState(plan.After, plan.Before, ct, working);
        if (Fingerprint(await Capture(plan.After.Profile, ct, working: working)) != Fingerprint(plan.After)) throw Error("UpdatesStale");
        op = op with { State = "Committed" };
        await AtomicFile.WriteJsonAsync(PendingPath(plan), op, ct);
        await AtomicFile.WriteJsonAsync(OperationPath(p.Id, plan.Id), op, ct);
        Fault?.Invoke("committed");
        File.Delete(PendingPath(plan));
        await store.AddHistoryAsync(p.Id, "mods." + plan.Kind.ToLowerInvariant(), plan.Id.ToString("N"), CancellationToken.None);
    }
    public async Task<IReadOnlyList<ModUpdateHistory>> HistoryAsync(GameProfile p, CancellationToken ct = default)
    {
        var root = SafePath.Join(paths.Profile(p.Id), "mod-updates"); if (!Directory.Exists(root)) return [];
        var result = new List<ModUpdateHistory>();
        foreach (var dir in Directory.EnumerateDirectories(root).OrderBy(x => x, StringComparer.Ordinal).Take(1024))
        {
            SafePath.RejectLinks(dir);
            if (!Guid.TryParseExact(Path.GetFileName(dir), "N", out var id) || !File.Exists(OperationPath(p.Id, id))) continue;
            var op = await AtomicFile.ReadJsonAsync<ModUpdateOperation>(OperationPath(p.Id, id), ct) ?? throw Error("UpdatesIntegrity");
            if (op.Plan.ProfileId != p.Id || op.Plan.Id != id) throw Error("UpdatesIntegrity");
            ValidateOperation(op.Plan.Before.Profile, op);
            if (op.Plan.Kind != "Working" && op.Plan.Before.Profile == p) result.Add(new(id, op.Plan.At, op.Plan.Kind, op.State, op.Backup));
        }
        return result.OrderByDescending(x => x.At).ToArray();
    }
    public async Task<ModUpdatePlan> PreviewRollbackAsync(GameProfile p, Guid operationId, CancellationToken ct = default)
    {
        using var lease = new ProfileUsage(paths).Acquire(p.Id); new ProfileUsage(paths).CheckWorldLocks(p.Id); RequireNoPending(paths, p.Id);
        var op = await ReadOperation(p, operationId, ct);
        if (op.State != "Committed" || op.Plan.Kind == "Working") throw Error("UpdatesStale");
        var before = await Capture(p, ct);
        if (Fingerprint(before) != Fingerprint(op.Plan.After)) throw Error("UpdatesStale");
        await VerifyBackup(op, ct);
        var after = op.Plan.Before with { Epoch = JsonSerializer.SerializeToUtf8Bytes(Guid.NewGuid(), AtomicFile.JsonOptions) };
        var changes = op.Plan.Changes.Select(c => new ModUpdateChange("Restore", c.After, c.Before, null)).ToArray();
        var plan = new ModUpdatePlan(Guid.NewGuid(), p.Id, DateTimeOffset.UtcNow, "Rollback", before, after, changes, [], [], operationId);
        ValidatePlan(p, plan); await AtomicFile.WriteJsonAsync(PlanPath(p.Id, plan.Id), plan, ct); return plan;
    }
    public async Task RecoverAsync(GameProfile p, bool confirmed, CancellationToken ct = default)
    {
        if (!confirmed) throw Error("UpdatesConfirm");
        using var lease = new ProfileUsage(paths).Acquire(p.Id); new ProfileUsage(paths).CheckWorldLocks(p.Id);
        var op = await AtomicFile.ReadJsonAsync<ModUpdateOperation>(PendingPath(paths, p.Id), ct); if (op is null) return;
        if (op.Plan.Kind == "Working") throw Error("UpdatesIntegrity");
        await RecoverOperation(p, op, ct);
    }
    private async Task RecoverOperation(GameProfile p, ModUpdateOperation op, CancellationToken ct)
    {
        var working = op.Plan.Kind == "Working";
        ValidateOperation(p, op); await ValidateSavedPlan(p, op.Plan, ct); await VerifyBackup(op, ct);
        var current = await Capture(p, ct, partial: true, working: working);
        if (op.State == "Committed")
        {
            if (Fingerprint(current) != Fingerprint(op.Plan.After)) throw Error("UpdatesStale");
            await AtomicFile.WriteJsonAsync(OperationPath(p.Id, op.Plan.Id), op, ct); File.Delete(PendingPath(op.Plan)); return;
        }
        if (op.State != "Prepared") throw Error("UpdatesIntegrity");
        var plan = op.Plan;
        if (current.ContextHash != plan.Before.ContextHash || new[] { (current.Inventory, plan.Before.Inventory, plan.After.Inventory), (current.Pins, plan.Before.Pins, plan.After.Pins), (current.Epoch, plan.Before.Epoch, plan.After.Epoch), (current.Receipt, plan.Before.Receipt, plan.After.Receipt) }.Any(t => Hash(t.Item1) != Hash(t.Item2) && Hash(t.Item1) != Hash(t.Item3))) throw Error("UpdatesStale");
        var affected = plan.Changes.Select(c => c.Before?.Path ?? c.After!.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var f in current.Files)
            if (!plan.Before.Files.Contains(f) && !plan.After.Files.Contains(f)) throw Error("UpdatesStale", f.Path);
        foreach (var f in plan.Before.Files.Where(f => !affected.Contains(f.Path))) if (!current.Files.Contains(f)) throw Error("UpdatesStale", f.Path);
        foreach (var c in plan.Changes)
        {
            if (c.Before is { } old && File.Exists(Stage(plan, "old", old.Path)) && await FileState(Stage(plan, "old", old.Path), old.Path, ct) != old) throw Error("UpdatesStale", old.Path);
            if (c.After is { } next && File.Exists(Stage(plan, "aborted-new", next.Path)) && await FileState(Stage(plan, "aborted-new", next.Path), next.Path, ct) != next) throw Error("UpdatesStale", next.Path);
        }
        // All files and metadata are preflighted before recovery changes anything. Unknown user bytes are preserved.
        foreach (var c in plan.Changes)
        {
            var relative = c.Before?.Path ?? c.After!.Path; var target = SafePath.Join(paths.Game(p.Id), relative);
            if (File.Exists(target))
            {
                var f = await FileState(target, relative, ct);
                if (f == c.Before) continue;
                if (f != c.After) throw Error("UpdatesStale", relative);
                var held = Stage(plan, "aborted-new", relative); Directory.CreateDirectory(Path.GetDirectoryName(held)!);
                SafePath.RejectLinks(held); File.Move(target, held, false);
            }
            if (c.Before is { } old) await RestoreEntry(op, old, target, ct);
        }
        await WriteState(plan.Before, plan.After, ct, working);
        if (Fingerprint(await Capture(plan.Before.Profile, ct, partial: working, working: working)) != Fingerprint(plan.Before)) throw Error("UpdatesStale");
        await AtomicFile.WriteJsonAsync(OperationPath(p.Id, plan.Id), op with { State = "Recovered" }, ct);
        File.Delete(PendingPath(op.Plan));
    }
}
