using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Desktop.Dialogs;
namespace PTenoLauncher.Desktop;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ModDropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        ModDropZone.AddHandler(DragDrop.DropEvent, OnDrop);
    }
    private void Navigate(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is Button { Tag: string tag } && Enum.TryParse<LauncherPage>(tag, out var page)) vm.Page = page;
    }
    private async void CreateProfile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.IsBusy) return;
        var previous = vm.SelectedProfile;
        await vm.NewCommand.ExecuteAsync();
        var saved = await new ProfileEditor { DataContext = vm }.ShowDialog<bool>(this);
        if (!saved && vm.SelectedProfile is null) vm.SelectedProfile = previous;
    }
    private async void EditProfile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.IsBusy || vm.SelectedProfile is null) return;
        await new ProfileEditor { DataContext = vm }.ShowDialog<bool>(this);
    }
    private async void DeleteProfile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { SelectedProfile: not null, IsBusy: false } vm) await new DeleteProfileDialog(vm).ShowDialog<bool>(this);
    }
    private async void AutoJava(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.PrepareJavaAsync(); }
    private async void Unarchive(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.UnarchiveAsync(); }
    private async void ThemeChanged(object? sender, SelectionChangedEventArgs e) { if (e.RemovedItems.Count > 0 && DataContext is MainViewModel { IsBusy: false, IsInitialized: true } vm) await vm.SaveThemeAsync(); }
    private async void LanguageChanged(object? sender, SelectionChangedEventArgs e) { if (e.RemovedItems.Count > 0 && DataContext is MainViewModel { IsBusy: false, IsInitialized: true } vm) await vm.SaveLanguageAsync(); }
    private async void AccountChanged(object? sender, SelectionChangedEventArgs e) { if (e.RemovedItems.Count > 0 && DataContext is MainViewModel { IsBusy: false, IsInitialized: true, SelectedAccount: not null } vm) await vm.SelectAccountAsync(); }
    private async void AiChanged(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel { IsInitialized: true } vm) await vm.SaveAiAsync(); }
    private async void ApplyTheme(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.SaveThemeAsync(); }
    private void ClearCart(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel { IsBusy: false } vm) { vm.ClearInstallationCart(); } }
    private async void RestoreBackup(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.IsBusy) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "PTenoLauncher backup", AllowMultiple = false, FileTypeFilter = [new("Backup") { Patterns = ["*.zip"] }] });
            if (files.Count > 0 && files[0].TryGetLocalPath() is string path) await vm.RestoreBackupAsync(path);
        }
        catch (Exception ex) { vm.Status = ex.Message; }
    }
    private async void ImportMods(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.IsBusy) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Minecraft JAR", AllowMultiple = true, FileTypeFilter = [new("Mods") { Patterns = ["*.jar"] }] });
            await vm.AddModsAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
        }
        catch (Exception ex) { vm.Status = ex.Message; }
    }
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel { IsBusy: false } vm) return;
        var files = e.DataTransfer.TryGetFiles();
        if (files is not null) await vm.AddModsAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>().Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)));
        e.Handled = true;
    }
    private async void ApplyOptionalSelection(object? sender, RoutedEventArgs e) { if(DataContext is MainViewModel vm)await vm.ResolveCartAsync(); }
    private async void ClearOptionalSelection(object? sender, RoutedEventArgs e) { if(DataContext is MainViewModel vm)await vm.ClearOptionalAsync(); }
    private async void ResolveModCart(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.ResolveCartAsync(); }
    private async void SearchModrinth(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.SearchRemoteAsync(); }
    private async void LoadModVersions(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.LoadRemoteVersionsAsync(); }
    private async void AddRemoteMod(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.AddRemoteAsync(); }
    private async void InstallRemoteMods(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.InstallRemoteAsync(); }
    private void ClearRemoteCart(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel { IsBusy: false } vm) vm.ClearInstallationCart(); }
    private void OpenModsFolder(object? sender,RoutedEventArgs e) { if(DataContext is MainViewModel {CanManageMods:true} vm)vm.OpenModsFolder(); }
    private async void ReviewInstalledMods(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { CanManageMods: true, IsBusy: false } vm) await new ModBaselineDialog(vm).ShowDialog<bool>(this);
    }
    private async void LoadQuiltVersions(object? sender,RoutedEventArgs e) { if(DataContext is MainViewModel vm)await vm.LoadQuiltVersionsAsync(); }
    private async void LoadFabricVersions(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.LoadFabricVersionsAsync(); }
}