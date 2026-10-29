using Avalonia.Controls;
using Avalonia.Interactivity;
using PTenoLauncher.Desktop.ViewModels;

namespace PTenoLauncher.Desktop.Dialogs;

public sealed partial class LanguageSelectionDialog : Window
{
    public LanguageSelectionDialog()
    {
        InitializeComponent();
        Closing += (_, e) => { if (DataContext is MainViewModel { IsBusy: true }) e.Cancel = true; };
    }
    public LanguageSelectionDialog(MainViewModel vm) : this() => DataContext = vm;
    private void Exit(object? sender, RoutedEventArgs e) => Close(false);
    private async void Continue(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsBusy: false } vm) return;
        LanguageSaveError.IsVisible = false;
        await vm.SaveLanguageAsync();
        if (vm.LastOperationSucceeded) Close(true);
        else LanguageSaveError.IsVisible = true;
    }
}
