using PTenoLauncher.Core;
namespace PTenoLauncher.Infrastructure.Mods;

internal sealed record ForgeCompatibilityReport(IReadOnlyList<ReadinessIssue> Issues, IReadOnlyList<string> Warnings);
internal static class ForgeCompatibility
{
    internal static ForgeCompatibilityReport Check(GameProfile profile, InstallationReceipt? receipt, IEnumerable<ModInspection> inspections)
    {
        var issues = new List<ReadinessIssue>(); var warnings = new List<string>();
        var active = inspections.Where(m => m.Loader == profile.Loader && m.ForgeMetadata is not null).ToArray();
        if (active.Sum(m => m.ForgeMetadata!.Mods.Count) > 4096 || active.Sum(m => m.ForgeMetadata!.Mods.Sum(d => d.Dependencies.Count)) > 8192)
            return new([new("mods.limit.unknown", "Перевищено ліміт статичної перевірки Forge/NeoForge залежностей.")], []);
        var versions = new Dictionary<string, string?>(StringComparer.Ordinal) { ["minecraft"] = profile.MinecraftVersion, ["java"] = null };
        string builtin = profile.Loader == ModLoader.Forge ? "forge" : "neoforge"; versions[builtin] = null;
        bool receiptMatches = receipt is not null && receipt.Version == profile.MinecraftVersion && receipt.Loader == profile.Loader;
        if (receiptMatches) { versions["java"] = receipt!.JavaMajor.ToString(); versions[builtin] = receipt.LoaderVersion; }
        var roots = new HashSet<string>(versions.Keys, StringComparer.Ordinal);
        foreach (var file in active)
            foreach (var mod in file.ForgeMetadata!.Mods)
            {
                if (!roots.Add(mod.Id)) issues.Add(new("mods.duplicate", file.FileName + ": повторний Mod ID " + mod.Id));
                if (versions.TryGetValue(mod.Id, out var prior) && prior != mod.Version) versions[mod.Id] = null;
                else versions[mod.Id] = mod.Version;
            }
        var graph = active.SelectMany(f => f.ForgeMetadata!.Mods).Select(m => m.Id).Distinct(StringComparer.Ordinal)
            .ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var file in active)
        {
            var metadata = file.ForgeMetadata!;
            string? languageVersion = null;
            if (profile.Loader == ModLoader.Forge && metadata.LanguageLoader is "javafml" or "lowcodefml" && versions[builtin] is { } forgeVersion)
                languageVersion = forgeVersion.Split('.')[0]; // Official Forge FML uses the Forge major, not its full version.
            if (languageVersion is null || metadata.LanguageLoader is not ("javafml" or "lowcodefml"))
                issues.Add(new("mods.language.unknown", file.FileName + ": версію FML language loader неможливо підтвердити; NeoForge FML має незалежну версію."));
            else if (MavenVersionConstraints.Match(languageVersion, metadata.LanguageRange) is var languageMatch && languageMatch != ConstraintMatch.Match)
                issues.Add(new(languageMatch == ConstraintMatch.Unknown ? "mods.constraint.unknown" : "mods.runtime.version", file.FileName + ": FML " + languageVersion + " не підтверджує " + metadata.LanguageRange));
            foreach (var mod in metadata.Mods)
            {
                foreach (var feature in mod.Features.Where(f => f.Key == "java"))
                    CheckRange("java", feature.Value, "required", mod.Id);
                foreach (var dependency in mod.Dependencies.Where(d => d.Side is "BOTH" or "CLIENT"))
                {
                    CheckRange(dependency.Id, dependency.Range, dependency.Kind, mod.Id);
                    if (dependency.Kind is "required" or "optional" && graph.ContainsKey(dependency.Id) &&
                        versions.TryGetValue(dependency.Id, out var version) && MavenVersionConstraints.Match(version, dependency.Range) == ConstraintMatch.Match)
                    {
                        if (dependency.Ordering == "BEFORE") graph[mod.Id].Add(dependency.Id);
                        if (dependency.Ordering == "AFTER") graph[dependency.Id].Add(mod.Id);
                    }
                }
            }
        }
        void CheckRange(string id, string range, string kind, string owner)
        {
            string context = owner + ": " + id + " " + range;
            bool present = versions.TryGetValue(id, out var actual);
            if (!present)
            { if (kind == "required") issues.Add(new("mods.dependency.missing", "Відсутня залежність " + context)); return; }
            var match = MavenVersionConstraints.Match(actual, range);
            if (match == ConstraintMatch.Unknown) { issues.Add(new("mods.constraint.unknown", "Невідоме обмеження Maven " + context)); return; }
            if (kind is "required" or "optional" && match == ConstraintMatch.Mismatch)
                issues.Add(new(id is "minecraft" or "java" || id == builtin ? "mods.runtime.version" : "mods.dependency.version", "Несумісна версія " + context));
            if (kind == "incompatible" && match == ConstraintMatch.Match) issues.Add(new("mods.conflict", "Конфлікт " + context));
            if (kind == "discouraged" && match == ConstraintMatch.Match) warnings.Add("Небажане поєднання " + context);
        }
        var indegree = graph.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        foreach (var next in graph.Values.SelectMany(x => x)) indegree[next]++;
        var ready = new Queue<string>(indegree.Where(p => p.Value == 0).Select(p => p.Key)); int visited = 0;
        while (ready.TryDequeue(out var id)) { visited++; foreach (var next in graph[id]) if (--indegree[next] == 0) ready.Enqueue(next); }
        if (visited != graph.Count) issues.Add(new("mods.ordering.cycle", "Цикл BEFORE/AFTER у порядку завантаження Forge/NeoForge модів."));
        return new(issues.Distinct().ToArray(), warnings.Distinct().ToArray());
    }
}
