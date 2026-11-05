using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Desktop.Localization;
using Xunit;

namespace PTenoLauncher.Tests;
public sealed class ModUpdateUiTests
{
    [AvaloniaFact] public async Task ActualModsPanelSupportsCheckSelectConfirmApplyHistoryAndOfflineRollbackWithAiOff()
    {
        using var f = new ModUpdateTests.Fixture(); await f.Start();
        using var services = new LauncherServices(f.Ws.Paths); var vm = new MainViewModel(services, f.Manager); await vm.InitializeAsync();
        vm.SelectedProfile = f.Profile; vm.Page = LauncherPage.Mods;
        var window = new MainWindow { DataContext = vm }; window.Show();
        try
        {
            Assert.False(services.Ai.Enabled);
            Assert.True(window.FindControl<ScrollViewer>("ModsScroll")!.IsVisible);
            var button = window.FindControl<Button>("CheckModUpdatesButton")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; vm.IsBusy && i < 1000; i++) await Task.Delay(10);
            Assert.True(vm.LastOperationSucceeded, vm.Status); Assert.Single(vm.UpdateChoices);
            vm.UpdateChoices[0].IsSelected = true;
            await vm.PreviewModUpdatesAsync(); Assert.True(vm.LastOperationSucceeded, vm.Status);
            Assert.Contains("A2", window.FindControl<TextBlock>("ModUpdateSummaryText")!.Text);
            await vm.ApplyModUpdatesAsync(); Assert.False(vm.LastOperationSucceeded); // Explicit confirmation enforced through VM too.
            window.FindControl<CheckBox>("ModUpdatesConfirm")!.IsChecked = true;
            window.FindControl<Button>("ApplyModUpdatesButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; vm.IsBusy && i < 1000; i++) await Task.Delay(10);
            Assert.True(vm.LastOperationSucceeded, vm.Status); Assert.Single(vm.ModUpdateHistory);
            f.Network.Offline = true; await vm.PreviewModRollbackAsync(); Assert.True(vm.LastOperationSucceeded, vm.Status); Assert.False(vm.UpdatesConfirmed);
            vm.UpdatesConfirmed = true; await vm.ApplyModUpdatesAsync(); Assert.True(vm.LastOperationSucceeded, vm.Status);
            Assert.Equal(f.Bytes["A1"], await File.ReadAllBytesAsync(f.Physical("mods/unrelated-name.jar")));
            await vm.CheckModUpdatesAsync(); vm.UpdatesConfirmed = true; await vm.SetModPinAsync(true); Assert.True(vm.LastOperationSucceeded, vm.Status);
            await vm.CheckModUpdatesAsync(); Assert.True(Assert.Single(vm.UpdateChoices).Row.Pinned);
            vm.UpdatesConfirmed = true; await vm.SetModPinAsync(false); Assert.True(vm.LastOperationSucceeded, vm.Status);
            vm.SelectedProfile = null; Assert.Empty(vm.UpdateChoices); Assert.Empty(vm.ModUpdateHistory); Assert.Equal("", vm.ModUpdateSummary);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact] public async Task AllEightLanguagesCoverNewWorkflowAndSelectionInvalidatesConfirmation()
    {
        using var f = new ModUpdateTests.Fixture(); await f.Start(); using var services = new LauncherServices(f.Ws.Paths);
        var vm = new MainViewModel(services, f.Manager); await vm.InitializeAsync(); vm.SelectedProfile = f.Profile;
        await vm.CheckModUpdatesAsync(); vm.UpdateChoices[0].IsSelected = true; await vm.PreviewModUpdatesAsync(); vm.UpdatesConfirmed = true;
        vm.UpdateChoices[0].Remove = true; Assert.False(vm.UpdatesConfirmed);
        var window = new MainWindow { DataContext = vm }; window.Show();
        try
        {
            foreach (var language in TextCatalog.Languages)
            {
                vm.SelectedLanguage = language; TextCatalog.ValidateCatalog(language.Code, TextCatalog.Catalog(language.Code));
                Assert.Equal(TextCatalog.Get("UpdatesCheck"), window.FindControl<Button>("CheckModUpdatesButton")!.Content);
                Assert.Contains(TextCatalog.Get("UpdatesRow").Split('{')[0], vm.UpdateChoices[0].Details);
            }
        }
        finally { TextCatalog.Apply("uk"); window.Close(); }
    }
}
