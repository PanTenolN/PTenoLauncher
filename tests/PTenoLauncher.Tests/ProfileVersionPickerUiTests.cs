using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using System.Net;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class ProfileVersionPickerUiTests
{
    [AvaloniaFact]
    public async Task ForgeCatalogUsesMinecraftCompatibilityAndCanReuseTheCacheOffline()
    {
        using var ws = new TestWorkspace();
        using var transport = new StubHandler(request =>
        {
            if (request.RequestUri?.Host == "files.minecraftforge.net")
                return new(HttpStatusCode.OK) { Content = new StringContent("""{"promos":{"1.21.1-recommended":"52.0.1"}}""") };
            Assert.Equal("maven.minecraftforge.net", request.RequestUri?.Host);
            return new(HttpStatusCode.OK) { Content = new StringContent("""
                <metadata><groupId>net.minecraftforge</groupId><artifactId>forge</artifactId><versioning><versions>
                <version>1.21.1-52.0.1</version><version>1.20.1-47.2.0</version>
                </versions></versioning></metadata>
                """) };
        });
        using var services = new LauncherServices(ws.Paths, coreTransport: transport);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.ProfileName = "Forge";
        vm.Loader = ModLoader.Forge;
        vm.VersionId = "1.21.1";
        await vm.LoadEditorLoaderVersionsAsync(CancellationToken.None);
        vm.EditorLoaderVersion = Assert.Single(vm.EditorLoaderVersions);
        Assert.Equal("1.21.1-52.0.1", vm.EditorLoaderVersion);
        Assert.Equal("LoaderRecommended", Assert.Single(vm.EditorLoaderOptions).Badge);
        vm.VersionId = "1.20.1";
        Assert.Empty(vm.EditorLoaderVersions);
        Assert.Null(vm.EditorLoaderVersion);
        vm.Offline = true;
        await vm.LoadEditorLoaderVersionsAsync(CancellationToken.None);
        Assert.True(vm.LastOperationSucceeded, vm.Status);
        vm.EditorLoaderVersion = Assert.Single(vm.EditorLoaderVersions);
        await vm.SaveCommand.ExecuteAsync();
        Assert.True(vm.LastOperationSucceeded, vm.Status);
        Assert.Equal("1.20.1-47.2.0", Assert.Single(await services.Store.GetProfilesAsync()).PreferredLoaderVersion);
        Assert.Equal(2, transport.Requests);
    }

    [AvaloniaFact]
    public async Task RestartRestoresSavedMicrosoftConfigurationAndAccountFromSameDataFolder()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var ws = new TestWorkspace();
        using var services = new LauncherServices(ws.Paths);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.Offline = true;
        var clientId = Guid.NewGuid().ToString();
        vm.ClientId = clientId;
        await vm.SignInCommand.ExecuteAsync(); // Offline prevents a real sign-in.
        Assert.Equal(clientId, await services.Store.GetSettingAsync("microsoft.clientId"));

        var uuid = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        new PTenoLauncher.Infrastructure.Security.SecretVault(ws.Paths).Write("minecraft-accounts",
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                ActiveUuid = uuid,
                Accounts = new[] { new { MicrosoftId = "synthetic", Session = new MinecraftSession(new(uuid, "FixturePlayer", now), "fixture", now.AddHours(1)) } }
            }, AtomicFile.JsonOptions));
        using var restartedServices = new LauncherServices(ws.Paths);
        var restarted = new MainViewModel(restartedServices);
        await restarted.InitializeAsync();
        Assert.Equal(clientId, restarted.ClientId);
        Assert.Equal(uuid, restarted.AccountUuid);
    }

    [AvaloniaFact]
    public async Task FabricChoiceIsLoadedForTheSelectedMinecraftAndPersisted()
    {
        using var ws = new TestWorkspace();
        using var transport = new StubHandler(request =>
        {
            Assert.Equal("https://meta.fabricmc.net/v2/versions/loader/1.21.1", request.RequestUri?.AbsoluteUri);
            return new(HttpStatusCode.OK) { Content = new StringContent(
                """[{"loader":{"version":"0.16.10","stable":true},"launcherMeta":{}},{"loader":{"version":"0.16.9","stable":true},"launcherMeta":{}}]""") };
        });
        using var services = new LauncherServices(ws.Paths, coreTransport: transport);
        var now = DateTimeOffset.UtcNow;
        ws.Paths.Initialize();
        await AtomicFile.WriteJsonAsync(Path.Combine(ws.Paths.Cache, "metadata", "catalog.json"),
            new VersionCatalog(now, "1.21.1", [new("1.21.1", "release", new("https://piston-meta.mojang.com/version.json"), new string('a', 40), now)], null));
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        var owner = new Window(); owner.Show();
        var dialog = new ProfileEditor { DataContext = vm };
        try
        {
            var result = dialog.ShowDialog<bool>(owner);
            dialog.FindControl<ListBox>("MinecraftVersionList")!.SelectedIndex = 0;
            vm.Loader = ModLoader.Fabric;
            dialog.FindControl<Button>("RefreshProfileLoaderVersions")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; vm.IsBusy && i < 300; i++) await Task.Delay(10);
            Assert.True(vm.LastOperationSucceeded, vm.Status);
            var versions = dialog.FindControl<ComboBox>("ProfileLoaderVersionList")!;
            var options = versions.Items.Cast<EditorLoaderOption>().ToArray();
            Assert.Equal(["0.16.10", "0.16.9"], options.Select(o => o.Version));
            Assert.Equal("LoaderLatestStable", options[0].Badge);
            Assert.Null(options[1].Badge);
            versions.SelectedItem = options[1];
            dialog.FindControl<TextBox>("ProfileNameInput")!.Text = "Fabric catalog test";
            vm.Offline = true;
            dialog.FindControl<Button>("SaveProfileButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await result.WaitAsync(TimeSpan.FromSeconds(10)));
            var saved = Assert.Single(await services.Store.GetProfilesAsync());
            Assert.Equal("0.16.9", saved.PreferredLoaderVersion);
            Assert.Null(saved.JavaPath);
            Assert.True(vm.CanRetryJava);
            Assert.Equal(PTenoLauncher.Desktop.Localization.TextCatalog.Get("ProfileJavaOffline"), vm.ProfileJavaStatus);
            var restarted = new MainViewModel(services);
            await restarted.InitializeAsync();
            Assert.Equal("0.16.9", restarted.SelectedProfile?.PreferredLoaderVersion);
            Assert.Equal("0.16.9", await PTenoLauncher.Infrastructure.Modpacks.MrpackRequirements.RequestedAsync(ws.Paths, saved));
            Assert.Equal(1, transport.Requests);
            var main = new MainWindow { DataContext = vm };
            main.Show();
            try
            {
                vm.Page = LauncherPage.Profiles;
                Assert.True(main.FindControl<Button>("ProfilesFolderButton")!.IsVisible);
                main.FindControl<Button>("ProfilesEditButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(LauncherPage.ProfileWorkspace, vm.Page);
                Assert.Equal(saved.Id, vm.WorkspaceProfileId);
                main.FindControl<Button>("ProfileModsSection")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(ProfileSection.Mods, vm.WorkspaceSection);
                Assert.True(main.FindControl<Button>("WorkspaceCatalogButton")!.IsEffectivelyVisible);
                for (var i = 0; vm.IsBusy && i < 300; i++) await Task.Delay(10);
                Assert.False(vm.IsBusy);
            }
            finally { main.DataContext = null; main.Close(); }
        }
        finally { dialog.Close(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task SearchFiltersCatalogAndSavingKeepsExplicitChoiceEvenIfHidden()
    {
        using var ws = new TestWorkspace();
        using var transport = new StubHandler(_ => throw new InvalidOperationException("No network expected"));
        using var services = new LauncherServices(ws.Paths, coreTransport: transport);
        await services.Store.InitializeAsync();
        await services.Store.SetSettingAsync("ui.language", "en");
        var now = DateTimeOffset.UtcNow;
        VersionEntry Entry(string id, string type, int days) =>
            new(id, type, new("https://piston-meta.mojang.com/version.json"), new string('a', 40), now.AddDays(days));
        await AtomicFile.WriteJsonAsync(Path.Combine(ws.Paths.Cache, "metadata", "catalog.json"),
            new VersionCatalog(now, "1.21.8",
                [Entry("1.21.1", "release", -10), Entry("25w01a", "snapshot", 0), Entry("1.21.8", "release", -1),
                    Entry("b1.7.3", "old_beta", -100)], "25w01a"));
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.Offline = true;
        await vm.NewCommand.ExecuteAsync();
        var owner = new Window(); owner.Show();
        var dialog = new ProfileEditor { DataContext = vm };
        try
        {
            var result = dialog.ShowDialog<bool>(owner);
            var list = dialog.FindControl<ListBox>("MinecraftVersionList")!;
            var search = dialog.FindControl<TextBox>("VersionSearchInput")!;
            var save = dialog.FindControl<Button>("SaveProfileButton")!;
            var name = dialog.FindControl<TextBox>("ProfileNameInput")!;
            var hint = dialog.FindControl<TextBlock>("MissingProfileFieldsHint")!;
            var nameBorder = Assert.IsType<Border>(name.Parent);
            Assert.True(save.IsEnabled);
            Assert.False(hint.IsVisible);
            Assert.DoesNotContain("missing", nameBorder.Classes);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(hint.IsVisible);
            Assert.Contains("missing", nameBorder.Classes);
            Assert.Same(name, dialog.FocusManager?.GetFocusedElement());
            Assert.Equal(["1.21.8", "1.21.1"], list.Items.Cast<VersionEntry>().Select(v => v.Id));
            search.Text = "1.21.1";
            list.SelectedItem = Assert.Single(list.Items.Cast<VersionEntry>());
            Assert.Equal("1.21.1", vm.VersionId);
            search.Text = "25W";
            Assert.Empty(list.Items);
            vm.ShowSnapshots = true;
            Assert.Equal("25w01a", Assert.Single(list.Items.Cast<VersionEntry>()).Id);
            Assert.Null(list.SelectedItem);
            Assert.Equal("1.21.1", vm.VersionId);
            search.Text = "";
            Assert.Equal("1.21.1", Assert.IsType<VersionEntry>(list.SelectedItem).Id);
            vm.ShowReleases = false;
            vm.ShowHistorical = true;
            Assert.Equal(["25w01a", "b1.7.3"], list.Items.Cast<VersionEntry>().Select(v => v.Id));
            Assert.True(save.IsEnabled);
            name.Text = "Catalog test";
            Assert.False(hint.IsVisible);
            Assert.DoesNotContain("missing", nameBorder.Classes);
            Assert.True(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await result.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal("1.21.1", vm.SelectedProfile!.MinecraftVersion);
            Assert.Equal(0, transport.Requests);
        }
        finally { dialog.Close(); owner.Close(); }
    }
}
