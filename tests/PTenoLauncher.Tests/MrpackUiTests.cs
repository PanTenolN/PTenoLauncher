using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using Xunit;
namespace PTenoLauncher.Tests;

public sealed class MrpackUiTests
{
    private static async Task Idle(MainViewModel vm)
    { for (int i = 0; vm.IsBusy && i < 400; i++) await Task.Delay(10); Assert.False(vm.IsBusy); }
    [AvaloniaFact] public async Task ActualControlsRequirePreviewConsentAndCreateANewOfflineProfileWithoutAnAccount()
    {
        using var f = new MrpackFixture(); using var services = new LauncherServices(f.Workspace.Paths);
        await services.Store.InitializeAsync(); await services.Store.SetSettingAsync("ui.language", "en");
        var vm = new MainViewModel(services); await vm.InitializeAsync(); vm.Page = LauncherPage.Modpacks; vm.Offline = true;
        var window = new MainWindow { DataContext = vm }; window.Show();
        try
        {
            Assert.NotNull(window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("ChooseMrpackButton"));
            var pack = f.Pack(null, ("overrides/config/example.txt", [1, 2, 3])); await vm.ChoosePackAsync(pack);
            Assert.Contains("1.21.1", window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<TextBlock>("MrpackSummary")!.Text);
            Assert.NotEmpty(vm.PackFiles); Assert.False(window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("ImportMrpackButton")!.IsEnabled);
            window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<CheckBox>("MrpackConsent")!.IsChecked = true; Assert.True(vm.CanImportPack);
            vm.PackName = "Changed profile"; Assert.False(vm.PackConsent); Assert.False(vm.CanImportPack);
            window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("PreviewMrpackButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle(vm);
            window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<CheckBox>("MrpackConsent")!.IsChecked = true;
            window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("ImportMrpackButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle(vm);
            Assert.True(vm.LastOperationSucceeded, vm.Status); Assert.Equal("Changed profile", Assert.Single(vm.Profiles).Name);
            Assert.Equal(100, vm.PackProgress); Assert.False(services.Ai.Enabled); Assert.Empty(services.Authentication.GetAccounts());
            Assert.False(System.IO.File.Exists(services.Paths.Receipt(vm.SelectedProfile!.Id))); Assert.False(vm.PackConsent);
        }
        finally { window.DataContext = null; window.Close(); }
    }
    [AvaloniaFact] public async Task OptionalChangeInvalidatesConfirmationAndAllEightLanguagesReachActualControls()
    {
        using var f = new MrpackFixture(); using var services = new LauncherServices(f.Workspace.Paths); await services.Store.InitializeAsync(); await services.Store.SetSettingAsync("ui.language", "en");
        var vm = new MainViewModel(services); await vm.InitializeAsync(); vm.Page = LauncherPage.Modpacks;
        var window = new MainWindow { DataContext = vm }; window.Show();
        try
        {
            await vm.ChoosePackAsync(f.Pack(MrpackFixture.Index([f.File(client: "optional")]))); Assert.False(Assert.Single(vm.PackOptionalFiles).Selected);
            vm.PackConsent = true; Assert.True(vm.CanImportPack); vm.PackOptionalFiles[0].Selected = true; Assert.False(vm.PackConsent); Assert.False(vm.CanImportPack);
            foreach (var language in TextCatalog.Languages)
            {
                vm.SelectedLanguage = language; await Idle(vm); TextCatalog.ValidateCatalog(language.Code, TextCatalog.Catalog(language.Code));
                Assert.Equal(TextCatalog.Get("InstallPackUi"), window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("ImportMrpackButton")!.Content);
                Assert.Equal(TextCatalog.Get("PackPreview"), window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("PreviewMrpackButton")!.Content);
                Assert.Equal(TextCatalog.Get("PackChoose"), window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("ChooseMrpackButton")!.Content);
                Assert.Equal(TextCatalog.Get("PackRecover"), window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("RecoverMrpackButton")!.Content);
            }
        }
        finally { await Idle(vm); window.DataContext = null; window.Close(); TextCatalog.Apply("uk"); }
    }
    [AvaloniaFact] public async Task InterruptedReadyImportIsVisibleAndConfirmedRecoveryPublishesTheVerifiedProfile()
    {
        using var f = new MrpackFixture(); using var services = new LauncherServices(f.Workspace.Paths); await services.Store.InitializeAsync(); await services.Store.SetSettingAsync("ui.language", "en");
        var vm = new MainViewModel(services); await vm.InitializeAsync(); var window = new MainWindow { DataContext = vm }; window.Show();
        try
        {
            await vm.ChoosePackAsync(f.Pack(null, ("overrides/options.txt", [1]))); vm.PackConsent = true;
            services.Modpacks.Fault = stage => { if (stage == "ready") throw new IOException("Synthetic interruption"); };
            await vm.ImportPackAsync(); Assert.Empty(vm.Profiles); Assert.Contains("1", vm.PackPending);
            services.Modpacks.Fault = null; window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("RecoverMrpackButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle(vm); Assert.Empty(vm.Profiles);
            vm.PackConsent = true; window.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("RecoverMrpackButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle(vm);
            Assert.True(vm.LastOperationSucceeded, vm.Status); Assert.Single(vm.Profiles); Assert.Contains("0", vm.PackPending);
        }
        finally { window.DataContext = null; window.Close(); }
    }
}
