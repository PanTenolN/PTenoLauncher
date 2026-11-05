using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
    public ContentCatalogDialog() => InitializeComponent();
    internal ContentCatalogDialog(MainViewModel vm, GameProfile profile, ProfileContentKind kind) : this()
    {
        this.vm = vm;
        this.profile = profile;
        this.kind = kind;
        DataContext = vm;
        Title = TextCatalog.Get(kind == ProfileContentKind.ResourcePacks ? "ContentDownloadResources" : "ContentDownloadShaders");
        ContentTarget.Text = profile.Name + " · Minecraft " + profile.MinecraftVersion;
        Closing += (_, e) => { if (vm.IsBusy) e.Cancel = true; };
    }

    private void SelectSource(object? sender, SelectionChangedEventArgs e)
    {
        if (vm is null) return;
        var available = ContentSource.SelectedIndex == 0;
        ContentSearchBar.IsVisible = available;
        ContentSourceUnavailable.IsVisible = !available;
        ContentResults.ItemsSource = null;
        ContentVersions.ItemsSource = null;
        ContentFile.Text = ContentRequirements.Text = "";
        ContentInstall.IsEnabled = false;
    }

    private Task SearchAsync() => vm is not { IsIdle: true, Offline: false } || profile is null ||
        ContentSource.SelectedIndex != 0 || string.IsNullOrWhiteSpace(ContentQuery.Text)
        ? Task.CompletedTask : vm.RunAsync(async ct =>
        {
            var hits = await vm.Services.Modrinth.SearchContentAsync(ContentQuery.Text!, profile.MinecraftVersion, kind, ct);
            if (ContentSource.SelectedIndex == 0) ContentResults.ItemsSource = hits;
        });

    private async void Search(object? sender, RoutedEventArgs e) => await SearchAsync();
    private async void SearchKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SearchAsync();
    }
    private void SelectResult(object? sender, SelectionChangedEventArgs e)
    {
        ContentVersions.ItemsSource = null;
        ContentInstall.IsEnabled = false;
        ContentFile.Text = ContentRequirements.Text = "";
        var hit = ContentResults.SelectedItem as CatalogSearchHit;
        ContentTitle.Text = hit?.Title;
        ContentDescription.Text = hit?.Description;
        if (hit is null) return;
        Dispatcher.UIThread.Post(async () =>
        {
            if (vm is not { IsIdle: true } || profile is null || !ReferenceEquals(ContentResults.SelectedItem, hit)) return;
            await vm.RunAsync(async ct =>
            {
                var versions = await vm.Services.Modrinth.GetContentVersionsAsync(hit.Id, profile.MinecraftVersion, kind, ct);
                if (!ReferenceEquals(ContentResults.SelectedItem, hit)) return;
                ContentVersions.ItemsSource = versions;
                ContentVersions.SelectedIndex = versions.Count == 0 ? -1 : 0;
                vm.Status = TextCatalog.Format("CompatibleCount", versions.Count);
            });
        });
    }
    private void SelectVersion(object? sender, SelectionChangedEventArgs e)
    {
        var version = ContentVersions.SelectedItem as CatalogModVersion;
        ContentInstall.IsEnabled = ContentSource.SelectedIndex == 0 && version is not null &&
            ContentResults.SelectedItem is CatalogSearchHit hit && version.ProjectId == hit.Id;
        ContentFile.Text = version?.File.Name ?? "";
        if (kind != ProfileContentKind.Shaders || version is null) { ContentRequirements.Text = ""; return; }
        var components = version.Loaders.Where(loader => loader is "iris" or "optifine" or "oculus" or "canvas" or "vanillashader").ToArray();
        ContentRequirements.Text = (components.Length == 0 ? TextCatalog.Get("ContentShaderComponentUnknown") :
            TextCatalog.Format("ContentShaderRequirements", string.Join(", ", components))) + Environment.NewLine + vm?.WorkspaceShaderSupport;
    }
    private async void Install(object? sender, RoutedEventArgs e)
    {
        if (vm is not { IsIdle: true, Offline: false } || profile is null || ContentSource.SelectedIndex != 0 ||
            ContentVersions.SelectedItem is not CatalogModVersion version ||
            ContentResults.SelectedItem is not CatalogSearchHit hit || version.ProjectId != hit.Id) return;
        await vm.InstallWorkspaceContentAsync(profile, kind, hit, version);
        if (vm.LastOperationSucceeded) Close();
    }
    private void CloseWindow(object? sender, RoutedEventArgs e) => Close();
}
