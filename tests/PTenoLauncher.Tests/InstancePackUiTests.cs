using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using Xunit;
namespace PTenoLauncher.Tests;

public sealed class InstancePackUiTests
{
    private static async Task Idle(MainViewModel vm)
    { for (int i = 0; vm.IsBusy && i < 600; i++) await Task.Delay(10); Assert.False(vm.IsBusy); }
    [AvaloniaFact] public async Task ActualInstanceControlsPreviewRenameConsentImportOfflineWithoutAiOrAccount()
    {
        using var f = new MrpackFixture(); using var services = new LauncherServices(f.Workspace.Paths); await services.Store.InitializeAsync(); await services.Store.SetSettingAsync("ui.language", "en");
        var vm = new MainViewModel(services); await vm.InitializeAsync(); vm.Page = LauncherPage.Modpacks; vm.Offline = true;
        var window = new MainWindow { DataContext = vm }; window.Show();
        try
        {
            Assert.NotNull(window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("ChooseInstanceDirectoryButton")); Assert.NotNull(window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("ChooseInstanceZipButton"));
            await vm.ChooseInstancePackAsync(InstancePackTests.DirectorySource(f)); Assert.True(vm.LastOperationSucceeded, vm.Status); Assert.True(vm.IsInstancePack); Assert.False(vm.IsZipPack); Assert.Contains("minecraft", vm.InstanceLayout);
            Assert.False(window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("ImportMrpackButton")!.IsEnabled);
            window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<CheckBox>("MrpackConsent")!.IsChecked = true; Assert.True(vm.CanImportPack);
            window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<TextBox>("MrpackProfileName")!.Text = "Renamed import"; Assert.False(vm.PackConsent); Assert.False(vm.CanImportPack);
            window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("PreviewMrpackButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle(vm);
            window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<CheckBox>("MrpackConsent")!.IsChecked = true;
            window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("ImportMrpackButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle(vm);
            Assert.True(vm.LastOperationSucceeded, vm.Status); Assert.Equal("Renamed import", Assert.Single(vm.Profiles).Name); Assert.Equal(100, vm.PackProgress);
            Assert.False(services.Ai.Enabled); Assert.Empty(services.Authentication.GetAccounts()); Assert.False(File.Exists(services.Paths.Receipt(vm.SelectedProfile!.Id)));
        }
        finally { await Idle(vm); window.DataContext = null; window.Close(); }
    }
    [AvaloniaFact] public async Task EightLocalesAndSourceSwitchingPreserveMrpackAndZipWorkflows()
    {
        using var f = new MrpackFixture(); using var services = new LauncherServices(f.Workspace.Paths); await services.Store.InitializeAsync(); await services.Store.SetSettingAsync("ui.language", "en");
        var vm = new MainViewModel(services); await vm.InitializeAsync(); var window = new MainWindow { DataContext = vm }; window.Show();
        try
        {
            await vm.ChooseInstancePackAsync(InstancePackTests.Archive(f, "wrapper/")); Assert.True(vm.IsInstancePack);
            foreach (var language in TextCatalog.Languages)
            {
                vm.SelectedLanguage = language; await Idle(vm); TextCatalog.ValidateCatalog(language.Code, TextCatalog.Catalog(language.Code));
                Assert.Equal(TextCatalog.Get("InstanceChooseDirectory"), window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("ChooseInstanceDirectoryButton")!.Content);
                Assert.Equal(TextCatalog.Get("InstancePolicy"), window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<TextBlock>("InstancePolicy")!.Text); Assert.Contains("wrapper", vm.InstanceLayout);
            }
            vm.PackConsent = true; await vm.ChoosePackAsync(f.Pack()); Assert.False(vm.IsInstancePack); Assert.False(vm.PackConsent); Assert.False(vm.IsZipPack); Assert.Contains("1.21.1", vm.PackSummary);
            vm.ZipMinecraft = "1.21.1"; await vm.ChooseZipPackAsync(ZipPackTests.Zip(f, ("config/local.txt", [1]))); Assert.True(vm.IsZipPack); Assert.False(vm.IsInstancePack); Assert.False(vm.PackConsent);
        }
        finally { await Idle(vm); window.DataContext = null; window.Close(); TextCatalog.Apply("uk"); }
    }
    [AvaloniaFact] public async Task StaleSourceBlocksApplyAndInterruptedPublicationUsesSharedUiRecovery()
    {
        using var f = new MrpackFixture(); using var services = new LauncherServices(f.Workspace.Paths); await services.Store.InitializeAsync(); await services.Store.SetSettingAsync("ui.language", "en");
        var vm = new MainViewModel(services); await vm.InitializeAsync(); var window = new MainWindow { DataContext = vm }; window.Show();
        try
        {
            var source = InstancePackTests.DirectorySource(f); await vm.ChooseInstancePackAsync(source); vm.PackConsent = true;
            InstancePackTests.Put(source, "minecraft/config/new.txt", [2]); await vm.ImportPackAsync(); Assert.False(vm.LastOperationSucceeded); Assert.Empty(vm.Profiles); Assert.False(vm.CanImportPack);
            await vm.PreviewPackAsync(); vm.PackConsent = true; services.Modpacks.Fault = s => { if (s == "moved") throw new IOException("Controlled interruption"); };
            await vm.ImportPackAsync(); Assert.False(vm.LastOperationSucceeded); Assert.Empty(vm.Profiles); Assert.Contains("1", vm.PackPending);
            services.Modpacks.Fault = null; vm.PackConsent = true; window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("RecoverMrpackButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle(vm);
            Assert.True(vm.LastOperationSucceeded, vm.Status); Assert.Single(vm.Profiles); Assert.Contains("0", vm.PackPending);
        }
        finally { await Idle(vm); window.DataContext = null; window.Close(); }
    }
}
