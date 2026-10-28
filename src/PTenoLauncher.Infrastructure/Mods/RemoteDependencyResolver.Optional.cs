using PTenoLauncher.Core;
namespace PTenoLauncher.Infrastructure.Mods;

public sealed partial class RemoteDependencyResolver
{
    /// <summary>Only explicitly selected, reachable optional edges become roots. No file-name or mod-ID guessing.</summary>
    public async Task<DependencyResolution> ResolveWithOptionalAsync(GameProfile profile, IReadOnlyList<RemoteModRequest> roots,
        IReadOnlyList<InstalledCatalogMod> installed, IReadOnlyList<OptionalDependencyKey> selections, CancellationToken ct = default)
    {
        if(selections.Count>128) return new([],[],["Перевищено ліміт 128 необов'язкових компонентів."],[]);
        var result=await ResolveAsync(profile,roots,installed,ct);
        if(!result.CanInstall || selections.Count==0) return result;
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
}
