using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Diagnostics;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Minecraft;

namespace PTenoLauncher.Desktop.Dialogs;

public sealed partial class ContentCatalogDialog : Window
{
    private MainViewModel? vm;
    private GameProfile? profile;
    private ProfileContentKind kind;
    private CatalogIconCache? icons;
    private CancellationTokenSource? search, details;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, ContentCartPick> cart = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenSource> pending = new(StringComparer.Ordinal);
    private CatalogSearchHit? loadingProject;
    private CatalogSearchHit? verifiedProject;
    public ContentCatalogDialog() => InitializeComponent();
    internal ContentCatalogDialog(MainViewModel vm, GameProfile profile, ProfileContentKind kind) : this()
    {
        this.vm = vm;
        this.profile = profile;
        this.kind = kind;
        icons = new(vm.LoadCatalogIconAsync);
        DataContext = vm;
        Title = TextCatalog.Get(kind == ProfileContentKind.ResourcePacks ? "ContentDownloadResources" : "ContentDownloadShaders");
        ContentTarget.Text = profile.Name + " · Minecraft " + profile.MinecraftVersion;
        ContentCartTarget.Text = TextCatalog.Format("CartTarget", ContentTarget.Text);
        RefreshCart();
        Closing += (_, e) => { if (vm.IsBusy) e.Cancel = true; };
        Closed += (_, _) => { lifetime.Cancel(); search?.Cancel(); icons.Dispose(); lifetime.Dispose(); };
        Opened += (_, _) => { if (vm.Offline) ContentSearchStatus.Text = TextCatalog.Get("CatalogSearchOffline"); ContentQuery.Focus(); };
    }

    private void SelectSource(object? sender, SelectionChangedEventArgs e)
    {
        if (vm is null) return;
        search?.Cancel();
        var available = ContentSource.SelectedIndex == 0;
        ContentSearchBar.IsVisible = available;
        ContentSourceUnavailable.IsVisible = !available;
        ContentResults.ItemsSource = null;
        ContentVersions.ItemsSource = null;
        ContentFile.Text = ContentRequirements.Text = "";
        ContentSearchStatus.Text = "";
        if (available) _ = SearchAsync(immediate: true);
    }

    private async Task SearchAsync(bool immediate)
    {
        search?.Cancel();
        if (vm is null || profile is null || ContentSource.SelectedIndex != 0) return;
        if (vm.Offline) { ContentSearchStatus.Text = TextCatalog.Get("CatalogSearchOffline"); return; }
        var query = ContentQuery.Text?.Trim() ?? "";
        if (query.Length == 0) { ContentResults.ItemsSource = null; ContentSearchStatus.Text = ""; return; }
        using var request = new CancellationTokenSource();
        search = request;
        try
        {
            if (!immediate) await Task.Delay(350, request.Token);
            ContentSearchStatus.Text = TextCatalog.Get("CatalogSearching");
            ContentResults.ItemsSource = null;
            var hits = await vm.Services.Modrinth.SearchContentAsync(query, profile.MinecraftVersion, kind, request.Token);
            if (request.IsCancellationRequested || vm.Offline) return;
            ContentResults.ItemsSource = hits;
            ContentSearchStatus.Text = hits.Count == 0 ? TextCatalog.Get("CatalogSearchEmpty") : TextCatalog.Format("SearchCount", hits.Count);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error) when (CatalogFailure(error))
        {
            if (!request.IsCancellationRequested) ContentSearchStatus.Text = TextCatalog.Get("CatalogSearchFailed");
            vm.Services.Log.Error("content.search", error);
        }
        finally { if (ReferenceEquals(search, request)) search = null; }
    }

    private static bool CatalogFailure(Exception error) => error is IOException or InvalidDataException or HttpRequestException or InvalidOperationException or
        System.Text.Json.JsonException or ArgumentException or KeyNotFoundException or TaskCanceledException;
    private async void Search(object? sender, RoutedEventArgs e) => await SearchAsync(immediate: true);
    private async void QueryChanged(object? sender, TextChangedEventArgs e) => await SearchAsync(immediate: false);
    private async void SearchKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SearchAsync(immediate: true);
    }
    private async void SelectResult(object? sender, SelectionChangedEventArgs e)
    {
        if (loadingProject is not { } loading || !ReferenceEquals(pending.GetValueOrDefault(loading.Id), details)) details?.Cancel();
        details = null;
        loadingProject = null;
        verifiedProject = null;
        ContentProjectPage.IsVisible = false;
        ContentVersionStatus.Text = "";
        ContentVersions.ItemsSource = null;
        ContentFile.Text = ContentRequirements.Text = "";
        var hit = ContentResults.SelectedItem as CatalogSearchHit;
        ContentTitle.Text = hit?.Title;
        ContentDescription.Text = hit?.Description;
        ContentIconPanel.IsVisible = hit is not null;
        ContentIcon.DataContext = hit;
        RefreshCart();
        if (hit is null || vm is null || profile is null || vm.Offline) return;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        details = request;
        loadingProject = hit;
        ContentVersionStatus.Text = TextCatalog.Get("CatalogSearching");
        try
        {
            var versions = await vm.Services.Modrinth.GetContentVersionsAsync(hit.Id, profile.MinecraftVersion, kind, request.Token);
            if (request.IsCancellationRequested) return;
            if (ReferenceEquals(ContentResults.SelectedItem, hit) && ReferenceEquals(details, request))
            {
                verifiedProject = hit;
                ContentProjectPage.IsVisible = true;
                ContentVersions.ItemsSource = versions;
                ContentVersions.SelectedIndex = versions.Count == 0 ? -1 : 0;
                ContentVersionStatus.Text = TextCatalog.Format("CompatibleCount", versions.Count);
            }
            if (ReferenceEquals(pending.GetValueOrDefault(hit.Id), request) && pending.Remove(hit.Id))
            {
                if (versions.FirstOrDefault() is { } version) Queue(hit, version);
                else ContentInstallStatus.Text = TextCatalog.Format("ContentCartNoVersion", hit.Title);
                RefreshCart();
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error) when (CatalogFailure(error))
        {
            if (!request.IsCancellationRequested)
            {
                if (ReferenceEquals(details, request)) ContentVersionStatus.Text = TextCatalog.Get("CatalogSearchFailed");
                if (ReferenceEquals(pending.GetValueOrDefault(hit.Id), request) && pending.Remove(hit.Id))
                {
                    ContentInstallStatus.Text = TextCatalog.Get("CatalogSearchFailed");
                    RefreshCart();
                }
            }
            vm.Services.Log.Error("content.versions", error);
        }
        finally { if (ReferenceEquals(details, request)) { details = null; loadingProject = null; } }
    }
    private void IconAttached(object? sender, VisualTreeAttachmentEventArgs e) => IconChanged(sender, e);
    private async void IconChanged(object? sender, EventArgs e) { if (sender is Image image && icons is not null) await icons.ShowAsync(image); }
    private void OpenProject(object? sender, RoutedEventArgs e)
    {
        if (verifiedProject is not { } hit || !ReferenceEquals(ContentResults.SelectedItem, hit)) return;
        var route = kind == ProfileContentKind.ResourcePacks ? "resourcepack" : "shader";
        try { using var browser = Process.Start(new ProcessStartInfo($"https://modrinth.com/{route}/{Uri.EscapeDataString(hit.Id)}") { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        { vm?.Services.Log.Error("content.project", error); ContentVersionStatus.Text = TextCatalog.Get("CatalogSearchFailed"); }
    }
    private void SelectVersion(object? sender, SelectionChangedEventArgs e)
    {
        var version = ContentVersions.SelectedItem as CatalogModVersion;
        RefreshCart();
        ContentFile.Text = version?.File.Name ?? "";
        if (kind != ProfileContentKind.Shaders || version is null) { ContentRequirements.Text = ""; return; }
        var components = version.Loaders.Where(loader => loader is "iris" or "optifine" or "oculus" or "canvas" or "vanillashader").ToArray();
        ContentRequirements.Text = (components.Length == 0 ? TextCatalog.Get("ContentShaderComponentUnknown") :
            TextCatalog.Format("ContentShaderRequirements", string.Join(", ", components))) + Environment.NewLine + vm?.WorkspaceShaderSupport;
    }
    private void QueueResult(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual source ||
            (source as ListBoxItem ?? source.FindAncestorOfType<ListBoxItem>())?.DataContext is not CatalogSearchHit hit) return;
        e.Handled = true;
        QueueProject(hit);
    }
    private void Add(object? sender, RoutedEventArgs e)
    {
        if (ContentResults.SelectedItem is CatalogSearchHit hit) QueueProject(hit);
    }
    internal void QueueProject(CatalogSearchHit hit)
    {
        if (vm is not { IsIdle: true, Offline: false } || ContentSource.SelectedIndex != 0 ||
            !ReferenceEquals(ContentResults.SelectedItem, hit)) return;
        if (ReferenceEquals(verifiedProject, hit) && ContentVersions.SelectedItem is CatalogModVersion version)
            Queue(hit, version);
        else if (ReferenceEquals(loadingProject, hit) && details is { } request)
        {
            pending[hit.Id] = request;
            ContentInstallStatus.Text = TextCatalog.Get("CatalogSearching");
            RefreshCart();
        }
        else if (ReferenceEquals(verifiedProject, hit))
            ContentInstallStatus.Text = TextCatalog.Format("ContentCartNoVersion", hit.Title);
        else ContentInstallStatus.Text = ContentVersionStatus.Text;
    }
    private void Queue(CatalogSearchHit hit, CatalogModVersion version)
    {
        if (vm is not { Offline: false } || profile is null || version.ProjectId != hit.Id ||
            !version.GameVersions.Contains(profile.MinecraftVersion, StringComparer.Ordinal)) return;
        pending.Remove(hit.Id);
        if (cart.GetValueOrDefault(hit.Id)?.Version.Id == version.Id) { RefreshCart(); return; }
        if (cart.Count >= 64 && !cart.ContainsKey(hit.Id))
        {
            ContentInstallStatus.Text = TextCatalog.Get("ContentCartLimit");
            return;
        }
        cart[hit.Id] = new(hit, version);
        ContentInstallStatus.Text = "";
        RefreshCart();
    }
    private void RemoveSelected(object? sender, RoutedEventArgs e)
    {
        if (ContentVersions.SelectedItem is CatalogModVersion version && cart.GetValueOrDefault(version.ProjectId) is { Installed: false } pick &&
            pick.Version.Id == version.Id) Remove(pick);
    }
    private void RemoveCartPick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ContentCartPick pick }) Remove(pick);
    }
    private void Remove(ContentCartPick pick)
    {
        if (vm is not { IsIdle: true } || pick.Installed || cart.GetValueOrDefault(pick.Project.Id) != pick) return;
        cart.Remove(pick.Project.Id);
        RefreshCart();
    }
    private void RefreshCart()
    {
        if (ContentAdd is null || ContentDone is null || ContentCartRows is null) return;
        var version = ContentVersions.SelectedItem as CatalogModVersion;
        var pick = version is null ? null : cart.GetValueOrDefault(version.ProjectId);
        var queued = pick is not null && pick.Version.Id == version?.Id;
        ContentAdd.Content = TextCatalog.Get(queued ? pick!.Installed ? "CartAlreadyInstalled" : "CatalogInCart" :
            pick is { Installed: false } ? "CatalogReplace" : "CatalogAdd");
        ContentAdd.IsEnabled = !queued && vm is { Offline: false } && version is not null && ContentSource.SelectedIndex == 0;
        ContentRemove.IsVisible = queued && pick is { Installed: false };
        var count = cart.Values.Count(item => !item.Installed);
        ContentDone.Content = TextCatalog.Format("CatalogDoneCount", count);
        ContentDone.IsEnabled = cart.Count != 0;
        ContentInstall.IsEnabled = count != 0 && pending.Count == 0 && vm is { Offline: false };
        ContentCartRows.ItemsSource = cart.Values.ToArray();
    }
    private void Review(object? sender, RoutedEventArgs e) => SetStep(review: true);
    private void Back(object? sender, RoutedEventArgs e) => SetStep(review: false);
    private void SetStep(bool review)
    {
        ContentSearchStep.IsVisible = ContentCatalogStep.IsVisible = ContentDone.IsVisible = !review;
        ContentCartStep.IsVisible = ContentBack.IsVisible = ContentInstall.IsVisible = review;
        ContentSearchStatus.IsVisible = !review;
        if (!review) ContentQuery.Focus();
    }
    private async void Install(object? sender, RoutedEventArgs e)
    {
        if (vm is not { IsIdle: true, Offline: false } || profile is null || !ContentCartStep.IsVisible) return;
        var picks = cart.Values.Where(pick => !pick.Installed).ToArray();
        if (picks.Length == 0) return;
        await vm.InstallWorkspaceContentAsync(profile, kind, picks, (pick, result, text) =>
        {
            cart[pick.Project.Id] = pick with { Installed = result is not null, Result = text };
            RefreshCart();
        });
        ContentInstallStatus.Text = vm.Status;
    }
    private void CloseWindow(object? sender, RoutedEventArgs e) => Close();
}
