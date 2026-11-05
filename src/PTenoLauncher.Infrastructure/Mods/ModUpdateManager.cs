using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Mods;

public sealed partial class ModUpdateManager(LauncherPaths paths, ILauncherStore store, ModrinthClient catalog, VerifiedDownloader downloads, LocalModManager local)
{
    internal Action<string>? Fault { get; set; } // Controlled tests only; never configured by production UI.
    private static ModUpdateException Error(string code, string detail = "") => new(code, detail);
    private string PinsPath(Guid id) => SafePath.Join(paths.Profile(id), "mod-pins.json");
    private string EpochPath(Guid id) => SafePath.Join(paths.Profile(id), "mod-update-epoch.json");
    private string OperationRoot(Guid id, Guid op) => SafePath.Join(paths.Profile(id), "mod-updates/" + op.ToString("N"));
    private string PlanPath(Guid id, Guid op) => SafePath.Join(OperationRoot(id, op), "plan.json");
    private string OperationPath(Guid id, Guid op) => SafePath.Join(OperationRoot(id, op), "operation.json");
    internal static string PendingPath(LauncherPaths p, Guid id) => SafePath.Join(p.Profile(id), "transactions/mod-update.json");
    internal static void RequireNoPending(LauncherPaths p, Guid id)
    {
        var transactions = SafePath.Join(p.Profile(id), "transactions");
        if (File.Exists(PendingPath(p, id)) || Directory.Exists(transactions) && Directory.EnumerateFiles(transactions, "mods-*.json").Any()) throw Error("UpdatesRecovery");
    }
    private async Task<byte[]?> ReadBytes(string file, CancellationToken ct)
    {
        SafePath.RejectLinks(file); if (!File.Exists(file)) return null;
        await using var s = File.OpenRead(file); if (s.Length > 8 * 1024 * 1024) throw Error("UpdatesIntegrity", "Metadata exceeds 8 MiB.");
        using var m = new MemoryStream(); await s.CopyToAsync(m, ct); return m.ToArray();
    }
    private static string Hash(byte[]? bytes) => bytes is null ? "absent" : Convert.ToHexString(SHA256.HashData(bytes));
    internal static string Fingerprint(ModSetState s) => Hash(JsonSerializer.SerializeToUtf8Bytes(new { s.Profile, s.Files, Inventory = Hash(s.Inventory), Pins = Hash(s.Pins), Epoch = Hash(s.Epoch), s.ContextHash }, AtomicFile.JsonOptions));
    private async Task<ModSetState> Capture(GameProfile p, CancellationToken ct, bool partial = false)
    {
        ProfileRules.Validate(p);
        if (!(await store.GetProfilesAsync(ct)).Contains(p)) throw Error("UpdatesStale");
        var dir = SafePath.Join(paths.Game(p.Id), "mods");
        var files = Directory.Exists(dir) ? Directory.EnumerateFileSystemEntries(dir).Take(8193).ToArray() : [];
        if (files.Length > 8192) throw Error("UpdatesIntegrity", "Mods directory exceeds 8192 entries.");
        foreach (var f in files) { SafePath.RejectLinks(f); if (Directory.Exists(f) || Path.GetFileName(f).Contains(".pending-", StringComparison.Ordinal)) throw Error("UpdatesIntegrity", "Unsupported/pending mods layout."); }
        var jars = files.Where(f => Path.GetExtension(f).Equals(".jar", StringComparison.OrdinalIgnoreCase)).OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToArray();
        if (jars.Length > 128) throw Error("UpdatesIntegrity", "Update workflow is bounded to 128 root JARs.");
        var states = new List<ModFileState>();
        foreach (var f in jars) states.Add(await FileState(f, "mods/" + Path.GetFileName(f), ct));
        if (states.Sum(f => f.Size) > 8L * 1024 * 1024 * 1024) throw Error("UpdatesIntegrity", "Mod set exceeds 8 GiB.");
        var inv = await new ModInventoryStore(paths).ReadAsync(p.Id, ct);
        foreach (var old in partial ? [] : inv?.Files ?? [])
            if (!states.Any(f => f.Path == old.RelativePath && f.Size == old.Size && f.Sha256.Equals(old.Sha256, StringComparison.OrdinalIgnoreCase)))
                throw Error("UpdatesIntegrity", old.RelativePath);
        var state = new ModSetState(p, states, await ReadBytes(new ModInventoryStore(paths).PathFor(p.Id), ct), await ReadBytes(PinsPath(p.Id), ct), await ReadBytes(EpochPath(p.Id), ct),
            Hash(JsonSerializer.SerializeToUtf8Bytes(new { Receipt = Hash(await ReadBytes(paths.Receipt(p.Id), ct)), QuiltOverrides = Hash(await ReadBytes(SafePath.Join(paths.Game(p.Id), "config/quilt-loader-overrides.json"), ct)) })));
        var pins = ReadPins(state);
        foreach (var pin in pins.Pins) if (!partial && !states.Contains(pin.File)) throw Error("UpdatesPinConflict", pin.File.Path);
        return state;
    }
    private async Task<ModFileState> FileState(string file, string relative, CancellationToken ct)
    {
        _ = SafePath.Join(paths.Cache, relative); SafePath.RejectLinks(file);
        await using var s = File.OpenRead(file); if (s.Length is <= 0 or > 512L * 1024 * 1024) throw Error("UpdatesIntegrity", relative);
        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(s, ct)).ToLowerInvariant(); s.Position = 0;
        return new(relative, sha256, Convert.ToHexString(await SHA512.HashDataAsync(s, ct)).ToLowerInvariant(), s.Length);
    }
    private ModPinSet ReadPins(ModSetState state)
    {
        var pins = state.Pins is null ? new(state.Profile.Id, [], 1) : JsonSerializer.Deserialize<ModPinSet>(state.Pins, AtomicFile.JsonOptions) ?? throw Error("UpdatesIntegrity");
        if (pins.ProfileId != state.Profile.Id || pins.Schema != 1 || pins.Pins is null || pins.Pins.Count > 128 || pins.Pins.Select(p => p.File.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != pins.Pins.Count) throw Error("UpdatesIntegrity");
        foreach (var p in pins.Pins) { ValidateFile(p.File); if (p.Provider is { } v) { ValidateVersion(v); if (v.File.Sha512 != p.File.Sha512 || v.File.Size != p.File.Size) throw Error("UpdatesIntegrity"); } }
        return pins;
    }
    private void ValidateFile(ModFileState f)
    {
        new ModInventoryStore(paths).Validate(Guid.Empty, new(Guid.Empty, [new(f.Path, f.Sha256, f.Size)]));
        if (f.Sha512.Length != 128 || !f.Sha512.All(Uri.IsHexDigit)) throw Error("UpdatesIntegrity");
    }
    private void ValidateVersion(CatalogModVersion v)
    {
        static bool Id(string s) => s.Length is > 0 and <= 128 && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
        if (!Id(v.Id) || !Id(v.ProjectId) || v.Channel is not ("release" or "beta" or "alpha") || v.File.Size is <= 0 or > 512L * 1024 * 1024 || v.File.Sha512.Length != 128 || !v.File.Sha512.All(Uri.IsHexDigit) || v.Dependencies.Count > 256) throw Error("UpdatesIntegrity");
        TrustedSources.Validate(v.File.Url, DownloadSource.Modrinth);
        if (v.File.Url.Host != "cdn.modrinth.com" || v.File.Name.Contains('/') || v.File.Name.Contains('\\')) throw Error("UpdatesIntegrity");
        _ = SafePath.Join(paths.Cache, "file-check/" + v.File.Name);
    }
    private async Task<CatalogModVersion?> Identity(ModFileState f, CancellationToken ct)
    {
        var v = await catalog.FindBySha512Async(f.Sha512, ct);
        if (v is not null) { ValidateVersion(v); if (v.File.Sha512 != f.Sha512 || v.File.Size != f.Size) throw Error("UpdatesIntegrity", f.Path); }
        return v;
    }
    public async Task<ModUpdateScan> CheckAsync(GameProfile p, bool includePrerelease = false, CancellationToken ct = default)
    {
        using var lease = new ProfileUsage(paths).Acquire(p.Id); new ProfileUsage(paths).CheckWorldLocks(p.Id); RequireNoPending(paths, p.Id);
        catalog.BeginOperation(); var state = await Capture(p, ct, partial: true); var pins = ReadPins(state); var rows = new List<ModUpdateRow>(); var notes = new List<string>();
        foreach (var f in state.Files)
        {
            CatalogModVersion? current = null, available = null; string? note = null;
            try
            {
                current = await Identity(f, ct);
                if (current is null) note = "UpdatesUnknown";
                else
                {
                    var versions = await catalog.GetVersionsAsync(current.ProjectId, p.MinecraftVersion, p.Loader, ct);
                    available = versions.Where(v => v.Published > current.Published && (includePrerelease || v.Channel == "release"))
                        .OrderByDescending(v => v.Published).ThenBy(v => v.Id, StringComparer.Ordinal).FirstOrDefault();
                    if (available is null) note = "UpdatesNoNewer";
                }
            }
            catch (Exception e) when (e is HttpRequestException or InvalidDataException or System.Text.Json.JsonException) { note = "UpdatesUnavailable"; notes.Add(f.Path); }
            if (pins.Pins.Any(x => x.File.Path == f.Path && x.File != f)) { note = "UpdatesPinConflict"; available = null; }
            rows.Add(new(f, current, available, pins.Pins.Any(x => x.File.Path == f.Path), note));
        }
        foreach (var missing in pins.Pins.Where(x => !state.Files.Any(f => f.Path == x.File.Path)))
            rows.Add(new(missing.File, missing.Provider, null, true, "UpdatesPinConflict"));
        return new(Fingerprint(state), rows, notes, catalog.UsedCache, catalog.OldestMetadataAt);
    }
    public async Task SetPinAsync(GameProfile p, string relative, bool pin, bool confirmed, CancellationToken ct = default)
    {
        if (!confirmed) throw Error("UpdatesConfirm");
        using var lease = new ProfileUsage(paths).Acquire(p.Id); new ProfileUsage(paths).CheckWorldLocks(p.Id); RequireNoPending(paths, p.Id);
        var state = await Capture(p, ct, partial: true); var prior = ReadPins(state).Pins;
        var f = state.Files.SingleOrDefault(f => f.Path == relative);
        if (pin && f is null || !pin && !prior.Any(x => x.File.Path == relative)) throw Error("UpdatesStale");
        if (pin && prior.Any(x => x.File.Path == relative && x.File != f)) throw Error("UpdatesPinConflict", relative);
        var pins = prior.Where(x => x.File.Path != relative).ToList();
        CatalogModVersion? provider = null;
        if (pin)
        {
            try { provider = await Identity(f!, ct); } catch (Exception e) when (e is HttpRequestException or InvalidDataException) { }
            pins.Add(new(f!, provider));
        }
        // Pin bytes alone already invalidate both preview and rollback, even if epoch writing is interrupted.
        await AtomicFile.WriteJsonAsync(PinsPath(p.Id), new ModPinSet(p.Id, pins.OrderBy(x => x.File.Path, StringComparer.Ordinal).ToArray()), ct);
        await AtomicFile.WriteJsonAsync(EpochPath(p.Id), Guid.NewGuid(), CancellationToken.None);
        await store.AddHistoryAsync(p.Id, pin ? "mods.pin" : "mods.unpin", relative, CancellationToken.None);
    }
}
