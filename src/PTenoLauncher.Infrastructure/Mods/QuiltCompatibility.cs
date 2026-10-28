using PTenoLauncher.Core;
namespace PTenoLauncher.Infrastructure.Mods;

/// <summary>Read-only checks of a fixed installed/cart set, not a replacement for Quilt's SAT solver.</summary>
internal static class QuiltCompatibility
{
    internal static IReadOnlyList<ReadinessIssue> Check(GameProfile profile, InstallationReceipt? receipt, IEnumerable<ModInspection> inspections)
    {
        var issues = new List<ReadinessIssue>();
        var versions = new Dictionary<string, string?>(StringComparer.Ordinal) { ["minecraft"] = profile.MinecraftVersion };
        if (receipt is not null && receipt.Version == profile.MinecraftVersion && receipt.Loader == ModLoader.Quilt)
        { versions["java"] = receipt.JavaMajor.ToString(); versions["quilt_loader"] = receipt.LoaderVersion; }
        var active = inspections.Where(m => m.Loader == ModLoader.Quilt && m.Environment is "*" or "client" && m.QuiltMetadata is not null).ToArray();
        var roots = new HashSet<string>(versions.Keys, StringComparer.Ordinal);
        void Supply(string id, string? version)
        {
            if (versions.TryGetValue(id, out var prior) && prior != version)
            { versions[id] = null; issues.Add(new("mods.selection.unknown", id + ": неоднозначний вибір версії Quilt.")); }
            else if (!versions.ContainsKey(id)) versions[id] = version;
        }
        foreach (var mod in active)
        {
            if (mod.Id is not null)
            {
                if (!roots.Add(mod.Id)) issues.Add(new("mods.duplicate", mod.FileName + ": повторний Mod ID " + mod.Id));
                Supply(mod.Id, mod.Version); Supply(mod.QuiltMetadata!.Group + ":" + mod.Id, mod.Version);
            }
            foreach (var alias in mod.ProvidedMods ?? [])
            { Supply(alias.Id, alias.Version); if (alias.Group is not null) Supply(alias.Group + ":" + alias.Id, alias.Version); }
        }
        foreach (var mod in active)
        {
            foreach (var dependency in mod.QuiltMetadata!.Dependencies)
            {
                var match = Evaluate(dependency, versions, true);
                var label = Label(dependency);
                if (match == ConstraintMatch.Mismatch)
                    issues.Add(new(Ids(dependency).Any(id => !versions.ContainsKey(id)) ? "mods.dependency.missing" : "mods.dependency.version",
                        mod.FileName + ": не виконано залежність Quilt " + label));
                else if (match == ConstraintMatch.Unknown) issues.Add(new("mods.constraint.unknown", mod.FileName + ": невідома залежність Quilt " + label));
            }
            foreach (var conflict in mod.QuiltMetadata.Conflicts)
            {
                var match = Evaluate(conflict, versions, false);
                if (match == ConstraintMatch.Match) issues.Add(new("mods.conflict", mod.FileName + ": конфлікт Quilt " + Label(conflict)));
                else if (match == ConstraintMatch.Unknown) issues.Add(new("mods.constraint.unknown", mod.FileName + ": невідомий конфлікт Quilt " + Label(conflict)));
            }
        }
        return issues.Distinct().ToArray();
    }
    private static ConstraintMatch Evaluate(QuiltDependency node, IReadOnlyDictionary<string, string?> versions, bool requirement)
    {
        if (!node.Supported) return ConstraintMatch.Unknown;
        if (node.Children is { } children)
        {
            var matches = children.Select(c => Evaluate(c, versions, requirement)).ToArray();
            if (matches.Length == 0) return ConstraintMatch.Unknown;
            return node.All ? matches.Contains(ConstraintMatch.Mismatch) ? ConstraintMatch.Mismatch : matches.Contains(ConstraintMatch.Unknown) ? ConstraintMatch.Unknown : ConstraintMatch.Match
                : VersionConstraints.Or(matches);
        }
        if (node.Id is null) return ConstraintMatch.Unknown;
        var present = versions.TryGetValue(node.Id, out var actual);
        var match = !present ? node.Optional && requirement ? ConstraintMatch.Match : ConstraintMatch.Mismatch
            : actual is null ? ConstraintMatch.Unknown : QuiltVersionConstraints.Match(actual, node.VersionsJson);
        if (node.Unless is not null)
        {
            var exception = Evaluate(node.Unless, versions, false);
            if (exception == ConstraintMatch.Match) return requirement ? ConstraintMatch.Match : ConstraintMatch.Mismatch;
            if (exception == ConstraintMatch.Unknown && match != (requirement ? ConstraintMatch.Match : ConstraintMatch.Mismatch)) return ConstraintMatch.Unknown;
        }
        return match;
    }
    internal static IEnumerable<string> Ids(QuiltDependency node) => node.Children is { } children ? children.SelectMany(Ids)
        : node.Id is not null ? [node.Id] : [];
    private static string Label(QuiltDependency node) => node.Children is { } children
        ? "(" + string.Join(node.All ? " AND " : " OR ", children.Select(Label)) + ")" : node.Id + " " + node.VersionsJson;
}
