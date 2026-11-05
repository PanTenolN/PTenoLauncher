using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using Xunit;
namespace PTenoLauncher.Tests;

public sealed class ZipPackUiTests
{
    private static async Task Idle(MainViewModel vm)
    { for (int i = 0; vm.IsBusy && i < 600; i++) await Task.Delay(10); Assert.False(vm.IsBusy); }
    [AvaloniaFact] public async Task ActualZipControlsRequireExplicitTargetFreshPreviewAndConsentOffline()
    {
        using var f = new MrpackFixture(); using var services = new LauncherServices(f.Workspace.Paths); await services.Store.InitializeAsync(); await services.Store.SetSettingAsync("ui.language", "en");
        var vm = new MainViewModel(services); await vm.InitializeAsync(); vm.Page = LauncherPage.Modpacks; vm.Offline = true;
        var window = new MainWindow { DataContext = vm }; window.Show();
        try
        {
            Assert.NotNull(window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("ChooseZipPackButton")); Assert.NotNull(window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("ChooseMrpackButton"));
            await vm.ChooseZipPackAsync(ZipPackTests.Zip(f, ("wrapper/config/x", [1]), ("wrapper/saves/world.dat", [2])));
            Assert.True(vm.IsZipPack); Assert.False(vm.CanImportPack); Assert.Contains(vm.PackBlockers, b => b.Contains("Minecraft"));
            window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<TextBox>("ZipMinecraft")!.Text = "1.21.1";
            window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<ComboBox>("ZipLoader")!.SelectedItem = ModLoader.Fabric;
            window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<TextBox>("ZipLoaderVersion")!.Text = "0.16.10";
            window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("PreviewMrpackButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle(vm);
            Assert.True(vm.LastOperationSucceeded, vm.Status); Assert.Contains("wrapper", vm.ZipLayout); Assert.Contains(vm.PackFiles, p => p.Contains("Excluded:") && p.Contains("world.dat"));
            window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<CheckBox>("MrpackConsent")!.IsChecked = true; Assert.True(vm.CanImportPack);
            vm.ZipLoaderVersion = "0.16.9"; Assert.False(vm.PackConsent); Assert.False(vm.CanImportPack);
            window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("PreviewMrpackButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle(vm); window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<CheckBox>("MrpackConsent")!.IsChecked = true;
            window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("ImportMrpackButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle(vm);
            Assert.True(vm.LastOperationSucceeded, vm.Status); Assert.Single(vm.Profiles); Assert.Equal(100, vm.PackProgress); Assert.Empty(services.Authentication.GetAccounts()); Assert.False(services.Ai.Enabled); Assert.False(File.Exists(services.Paths.Receipt(vm.SelectedProfile!.Id)));
        }
        finally { window.DataContext = null; window.Close(); }
    }
    [AvaloniaFact] public async Task RootSelectionInvalidatesPreviewAndAllEightCatalogsReachZipControls()
    {
        using var f = new MrpackFixture(); using var services = new LauncherServices(f.Workspace.Paths); await services.Store.InitializeAsync(); await services.Store.SetSettingAsync("ui.language", "en"); var vm = new MainViewModel(services); await vm.InitializeAsync();
        var window = new MainWindow { DataContext = vm }; window.Show();
        try
        {
            vm.ZipMinecraft = "1.21.1"; await vm.ChooseZipPackAsync(ZipPackTests.Zip(f, ("a/config/x", [1]), ("b/config/y", [2]))); Assert.False(vm.CanImportPack); Assert.Equal(2, vm.ZipRoots.Count);
            window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<ComboBox>("ZipRoot")!.SelectedItem = "b"; await vm.PreviewPackAsync(); vm.PackConsent = true; Assert.True(vm.CanImportPack);
            window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<ComboBox>("ZipRoot")!.SelectedItem = "a"; Assert.False(vm.PackConsent); Assert.False(vm.CanImportPack);
            foreach (var language in TextCatalog.Languages)
            {
                vm.SelectedLanguage = language; await Idle(vm); TextCatalog.ValidateCatalog(language.Code, TextCatalog.Catalog(language.Code));
                Assert.Equal(TextCatalog.Get("ZipChoose"), window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("ChooseZipPackButton")!.Content);
                Assert.Equal(TextCatalog.Get("ZipPolicy"), window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<TextBlock>("ZipPolicy")!.Text);
                Assert.Contains("b", vm.ZipLayout); Assert.False(vm.CanImportPack); // stale until recalculated, not an invented dependency blocker
            }
        }
        finally { await Idle(vm); window.DataContext = null; window.Close(); TextCatalog.Apply("uk"); }
    }
    [AvaloniaFact] public async Task ZipInterruptedImportRecoversThroughExistingConsentWorkflowAndMrpackStillOpens()
    {
        using var f = new MrpackFixture(); using var services = new LauncherServices(f.Workspace.Paths); await services.Store.InitializeAsync(); await services.Store.SetSettingAsync("ui.language", "en"); var vm = new MainViewModel(services); await vm.InitializeAsync();
        var window = new MainWindow { DataContext = vm }; window.Show();
        try
        {
            vm.ZipMinecraft = "1.21.1"; await vm.ChooseZipPackAsync(ZipPackTests.Zip(f, ("config/x", [1]))); vm.PackConsent = true;
            services.Modpacks.Fault = s => { if (s == "moved") throw new IOException("Controlled interruption"); }; await vm.ImportPackAsync(); Assert.Empty(vm.Profiles); Assert.Contains("1", vm.PackPending);
            services.Modpacks.Fault = null; vm.PackConsent = true; window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("RecoverMrpackButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle(vm); Assert.Single(vm.Profiles); Assert.Contains("0", vm.PackPending);
            await vm.ChooseZipPackAsync(f.Pack()); Assert.False(vm.IsZipPack); Assert.Contains("1.21.1", vm.PackSummary); Assert.NotNull(window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("ChooseMrpackButton"));
        }
        finally { window.DataContext = null; window.Close(); }
    }
}
