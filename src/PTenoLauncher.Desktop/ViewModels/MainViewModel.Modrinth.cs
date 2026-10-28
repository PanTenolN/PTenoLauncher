using System.Collections.ObjectModel;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
namespace PTenoLauncher.Desktop.ViewModels;
public sealed partial class MainViewModel
{
    public ObservableCollection<CatalogSearchHit> RemoteSearchResults { get; } = [];
    public ObservableCollection<CatalogModVersion> RemoteVersions { get; } = [];
    public ObservableCollection<ModPlanEntry> RemoteEntries { get; } = [];
    public ObservableCollection<OptionalDependencyChoice> OptionalChoices { get; } = [];
    public bool HasOptionalChoices => OptionalChoices.Count > 0;
    private int optionalRevision;
    private readonly List<RemoteModRequest> remoteRoots = [];
    private LocalDependencyPlan? combinedPlan;
    public bool HasLocalMods => Cart.Count > 0;
    public bool NeedsUnknownAcceptance => combinedPlan?.RequiresUnknownAcceptance ?? HasLocalMods;
    private string remoteQuery = "", remoteSummary = "";
    private bool remoteConfirmed;
    private CatalogSearchHit? remoteProject;
    private CatalogModVersion? remoteVersion;
    public string RemoteQuery { get => remoteQuery; set => Set(ref remoteQuery, value); }
    public string RemoteSummary { get => remoteSummary; set => Set(ref remoteSummary, value); }
    public bool RemoteConfirmed { get => remoteConfirmed; set => Set(ref remoteConfirmed, value); }
    public CatalogSearchHit? SelectedRemoteProject
    {
        get => remoteProject;
        set { if (Set(ref remoteProject, value)) { RemoteVersions.Clear(); SelectedRemoteVersion = null; } }
    }
    public CatalogModVersion? SelectedRemoteVersion { get => remoteVersion; set => Set(ref remoteVersion, value); }
    public void ClearRemotePlan()
    {
        remoteRoots.Clear(); InvalidateModPlan(); RemoteSummary = "";
    }
    public Task SearchRemoteAsync() => RunAsync(async ct =>
    {
        var profile = RequireProfile();
        services.Modrinth.BeginOperation();
        var hits = await services.Modrinth.SearchAsync(RemoteQuery, profile.MinecraftVersion, profile.Loader, ct);
        RemoteSearchResults.Clear(); foreach (var hit in hits) RemoteSearchResults.Add(hit);
        SelectedRemoteProject = RemoteSearchResults.FirstOrDefault();
        Status = $"Modrinth: {hits.Count} результатів" + (services.Modrinth.UsedCache ? " (кеш)." : ".");
    });
    public Task LoadRemoteVersionsAsync() => RunAsync(async ct =>
    {
        var profile = RequireProfile(); var project = SelectedRemoteProject ?? throw new InvalidOperationException("Виберіть мод.");
        var versions = await services.Modrinth.GetVersionsAsync(project.Id, profile.MinecraftVersion, profile.Loader, ct);
        RemoteVersions.Clear();
        foreach (var version in versions.OrderBy(v => v.Channel == "release" ? 0 : v.Channel == "beta" ? 1 : 2).ThenByDescending(v => v.Published).ThenBy(v => v.Id, StringComparer.Ordinal)) RemoteVersions.Add(version);
        SelectedRemoteVersion = RemoteVersions.FirstOrDefault();
        Status = $"Сумісних версій: {RemoteVersions.Count}.";
    });
    public void InvalidateModPlan()
    {
        combinedPlan = null; RemoteConfirmed = false; UnknownSourceAccepted = false; RemoteEntries.Clear(); RemoteSummary = "";
        OptionalChoices.Clear(); optionalRevision++; Changed(nameof(HasOptionalChoices));
        Changed(nameof(HasLocalMods)); Changed(nameof(NeedsUnknownAcceptance));
    }
    private void OptionalSelectionChanged(OptionalDependencyChoice choice)
    {
        if(!OptionalChoices.Contains(choice))return;
        combinedPlan=null;RemoteConfirmed=false;UnknownSourceAccepted=false;RemoteEntries.Clear();optionalRevision++;
        RemoteSummary="Вибір необов'язкових компонентів змінено. Застосуйте його й перевірте новий кошик.";
    }
    public Task ClearOptionalAsync() => RunAsync(async ct =>
    {
        foreach(var choice in OptionalChoices)choice.IsSelected=false;
        await ResolveCombinedAsync(ct);
    });
    public void ClearInstallationCart() { Cart.Clear(); ClearRemotePlan(); CartSummary = "Кошик порожній."; UnknownSourceAccepted = false; }
    public Task ResolveCartAsync() => RunAsync(ResolveCombinedAsync);
    private async Task ResolveCombinedAsync(CancellationToken ct)
    {
        var profile = RequireProfile(); var inputs = Cart.ToArray(); var roots = remoteRoots.ToArray();
        var selectedOptions=OptionalChoices.Where(o=>o.IsSelected).Select(o=>o.Option.Key).ToArray();var revision=optionalRevision;
        combinedPlan=null;RemoteConfirmed=false;UnknownSourceAccepted=false;
        var plan = await services.RemoteMods.PreviewLocalAsync(profile, inputs, roots, ct, selectedOptions);
        if (SelectedProfile?.Id != profile.Id || !inputs.SequenceEqual(Cart) || revision != optionalRevision)
        { InvalidateModPlan(); Status = "Профіль або кошик змінено; план відхилено."; return; }
        combinedPlan = plan; RemoteConfirmed = false; UnknownSourceAccepted = false;
        RemoteEntries.Clear(); foreach (var entry in plan.Remote.Resolution.Entries) RemoteEntries.Add(entry);
        var activeOptions=(plan.Remote.Resolution.SelectedOptional ?? []).ToHashSet();
        OptionalChoices.Clear();foreach(var option in plan.Remote.Resolution.Optional.DistinctBy(o=>o.Key))OptionalChoices.Add(new(option,activeOptions.Contains(option.Key),OptionalSelectionChanged));
        Changed(nameof(HasOptionalChoices));
        CartSummary = string.Join(Environment.NewLine, plan.LocalFiles.Select(f =>
            (f.Item.Inspection.Name ?? f.Item.Inspection.FileName) + " " + f.Item.Inspection.Version + " · " +
            (f.Identity is null ? "Локальний JAR / Unknown Source" : "Локальний JAR / Modrinth SHA512") +
            " · " + (f.Item.Inspection.Size / 1048576.0).ToString("0.##") + " MB"));
        RemoteSummary = string.Join(Environment.NewLine, new[] {
            "Локальних файлів: " + plan.LocalFiles.Count + "; каталог: " + plan.Remote.Resolution.Entries.Count + "; завантаження: " + (plan.DownloadBytes / 1048576.0).ToString("0.##") + " MB.",
            plan.Remote.Resolution.Entries.Count == 0 ? "Використано лише локальні метадані; зовнішні залежності не підтверджені." : plan.Remote.CachedMetadata ? "Використано кешовані метадані; новіші версії не підтверджені." : "Використано доступні метадані Modrinth."
        }.Concat(plan.Blockers.Concat(plan.Remote.Resolution.Blockers).Select(b => "Блокування: " + b))
         .Concat(plan.Warnings.Concat(plan.Remote.Resolution.Warnings).Select(w => "Попередження: " + w)));
        Changed(nameof(NeedsUnknownAcceptance));
        Status = plan.CanInstall ? "Спільний кошик розраховано. Перегляньте всі файли й підтвердьте встановлення." : "Кошик містить помилки; встановлення заблоковано.";
    }
    public Task AddRemoteAsync() => RunAsync(async ct =>
    {
        var version = SelectedRemoteVersion ?? throw new InvalidOperationException("Спочатку виберіть версію мода.");
        remoteRoots.RemoveAll(r => r.ProjectId == version.ProjectId);
        remoteRoots.Add(new(version.ProjectId, version.Id)); InvalidateModPlan();
        await ResolveCombinedAsync(ct);
    });
    private async Task InstallCombinedAsync(CancellationToken ct)
    {
        var profile = RequireProfile(); var plan = combinedPlan ?? throw new InvalidOperationException("Спочатку розрахуйте спільний кошик кнопкою «Знайти залежності».");
        var progress = new Progress<DownloadProgress>(p => Status = "Моди: " + p.Name + " — " + (p.Received / 1024).ToString("N0") + " / " + (p.Total / 1024)?.ToString("N0") + " KB");
        await services.RemoteMods.InstallLocalAsync(profile, plan, RemoteConfirmed, UnknownSourceAccepted, progress, ct);
        ClearInstallationCart();
        Status = "Спільний кошик встановлено однією транзакцією. Для гри потрібні встановлений Mod Loader, Java й підтверджений акаунт.";
    }
    public Task InstallRemoteAsync() => RunAsync(InstallCombinedAsync);
}
