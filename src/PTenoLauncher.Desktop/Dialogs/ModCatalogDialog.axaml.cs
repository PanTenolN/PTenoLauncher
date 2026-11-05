using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
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
    private CancellationTokenSource? searchDelay;
    private readonly CancellationTokenSource iconLifetime = new();
    private readonly Dictionary<CatalogSearchHit, Task<Bitmap?>> icons = [];
    public ModCatalogDialog() => InitializeComponent();
    public ModCatalogDialog(MainViewModel vm, GameProfile profile, bool review = false) : this()
    {
        this.vm = vm;
        this.profile = profile;
        if (!review && !vm.Offline) vm.ResetRemoteSearch();
        DataContext = vm;
        vm.PropertyChanged += ViewModelChanged;
        vm.UnknownSourceAccepted = false;
        SetStep(review || vm.Offline);
        Closing += (_, e) => { if (vm.IsBusy) e.Cancel = true; };
        Closed += (_, _) =>
        {
            closed = true;
            searchDelay?.Cancel();
            iconLifetime.Cancel();
            foreach (var icon in icons.Values)
                if (icon.IsCompletedSuccessfully) icon.Result?.Dispose();
            iconLifetime.Dispose();
            vm.PropertyChanged -= ViewModelChanged;
            vm.UnknownSourceAccepted = false;
        };
        Opened += async (_, _) =>
        {
            if (reviewing) await vm.ReviewCartAsync();
            else
            {
                CatalogQuery.Focus();
                QueueVersions();
            }
        };
    }
    private void CatalogIconAttached(object? sender, VisualTreeAttachmentEventArgs e) => CatalogIconChanged(sender, e);
    private async void CatalogIconChanged(object? sender, EventArgs e)
    {
        if (sender is not Image image) return;
        image.Source = null;
        if (closed || vm is null || image.DataContext is not CatalogSearchHit hit || hit.IconUrl is null) return;
        if (!icons.TryGetValue(hit, out var pending))
        {
            if (icons.Count >= 80) return;
            icons.Add(hit, pending = LoadIconAsync(hit, iconLifetime.Token));
        }
        var bitmap = await pending;
        if (!closed && Equals(image.DataContext, hit)) image.Source = bitmap;
    }
    private async Task<Bitmap?> LoadIconAsync(CatalogSearchHit hit, CancellationToken ct)
    {
        try
        {
            var bytes = await vm.LoadCatalogIconAsync(hit, ct);
            if (closed || bytes is null) return null;
            using var data = SkiaSharp.SKData.CreateCopy(bytes);
            using var codec = SkiaSharp.SKCodec.Create(data);
            if (codec is null || codec.Info.Width is < 1 or > 512 || codec.Info.Height is < 1 or > 512) return null;
            using var stream = new MemoryStream(bytes);
            return Bitmap.DecodeToWidth(stream, 28);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or HttpRequestException or OperationCanceledException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
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
                if (vm.LastOperationSucceeded) vm.AddRemote();
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
            var query = vm.RemoteQuery.Trim();
            if (!immediate && query.Length == 1) return;
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
    private void Add(object? sender, RoutedEventArgs e) { if (Current()) vm.AddRemote(); }
    private void AddDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual source || (source as ListBoxItem ?? source.FindAncestorOfType<ListBoxItem>())?.DataContext is not CatalogSearchHit hit ||
            vm.SelectedRemoteProject != hit || !Current()) return;
        if (vm.CanAddCatalogVersion) vm.AddRemote();
        else
        {
            addAfterLoading = hit.Id;
            if (vm.IsIdle) { loadedProject = null; QueueVersions(); }
        }
        e.Handled = true;
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
    private async void RemoveCatalog(object? sender, RoutedEventArgs e)
    {
        if (Current() && sender is Button { DataContext: MainViewModel.CatalogPick pick }) await vm.RemoveCatalogPickAsync(pick);
    }
    private async void RemoveLocal(object? sender, RoutedEventArgs e)
    {
        if (Current() && sender is Button { DataContext: ModCartItem item }) await vm.RemoveLocalPickAsync(item);
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
