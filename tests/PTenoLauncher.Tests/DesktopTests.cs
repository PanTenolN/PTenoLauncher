using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Desktop.ViewModels;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(PTenoLauncher.Tests.TestAppBuilder))]
namespace PTenoLauncher.Tests;
public sealed class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
public sealed class DesktopTests
{
    [AvaloniaFact]
    public async Task VisibleSignInButtonsUseTheApplicationClientAndTrackAccountAndLogout()
    {
        using var ws = new TestWorkspace(); using var services = new LauncherServices(ws.Paths);
        var vm = new MainViewModel(services); await vm.InitializeAsync();
        Assert.True(Guid.TryParse(vm.ClientId, out var clientId)); Assert.NotEqual(Guid.Empty, clientId);
        Assert.Equal(PTenoLauncher.Infrastructure.Authentication.LauncherMicrosoftApplication.ClientId, vm.ClientId);
        var window = new MainWindow { DataContext = vm }; window.Show();
        try
        {
            var header = window.FindControl<Button>("HeaderSignInButton")!;
            var accountButton = window.FindControl<Button>("AccountSignInButton")!;
            var name = window.FindControl<TextBlock>("HeaderAccountName")!;
            Assert.True(header.IsVisible); Assert.True(accountButton.IsVisible); Assert.False(name.IsVisible);
            Assert.Equal(PTenoLauncher.Desktop.Localization.TextCatalog.Get("PerformSignIn"), header.Content);
            Assert.Same(vm.SignInCommand, header.Command); Assert.Same(vm.SignInCommand, accountButton.Command);
            vm.Offline = true;
            await Assert.IsType<AsyncCommand>(header.Command).ExecuteAsync();
            Assert.False(vm.LastOperationSucceeded); Assert.Contains("офлайн", vm.Status);
            Assert.Equal(LauncherPage.Home, vm.Page); // The header directly starts sign-in, not Settings navigation.

            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var operation = vm.RunAsync(_ => waiting.Task);
            try { Assert.False(header.IsEnabled); }
            finally { waiting.TrySetResult(); await operation; }

            // Synthetic protected test state only; no Microsoft/Xbox/game call or real user credential is used.
            var session = new MinecraftSession(new(new string('a', 32), "SyntheticHeaderAccount", DateTimeOffset.UtcNow),
                "synthetic-header-token", DateTimeOffset.UtcNow.AddHours(1));
            new PTenoLauncher.Infrastructure.Security.SecretVault(ws.Paths).Write("minecraft-accounts",
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
                {
                    ActiveUuid = session.Account.Uuid,
                    Accounts = new[] { new { MicrosoftId = "", Session = session } } // Legacy fixture has no MSAL mapping to query.
                }, PTenoLauncher.Infrastructure.Storage.AtomicFile.JsonOptions));
            await vm.InitializeAsync();
            Assert.False(header.IsVisible); Assert.False(accountButton.IsVisible); Assert.True(name.IsVisible);
            Assert.Equal("SyntheticHeaderAccount", name.Text);
            await vm.SignOutCommand.ExecuteAsync();
            Assert.True(vm.LastOperationSucceeded, vm.Status); Assert.True(header.IsVisible); Assert.False(name.IsVisible);

            var custom = Guid.NewGuid().ToString();
            await services.Store.SetSettingAsync("microsoft.clientId", custom);
            await vm.InitializeAsync(); Assert.Equal(custom, vm.ClientId); // Advanced override remains supported.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ModalEditorLoadsRealXamlAndSavesThroughBindings()
    {
        using var ws = new TestWorkspace(); using var services = new LauncherServices(ws.Paths);
        var vm = new MainViewModel(services); var window = new MainWindow { DataContext = vm }; window.Show();
        var dialog = new ProfileEditor { DataContext = vm };
        try
        {
            await vm.InitializeAsync();
            Assert.NotNull(window.FindControl<Button>("CreateProfileButton"));
            Assert.Null(window.FindControl<TextBox>("ProfileNameInput"));
            vm.Offline = true; dialog.Show(window);
            var input = dialog.FindControl<TextBox>("ProfileNameInput")!;
            input.Focus(); dialog.KeyTextInput("Headless profile");
            vm.VersionId = "1.21.1";
            await vm.SaveCommand.ExecuteAsync();
            Assert.Equal("Headless profile", Assert.Single(await services.Store.GetProfilesAsync()).Name);
            Assert.False(vm.IsBusy); Assert.True(vm.HasProfile);
        }
        finally { dialog.Close(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task ThemeSwitchesAtRuntimePersistsAndAiDefaultsOff()
    {
        using var ws = new TestWorkspace(); using var services = new LauncherServices(ws.Paths);
        var vm = new MainViewModel(services); await vm.InitializeAsync();
        Assert.False(vm.AiEnabled);
        vm.SelectedTheme = LauncherTheme.Light; await vm.SaveThemeAsync();
        Assert.Equal(Color.Parse("#F2F3F5"), ((SolidColorBrush)Application.Current!.Resources["WindowBrush"]!).Color);
        vm.SelectedTheme = LauncherTheme.MidnightBlack; await vm.SaveThemeAsync();
        Assert.Equal(Color.Parse("#08090A"), ((SolidColorBrush)Application.Current.Resources["WindowBrush"]!).Color);
        Assert.Equal(LauncherTheme.MidnightBlack, (await services.Preferences.GetThemeAsync()).Theme);
        vm.SelectedTheme = LauncherTheme.GraphiteDark; await vm.SaveThemeAsync();
    }
    [AvaloniaFact]
    public async Task ModsNavigationIsHiddenForVanillaAndReturnsHomeWhenSwitching()
    {
        using var ws = new TestWorkspace(); using var services = new LauncherServices(ws.Paths);
        var vm = new MainViewModel(services); await vm.InitializeAsync();
        var window = new MainWindow { DataContext = vm }; window.Show();
        try
        {
            var button = window.FindControl<Button>("ModsNavButton")!;
            Assert.False(vm.CanManageMods); Assert.False(button.IsVisible);
            foreach (var loader in new[] {ModLoader.Fabric, ModLoader.Forge, ModLoader.NeoForge, ModLoader.Quilt})
            {
                var modded = ws.Profile(loader); await services.Store.SaveProfileAsync(modded);
                vm.SelectedProfile = modded;
                Assert.True(vm.CanManageMods); Assert.True(button.IsVisible);
                Assert.Equal(loader == ModLoader.Fabric, vm.IsFabricProfile);
                Assert.Equal(loader == ModLoader.Quilt, vm.IsQuiltProfile);
                Assert.Equal(vm.IsQuiltProfile, window.FindControl<Border>("QuiltLoaderPanel")!.IsVisible);
                Assert.Equal(vm.IsFabricProfile, window.FindControl<Border>("FabricLoaderPanel")!.IsVisible);
                vm.Page = LauncherPage.Mods; Assert.True(vm.IsMods);
                vm.FabricStatus = "prior profile installation status"; vm.QuiltStatus = "prior profile installation status";
                var vanilla = modded with {Loader = ModLoader.Vanilla};
                await services.Store.SaveProfileAsync(vanilla); vm.SelectedProfile = vanilla;
                Assert.False(vm.CanManageMods); Assert.False(button.IsVisible);
                Assert.DoesNotContain("prior profile",vm.FabricStatus); Assert.DoesNotContain("prior profile",vm.QuiltStatus);
                Assert.False(vm.IsFabricProfile); Assert.False(window.FindControl<Border>("FabricLoaderPanel")!.IsVisible);
                Assert.Equal(LauncherPage.Home, vm.Page); Assert.False(vm.IsMods);
                vm.Page = LauncherPage.Mods; Assert.Equal(LauncherPage.Home, vm.Page);
            }
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task LocalModReviewDialogLoadsHashesAndRequiresExplicitAcceptance()
    {
        using var ws = new TestWorkspace(); using var services = new LauncherServices(ws.Paths);
        var vm = new MainViewModel(services); await vm.InitializeAsync();
        var profile = ws.Profile(ModLoader.Fabric); await services.Store.SaveProfileAsync(profile); vm.SelectedProfile=profile;
        var jar=Path.Combine(ws.Paths.Game(profile.Id),"mods","manual.jar"); SecurityAndModsTests.CreateJar(jar);
        var dialog=new ModBaselineDialog(vm); await dialog.RefreshAsync();
        Assert.Contains("mods/manual.jar",dialog.FindControl<TextBox>("FileList")!.Text);
        Assert.Contains("SHA256",dialog.FindControl<TextBox>("FileList")!.Text);
        var confirm=dialog.FindControl<Button>("ConfirmButton")!; Assert.False(confirm.IsEnabled);
        dialog.FindControl<CheckBox>("Acceptance")!.IsChecked=true; Assert.True(confirm.IsEnabled);
        var preview=await vm.GetModBaselineAsync(); await vm.ConfirmModBaselineAsync(preview);
        Assert.True(vm.LastOperationSucceeded); Assert.Contains("account",string.Join(";",(await services.Readiness.CheckAsync(profile)).Issues.Select(i=>i.Code)));
    }
    [AvaloniaFact]
    public async Task UnifiedLocalCartRequiresConfirmationAndInvalidatesAfterEditsOffline()
    {
        using var ws=new TestWorkspace();using var services=new LauncherServices(ws.Paths);
        var vm=new MainViewModel(services);await vm.InitializeAsync();vm.Offline=true;
        var p=ws.Profile(ModLoader.Fabric);await services.Store.SaveProfileAsync(p);vm.SelectedProfile=p;
        var first=Path.Combine(ws.Paths.Root,"first.jar");SecurityAndModsTests.CreateJar(first,"first");
        var second=Path.Combine(ws.Paths.Root,"second.jar");SecurityAndModsTests.CreateJar(second,"second");
        var window=new MainWindow{DataContext=vm};window.Show();
        try
        {
            vm.Page=LauncherPage.Mods;Assert.False(vm.IsBusy);Assert.NotNull(window.FindControl<Button>("ResolveModCartButton"));
            await vm.AddModsAsync([first]);Assert.True(vm.LastOperationSucceeded,vm.Status);await vm.ResolveCartAsync();Assert.True(vm.LastOperationSucceeded,vm.Status);
            Assert.True(vm.NeedsUnknownAcceptance);Assert.True(window.FindControl<CheckBox>("UnknownModAcceptance")!.IsVisible);
            vm.RemoteConfirmed=true;vm.UnknownSourceAccepted=true;
            await vm.AddModsAsync([second]);Assert.False(vm.RemoteConfirmed);Assert.False(vm.UnknownSourceAccepted);
            await vm.InstallCartCommand.ExecuteAsync();Assert.False(vm.LastOperationSucceeded);Assert.Empty(Directory.GetFiles(Path.Combine(ws.Paths.Game(p.Id),"mods")));
            await vm.ResolveCartAsync();vm.RemoteConfirmed=true;
            await vm.InstallCartCommand.ExecuteAsync();Assert.False(vm.LastOperationSucceeded);
            vm.UnknownSourceAccepted=true;await vm.InstallCartCommand.ExecuteAsync();Assert.True(vm.LastOperationSucceeded);
            Assert.Equal(2,Directory.GetFiles(Path.Combine(ws.Paths.Game(p.Id),"mods")).Length);Assert.Empty(vm.Cart);Assert.False(vm.RemoteConfirmed);
        }
        finally{window.Close();}
    }
    [AvaloniaFact]
    public async Task OptionalCheckboxInvalidatesConfirmationAndReplansUsingCachedRealMetadataShape()
    {
        using var ws=new TestWorkspace();await ws.Store.InitializeAsync();var p=ws.Profile(ModLoader.Fabric);await ws.Store.SaveProfileAsync(p);
        using var fixture=new LocalDependencyTests.Fixture(ws);var input=await fixture.Input();
        var initial=await fixture.Installer.PreviewLocalAsync(p,[input]);var key=Assert.Single(initial.Remote.Resolution.Optional).Key;
        await fixture.Installer.PreviewLocalAsync(p,[input],optionalSelections:[key]); // Warm only metadata; no profile mods installed.
        using var services=new LauncherServices(ws.Paths);var vm=new MainViewModel(services);await vm.InitializeAsync();vm.Offline=true;vm.SelectedProfile=p;
        await vm.AddModsAsync([input.SourcePath]);await vm.ResolveCartAsync();Assert.True(vm.LastOperationSucceeded,vm.Status);
        var window=new MainWindow{DataContext=vm};window.Show();
        try
        {
            vm.Page=LauncherPage.Mods;window.UpdateLayout();
            Assert.NotNull(window.FindControl<ItemsControl>("OptionalDependencyList"));
            var choice=Assert.Single(vm.OptionalChoices);Assert.False(choice.IsSelected);Assert.DoesNotContain(vm.RemoteEntries,e=>e.Version.ProjectId=="D");
            var checkbox=window.GetVisualDescendants().OfType<CheckBox>().Single(b=>ReferenceEquals(b.DataContext,choice));
            vm.RemoteConfirmed=true;checkbox.IsChecked=true;Assert.True(choice.IsSelected);Assert.False(vm.RemoteConfirmed);
            await vm.InstallCartCommand.ExecuteAsync();Assert.False(vm.LastOperationSucceeded);
            await vm.ResolveCartAsync();Assert.True(vm.LastOperationSucceeded,vm.Status);
            Assert.Contains(vm.RemoteEntries,e=>e.Version.ProjectId=="D");Assert.True(Assert.Single(vm.OptionalChoices).IsSelected);Assert.False(vm.RemoteConfirmed);
            await vm.ClearOptionalAsync();Assert.True(vm.LastOperationSucceeded,vm.Status);Assert.DoesNotContain(vm.RemoteEntries,e=>e.Version.ProjectId=="D");
            Assert.Empty(Directory.GetFiles(Path.Combine(ws.Paths.Game(p.Id),"mods")));
        }
        finally{window.Close();}
    }
    [AvaloniaFact]
    public async Task ModsFolderButtonTargetsOnlyTheSelectedModdedProfile()
    {
        using var ws=new TestWorkspace();using var services=new LauncherServices(ws.Paths);var vm=new MainViewModel(services);await vm.InitializeAsync();
        var a=ws.Profile(ModLoader.Fabric);var b=ws.Profile(ModLoader.Quilt);await services.Store.SaveProfileAsync(a);await services.Store.SaveProfileAsync(b);
        var window=new MainWindow{DataContext=vm};window.Show();
        try
        {
            Assert.NotNull(window.FindControl<Button>("OpenModsFolderButton"));
            vm.SelectedProfile=a;Assert.Equal(Path.Combine(ws.Paths.Game(a.Id),"mods"),vm.GetModsFolderPath());
            vm.SelectedProfile=b;Assert.Equal(Path.Combine(ws.Paths.Game(b.Id),"mods"),vm.GetModsFolderPath());
            vm.SelectedProfile=a with{Loader=ModLoader.Vanilla};Assert.Throws<InvalidOperationException>(()=>vm.GetModsFolderPath());
        }
        finally{window.Close();}
    }
}
