using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Diagnostics;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Mods;

namespace PTenoLauncher.Desktop.Dialogs;

public sealed partial class ModCatalogDialog : Window
{
    private readonly MainViewModel vm = null!;
    private readonly GameProfile profile = null!;
    private string? loadedProject;
    private bool closed;
    private bool reviewing;
    private string? addAfterLoading;
    private ModRequirement? manualRequirement;
    private CancellationTokenSource? searchDelay;
    private CatalogIconCache? icons;
    public ModCatalogDialog() => InitializeComponent();
    public ModCatalogDialog(MainViewModel vm, GameProfile profile, bool review = false, string? initialQuery = null) : this()
    {
        this.vm = vm;
        this.profile = profile;
        icons = new(vm.LoadCatalogIconAsync);
        if (!review && !vm.Offline) vm.ResetRemoteSearch();
        DataContext = vm;
        vm.PropertyChanged += ViewModelChanged;
        vm.UnknownSourceAccepted = false;
        SetStep(review);
        Closing += (_, e) => { if (vm.IsBusy) e.Cancel = true; };
        Closed += (_, _) =>
        {
            closed = true;
            searchDelay?.Cancel();
            icons.Dispose();
            vm.PropertyChanged -= ViewModelChanged;
            vm.UnknownSourceAccepted = false;
        };
        Opened += async (_, _) =>
        {
            if (reviewing) await vm.ReviewCartAsync();
            else
            {
                CatalogQuery.Focus();
                if (initialQuery is null) QueueVersions();
                else
                {
                    vm.RemoteQuery = initialQuery;
                    await QueueSearchAsync(immediate: true);
                }
            }
        };
    }
    private void CatalogIconAttached(object? sender, VisualTreeAttachmentEventArgs e) => CatalogIconChanged(sender, e);
    private async void CatalogIconChanged(object? sender, EventArgs e) { if (sender is Image image && icons is not null) await icons.ShowAsync(image); }
    private void ViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedRemoteProject))
        {
            loadedProject = null;
            if (addAfterLoading != vm.SelectedRemoteProject?.Id) addAfterLoading = null;
        }
        if (e.PropertyName is nameof(MainViewModel.SelectedRemoteProject) or nameof(MainViewModel.IsIdle)) QueueVersions();
    }
    private void QueueVersions() => Dispatcher.UIThread.Post(async () =>
    {
        if (closed || !vm.CanUseOnlineCatalog || vm.SelectedRemoteProject is not { } project || loadedProject == project.Id ||
            reviewing || !Current()) return;
        loadedProject = project.Id;
        CatalogVersions.IsVisible = false;
        await vm.LoadRemoteVersionsAsync();
        if (!closed && vm.SelectedRemoteProject?.Id == project.Id)
        {
            this.FindControl<Button>("CatalogVersions")!.IsVisible = !vm.LastOperationSucceeded;
            if (addAfterLoading == project.Id)
            {
                addAfterLoading = null;
                if (vm.LastOperationSucceeded) await AddSelectedAsync();
            }
        }
    });
    private async void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await QueueSearchAsync(immediate: true); }
    }
    private async void CatalogQueryChanged(object? sender, TextChangedEventArgs e) => await QueueSearchAsync(immediate: false);
    private async Task QueueSearchAsync(bool immediate)
    {
        if (vm is null || closed || reviewing) return;
        searchDelay?.Cancel();
        using var request = new CancellationTokenSource();
        searchDelay = request;
        try
        {
            if (!immediate) await Task.Delay(350, request.Token);
            while (vm.IsBusy && !vm.Offline) await Task.Delay(30, request.Token);
            if (!closed && !reviewing && vm.CanUseOnlineCatalog && Current())
                await vm.SearchRemoteAsync(request.Token);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally { if (ReferenceEquals(searchDelay, request)) searchDelay = null; }
    }
    private bool Current()
    {
        if (vm.SelectedProfile == profile) return true;
        vm.InvalidateModPlan();
        vm.Status = TextCatalog.Get("Message046");
        return false;
    }
    private void SetStep(bool cart)
    {
        reviewing = cart;
        SearchStep.IsVisible = !cart;
        CartStep.IsVisible = cart;
        CatalogReview.IsVisible = !cart;
        InstallCombinedCartButton.IsVisible = cart;
        CatalogBack.IsVisible = cart && !vm.Offline;
    }
    private async void Search(object? sender, RoutedEventArgs e) => await QueueSearchAsync(immediate: true);
    private void Versions(object? sender, RoutedEventArgs e) { loadedProject = null; QueueVersions(); }
    private async void Add(object? sender, RoutedEventArgs e) => await AddSelectedAsync();
    private async Task AddSelectedAsync()
    {
        if (!Current() || !vm.CanAddCatalogVersion) return;
        if (manualRequirement is { } needed && vm.SelectedRemoteVersion is { } version)
        {
            await vm.RunAsync(async ct =>
            {
                if (!await vm.Services.RemoteMods.VerifyFabricProviderAsync(profile, version, needed, ct))
                    throw new InvalidOperationException(TextCatalog.Get("CartProviderMismatch") + " " + needed.Id + " " +
                        string.Join(" || ", needed.Alternatives));
            });
            if (!vm.LastOperationSucceeded || vm.SelectedRemoteVersion != version || !Current()) return;
            manualRequirement = null;
        }
        vm.AddRemote();
    }
    private async void RemoveSelected(object? sender, RoutedEventArgs e) { if (Current()) await vm.RemoveSelectedCatalogVersionAsync(); }
    private async void AddDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual source || (source as ListBoxItem ?? source.FindAncestorOfType<ListBoxItem>())?.DataContext is not CatalogSearchHit hit ||
            vm.SelectedRemoteProject != hit || !Current()) return;
        e.Handled = true;
        if (vm.CatalogVersionQueued) return;
        if (vm.CanAddCatalogVersion) await AddSelectedAsync();
        else
        {
            addAfterLoading = hit.Id;
            if (vm.IsIdle) { loadedProject = null; QueueVersions(); }
        }
    }
    private async void OpenProject(object? sender, RoutedEventArgs e)
    {
        if (!Current() || !vm.CanUseOnlineCatalog || vm.SelectedRemoteProject is not { } hit) return;
        await vm.RunAsync(async ct =>
        {
            var project = await vm.Services.Modrinth.GetProjectAsync(hit.Id, ct);
            if (project.Id != hit.Id || vm.SelectedRemoteProject != hit || closed) return;
            using var browser = Process.Start(new ProcessStartInfo("https://modrinth.com/mod/" + Uri.EscapeDataString(project.Id))
            { UseShellExecute = true });
        });
    }
    private async void Review(object? sender, RoutedEventArgs e)
    {
        if (!Current() || !vm.IsIdle || !vm.HasCatalogSelection) return;
        searchDelay?.Cancel();
        manualRequirement = null;
        SetStep(true);
        if (CatalogBack.IsVisible) CatalogBack.Focus();
        await vm.ReviewCartAsync();
    }
    private void Back(object? sender, RoutedEventArgs e)
    {
        if (!Current() || !vm.IsIdle || vm.Offline) return;
        SetStep(false);
        CatalogQuery.Focus();
        QueueVersions();
    }
    private async void FindManualProvider(object? sender, RoutedEventArgs e)
    {
        if (!Current() || !vm.IsIdle || vm.Offline || CartMissingProvider.SelectedItem is not ModRequirement requirement) return;
        manualRequirement = requirement;
        SetStep(false);
        CatalogQuery.Text = requirement.Id;
        CatalogQuery.Focus();
        await QueueSearchAsync(immediate: true);
    }
    private async void RemoveCatalog(object? sender, RoutedEventArgs e)
    {
        if (Current() && sender is Button { DataContext: MainViewModel.CatalogPick pick }) await vm.RemoveCatalogPickAsync(pick);
    }
    private async void RemoveLocal(object? sender, RoutedEventArgs e)
    {
        if (Current() && sender is Button { DataContext: ModCartItem item }) await vm.RemoveLocalPickAsync(item);
    }
    private async void AddLocalDependency(object? sender, RoutedEventArgs e)
    {
        if (!Current() || !vm.IsIdle) return;
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = TextCatalog.Get("CartAddLocalDependency"), AllowMultiple = true,
            FileTypeFilter = [new("Minecraft JAR") { Patterns = ["*.jar"] }]
        });
        var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
        if (paths.Length == 0 || !Current()) return;
        await vm.AddModsAsync(paths);
        if (vm.LastOperationSucceeded) await vm.ResolveCartAsync();
    }
    private async void ResolveModCart(object? sender, RoutedEventArgs e) { if (Current()) await vm.ResolveCartAsync(); }
    private async void ClearOptionalSelection(object? sender, RoutedEventArgs e) { if (Current()) await vm.ClearOptionalAsync(); }
    private async void ManualOnly(object? sender, RoutedEventArgs e) { if (Current()) { vm.SelectLocalImportMode(true); await vm.ResolveCartAsync(); } }
    private void ClearCart(object? sender, RoutedEventArgs e) { if (Current()) vm.ClearInstallationCart(); }
    private async void Install(object? sender, RoutedEventArgs e)
    {
        if (!Current() || !vm.CanCommitCatalog) return;
        await vm.InstallCartCommand.ExecuteAsync();
        if (vm.LastOperationSucceeded) Close(true);
    }
    private void CloseDialog(object? sender, RoutedEventArgs e) => Close();
}
