using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using Xunit;
namespace PTenoLauncher.Tests;
public sealed class MinecraftLauncherUiTests
{
    private static async Task Idle(MainViewModel vm){for(int i=0;vm.IsBusy&&i<600;i++)await Task.Delay(10);Assert.False(vm.IsBusy);}
    [AvaloniaFact]public async Task ActualControlsRequireProfileDirectoryFreshPreviewAndConsentOffline()
    {
        using var f=new MrpackFixture();using var services=new LauncherServices(f.Workspace.Paths);await services.Store.InitializeAsync();await services.Store.SetSettingAsync("ui.language","en");var vm=new MainViewModel(services);await vm.InitializeAsync();vm.Offline=true;vm.Page=LauncherPage.Modpacks;var w=new MainWindow{DataContext=vm};w.Show();try
        {
            var s=MinecraftLauncherTests.Source(f,external:true);await vm.ChooseMinecraftPackAsync(s.File);Assert.True(vm.IsMinecraftPack);Assert.False(vm.CanImportPack);Assert.Equal(2,vm.MinecraftProfileChoices.Count);
            w.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<ComboBox>("MinecraftImportProfile")!.SelectedItem=vm.MinecraftProfileChoices.Single(c=>c.Key=="selected");await vm.PreviewPackAsync();Assert.Contains(s.Content,vm.MinecraftImportRequiredContent);Assert.False(vm.CanImportPack);
            vm.SetMinecraftImportContent(s.Content);w.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("PreviewMrpackButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Idle(vm);Assert.True(vm.LastOperationSucceeded,vm.Status);
            w.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<CheckBox>("MrpackConsent")!.IsChecked=true;Assert.True(vm.CanImportPack);vm.PackName="Independent renamed";Assert.False(vm.PackConsent);
            await vm.PreviewPackAsync();vm.PackConsent=true;w.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("ImportMrpackButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Idle(vm);Assert.True(vm.LastOperationSucceeded,vm.Status);Assert.Equal("Independent renamed",Assert.Single(vm.Profiles).Name);Assert.False(services.Ai.Enabled);Assert.Empty(services.Authentication.GetAccounts());Assert.False(File.Exists(services.Paths.Receipt(vm.SelectedProfile!.Id)));
        }finally{await Idle(vm);w.DataContext=null;w.Close();}
    }
    [AvaloniaFact]public async Task ProfileSelectionInvalidatesConsentAndExistingImportModesRemainAvailableInEightLanguages()
    {
        using var f=new MrpackFixture();using var services=new LauncherServices(f.Workspace.Paths);await services.Store.InitializeAsync();await services.Store.SetSettingAsync("ui.language","en");var vm=new MainViewModel(services);await vm.InitializeAsync();var w=new MainWindow{DataContext=vm};w.Show();try
        {
            var s=MinecraftLauncherTests.Source(f);await vm.ChooseMinecraftPackAsync(s.File);vm.SelectedMinecraftImportProfile=vm.MinecraftProfileChoices.Single(c=>c.Key=="selected");vm.SetMinecraftImportContent(s.Content);await vm.PreviewPackAsync();vm.PackConsent=true;Assert.True(vm.CanImportPack);
            foreach(var lang in TextCatalog.Languages){vm.SelectedLanguage=lang;await Idle(vm);Assert.Equal(TextCatalog.Get("MinecraftImportChooseFile"),w.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("ChooseMinecraftLauncherButton")!.Content);TextCatalog.ValidateCatalog(lang.Code,TextCatalog.Catalog(lang.Code));}
            vm.SelectedMinecraftImportProfile=vm.MinecraftProfileChoices.Single(c=>c.Key=="other");Assert.False(vm.CanImportPack);Assert.False(vm.PackConsent);Assert.Equal("",vm.MinecraftImportContent);
            await vm.ChooseInstancePackAsync(InstancePackTests.DirectorySource(f));Assert.True(vm.IsInstancePack);Assert.False(vm.IsMinecraftPack);await vm.ChoosePackAsync(f.Pack());Assert.False(vm.IsMinecraftPack);Assert.False(vm.IsInstancePack);Assert.NotNull(w.FindControl<PTenoLauncher.Desktop.Dialogs.ProfileImportView>("MainProfileImport")!.FindControl<Button>("ChooseZipPackButton"));
        }finally{await Idle(vm);w.DataContext=null;w.Close();TextCatalog.Apply("uk");}
    }
    [AvaloniaFact]public async Task InterruptedImportUsesSharedUiRecoveryWithoutReadingAccountsOrInstallingGame()
    {
        using var f=new MrpackFixture();using var services=new LauncherServices(f.Workspace.Paths);await services.Store.InitializeAsync();var vm=new MainViewModel(services);await vm.InitializeAsync();var s=MinecraftLauncherTests.Source(f);await vm.ChooseMinecraftPackAsync(s.File);vm.SelectedMinecraftImportProfile=vm.MinecraftProfileChoices.Single(c=>c.Key=="selected");vm.SetMinecraftImportContent(s.Content);await vm.PreviewPackAsync();vm.PackConsent=true;
        services.Modpacks.Fault=stage=>{if(stage=="moved")throw new IOException("Controlled interruption");};await vm.ImportPackAsync();Assert.Empty(vm.Profiles);Assert.Contains("1",vm.PackPending);services.Modpacks.Fault=null;vm.PackConsent=true;await vm.RecoverPacksAsync();Assert.Single(vm.Profiles);Assert.Empty(services.Authentication.GetAccounts());
    }
}
