using PTenoLauncher.Desktop.Localization;
using System.Collections.ObjectModel;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
namespace PTenoLauncher.Desktop.ViewModels;
public sealed partial class MainViewModel
{
    public ObservableCollection<CatalogSearchHit> RemoteSearchResults { get; } = [];
    internal Task<byte[]?> LoadCatalogIconAsync(CatalogSearchHit hit, CancellationToken ct) => services.Modrinth.GetIconAsync(hit, ct);
    public ObservableCollection<CatalogModVersion> RemoteVersions { get; } = [];
    public ObservableCollection<ModPlanEntry> RemoteEntries { get; } = [];
    public ObservableCollection<OptionalDependencyChoice> OptionalChoices { get; } = [];
    public bool HasOptionalChoices => OptionalChoices.Count > 0;
    public bool HasOptionalSection => HasOptionalChoices || CartOptionalFiles.Any();
    private int optionalRevision;
    public sealed record CatalogPick(RemoteModRequest Request, string Title, string Number, string FileName);
    private readonly List<CatalogPick> remoteRoots = [];
    public IReadOnlyList<CatalogPick> CatalogPicks
    {
        get
        {
            if (manualLocalOnly) return [];
            var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var downloads = combinedPlan?.Downloads.ToDictionary(e => e.Version.File.Sha512, StringComparer.OrdinalIgnoreCase);
            return remoteRoots.Select(pick =>
            {
                var entry = RemoteEntries.FirstOrDefault(e => e.Version.ProjectId == pick.Request.ProjectId);
                var file = entry is null ? pick.FileName : entry.AlreadyInstalled ? TextCatalog.Get("CartAlreadyInstalled") :
                    entry.FromLocalFile || !shown.Add(entry.Version.File.Sha512) ? TextCatalog.Get("CartSharedFile") :
                    downloads?.GetValueOrDefault(entry.Version.File.Sha512)?.Version.File.Name ?? entry.Version.File.Name;
                return pick with { FileName = file };
            }).ToArray();
        }
    }
    public string CatalogDoneLabel => TextCatalog.Format("CatalogDoneCount", Cart.Count + (manualLocalOnly ? 0 : remoteRoots.Count));
    public bool HasCatalogSelection => remoteRoots.Count > 0 || HasLocalMods;
    public string CatalogInstallLabel => TextCatalog.Get(Cart.Count + (manualLocalOnly ? 0 : remoteRoots.Count) == 1 ? "InstallOneMod" : "InstallManyMods");
    public string CartTarget => TextCatalog.Format("CartTarget", ActiveName);
    public string CartNextStep => TextCatalog.Get(HasCartConflict ? "CartChangeSelection" :
        combinedPlan is null ? "CartReviewFirst" : combinedPlan.CanInstall ? "CartConfirmNext" : "Message058");
    private ModIdentityConflictException? installConflict;
    public string CartConflict => installConflict?.Message ??
        string.Join(Environment.NewLine, (combinedPlan?.Conflicts ?? []).Take(8).Select(c => c.Summary));
    public bool HasCartConflict => CartConflict.Length != 0;
    public IEnumerable<ModPlanEntry> CartRequired => CartDependencies.Where(e => !IsSelectedOptional(e));
    public IEnumerable<ModPlanEntry> CartOptionalFiles => CartDependencies.Where(IsSelectedOptional);
    private IEnumerable<ModPlanEntry> CartDependencies
    {
        get
        {
            var roots = remoteRoots.Select(r => r.Request.ProjectId).ToHashSet(StringComparer.Ordinal);
            var rootFiles = RemoteEntries.Where(e => roots.Contains(e.Version.ProjectId))
                .Select(e => e.Version.File.Sha512).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return (combinedPlan?.Downloads ?? []).Where(e => !rootFiles.Contains(e.Version.File.Sha512));
        }
    }
    private bool IsSelectedOptional(ModPlanEntry entry) =>
        (combinedPlan?.Remote.Resolution.SelectedOptional ?? []).Any(o => o.ProjectId == entry.Version.ProjectId || o.VersionId == entry.Version.Id)
        && !RemoteEntries.Any(parent => parent.Version.Dependencies.Any(d => d.Kind == "required" &&
            (d.ProjectId == entry.Version.ProjectId || d.VersionId == entry.Version.Id)));
    private LocalDependencyPlan? combinedPlan;
    private bool manualLocalOnly;
    public bool ManualLocalOnly => manualLocalOnly;
    public void SelectLocalImportMode(bool manual)
    {
        manualLocalOnly = manual;
        InvalidateModPlan();
        Changed(nameof(ManualLocalOnly));
    }
    public bool HasLocalMods => Cart.Count > 0;
    public bool NeedsUnknownAcceptance => combinedPlan?.RequiresUnknownAcceptance ?? HasLocalMods;
    private string remoteQuery = "", remoteSummary = "";
    private CatalogSearchHit? remoteProject;
    private CatalogModVersion? remoteVersion;
    public string RemoteQuery { get => remoteQuery; set => Set(ref remoteQuery, value); }
    public string RemoteSummary
    {
        get => TextCatalog.Relocalize(remoteSummary) + (installConflict is { } failure ? Environment.NewLine + failure.Detail : "");
        set => Set(ref remoteSummary, value);
    }
    public bool CanCommitCatalog => IsIdle && HasCurrentInstallPlan;
    private bool HasCurrentInstallPlan => installConflict is null && combinedPlan?.CanInstall == true &&
        SelectedProfile == reviewedModProfile && (!NeedsUnknownAcceptance || UnknownSourceAccepted);
    public bool CanAddCatalogVersion => CanUseOnlineCatalog && SelectedRemoteVersion is not null;
    public string CatalogVersionDetails => SelectedRemoteVersion is { } v
        ? $"{string.Join(", ", v.GameVersions)} · {string.Join(", ", v.Loaders)} · {v.File.Size / 1048576.0:0.##} MB\n" +
            string.Join("\n", v.Dependencies.Where(d => d.Kind is "required" or "optional").GroupBy(d => d.Kind)
                .Select(g => TextCatalog.Get(g.Key == "required" ? "CartRequired" : "CartOptional") + " · " + g.Count()))
        : "";
    public CatalogSearchHit? SelectedRemoteProject
    {
        get => remoteProject;
        set { if (Set(ref remoteProject, value)) { RemoteVersions.Clear(); SelectedRemoteVersion = null; Changed(nameof(HasRemoteProject)); } }
    }
    public bool HasRemoteProject => SelectedRemoteProject is not null;
    public CatalogModVersion? SelectedRemoteVersion { get => remoteVersion; set { if (Set(ref remoteVersion, value)) { Changed(nameof(CatalogVersionDetails)); Changed(nameof(CanAddCatalogVersion)); } } }
    public void ClearRemotePlan()
    {
        remoteRoots.Clear(); manualLocalOnly = false; Changed(nameof(ManualLocalOnly)); InvalidateModPlan();
    }
    internal void ResetRemoteSearch()
    {
        RemoteQuery = "";
        RemoteSearchResults.Clear();
        SelectedRemoteProject = null;
    }
    public Task SearchRemoteAsync(CancellationToken request = default) => RunAsync(async ct =>
    {
        var profile = RequireProfile();
        var query = RemoteQuery.Trim();
        if (query.Length == 0) { ResetRemoteSearch(); return; }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, request);
        services.Modrinth.BeginOperation();
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => term.Equals("kothlin", StringComparison.OrdinalIgnoreCase) ? "kotlin" : term).ToArray();
        var hits = await services.Modrinth.SearchAsync(string.Join(' ', terms), profile.MinecraftVersion, profile.Loader, linked.Token);
        int Score(CatalogSearchHit hit) => terms.Count(term => hit.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            hit.Slug.Contains(term, StringComparison.OrdinalIgnoreCase));
        if (terms.Length > 1 && !hits.Any(hit => Score(hit) == terms.Length))
        {
            var anchor = terms.OrderByDescending(term => term.Length).First();
            var fallback = await services.Modrinth.SearchAsync(anchor, profile.MinecraftVersion, profile.Loader, linked.Token);
            hits = hits.Concat(fallback).DistinctBy(hit => hit.Id).ToArray();
        }
        if (linked.IsCancellationRequested || Offline || SelectedProfile != profile || RemoteQuery.Trim() != query) return;
        hits = hits.OrderByDescending(Score).ToArray();
        RemoteSearchResults.Clear(); foreach (var hit in hits) RemoteSearchResults.Add(hit);
        SelectedRemoteProject = RemoteSearchResults.FirstOrDefault();
        Status = TextCatalog.Format("SearchCount", hits.Count) + (services.Modrinth.UsedCache ? TextCatalog.Get("Message043") : ".");
    });
    public Task LoadRemoteVersionsAsync() => RunAsync(async ct =>
    {
        var profile = RequireProfile(); var project = SelectedRemoteProject ?? throw new InvalidOperationException(TextCatalog.Get("Message044"));
        var versions = await services.Modrinth.GetVersionsAsync(project.Id, profile.MinecraftVersion, profile.Loader, ct);
        if (SelectedProfile != profile || SelectedRemoteProject?.Id != project.Id) return;
        RemoteVersions.Clear();
        foreach (var version in versions.OrderBy(v => v.Channel == "release" ? 0 : v.Channel == "beta" ? 1 : 2).ThenByDescending(v => v.Published).ThenBy(v => v.Id, StringComparer.Ordinal)) RemoteVersions.Add(version);
        SelectedRemoteVersion = RemoteVersions.FirstOrDefault();
        Status = TextCatalog.Format("CompatibleCount", RemoteVersions.Count);
    });
    public void InvalidateModPlan()
    {
        combinedPlan = null; installConflict = null; UnknownSourceAccepted = false; RemoteEntries.Clear(); RemoteSummary = "";
        OptionalChoices.Clear(); optionalRevision++; Changed(nameof(HasOptionalChoices)); Changed(nameof(HasOptionalSection));
        Changed(nameof(HasLocalMods)); Changed(nameof(NeedsUnknownAcceptance));
        Changed(nameof(HasCatalogSelection));
        RefreshCartPresentation();
    }
    private void RefreshCartPresentation()
    {
        Changed(nameof(CartNextStep));
        Changed(nameof(CatalogPicks));
        Changed(nameof(CatalogDoneLabel));
        Changed(nameof(CatalogInstallLabel));
        Changed(nameof(CartRequired));
        Changed(nameof(CartOptionalFiles));
        Changed(nameof(HasOptionalSection));
        Changed(nameof(CartConflict));
        Changed(nameof(HasCartConflict));
        Changed(nameof(RemoteSummary));
        Changed(nameof(CanCommitCatalog));
    }
    private void OptionalSelectionChanged(OptionalDependencyChoice choice)
    {
        if(!OptionalChoices.Contains(choice))return;
        combinedPlan=null;installConflict=null;UnknownSourceAccepted=false;RemoteEntries.Clear();optionalRevision++;
        RemoteSummary=TextCatalog.Get("Message045");
        RefreshCartPresentation();
        var revision = optionalRevision;
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            if (revision == optionalRevision && combinedPlan is null && IsIdle)
                await ResolveCartAsync();
        });
    }
    public Task ClearOptionalAsync() => RunAsync(async ct =>
    {
        foreach(var choice in OptionalChoices)choice.IsSelected=false;
        await ResolveCombinedAsync(ct);
    });
    public void ClearInstallationCart() { Cart.Clear(); ClearRemotePlan(); CartSummary = TextCatalog.Get("Message000"); UnknownSourceAccepted = false; }
    public Task ResolveCartAsync() => HasCatalogSelection ? RunAsync(ResolveCombinedAsync) : Task.CompletedTask;
    public Task ReviewCartAsync() => combinedPlan is null ? ResolveCartAsync() : Task.CompletedTask;
    private async Task ResolveCombinedAsync(CancellationToken ct)
    {
        var profile = RequireProfile(); var inputs = Cart.ToArray(); var roots = remoteRoots.ToArray();
        var selectedOptions=OptionalChoices.Where(o=>o.IsSelected).Select(o=>o.Option.Key).ToArray();var revision=optionalRevision;
        combinedPlan=null;installConflict=null;UnknownSourceAccepted=false;
        RemoteEntries.Clear(); RemoteSummary = ""; RefreshCartPresentation();
        var manual = manualLocalOnly;
        var automatic = !manual && roots.Length != 0;
        var plan = await services.RemoteMods.PreviewLocalAsync(profile, inputs, manual ? [] : roots.Select(r => r.Request).ToArray(), ct,
            selectedOptions, manualOnly: manual, automaticOptional: automatic);
        var titles = plan.Remote.Resolution.Entries.GroupBy(e => e.Version.ProjectId).ToDictionary(g => g.Key, g => g.First().Title, StringComparer.Ordinal);
        var versionTitles = plan.Remote.Resolution.Entries.GroupBy(e => e.Version.Id).ToDictionary(g => g.Key, g => g.First().Title, StringComparer.Ordinal);
        var unresolved = plan.Remote.Resolution.Optional.Select(o => o.Dependency).Where(d => d.ProjectId is { } id
            ? !titles.ContainsKey(id) : d.VersionId is { } version && !versionTitles.ContainsKey(version));
        foreach (var dependency in automatic ? [] : unresolved.DistinctBy(d => d.ProjectId ?? d.VersionId).Take(8).ToArray())
        {
            try
            {
                if (dependency.ProjectId is { } id) titles[id] = (await services.Modrinth.GetProjectAsync(id, ct)).Title;
                else if (dependency.VersionId is { } version)
                {
                    var metadata = await services.Modrinth.GetVersionAsync(version, ct);
                    versionTitles[version] = (await services.Modrinth.GetProjectAsync(metadata.ProjectId, ct)).Title;
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is IOException or InvalidDataException or HttpRequestException or System.Text.Json.JsonException or TaskCanceledException) { }
        }
        if (SelectedProfile != profile || !inputs.SequenceEqual(Cart) || !roots.SequenceEqual(remoteRoots) || revision != optionalRevision)
        { InvalidateModPlan(); Status = TextCatalog.Get("Message046"); return; }
        combinedPlan = plan; reviewedModProfile = profile;
        RemoteEntries.Clear(); foreach (var entry in plan.Remote.Resolution.Entries) RemoteEntries.Add(entry);
        var activeOptions=(plan.Remote.Resolution.SelectedOptional ?? []).ToHashSet();
        OptionalChoices.Clear();
        foreach (var option in automatic ? [] : plan.Remote.Resolution.Optional.DistinctBy(o => o.Key).ToArray())
            OptionalChoices.Add(new(option, activeOptions.Contains(option.Key), OptionalSelectionChanged,
                option.Dependency.ProjectId is { } id ? titles.GetValueOrDefault(id) :
                option.Dependency.VersionId is { } version ? versionTitles.GetValueOrDefault(version) : null));
        Changed(nameof(HasOptionalChoices));
        CartSummary = string.Join(Environment.NewLine, plan.LocalFiles.Select(f =>
            (f.Item.Inspection.Name ?? f.Item.Inspection.FileName) + " " + f.Item.Inspection.Version + " · " + f.Item.Inspection.FileName + " · " +
            (f.Identity is null ? TextCatalog.Get("Message047") : TextCatalog.Get("Message048")) +
            " · " + (f.Item.Inspection.Size / 1048576.0).ToString("0.##") + " MB"));
        RemoteSummary = string.Join(Environment.NewLine, new[] {
            TextCatalog.Get("Message049") + plan.LocalFiles.Count + TextCatalog.Get("Message050") + plan.Remote.Resolution.Entries.Count + TextCatalog.Get("Message051") + (plan.DownloadBytes / 1048576.0).ToString("0.##") + " MB.",
            plan.Remote.Resolution.Entries.Count == 0 ? TextCatalog.Get("Message052") : plan.Remote.CachedMetadata ? TextCatalog.Get("Message053") : TextCatalog.Get("Message054")
        }.Concat(plan.Blockers.Concat(plan.Remote.Resolution.Blockers).Select(b => TextCatalog.Get("Message055") + b))
         .Concat(plan.Warnings.Concat(plan.Remote.Resolution.Warnings).Select(w => TextCatalog.Get("Message056") + w))
         .Concat(plan.Remote.Resolution.Entries.Select(e => $"{e.Title}: {e.Version.ProjectId}/{e.Version.Id} · {e.Reason} · {e.Dependencies}")));
        Changed(nameof(NeedsUnknownAcceptance));
        RefreshCartPresentation();
        Status = plan.CanInstall ? TextCatalog.Get("Message057") : TextCatalog.Get("Message058");
    }
    public void AddRemote()
    {
        if (!CanAddCatalogVersion || SelectedRemoteVersion is not { } version || SelectedRemoteProject is not { } project ||
            version.ProjectId != project.Id) return;
        QueueCatalogVersion(version, project.Title);
    }
    internal void QueueCatalogVersion(CatalogModVersion version, string title)
    {
        if (!manualLocalOnly && remoteRoots.Any(root => root.Request.ProjectId == version.ProjectId && root.Request.VersionId == version.Id)) return;
        manualLocalOnly = false; Changed(nameof(ManualLocalOnly));
        remoteRoots.RemoveAll(r => r.Request.ProjectId == version.ProjectId);
        remoteRoots.Add(new(new(version.ProjectId, version.Id), title, version.Number, version.File.Name));
        InvalidateModPlan();
        Status = TextCatalog.Format("CatalogQueuedVersion", title, version.Number);
    }
    public async Task RemoveCatalogPickAsync(CatalogPick pick)
    {
        if (!IsIdle || remoteRoots.RemoveAll(root => root.Request == pick.Request) == 0) return;
        InvalidateModPlan();
        await ResolveCartAsync();
    }
    public async Task RemoveLocalPickAsync(ModCartItem item)
    {
        if (!IsIdle || !Cart.Remove(item)) return;
        await ResolveCartAsync();
    }
    private GameProfile? reviewedModProfile;
    private async Task InstallCombinedAsync(CancellationToken ct)
    {
        var profile = RequireProfile();
        if (!HasCurrentInstallPlan || combinedPlan is not { } plan)
        {
            throw new InvalidOperationException(TextCatalog.Get("Message060"));
        }
        var names = plan.LocalFiles.Select(f => f.Item.Inspection.Name ?? f.Item.Inspection.FileName)
            .Concat(CatalogPicks.Select(p => p.Title));
        var title = string.Join(", ", names.Distinct());
        try
        {
            await TrackDownloadsAsync(string.IsNullOrEmpty(title) ? CatalogInstallLabel : title, profile.Name, async progress =>
            {
                await services.RemoteMods.InstallLocalAsync(profile, plan, confirmed: true, UnknownSourceAccepted, progress, ct);
                return true;
            }, ct);
        }
        catch (ModIdentityConflictException ex)
        {
            installConflict = ex;
            RefreshCartPresentation();
            throw;
        }
        if (plan.ManualOnly && remoteRoots.Count > 0)
        {
            Cart.Clear();
            SelectLocalImportMode(false);
            CartSummary = "";
        }
        else ClearInstallationCart();
        Status = TextCatalog.Get("Message062");
    }
    public Task InstallRemoteAsync() => RunAsync(InstallCombinedAsync);
}
