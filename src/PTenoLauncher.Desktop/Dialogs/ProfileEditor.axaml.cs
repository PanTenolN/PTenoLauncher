using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PTenoLauncher.Desktop.ViewModels;
namespace PTenoLauncher.Desktop.Dialogs;
public sealed partial class ProfileEditor : Window
{
    private readonly CancellationTokenSource refreshCancellation = new();
    public ProfileEditor()
    {
        InitializeComponent();
        Opened += async (_, _) => { if (DataContext is MainViewModel vm) await vm.RefreshCatalogIfStaleAsync(refreshCancellation.Token); };
        Closed += (_, _) => refreshCancellation.Cancel();
    }
    private async void FiltersChanged(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) await vm.SaveVersionVisibilityAsync(); }
    private async void Save(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.IsBusy) return;
        await vm.SaveCommand.ExecuteAsync(); if (vm.LastOperationSucceeded) Close(true);
    }
    private void Cancel(object? sender, RoutedEventArgs e) => Close(false);
    private async void BrowseJava(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.IsBusy) return;
        await vm.RunAsync(async _ =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Java x64", FileTypeFilter = [new("Java") { Patterns = ["java.exe"] }] });
            if (files.Count > 0) vm.JavaPath = files[0].TryGetLocalPath() ?? "";
        });
    }
}