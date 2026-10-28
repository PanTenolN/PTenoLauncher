using PTenoLauncher.Core;

namespace PTenoLauncher.Infrastructure.Mods;

/// <summary>Deterministic bounded backtracking over real project/version dependencies. No AI.</summary>
public sealed partial class RemoteDependencyResolver(IModCatalog catalog)
{
    private sealed class SearchLimitException(string message) : Exception(message);
    private sealed record Need(string? Project, string? Version, string Reason);
    private sealed record Choice(CatalogModVersion Version, IReadOnlyList<string> Reasons);
    public async Task<DependencyResolution> ResolveAsync(GameProfile profile, IReadOnlyList<RemoteModRequest> roots,
        IReadOnlyList<InstalledCatalogMod> installed, CancellationToken ct = default)
    {
        if (profile.Loader == ModLoader.Vanilla) return new([], [], ["Для модів потрібен Mod Loader."], []);
        if (roots.Count is < 1 or > 128) return new([], [], ["Виберіть від 1 до 128 модів."], []);
        var duplicates = installed.GroupBy(i => i.Version.ProjectId).Where(g => g.Count() > 1).ToArray();
        if (duplicates.Length != 0) return new([], [], duplicates.Select(g => "Дублікати встановленого Modrinth-проєкту: " + g.Key).ToArray(), []);
        var existing = installed.ToDictionary(i => i.Version.ProjectId, i => i.Version, StringComparer.Ordinal);
        var projects = new Dictionary<string, CatalogProject>(StringComparer.Ordinal);
        var lists = new Dictionary<string, IReadOnlyList<CatalogModVersion>>(StringComparer.Ordinal);
        var exact = new Dictionary<string, CatalogModVersion>(StringComparer.Ordinal);
        var failures = new List<string>();
        int steps = 0;
        string loader = ModrinthClient.LoaderName(profile.Loader);
        async Task<CatalogModVersion> Version(string id)
        {
            if (!exact.TryGetValue(id, out var v)) exact[id] = v = await catalog.GetVersionAsync(id, ct);
            return v;
        }
        async Task<CatalogProject> Project(string id)
        {
            if (!projects.TryGetValue(id, out var p)) { p = await catalog.GetProjectAsync(id, ct); projects[id] = p; projects[p.Id] = p; }
            return p;
        }
        bool Compatible(CatalogModVersion v) => v.GameVersions.Contains(profile.MinecraftVersion, StringComparer.Ordinal) &&
            v.Loaders.Contains(loader, StringComparer.Ordinal);
        static bool Forbids(CatalogModVersion a, CatalogModVersion b) => a.Dependencies.Any(d =>
            d.Kind == "incompatible" && (d.VersionId is not null ? d.VersionId == b.Id : d.ProjectId is not null && d.ProjectId == b.ProjectId));
        void Fail(string message) { if (failures.Count < 40 && !failures.Contains(message)) failures.Add(message); }
        async Task<Dictionary<string, Choice>?> Search(List<Need> pending, Dictionary<string, Choice> chosen, int depth)
        {
            ct.ThrowIfCancellationRequested();
            if (++steps > 4096 || depth > 512 || chosen.Count > 128)
                throw new SearchLimitException("Досягнуто безпечного ліміту пошуку залежностей. Зменште кошик.");
            if (pending.Count == 0) return chosen;
            var need = pending[0]; var rest = pending.Skip(1).ToList();
            if (need.Project is null && need.Version is null) { Fail("Залежність має лише ім'я файла; її неможливо достовірно підібрати: " + need.Reason); return null; }
            CatalogModVersion? pinned = need.Version is null ? null : await Version(need.Version);
            var project = await Project(need.Project ?? pinned!.ProjectId);
            if (pinned is not null && pinned.ProjectId != project.Id) { Fail("Невідповідний проєкт залежності: " + project.Title); return null; }
            if (!project.SupportsClient) { Fail("Мод не підтримує клієнт Minecraft: " + project.Title); return null; }
            if (chosen.TryGetValue(project.Id, out var selected))
            {
                if (pinned is not null && selected.Version.Id != pinned.Id)
                { Fail($"Конфлікт точних версій {project.Title}: {selected.Version.Number} / {pinned.Number}"); return null; }
                var updated = new Dictionary<string, Choice>(chosen, StringComparer.Ordinal)
                { [project.Id] = selected with { Reasons = selected.Reasons.Append(need.Reason).Distinct().ToArray() } };
                return await Search(rest, updated, depth + 1);
            }
            IReadOnlyList<CatalogModVersion> candidates;
            if (existing.TryGetValue(project.Id, out var present))
            {
                if (pinned is not null && present.Id != pinned.Id)
                { Fail($"Встановлено іншу версію {project.Title}: {present.Number}. Автоматична заміна заборонена."); return null; }
                candidates = [present];
            }
            else if (pinned is not null) candidates = [pinned];
            else
            {
                if (!lists.TryGetValue(project.Id, out candidates!))
                    lists[project.Id] = candidates = await catalog.GetVersionsAsync(project.Id, profile.MinecraftVersion, profile.Loader, ct);
            }
            var compatible = candidates.Where(v => v.ProjectId == project.Id && Compatible(v))
                .OrderBy(v => v.Channel == "release" ? 0 : v.Channel == "beta" ? 1 : 2)
                .ThenByDescending(v => v.Published).ThenBy(v => v.Id, StringComparer.Ordinal).ToArray();
            if (compatible.Length == 0) { Fail($"Немає сумісної версії {project.Title} для {profile.MinecraftVersion} / {loader}."); return null; }
            foreach (var candidate in compatible)
            {
                if (chosen.Values.Select(c => c.Version).Concat(existing.Values).Any(other =>
                    other.ProjectId != project.Id && (Forbids(candidate, other) || Forbids(other, candidate))))
                { Fail("Несумісність Modrinth: " + project.Title + " " + candidate.Number); continue; }
                var next = new Dictionary<string, Choice>(chosen, StringComparer.Ordinal)
                { [project.Id] = new(candidate, [need.Reason]) };
                var requirements = candidate.Dependencies.Where(d => d.Kind == "required")
                    .OrderBy(d => d.ProjectId ?? d.VersionId ?? d.FileName, StringComparer.Ordinal)
                    .Select(d => new Need(d.ProjectId, d.VersionId, $"Обов'язкова залежність {project.Title} {candidate.Number}")).ToList();
                requirements.AddRange(rest);
                Dictionary<string, Choice>? solved;
                try { solved = await Search(requirements, next, depth + 1); }
                catch (Exception ex) when (ex is InvalidDataException or HttpRequestException) { Fail(ex.Message); continue; }
                if (solved is not null) return solved;
            }
            return null;
        }
        Dictionary<string, Choice>? solution;
        try
        {
            solution = await Search(roots.Select(r => new Need(r.ProjectId, r.VersionId, r.Reason ?? "Вибрано користувачем")).ToList(),
                new(StringComparer.Ordinal), 0);
        }
        catch (Exception ex) when (ex is InvalidDataException or HttpRequestException or SearchLimitException)
        { Fail(ex.Message); solution = null; }
        if (solution is null) return new([], [], failures.Count == 0 ? ["Сумісний набір залежностей не знайдено."] : failures, []);
        var entries = solution.Select(k => new ModPlanEntry(k.Value.Version, projects[k.Key].Title,
            string.Join("; ", k.Value.Reasons), existing.ContainsKey(k.Key))).OrderBy(e => e.Title, StringComparer.Ordinal).ToArray();
        var optional = entries.SelectMany(e => e.Version.Dependencies.Where(d => d.Kind == "optional")
            .Select(d => new OptionalModDependency(e.Title, d, e.Version.ProjectId, e.Version.Id))).ToArray();
        var embedded = entries.Where(e => e.Version.Dependencies.Any(d => d.Kind == "embedded"))
            .Select(e => e.Title + ": вбудовані залежності окремо не завантажуються.").ToArray();
        return new(entries, optional, [], embedded);
    }
}
