using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Desktop.ViewModels;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class GameStopUiTests
{
    [AvaloniaFact]
    public async Task StopDialogRequiresExplicitAcceptanceAndDefaultsToCancel()
    {
        using var ws = new TestWorkspace(); using var services = new LauncherServices(ws.Paths);
        var vm = new MainViewModel(services); var window = new MainWindow { DataContext = vm }; window.Show();
        try
        {
            await vm.InitializeAsync();
            Assert.False(window.FindControl<Button>("StopGameButton")!.IsVisible);
            Assert.False(window.FindControl<Button>("ConsoleStopGameButton")!.IsVisible);
            Assert.False(await vm.StopGameAsync(Guid.NewGuid(), true));
            Assert.False(await vm.StopGameAsync(Guid.NewGuid(), false));
            var dialog = new StopGameDialog(); var result = dialog.ShowDialog<bool>(window);
            var confirm = dialog.FindControl<Button>("ConfirmStopButton")!;
            var cancel = dialog.FindControl<Button>("CancelStopButton")!;
            Assert.False(confirm.IsEnabled); Assert.True(cancel.IsDefault);
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.False(await result);
            dialog = new StopGameDialog(); result = dialog.ShowDialog<bool>(window);
            var acceptance = dialog.FindControl<CheckBox>("StopAcceptance")!;
            acceptance.IsChecked = true; acceptance.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            confirm = dialog.FindControl<Button>("ConfirmStopButton")!; Assert.True(confirm.IsEnabled);
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.True(await result);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task OrdinaryOperationCancellationStillWorksAndStopCannotTargetIt()
    {
        using var ws = new TestWorkspace(); using var services = new LauncherServices(ws.Paths);
        var vm = new MainViewModel(services); await vm.InitializeAsync();
        var running = vm.RunAsync(ct => Task.Delay(Timeout.Infinite, ct));
        Assert.True(vm.CanCancelOperation); Assert.False(vm.IsGameLaunchActive);
        Assert.False(await vm.StopGameAsync(Guid.NewGuid(), true));
        Assert.True(vm.IsBusy);
        await vm.CancelCommand.ExecuteAsync(); await running;
        Assert.False(vm.IsBusy); Assert.False(vm.LastOperationSucceeded); Assert.False(vm.CanCancelOperation);
    }
}
