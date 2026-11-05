using PTenoLauncher.Core;
using System.Text.Json;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Mods;

public sealed record SelectedNestedArtifact(string Identity, string Version, string Sha256, string Origin, bool Library);
public sealed record NativeModSelection(IReadOnlyList<ModInspection> ActiveMods, IReadOnlyList<SelectedNestedArtifact> Artifacts,
    IReadOnlyList<ReadinessIssue> Issues, IReadOnlyList<string> Warnings)
{
    public bool Confirmed => Issues.Count == 0;
}

/// <summary>Bounded static native-loader subset. Never extracts/executes mods, accesses the network or invokes AI.</summary>
public static partial class NativeNestedResolver
{
    public static async Task<NativeModSelection> AnalyzeProfileAsync(LauncherPaths paths, GameProfile profile, InstallationReceipt? receipt,
        IReadOnlyList<ModInspection> roots, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (profile.Loader == ModLoader.Quilt)
        {
            if (receipt?.JvmArguments.Any(a => a.StartsWith("-Dloader.", StringComparison.Ordinal)) == true)
                return Unknown(roots, "Custom Quilt loader JVM properties can change discovery/selection and are not modeled.");
            var file = SafePath.Join(paths.Game(profile.Id), "config/quilt-loader-overrides.json");
            if (File.Exists(file))
            {
                try
                {
                    await using var input = File.OpenRead(file); // Windows read-sharing denies concurrent writes to this handle.
                    if (input.Length > 1024 * 1024) return Unknown(roots, "Quilt overrides exceed the 1 MiB static inspection limit.");
                    using var doc = await JsonDocument.ParseAsync(input, new() { MaxDepth = 32, CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }, ct);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Select(p => p.Name).Distinct().Count() != root.EnumerateObject().Count() ||
                        root.EnumerateObject().Any(p => p.Name is not ("schema_version" or "overrides")) ||
                        !root.TryGetProperty("schema_version", out var schema) || !schema.TryGetInt32(out int version) || version != 1 ||
                        root.TryGetProperty("overrides", out var overrides) && (overrides.ValueKind != JsonValueKind.Array || overrides.GetArrayLength() != 0))
                        return Unknown(roots, "Nonempty/unsupported Quilt overrides can replace versions/dependencies; selection needs an overrides adapter.");
                }
                catch (Exception e) when (e is JsonException or InvalidOperationException or IOException)
                { return Unknown(roots, "Quilt overrides could not be verified: " + e.GetType().Name); }
            }
        }
        return Analyze(profile, receipt, roots, ct);
    }
    public static bool HasCandidates(IEnumerable<ModInspection> mods) => mods.Any(m => m.Nested is { } n && (n.Candidates.Count != 0 || n.Issues.Count != 0));
    public static NativeModSelection Analyze(GameProfile profile, InstallationReceipt? receipt, IReadOnlyList<ModInspection> roots, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (profile.Loader is not (ModLoader.Quilt or ModLoader.Forge or ModLoader.NeoForge))
            return Unknown(roots, "This analyzer supports native Quilt/Forge/NeoForge metadata only; it does not provide a cross-loader bridge.");
        if (!HasCandidates(roots))
        {
            if (profile.Loader == ModLoader.Quilt) return new(roots, [], QuiltCompatibility.Check(profile, receipt, roots), []);
            var r = ForgeCompatibility.Check(profile, receipt, roots); return new(roots, [], r.Issues, r.Warnings);
        }
        return profile.Loader == ModLoader.Quilt ? SolveQuilt(profile, receipt, roots, ct) : SelectJarJar(profile, receipt, roots, ct);
    }
    private sealed record Located(ModInspection Mod, string Origin, bool Root, NativeNestedCandidate? Descriptor = null);
    private static (List<Located> All, List<ReadinessIssue> Issues) Collect(IReadOnlyList<ModInspection> roots, ModLoader loader, CancellationToken ct)
    {
        var all = new List<Located>(); var issues = new List<ReadinessIssue>();
        void Visit(ModInspection mod, string origin, bool root, NativeNestedCandidate? descriptor, int depth)
        {
            ct.ThrowIfCancellationRequested();
            if (all.Count >= 128 || depth > 4) { issues.Add(new("mods.nested.limit.unknown", "Native candidate graph exceeds 128 nodes or depth 4.")); return; }
            all.Add(new(mod, origin, root, descriptor));
            issues.AddRange(mod.Nested?.Issues ?? []);
            if (mod.Loader is not null && mod.Loader != loader) issues.Add(new("mods.nested.loader.unknown", origin + ": another native metadata format requires its own adapter; no cross-loader selection is performed."));
            foreach (var warning in mod.MetadataWarnings ?? []) issues.Add(new("mods.nested.metadata.unknown", origin + ": " + warning));
            foreach (var child in (mod.Nested?.Candidates ?? []).OrderBy(c => c.Path, StringComparer.Ordinal))
                Visit(child.Inspection, origin + "!/" + child.Path, false, child, depth + 1);
        }
        foreach (var root in roots.OrderBy(m => m.Sha256, StringComparer.Ordinal).ThenBy(m => m.FileName, StringComparer.Ordinal)) Visit(root, root.FileName, true, null, 0);
        return (all, issues.Distinct().ToList());
    }
    private static NativeModSelection Unknown(IReadOnlyList<ModInspection> roots, string reason) => new(roots, [], [new("mods.nested.selection.unknown", reason)], []);
    private static NativeModSelection SelectJarJar(GameProfile profile, InstallationReceipt? receipt, IReadOnlyList<ModInspection> roots, CancellationToken ct)
    {
        if (profile.Loader is not (ModLoader.Forge or ModLoader.NeoForge)) return Unknown(roots, "Unsupported native loader.");
        var (all, issues) = Collect(roots, profile.Loader, ct);
        if (issues.Count != 0) return new(roots, [], issues, []);
        var selected = new List<Located>(); var artifacts = new List<SelectedNestedArtifact>();
        var groups = all.Where(l => !l.Root).GroupBy(l => l.Descriptor!.ArtifactGroup + ":" + l.Descriptor.Artifact).OrderBy(g => g.Key, StringComparer.Ordinal);
        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            var candidates = group.OrderBy(c => c.Mod.Sha256, StringComparer.Ordinal).ThenBy(c => c.Origin, StringComparer.Ordinal).ToArray();
            if (candidates.Any(c => c.Descriptor!.IsObfuscated)) { issues.Add(new("mods.nested.obfuscated.unknown", group.Key + ": obfuscated library handling is unsupported.")); continue; }
            // Forge and NeoForge selector revisions differ on single-candidate validation and tie selection.
            // Confirm only the common subset: explicit restrictive intervals, one content-identical eligible artifact.
            var ranges = candidates.Select(c => c.Descriptor!.RequestedRange!).Distinct(StringComparer.Ordinal).ToArray();
            if (ranges.Any(r => r.Length == 0 || r[0] is not ('[' or '(')))
            { issues.Add(new("mods.nested.range.unknown", group.Key + ": bare Maven recommendations need a version-specific selector adapter.")); continue; }
            var eligible = new List<Located>(); bool unknown = false;
            foreach (var candidate in candidates)
            {
                var matches = ranges.Select(range => MavenVersionConstraints.Match(candidate.Descriptor!.ArtifactVersion, range)).ToArray();
                if (matches.Contains(ConstraintMatch.Unknown)) unknown = true;
                if (matches.All(m => m == ConstraintMatch.Match)) eligible.Add(candidate);
            }
            if (unknown) { issues.Add(new("mods.nested.range.unknown", group.Key + ": unsupported artifact version/range.")); continue; }
            if (eligible.Count == 0)
            {
                // Old NeoForge skips validation of a single descriptor. Do not claim a shared-loader verdict in this case.
                issues.Add(new(candidates.Select(c => (c.Descriptor!.ArtifactVersion, c.Mod.Sha256)).Distinct().Count() == 1 ? "mods.nested.selector.unknown" : "mods.nested.unsatisfied",
                    group.Key + ": no embedded artifact satisfies all restrictive ranges (" + string.Join(" AND ", ranges) + ").")); continue;
            }
            if (eligible.Select(c => (c.Descriptor!.ArtifactVersion, c.Mod.Sha256)).Distinct().Count() != 1)
            { issues.Add(new("mods.nested.ambiguous.unknown", group.Key + ": multiple eligible artifacts; official selector revisions/order can choose different bytes. No highest-version guess.")); continue; }
            var winner = eligible[0]; selected.Add(winner);
            artifacts.Add(new(group.Key, winner.Descriptor!.ArtifactVersion!, winner.Mod.Sha256, winner.Origin, winner.Mod.ForgeMetadata is null));
        }
        if (issues.Count != 0) return new(roots, artifacts, issues, []);
        var active = roots.OrderBy(m => m.Sha256, StringComparer.Ordinal).ThenBy(m => m.FileName, StringComparer.Ordinal).ToList();
        // Confirmed Forge 1.21.1 / NeoForge locator identification: concatenated mod IDs, not Maven coordinates.
        string Identity(ModInspection m) => m.ForgeMetadata is not null ? string.Concat(m.ForgeMetadata.Mods.Select(d => d.Id)) : m.Nested!.LibraryIdentity!;
        var rootIds = roots.Where(m => m.ForgeMetadata is not null).Select(Identity).ToHashSet(StringComparer.Ordinal);
        var identities = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var l in selected.OrderBy(l => l.Origin, StringComparer.Ordinal))
        {
            if (l.Mod.ForgeMetadata is null && (l.Mod.Nested?.Library != true || l.Mod.Nested.LibraryIdentity is null))
            { issues.Add(new("mods.nested.metadata.unknown", l.Origin + ": nested artifact is not a verified mod or explicit library.")); continue; }
            var id = Identity(l.Mod);
            if (rootIds.Contains(id))
            { artifacts.RemoveAll(a => a.Sha256 == l.Mod.Sha256 && a.Origin == l.Origin); continue; } // Official root-source identity takes precedence.
            if (identities.TryGetValue(id, out var hash))
            { if (hash != l.Mod.Sha256) issues.Add(new("mods.nested.identity.unknown", id + ": different selected artifacts claim the same mod-file identity.")); continue; }
            identities.Add(id, l.Mod.Sha256);
            if (l.Mod.ForgeMetadata is not null) active.Add(l.Mod with { FileName = l.Origin, Nested = null });
        }
        var result = ForgeCompatibility.Check(profile, receipt, active);
        return new(active, artifacts, issues.Concat(result.Issues).Distinct().ToArray(), result.Warnings);
    }
}
