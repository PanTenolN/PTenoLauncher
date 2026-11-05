using System.Text.RegularExpressions;
using PTenoLauncher.Core;

namespace PTenoLauncher.Infrastructure.Mods;

public static partial class NativeNestedResolver
{
    private sealed record Provider(string Id, string? Group, string? Version, string Environment, int Choice);
    private static NativeModSelection SolveQuilt(GameProfile profile, InstallationReceipt? receipt, IReadOnlyList<ModInspection> roots, CancellationToken ct)
    {
        var (discovered, issues) = Collect(roots, ModLoader.Quilt, ct);
        if (issues.Count != 0) return new(roots, [], issues, []);
        if (discovered.Any(l => l.Mod.QuiltMetadata is null || l.Mod.Id is null)) return Unknown(roots, "Quilt nested candidates without native quilt.mod.json are unsupported; plain libraries/other plugins are not assumed active.");
        if (discovered.Any(l => l.Mod.Environment is not ("*" or "client" or "server"))) return Unknown(roots, "Unknown Quilt candidate environment.");
        if (discovered.Where(l => l.Root && l.Mod.Environment != "server").GroupBy(l => l.Mod.Id).Any(g => g.Count() > 1))
            return new(roots, [], [new("mods.duplicate", "Two direct Quilt roots require the same Mod ID.")], []);
        // Byte-identical copies are observationally equivalent; preserve a deterministic origin for diagnostics.
        var choices = discovered.GroupBy(l => l.Mod.Sha256, StringComparer.Ordinal).Select(g => g.OrderByDescending(l => l.Root).ThenBy(l => l.Origin, StringComparer.Ordinal).First())
            .OrderBy(l => l.Mod.Id, StringComparer.Ordinal).ThenBy(l => l.Mod.Sha256, StringComparer.Ordinal).ToArray();
        if (choices.Count(l => !l.Root && l.Mod.Environment != "server") > 18) return Unknown(roots, "Quilt search exceeds 18 optional candidates; static search is bounded.");
        if (choices.GroupBy(l => (l.Mod.Id, l.Mod.Version)).Any(g => g.Select(l => l.Mod.Sha256).Distinct().Count() > 1))
            return Unknown(roots, "Equal Quilt ID/version has different bytes; discovery-order tie preferences are not confirmed.");
        if (choices.Any(l => l.Mod.QuiltMetadata!.LoadType is not ("always" or "if_possible" or "if_required"))) return Unknown(roots, "Unsupported Quilt load_type.");
        var providers = new List<Provider> {
            new("minecraft", null, profile.MinecraftVersion, "*", -1),
            new("java", null, receipt?.Version == profile.MinecraftVersion && receipt.Loader == ModLoader.Quilt ? receipt.JavaMajor.ToString() : null, "*", -1),
            new("quilt_loader", null, receipt?.Version == profile.MinecraftVersion && receipt.Loader == ModLoader.Quilt ? receipt.LoaderVersion : null, "*", -1)
        };
        for (int i = 0; i < choices.Length; i++)
        {
            var m = choices[i].Mod; providers.Add(new(m.Id!, m.QuiltMetadata!.Group, m.Version, m.Environment, i));
            foreach (var alias in m.ProvidedMods ?? []) providers.Add(new(alias.Id, alias.Group ?? m.QuiltMetadata.Group, alias.Version, m.Environment, i));
        }
        if (providers.GroupBy(p => (p.Choice, p.Id)).Any(g => g.Select(p => (p.Group, p.Version)).Distinct().Count() > 1)) return Unknown(roots, "One Quilt candidate supplies contradictory versions/groups for the same ID.");
        foreach (var g in providers.GroupBy(p => p.Id))
            if (g.Select(p => p.Choice).Distinct().Count() > 1 && g.Any(p => p.Choice >= 0 && choices[p.Choice].Mod.Id != p.Id) &&
                !g.Any(p => p.Choice == -1 || choices[p.Choice].Root && p.Environment != "server"))
                return Unknown(roots, "Multiple optional providers of alias " + g.Key + ": alias weighting requires a separate verified adapter.");
        bool Unsupported(QuiltDependency n, bool conflict) => !n.Supported || conflict && n.Unless is not null ||
            (n.Children?.Any(c => Unsupported(c, conflict)) ?? false) || n.Unless is not null && Unsupported(n.Unless, false);
        if (choices.Any(l => l.Mod.QuiltMetadata!.Dependencies.Any(n => Unsupported(n, false)) || l.Mod.QuiltMetadata.Conflicts.Any(n => Unsupported(n, true))))
            return Unknown(roots, "Unsupported Quilt dependency extension or breaks/unless solver form; no Fabric rule substitution.");
        var weights = new int[choices.Length];
        foreach (var g in choices.Select((l, i) => (l, i)).GroupBy(p => p.l.Mod.Id))
        {
            var options = g.ToList(); bool unsupportedVersion = false;
            options.Sort((a, b) => {
                int? order = QuiltOrder(a.l.Mod.Version, b.l.Mod.Version);
                if (order is null) { unsupportedVersion = true; return StringComparer.Ordinal.Compare(a.l.Mod.Sha256, b.l.Mod.Sha256); }
                return order.Value;
            });
            if (unsupportedVersion) return Unknown(roots, "Unsupported Quilt version comparison in candidate preference order.");
            for (int i = 1; i < options.Count; i++)
                if (QuiltOrder(options[i - 1].l.Mod.Version, options[i].l.Mod.Version) == 0 && options[i - 1].l.Mod.Sha256 != options[i].l.Mod.Sha256)
                    return Unknown(roots, "Semantically equal Quilt candidate versions contain different bytes; discovery-order ties are not resolved statically.");
            for (int i = 0; i < options.Count; i++) weights[options[i].i] = (options[i].l.Mod.QuiltMetadata!.LoadType == "if_possible" ? -1000 : 1000) - i;
        }
        var selected = new bool[choices.Length]; bool[]? best = null; int bestWeight = int.MaxValue, bestCount = 0, nodes = 0, work = 0; bool unknownSeen = false, limit = false;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var always = choices.Where(l => l.Mod.Environment != "server" && (l.Root || l.Mod.QuiltMetadata!.LoadType == "always")).Select(l => l.Mod.Id!).ToHashSet(StringComparer.Ordinal);
        var occupied = new HashSet<string>(StringComparer.Ordinal);
        bool IsSelected(Provider p) => p.Choice < 0 || selected[p.Choice];
        ConstraintMatch Evaluate(QuiltDependency n, bool requirement)
        {
            if (++work > 200000) { limit = true; return ConstraintMatch.Unknown; }
            if (n.Children is { } children)
            {
                var states = children.Select(c => Evaluate(c, requirement)).ToArray();
                return n.All ? states.Contains(ConstraintMatch.Mismatch) ? ConstraintMatch.Mismatch : states.Contains(ConstraintMatch.Unknown) ? ConstraintMatch.Unknown : ConstraintMatch.Match : VersionConstraints.Or(states);
            }
            if (n.Id is null) return ConstraintMatch.Unknown;
            var parts = n.Id.Split(':');
            if (parts.Length == 2 && parts[^1] is "minecraft" or "java" or "quilt_loader") return ConstraintMatch.Unknown;
            var sameId = providers.Where(p => p.Id == parts[^1]).ToArray();
            var candidates = sameId.Where(p => parts.Length == 1 || p.Group == parts[0]).ToArray();
            ConstraintMatch Match(Provider p)
            { if (++work > 200000) { limit = true; return ConstraintMatch.Unknown; } return p.Version is null ? ConstraintMatch.Unknown : QuiltVersionConstraints.Match(p.Version, n.VersionsJson); }
            var valid = candidates.Where(p => Match(p) == ConstraintMatch.Match).ToArray();
            ConstraintMatch result;
            if (candidates.Any(p => Match(p) == ConstraintMatch.Unknown)) result = ConstraintMatch.Unknown;
            else if (requirement && valid.Length > 0 && valid.All(p => p.Environment == "server")) result = ConstraintMatch.Match; // QuiltRuleDepOnly allWrongEnvironment
            else if (requirement && n.Optional && valid.Length == 0) result = sameId.Any(p => IsSelected(p) && p.Environment != "server") ? ConstraintMatch.Mismatch : ConstraintMatch.Match;
            else result = valid.Any(p => IsSelected(p) && p.Environment != "server") ? ConstraintMatch.Match : ConstraintMatch.Mismatch;
            if (n.Unless is not null)
            {
                var exception = Evaluate(n.Unless, false);
                if (exception == ConstraintMatch.Match) return requirement ? ConstraintMatch.Match : ConstraintMatch.Mismatch;
                if (exception == ConstraintMatch.Unknown) return ConstraintMatch.Unknown;
            }
            return result;
        }
        void Search(int index, int weight)
        {
            ct.ThrowIfCancellationRequested();
            if (limit || ++nodes > 65536 || System.Diagnostics.Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(2)) { limit = true; return; }
            if (index == choices.Length)
            {
                if (always.Any(id => !providers.Any(p => p.Id == id && IsSelected(p) && p.Environment != "server"))) return;
                bool unknown = false;
                for (int i = 0; i < choices.Length; i++) if (selected[i])
                {
                    var m = choices[i].Mod.QuiltMetadata!;
                    foreach (var d in m.Dependencies) { var match = Evaluate(d, true); if (match == ConstraintMatch.Mismatch) return; unknown |= match == ConstraintMatch.Unknown; }
                    foreach (var b in m.Conflicts) { var match = Evaluate(b, false); if (match == ConstraintMatch.Match) return; unknown |= match == ConstraintMatch.Unknown; }
                }
                if (unknown) { unknownSeen = true; return; }
                if (weight < bestWeight) { bestWeight = weight; best = (bool[])selected.Clone(); bestCount = 1; }
                else if (weight == bestWeight) bestCount++;
                return;
            }
            var c = choices[index];
            if (c.Mod.Environment == "server") { Search(index + 1, weight); return; }
            if (!c.Root) Search(index + 1, weight);
            if (limit) return;
            var ids = providers.Where(p => p.Choice == index).Select(p => p.Id).Distinct(StringComparer.Ordinal).ToArray();
            if (ids.Any(id => occupied.Contains(id) || providers.Any(p => p.Choice == -1 && p.Id == id))) return;
            foreach (var id in ids) occupied.Add(id); selected[index] = true;
            Search(index + 1, weight + weights[index]); selected[index] = false; foreach (var id in ids) occupied.Remove(id);
        }
        Search(0, 0);
        if (limit) return Unknown(roots, "Quilt search exceeded 65,536 states, 200,000 work steps or two seconds; no partial solution is reported ready.");
        if (unknownSeen) return Unknown(roots, "At least one feasible Quilt assignment contains an unsupported/unknown dependency or runtime version.");
        if (best is null)
        {
            var requirements = choices.Select(l => l.Mod.Id + ": depends=" + string.Join(",", l.Mod.QuiltMetadata!.Dependencies.Select(d => d.Id ?? "dependency group")) +
                "; breaks=" + string.Join(",", l.Mod.QuiltMetadata.Conflicts.Select(d => d.Id ?? "conflict group")));
            var detail = string.Join(" | ", requirements); if (detail.Length > 2048) detail = detail[..2048];
            return new(roots, [], [new("mods.nested.unsatisfied", "No valid Quilt assignment for required roots/always candidates, identities or constraints. " + detail)], []);
        }
        if (bestCount != 1) return Unknown(roots, "Multiple minimum-weight Quilt assignments remain; no discovery-order guess.");
        var active = choices.Where((_, i) => best[i]).Select(l => l.Mod with { FileName = l.Origin, Nested = null }).ToArray();
        var artifacts = choices.Where((l, i) => best[i] && !l.Root).Select(l => new SelectedNestedArtifact(l.Mod.QuiltMetadata!.Group + ":" + l.Mod.Id, l.Mod.Version!, l.Mod.Sha256, l.Origin, false)).ToArray();
        return new(active, artifacts, [], []);
    }
    private static int? QuiltOrder(string? a, string? b)
    {
        if (a is null || b is null) return null; if (a == b) return 0;
        bool Semantic(string s) => Regex.IsMatch(s, @"^\d+(?:\.\d+)*(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!Semantic(a) || !Semantic(b)) return null; // Generic-version classification needs a separate parser contract.
        return QuiltSemanticOrder.Compare(a, b);
    }
}
