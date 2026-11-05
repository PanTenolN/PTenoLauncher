using Avalonia.Controls;
using Avalonia.Interactivity;
using PTenoLauncher.Desktop.ViewModels;
namespace PTenoLauncher.Desktop.Dialogs;
public sealed partial class LauncherUpdateDialog : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext!;
    public LauncherUpdateDialog()
    {
        InitializeComponent();
        Closing += (_, e) => { if (DataContext is MainViewModel vm) { if (vm.IsBusy) e.Cancel = true; else if (!vm.LauncherHandoffReady) vm.DeferLauncherUpdate(); } };
    }
    public LauncherUpdateDialog(MainViewModel vm) : this() { DataContext = vm; }
    private void Later(object? sender, RoutedEventArgs e) { if (Vm.IsIdle) { Vm.DeferLauncherUpdate(); Close(); } }
    private async void Update(object? sender, RoutedEventArgs e) => await Vm.ApplyLauncherUpdateAsync();
}
