using PTenoLauncher.Core;
namespace PTenoLauncher.Infrastructure.Mods;

public sealed partial class RemoteDependencyResolver
{
    /// <summary>Only explicitly selected, reachable optional edges become roots. No file-name or mod-ID guessing.</summary>
    public async Task<DependencyResolution> ResolveWithOptionalAsync(GameProfile profile, IReadOnlyList<RemoteModRequest> roots,
        IReadOnlyList<InstalledCatalogMod> installed, IReadOnlyList<OptionalDependencyKey> selections, CancellationToken ct = default,
        bool automatic = false)
    {
        if(selections.Count>128) return new([],[],["Перевищено ліміт 128 необов'язкових компонентів."],[]);
        var result=await ResolveAsync(profile,roots,installed,ct);
        if (!result.CanInstall) return result;
        if (automatic) return await ResolveAutomaticOptionalAsync(profile, roots, installed, result, ct);
        if (selections.Count == 0) return result;
        var wanted=selections.ToHashSet(); var applied=new HashSet<OptionalDependencyKey>();
        var additions=new List<RemoteModRequest>();
        while(result.CanInstall)
        {
            ct.ThrowIfCancellationRequested();
            var eligible=result.Optional.Where(o=>wanted.Contains(o.Key) && !applied.Contains(o.Key)).DistinctBy(o=>o.Key).ToArray();
            if(eligible.Length==0)break;
            foreach(var option in eligible)
            {
                applied.Add(option.Key);
                if(!option.CanSelect)
                    return result with {Blockers=["Необов'язковий компонент має лише ім'я файла; виберіть підтверджений проєкт вручну."],SelectedOptional=applied.ToArray()};
                try
                {
                    var project=option.Dependency.ProjectId ?? (await catalog.GetVersionAsync(option.Dependency.VersionId!,ct)).ProjectId;
                    // Bind the chosen edge to the exact parent version whose metadata declared it.
                    additions.Add(new(option.ParentProjectId!,option.ParentVersionId,"Версія батьківського мода для вибраного необов'язкового компонента"));
                    additions.Add(new(project,option.Dependency.VersionId,"Необов'язковий компонент для " + option.Parent));
                }
                catch(Exception ex) when(ex is InvalidDataException or HttpRequestException)
                { return result with {Blockers=[ex.Message],SelectedOptional=applied.ToArray()}; }
            }
            var next=await ResolveAsync(profile,roots.Concat(additions).Distinct().ToArray(),installed,ct);
            if(!next.CanInstall)
                return next with {Optional=result.Optional,SelectedOptional=applied.ToArray()}; // Keep known choices so the conflict can be unchecked.
            result=next;
        }
        var warnings=result.Warnings.ToList();
        if(wanted.Except(applied).Any())warnings.Add("Недоступні або від'єднані необов'язкові компоненти вилучено з вибору; встановлені файли не видаляються.");
        return result with {SelectedOptional=applied.ToArray(),Warnings=warnings};
    }

    private async Task<DependencyResolution> ResolveAutomaticOptionalAsync(GameProfile profile, IReadOnlyList<RemoteModRequest> roots,
        IReadOnlyList<InstalledCatalogMod> installed, DependencyResolution result, CancellationToken ct)
    {
        var tried = new HashSet<OptionalDependencyKey>();
        var applied = new List<OptionalDependencyKey>();
        var additions = new List<RemoteModRequest>();
        var notices = new List<string>();
        while (result.Optional.FirstOrDefault(option => !tried.Contains(option.Key)) is { } option && tried.Count < 128)
        {
            ct.ThrowIfCancellationRequested();
            tried.Add(option.Key);
            var dependency = option.Dependency;
            var label = dependency.FileName ?? dependency.ProjectId ?? dependency.VersionId ?? "?";
            var requestCount = roots.Count + additions.Count;
            if (!option.CanSelect || requestCount >= 128)
            {
                notices.Add($"{option.Parent}: {label} — optional dependency cannot be resolved from the catalog.");
                continue;
            }
            string project;
            try { project = dependency.ProjectId ?? (await catalog.GetVersionAsync(dependency.VersionId!, ct)).ProjectId; }
            catch (Exception error) when (error is InvalidDataException or HttpRequestException or IOException)
            {
                notices.Add($"{option.Parent}: {label} — {error.Message}");
                continue;
            }
            var request = new RemoteModRequest(project, dependency.VersionId, "Optional dependency of " + option.Parent);
            var prior = result.Entries.ToDictionary(entry => entry.Version.ProjectId, entry => entry.Version.Id, StringComparer.Ordinal);
            var next = await ResolveAsync(profile, roots.Concat(additions).Append(request).ToArray(), installed, ct, prior);
            if (!next.CanInstall || next.Entries.Any(entry =>
                prior.TryGetValue(entry.Version.ProjectId, out var version) && version != entry.Version.Id))
            {
                notices.Add($"{option.Parent}: {label} — {next.Blockers.FirstOrDefault() ?? "optional dependency conflicts with the selected versions."}");
                continue;
            }
            result = next;
            additions.Add(request);
            applied.Add(option.Key);
        }
        if (result.Optional.Any(option => !tried.Contains(option.Key)))
            notices.Add("The optional dependency review reached its limit.");
        return result with { SelectedOptional = applied, Warnings = result.Warnings.Concat(notices).Distinct().ToArray() };
    }
}
