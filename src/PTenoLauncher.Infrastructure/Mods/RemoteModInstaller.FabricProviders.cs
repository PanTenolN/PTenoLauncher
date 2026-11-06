using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Mods;

public sealed partial class RemoteModInstaller
{
    // These are candidate projects. Only the SHA-512 verified JAR can establish that it provides a Mod ID.
    private static readonly IReadOnlyDictionary<string, string> FabricProviderProjects = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["fabric"] = "fabric-api",
        ["fabric-api"] = "fabric-api",
        ["fabric-language-kotlin"] = "fabric-language-kotlin",
        ["cardinal-components-base"] = "cardinal-components-api",
        ["cardinal-components-entity"] = "cardinal-components-api",
        ["cardinal-components-item"] = "cardinal-components-api",
        ["cardinal-components-block"] = "cardinal-components-api",
        ["paucal"] = "paucal",
        ["cloth-config"] = "cloth-config",
        ["patchouli"] = "patchouli",
        ["inline"] = "inline",
        ["architectury"] = "architectury-api"
    };

    private async Task<(DependencyResolution Resolution, ModCartReview Review)> ResolveFabricProvidersAsync(
        GameProfile profile, IReadOnlyList<ModCartItem> pending, Snapshot snapshot, DependencyResolution resolution,
        IReadOnlyList<RemoteModRequest> roots, SeededCatalog seeded, IReadOnlyList<OptionalDependencyKey> optional,
        bool automaticOptional, HashSet<string> localDigests, List<string> blockers, CancellationToken ct)
    {
        var cache = new Dictionary<string, ModCartItem>(StringComparer.OrdinalIgnoreCase);
        var attempted = new HashSet<string>(StringComparer.Ordinal);
        var matched = new Dictionary<string, (CatalogModVersion Version, string Reason)>(StringComparer.Ordinal);
        var review = await ReviewVerifiedAsync();
        while (attempted.Count < 16 && !review.Details.Any(b => b.Kind == ModBlockerKind.Definite))
        {
            var missing = review.Details.Select(b => b.Requirement).OfType<ModRequirement>().ToArray();
            var slug = missing.Select(r => FabricProviderProjects.GetValueOrDefault(r.Id))
                .OfType<string>().FirstOrDefault(attempted.Add);
            if (slug is null) break;
            var needed = missing.Where(r => FabricProviderProjects.GetValueOrDefault(r.Id) == slug).ToArray();
            try
            {
                var project = await catalog.GetProjectAsync(slug, ct);
                if (!project.SupportsClient || project.Slug != slug || resolution.Entries.Any(e => e.Version.ProjectId == project.Id)) continue;
                var versions = await catalog.GetVersionsAsync(project.Id, profile.MinecraftVersion, profile.Loader, ct);
                foreach (var version in versions.OrderBy(v => v.Channel != "release").ThenByDescending(v => v.Published).Take(4))
                {
                    var item = await InspectAsync(version);
                    if (!needed.All(requirement => Provides(item.Inspection, requirement))) continue;
                    matched.Add(project.Id, (version, "Fabric JAR metadata: " + string.Join(", ", needed.Select(r => r.Id).Distinct())));
                    resolution = WithInstalledArtifacts(await new RemoteDependencyResolver(seeded).ResolveWithOptionalAsync(profile,
                        roots.Concat(matched.Values.Select(m => new RemoteModRequest(m.Version.ProjectId, m.Version.Id))).ToArray(),
                        snapshot.Known, optional, ct, automatic: automaticOptional), snapshot);
                    review = await ReviewVerifiedAsync();
                    break;
                }
            }
            catch (Exception error) when (IsProviderLookupFailure(error, ct))
            {
                // Keep the original requirement visible. The user can supply a different catalog file or a local JAR.
            }
        }
        resolution = resolution with { Entries = resolution.Entries.Select(e => matched.TryGetValue(e.Version.ProjectId, out var match)
            ? e with { Reason = match.Reason } : e).ToArray() };
        return (resolution, review);

        async Task<ModCartReview> ReviewVerifiedAsync()
        {
            var proposed = new List<ModCartItem>(pending);
            var localHashes = pending.Select(p => p.Inspection.Sha256).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in DistinctArtifacts(resolution.Entries.Where(e => !e.AlreadyInstalled && !localDigests.Contains(e.Version.File.Sha512))))
            {
                try
                {
                    var item = await InspectAsync(entry.Version);
                    if (localHashes.Add(item.Inspection.Sha256)) proposed.Add(item);
                }
                catch (Exception error) when (IsProviderLookupFailure(error, ct))
                { blockers.Add(entry.Version.File.Name + " — Modrinth: " + error.GetType().Name); }
            }
            return await local.ReviewAsync(profile, proposed, ct);
        }

        async Task<ModCartItem> InspectAsync(CatalogModVersion version)
        {
            var key = version.File.Sha512;
            if (!cache.TryGetValue(key, out var item))
            {
                item = await PrepareCatalogJarAsync(version, null, null, null, ct);
                cache.Add(key, item);
            }
            return item;
        }
    }

    private static bool IsProviderLookupFailure(Exception error, CancellationToken ct) =>
        !ct.IsCancellationRequested && error is IOException or InvalidDataException or HttpRequestException or JsonException
            or InvalidOperationException or ArgumentException or KeyNotFoundException or TaskCanceledException;

    private static bool Provides(ModInspection inspection, ModRequirement requirement)
    {
        if (inspection.Loader != ModLoader.Fabric || inspection.Environment is not ("*" or "client") ||
            inspection.FabricMetadata is not { } fabric) return false;
        var roots = fabric.Aliases.Select(alias => alias.Id).Append(fabric.Id).ToHashSet(StringComparer.Ordinal);
        var supplied = LocalModManager.EffectiveFabricChildren(inspection, roots)
            .SelectMany(child => child.Aliases.Append(new(child.Id, child.Version)))
            .Concat(fabric.Aliases).Append(new(fabric.Id, fabric.Version));
        return supplied.Any(provider => provider.Id == requirement.Id &&
            requirement.Alternatives.Any(range => VersionConstraints.MatchFabric(provider.Version, range) == ConstraintMatch.Match));
    }

    public async Task<bool> VerifyFabricProviderAsync(GameProfile profile, CatalogModVersion version,
        ModRequirement requirement, CancellationToken ct)
    {
        if (profile.Loader != ModLoader.Fabric || !version.Loaders.Contains("fabric") ||
            !version.GameVersions.Contains(profile.MinecraftVersion)) return false;
        var item = await PrepareCatalogJarAsync(version, null, null, null, ct);
        return Provides(item.Inspection, requirement);
    }

    private async Task<ModCartItem> PrepareCatalogJarAsync(CatalogModVersion version, string? reason, string? title,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var file = version.File;
        var destination = SafePath.Join(paths.Cache, "modrinth/files/" + file.Sha512 + "/" + file.Name);
        await downloads.DownloadAsync(file.Url, destination, file.Sha512, file.Size, "SHA512", progress, ct, DownloadSource.Modrinth);
        var inspection = await local.InspectAsync(destination, ct);
        return new(destination, inspection with { SourceStatus = "Modrinth" }, reason, title);
    }
}
