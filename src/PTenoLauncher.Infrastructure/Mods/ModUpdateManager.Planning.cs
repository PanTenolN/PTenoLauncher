using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Mods;

public sealed partial class ModUpdateManager
{
    private sealed class PreferredCatalog(ModrinthClient source, IReadOnlyList<CatalogModVersion> current, bool prerelease) : IModCatalog
    {
        public Task<CatalogProject> GetProjectAsync(string id, CancellationToken ct = default) => source.GetProjectAsync(id, ct);
        public Task<CatalogModVersion> GetVersionAsync(string id, CancellationToken ct = default)
            => current.FirstOrDefault(v => v.Id == id) is { } v ? Task.FromResult(v) : source.GetVersionAsync(id, ct);
        public async Task<IReadOnlyList<CatalogModVersion>> GetVersionsAsync(string id, string game, ModLoader loader, CancellationToken ct = default)
        {
            var list = await source.GetVersionsAsync(id, game, loader, ct);
            return current.Where(v => v.ProjectId == id).Concat(list.Where(v => prerelease || v.Channel == "release")).DistinctBy(v => v.Id).ToArray();
        }
    }
    private string CacheFile(CatalogModVersion v) => SafePath.Join(paths.Cache, "modrinth/files/" + v.File.Sha512 + "/" + v.File.Name);
    public async Task<ModUpdatePlan> PreviewAsync(GameProfile p, IReadOnlyList<ModUpdateRequest> requests, bool includePrerelease = false, CancellationToken ct = default)
    {
        if (requests.Count is 0 or > 128 || requests.Select(r => r.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != requests.Count) throw Error("UpdatesSelect");
        using var lease = new ProfileUsage(paths).Acquire(p.Id); new ProfileUsage(paths).CheckWorldLocks(p.Id); RequireNoPending(paths, p.Id);
        catalog.BeginOperation(); var before = await Capture(p, ct); var pins = ReadPins(before); var known = new Dictionary<string, CatalogModVersion>(StringComparer.Ordinal);
        foreach (var f in before.Files)
        {
            CatalogModVersion? v;
            try { v = await Identity(f, ct); }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException) { v = pins.Pins.FirstOrDefault(x => x.File == f)?.Provider; }
            if (v is not null) known.Add(f.Path, v);
        }
        if (known.Values.GroupBy(v => v.ProjectId).Any(g => g.Count() > 1)) throw Error("UpdatesIntegrity", "Duplicate provider project files.");
        var inventory = before.Inventory is null ? new InstalledModInventory(p.Id, []) : JsonSerializer.Deserialize<InstalledModInventory>(before.Inventory, AtomicFile.JsonOptions)!;
        var selected = new Dictionary<string, CatalogModVersion>(StringComparer.Ordinal); var deleted = new HashSet<string>(StringComparer.Ordinal);
        var blockers = new List<string>(); var warnings = new List<string>();
        foreach (var request in requests)
        {
            var f = before.Files.SingleOrDefault(f => f.Path == request.Path) ?? throw Error("UpdatesStale");
            if (!inventory.Files.Any(x => x.RelativePath == f.Path && x.Sha256.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase) && x.Size == f.Size)) throw Error("UpdatesIntegrity", f.Path);
            if (!known.TryGetValue(f.Path, out var current)) throw Error("UpdatesUnknown", f.Path);
            if (pins.Pins.Any(x => x.File.Path == f.Path)) { blockers.Add("UpdatesPinConflict: " + f.Path); continue; }
            if (request.Remove) { if (request.VersionId is not null) throw Error("UpdatesSelect"); deleted.Add(f.Path); continue; }
            var version = await catalog.GetVersionAsync(request.VersionId ?? throw Error("UpdatesSelect"), ct); ValidateVersion(version);
            if (version.ProjectId != current.ProjectId || version.Published <= current.Published || !version.GameVersions.Contains(p.MinecraftVersion) || !version.Loaders.Contains(ModrinthClient.LoaderName(p.Loader)) || !includePrerelease && version.Channel != "release")
                throw Error("UpdatesStale", request.Path);
            selected.Add(current.ProjectId, version);
        }
        var retainedKnown = known.Where(k => !deleted.Contains(k.Key)).Select(k => k.Value).ToArray();
        var roots = selected.Values.Select(v => new RemoteModRequest(v.ProjectId, v.Id, "Direct update"))
            .Concat(pins.Pins.Where(x => x.Provider is not null).Select(x => new RemoteModRequest(x.Provider!.ProjectId, x.Provider.Id, "Persistent pin")))
            .Concat(retainedKnown.Where(v => !selected.ContainsKey(v.ProjectId)).Select(v => new RemoteModRequest(v.ProjectId, null, "Retain installed project"))).ToArray();
        DependencyResolution resolution = new([], [], [], []);
        if (roots.Length > 0)
            resolution = await new RemoteDependencyResolver(new PreferredCatalog(catalog, retainedKnown, includePrerelease)).ResolveAsync(p, roots, [], ct,
                preferredVersions: retainedKnown.ToDictionary(v => v.ProjectId, v => v.Id, StringComparer.Ordinal));
        blockers.AddRange(resolution.Blockers); warnings.AddRange(resolution.Warnings);
        var result = before.Files.Where(f => !known.ContainsKey(f.Path) && !deleted.Contains(f.Path)).ToList();
        var changes = new List<ModUpdateChange>();
        var reservedNames = before.Files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in deleted) changes.Add(new("Remove", before.Files.Single(f => f.Path == path), null, null));
        foreach (var entry in resolution.Entries)
        {
            var v = entry.Version; ValidateVersion(v);
            var existingPath = known.FirstOrDefault(k => k.Value.ProjectId == v.ProjectId).Key;
            var old = before.Files.SingleOrDefault(f => f.Path == existingPath);
            if (old is not null && !deleted.Contains(old.Path) && old.Sha512 == v.File.Sha512) { result.Add(old); continue; }
            if (old is not null && pins.Pins.Any(x => x.File.Path == old.Path)) { blockers.Add("UpdatesPinConflict: " + old.Path); continue; }
            if (old is not null && !inventory.Files.Any(f => f.RelativePath == old.Path)) { blockers.Add("UpdatesIntegrity: " + old.Path); continue; }
            if (old is not null && deleted.Contains(old.Path)) { blockers.Add("UpdatesCompatibility: " + v.ProjectId); continue; }
            await downloads.DownloadAsync(v.File.Url, CacheFile(v), v.File.Sha512, v.File.Size, "SHA512", ct: ct, source: DownloadSource.Modrinth);
            var payload = await FileState(CacheFile(v), "mods/" + v.File.Name, ct);
            var technical = old is not null && ReadableName.StripDigest(Path.GetFileNameWithoutExtension(old.Path), old.Sha256, old.Sha512) is not null;
            var followsProvider = old is not null && known.TryGetValue(old.Path, out var previousVersion) &&
                old.Path == "mods/" + previousVersion.File.Name && previousVersion.File.Name != v.File.Name;
            var relative = old is not null && !technical && !followsProvider ? old.Path :
                LocalModManager.ChooseInstallPath(paths.Game(p.Id), v.File.Name, payload.Sha256, reservedNames,
                    v.ProjectId + "-" + v.Number, payload.Sha512);
            var file = payload with { Path = relative };
            result.Add(file);
            var kind = old is null ? "DependencyAdd" : selected.ContainsKey(v.ProjectId) ? "Update" : "DependencyReplace";
            if (old is not null && old.Path != relative)
            {
                changes.Add(new(kind, old, null, null));
                old = null;
            }
            changes.Add(new(kind, old, file, v));
        }
        if (result.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count) blockers.Add("UpdatesIntegrity");
        // All untracked and unselected bytes are retained; dependency orphans are never automatically removed.
        foreach (var f in before.Files.Where(f => !deleted.Contains(f.Path) && !result.Any(x => x.Path == f.Path) && !changes.Any(c => c.Before?.Path == f.Path))) result.Add(f);
        var finalInventory = inventory.Files.Where(f => !changes.Any(c => c.Before?.Path == f.RelativePath)).ToList();
        finalInventory.AddRange(changes.Where(c => c.After is not null).Select(c => new InstalledModFile(c.After!.Path, c.After.Sha256, c.After.Size, "modrinth")));
        var invAfter = new InstalledModInventory(p.Id, finalInventory.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToArray()); new ModInventoryStore(paths).Validate(p.Id, invAfter);
        var after = before with { Files = result.OrderBy(f => f.Path, StringComparer.Ordinal).ToArray(), Inventory = JsonSerializer.SerializeToUtf8Bytes(invAfter, AtomicFile.JsonOptions), Epoch = JsonSerializer.SerializeToUtf8Bytes(Guid.NewGuid(), AtomicFile.JsonOptions) };
        if (blockers.Count == 0) await ReviewResult(p, before, after, changes, blockers, warnings, ct);
        if (Fingerprint(await Capture(p, ct)) != Fingerprint(before)) throw Error("UpdatesStale");
        var plan = new ModUpdatePlan(Guid.NewGuid(), p.Id, DateTimeOffset.UtcNow, "Update", before, after, changes, blockers.Distinct().ToArray(), warnings.Distinct().ToArray());
        await AtomicFile.WriteJsonAsync(PlanPath(p.Id, plan.Id), plan, ct); return plan;
    }
    private async Task ReviewResult(GameProfile p, ModSetState before, ModSetState after, IReadOnlyList<ModUpdateChange> changes, List<string> blockers, List<string> warnings, CancellationToken ct)
    {
        var items = new List<ModCartItem>();
        foreach (var f in after.Files)
        {
            var change = changes.SingleOrDefault(c => c.After?.Path == f.Path);
            var source = change is { Version: { } v } ? CacheFile(v) : SafePath.Join(paths.Game(p.Id), f.Path);
            if (await FileState(source, f.Path, ct) != f) throw Error("UpdatesIntegrity", f.Path);
            var inspection = await local.InspectAsync(source, ct); items.Add(new(source, inspection));
            if (inspection.Id is null || inspection.MetadataWarnings?.Count > 0 || inspection.Loader != p.Loader) blockers.Add("UpdatesUnknown: " + f.Path);
        }
        var review = await local.ReviewResultSetAsync(p, items, ct); blockers.AddRange(review.Blockers); warnings.AddRange(review.Warnings);
        if (review.NativeSelection is { Confirmed: false } n) blockers.AddRange(n.Issues.Select(i => i.Message));
        if (p.Loader == ModLoader.Fabric)
        {
            var receipt = await AtomicFile.ReadJsonAsync<InstallationReceipt>(paths.Receipt(p.Id), ct);
            blockers.AddRange(InstalledModReadiness.CheckFabricSet(p, receipt, items.Select(i => i.Inspection).ToList()).Select(i => i.Message));
        }
    }
}
