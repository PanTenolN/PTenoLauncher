using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Diagnostics;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class ProfileWorkspaceTests
{
    [AvaloniaFact]
    public async Task PartialAndMisspelledKotlinQueriesKeepTheProfileFiltersAndRankTheExactMod()
    {
        using var ws = new TestWorkspace();
        var queries = new List<string>();
        using var transport = new StubHandler(request =>
        {
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            queries.Add(query);
            return new(System.Net.HttpStatusCode.OK) { Content = new StringContent("""
                {"hits":[{"project_id":"Other123","slug":"other","title":"Other Language","description":"fixture"},
                {"project_id":"Kotlin12","slug":"fabric-language-kotlin","title":"Fabric Language Kotlin","description":"fixture"}]}
                """) };
        });
        using var services = new LauncherServices(ws.Paths, coreTransport: transport);
        await services.Store.InitializeAsync();
        await services.Store.SaveProfileAsync(ws.Profile(ModLoader.Fabric));
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        foreach (var query in new[] { "kotlin language", "kothlin language" })
        {
            vm.RemoteQuery = query;
            await vm.SearchRemoteAsync();
            Assert.True(vm.LastOperationSucceeded, vm.Status);
            Assert.Equal("Fabric Language Kotlin", vm.RemoteSearchResults[0].Title);
        }
        var request = Assert.Single(queries);
        Assert.Contains("query=kotlin language", request);
        Assert.Contains("versions:1.21.1", request);
        Assert.Contains("categories:fabric", request);
    }

    [Fact]
    public async Task DownloadHistoryKeepsDistinctOutcomesAcrossStoreInstances()
    {
        using var ws = new TestWorkspace();
        await ws.Store.InitializeAsync();
        foreach (var result in new[] { "Succeeded", "Failed", "Cancelled", "Running" })
        {
            var id = Guid.NewGuid();
            await ws.Store.StartDownloadAsync(id, result, "Fixture profile");
            if (result != "Running") await ws.Store.FinishDownloadAsync(id, result);
        }
        // A different process start time models an interrupted operation without starting a child process.
        await using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ws.Paths.Database }.ToString()))
        {
            await db.OpenAsync();
            await using var update = db.CreateCommand();
            update.CommandText = "UPDATE download_history SET process_start = 0 WHERE result = 'Running'";
            await update.ExecuteNonQueryAsync();
        }
        var reopened = new PTenoLauncher.Infrastructure.Storage.SqliteLauncherStore(ws.Paths);
        var history = await reopened.GetDownloadHistoryAsync();
        Assert.Equal(new[] { "Cancelled", "Failed", "Interrupted", "Succeeded" }, history.Select(h => h.Result).Order());
        Assert.All(history, h => Assert.Equal("Fixture profile", h.Profile));
    }

    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task LocalImportChoiceUsesTheProfileCartAndInstallsOnlyItsPlan(bool find, bool offline)
    {
        using var ws = new TestWorkspace();
        using var fixture = new LocalDependencyTests.Fixture(ws);
        using var services = new LauncherServices(ws.Paths, coreTransport: fixture.Handler);
        await services.Store.InitializeAsync();
        var target = ws.Profile(ModLoader.Fabric);
        var other = ws.Profile(ModLoader.Fabric);
        await services.Store.SaveProfileAsync(target);
        await services.Store.SaveProfileAsync(other);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.Offline = offline;
        vm.OpenProfileWorkspace(target.Id);
        vm.ChooseWorkspaceSection(ProfileSection.Mods);
        var window = new MainWindow { DataContext = vm, Width = 980, Height = 680 };
        window.Show();
        try
        {
            var input = await fixture.Input();
            var bad = Path.Combine(ws.Paths.Root, "invalid.jar");
            await File.WriteAllTextAsync(bad, "invalid ZIP");
            await vm.AddModsAsync([input.SourcePath, bad]);
            Assert.False(vm.LastOperationSucceeded);
            Assert.Empty(vm.Cart);
            await vm.AddModsAsync([input.SourcePath]);
            vm.SelectedRemoteProject = new("A", "a", "Previous search result", "");
            var operation = window.OpenLocalCartAsync(vm, target.Id);
            var choice = Assert.Single(window.OwnedWindows.OfType<LocalDependenciesDialog>());
            choice.FindControl<Button>(find ? "FindDependencies" : "ManualOnly")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 500 && !window.OwnedWindows.OfType<ModCatalogDialog>().Any(); i++) await Task.Delay(10);
            var dialog = Assert.Single(window.OwnedWindows.OfType<ModCatalogDialog>());
            await Until(() => vm.IsIdle && vm.LastOperationSucceeded);
            Assert.Equal(!find, vm.ManualLocalOnly);
            Assert.Empty(Directory.GetFiles(Path.Combine(ws.Paths.Game(target.Id), "mods")));
            dialog.FindControl<CheckBox>("UnknownModAcceptance")!.IsChecked = true;
            var install = dialog.FindControl<Button>("InstallCombinedCartButton")!;
            Assert.True(install.IsEnabled, vm.Status);
            install.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await operation.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(find ? 3 : 1, Directory.GetFiles(Path.Combine(ws.Paths.Game(target.Id), "mods"), "*.jar").Length);
            Assert.Empty(Directory.GetFiles(Path.Combine(ws.Paths.Game(other.Id), "mods")));
            if (!find) Assert.Equal(0, fixture.Handler.Requests);

            static async Task Until(Func<bool> check)
            {
                for (var i = 0; i < 500 && !check(); i++) await Task.Delay(10);
                Assert.True(check());
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task InstalledModsStayAlphabeticalThroughFilteringRefreshAndLocalInstall()
    {
        using var ws = new TestWorkspace();
        using var services = new LauncherServices(ws.Paths);
        await services.Store.InitializeAsync();
        var profile = ws.Profile(ModLoader.Fabric);
        await services.Store.SaveProfileAsync(profile);
        var mods = Path.Combine(ws.Paths.Game(profile.Id), "mods");
        await File.WriteAllBytesAsync(Path.Combine(mods, "zzz-original.jar"), ModUpdateTests.Fixture.Jar("alpha", "1.0"));
        await File.WriteAllBytesAsync(Path.Combine(mods, "mmm-original.jar"), ModUpdateTests.Fixture.Jar("middle", "1.0"));
        await File.WriteAllBytesAsync(Path.Combine(mods, "aaa-original.jar"), ModUpdateTests.Fixture.Jar("zeta", "1.0"));
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.Offline = true;
        vm.OpenProfileWorkspace(profile.Id);
        vm.ChooseWorkspaceSection(ProfileSection.Mods);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            await vm.WaitForWorkspaceSnapshotAsync();
            var expected = new[] { "alpha", "middle", "zeta" };
            Assert.Equal(expected, vm.WorkspaceModRows.Select(row => row.Name));
            Assert.Null(window.FindControl<Button>("WorkspaceCartButton"));
            Assert.Null(window.FindControl<Button>("OrganizeModNames"));
            vm.WorkspaceModQuery = "original";
            Assert.Equal(expected, vm.WorkspaceModRows.Select(row => row.Name));
            vm.WorkspaceModQuery = "aaa";
            Assert.Equal("zeta", Assert.Single(vm.WorkspaceModRows).Name);
            vm.WorkspaceModQuery = "";
            vm.ChooseWorkspaceSection(ProfileSection.Overview);
            vm.ChooseWorkspaceSection(ProfileSection.Mods);
            Assert.Equal(expected, vm.WorkspaceModRows.Select(row => row.Name));
            await vm.RefreshVisibleWorkspaceAsync();
            Assert.Equal(expected, vm.WorkspaceModRows.Select(row => row.Name));

            var input = Path.Combine(ws.Paths.Root, "q-added.jar");
            await File.WriteAllBytesAsync(input, ModUpdateTests.Fixture.Jar("beta", "1.0"));
            await vm.AddModsAsync([input]);
            var review = window.OpenLocalCartAsync(vm, profile.Id);
            var choice = Assert.Single(window.OwnedWindows.OfType<LocalDependenciesDialog>());
            choice.FindControl<Button>("ManualOnly")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 500 && (!vm.IsIdle || !window.OwnedWindows.OfType<ModCatalogDialog>().Any()); i++) await Task.Delay(10);
            var cart = Assert.Single(window.OwnedWindows.OfType<ModCatalogDialog>());
            Assert.False(File.Exists(Path.Combine(mods, "q-added.jar")));
            cart.FindControl<CheckBox>("UnknownModAcceptance")!.IsChecked = true;
            var install = cart.FindControl<Button>("InstallCombinedCartButton")!;
            Assert.True(install.IsEnabled, vm.Status);
            install.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await review.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(new[] { "alpha", "beta", "middle", "zeta" }, vm.WorkspaceModRows.Select(row => row.Name));
            Assert.Equal(new[] { "aaa-original.jar", "mmm-original.jar", "q-added.jar", "zzz-original.jar" },
                Directory.GetFiles(mods, "*.jar").Select(Path.GetFileName).Order(StringComparer.Ordinal));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SettingsSectionsFitVerticallyAtMinimumWindowSize()
    {
        using var ws = new TestWorkspace();
        using var services = new LauncherServices(ws.Paths);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        var window = new MainWindow { DataContext = vm, Width = 980, Height = 680 };
        window.Show();
        try
        {
            vm.Page = LauncherPage.Settings;
            window.UpdateLayout();
            var settings = window.FindControl<TabControl>("SettingsTabs")!;
            Assert.Equal(Dock.Left, settings.TabStripPlacement);
            var sections = settings.Items.OfType<TabItem>().ToArray();
            Assert.True(sections.Length >= 7);
            foreach (var section in sections)
            {
                settings.SelectedItem = section;
                window.UpdateLayout();
                Assert.True(section.Bounds.Bottom <= settings.Bounds.Height);
                Assert.True(Assert.IsAssignableFrom<Control>(section.Content).Bounds.Width > 250);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task CatalogControlsInstallExactVersionAndDependencyOnlyInOpenProfile()
    {
        using var ws = new TestWorkspace();
        using var fixture = new ModrinthIntegrationTests.Fixture(ws);
        using var services = new LauncherServices(ws.Paths, coreTransport: fixture.Handler);
        await services.Store.InitializeAsync();
        var first = ws.Profile(ModLoader.Fabric) with { Name = "Test1" };
        var second = ws.Profile(ModLoader.Fabric) with { Name = "Test2" };
        await services.Store.SaveProfileAsync(first);
        await services.Store.SaveProfileAsync(second);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        var window = new MainWindow { DataContext = vm, Width = 980, Height = 680 };
        window.Show();
        try
        {
            vm.Page = LauncherPage.Profiles;
            vm.LibraryGrid = true;
            Capture(window, "library");
            vm.OpenProfileWorkspace(first.Id);
            Capture(window, "profile");
            vm.ChooseWorkspaceSection(ProfileSection.Mods);
            Click(window, "WorkspaceCatalogButton");
            await Until(() => window.OwnedWindows.OfType<ModCatalogDialog>().Any());
            var dialog = Assert.Single(window.OwnedWindows.OfType<ModCatalogDialog>());
            dialog.Width = 650; dialog.Height = 480;
            var query = dialog.FindControl<TextBox>("CatalogQuery")!;
            var results = dialog.FindControl<ListBox>("CatalogResults")!;
            fixture.BeforeFirstVersionResponse = () => results.SelectedIndex = 1;
            var staleVersionShown = false;
            vm.RemoteVersions.CollectionChanged += (_, _) => staleVersionShown |= vm.SelectedRemoteProject?.Id == "B" && vm.RemoteVersions.Any(v => v.ProjectId != "B");
            query.Text = "JEI";
            await Until(() => vm.IsIdle && vm.SelectedRemoteVersion?.Id == "B1");
            Assert.False(staleVersionShown);
            dialog.UpdateLayout();
            Capture(dialog, "catalog-loading");
            await Until(() => results.GetVisualDescendants().OfType<Image>().Any(image => image.Source is not null));
            results.SelectedIndex = 0;
            await Until(() => vm.IsIdle && vm.SelectedRemoteVersion?.Id == "A1");
            Capture(dialog, "catalog");
            Click(dialog, "CatalogAdd");
            await Until(() => vm.IsIdle);
            var add = dialog.FindControl<Button>("CatalogAdd")!;
            Assert.False(add.IsEnabled);
            Assert.Equal(PTenoLauncher.Desktop.Localization.TextCatalog.Get("CatalogInCart"), add.Content);
            var selected = vm.SelectedRemoteVersion!;
            var otherVersion = selected with { Id = "other-version" };
            vm.RemoteVersions.Add(otherVersion);
            var versions = dialog.FindControl<ComboBox>("CatalogVersionList")!;
            versions.SelectedItem = otherVersion;
            Assert.True(add.IsEnabled);
            Assert.Equal(PTenoLauncher.Desktop.Localization.TextCatalog.Get("CatalogReplace"), add.Content);
            Click(dialog, "CatalogAdd");
            Assert.Equal(otherVersion.Id, Assert.Single(vm.CatalogPicks).Request.VersionId);
            versions.SelectedItem = selected;
            Click(dialog, "CatalogAdd");
            Click(dialog, "CatalogRemoveSelected");
            await Until(() => vm.IsIdle && add.IsEnabled);
            Assert.Empty(vm.CatalogPicks);
            Click(dialog, "CatalogAdd");
            Click(dialog, "CatalogReview");
            await Until(() => vm.IsIdle && dialog.FindControl<Panel>("CartStep")!.IsVisible && vm.RemoteEntries.Count == 2);
            Click(dialog, "CatalogBack");
            Assert.True(dialog.FindControl<Panel>("SearchStep")!.IsVisible);
            Assert.Equal("JEI", query.Text);
            Assert.Equal("A1", vm.SelectedRemoteVersion?.Id);
            Click(dialog, "CatalogReview");
            Assert.True(dialog.FindControl<Panel>("CartStep")!.IsVisible);
            Assert.Empty(Directory.GetFiles(Path.Combine(ws.Paths.Game(first.Id), "mods")));
            Assert.Null(dialog.FindControl<CheckBox>("CombinedCartAcceptance"));
            Assert.True(dialog.FindControl<Button>("InstallCombinedCartButton")!.IsEnabled);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            dialog.UpdateLayout();
            var expander = dialog.FindControl<Expander>("CartReviewDetails")!;
            var toggle = Assert.Single(expander.GetVisualDescendants().OfType<ToggleButton>());
            Capture(dialog, "cart");
            Assert.InRange(toggle.Bounds.Width, 1, dialog.Bounds.Width / 2);
            Assert.Null(toggle.FocusAdorner);
            Assert.True(toggle.Focus(NavigationMethod.Tab));
            Assert.True(toggle.IsFocused);
            Assert.Contains(":focus-visible", toggle.Classes);
            var presenter = Assert.Single(toggle.GetVisualDescendants().OfType<ContentPresenter>());
            Assert.Equal(default, presenter.BorderThickness);
            Capture(dialog, "cart-focus");
            toggle.IsChecked = true;
            Assert.True(expander.IsExpanded);
            vm.SelectedProfile = second;
            Assert.Equal(first.Id, vm.SelectedProfile?.Id);
            Click(dialog, "InstallCombinedCartButton");
            await Until(() => !window.OwnedWindows.OfType<ModCatalogDialog>().Any() && vm.IsIdle &&
                vm.WorkspaceModRows.Count == 2 && vm.WorkspaceModRows.All(row => row.Name == "Example"));
            Capture(window, "mods");
            Assert.Equal(2, Directory.GetFiles(Path.Combine(ws.Paths.Game(first.Id), "mods"), "*.jar").Length);
            Assert.Empty(Directory.GetFiles(Path.Combine(ws.Paths.Game(second.Id), "mods")));
            Assert.Contains("Just Enough Items (fixture)", vm.DownloadDetails);
            Assert.Contains(first.Name, vm.DownloadDetails);
            Click(window, "DownloadHistoryButton");
            await Until(() => vm.IsDownloads && vm.DownloadHistory.Any(row => row.Entry.Result == "Succeeded"));
            using var restartedServices = new LauncherServices(ws.Paths, coreTransport: fixture.Handler);
            var restarted = new MainViewModel(restartedServices);
            await restarted.InitializeAsync();
            await restarted.RefreshDownloadHistoryAsync();
            Assert.Contains(restarted.DownloadHistory, row => row.Entry.Profile == first.Name && row.Entry.Result == "Succeeded");
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.OfType<ModCatalogDialog>().ToArray()) dialog.Close();
            window.DataContext = null; window.Close();
        }

        static void Click(Window window, string name)
        {
            var button = window.FindControl<Button>(name)!;
            Assert.True(button.IsEnabled, name);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        static void Capture(Window window, string name)
        {
            if (Environment.GetEnvironmentVariable("PTENO_WORKSPACE_SCREENSHOTS") is not { Length: > 0 } directory) return;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var image = window.CaptureRenderedFrame();
            Assert.NotNull(image);
            image.Save(Path.Combine(directory, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        async Task Until(Func<bool> check)
        {
            for (var i = 0; i < 400 && !check(); i++) await Task.Delay(10);
            Assert.True(check(), vm.Status);
        }
    }

    [AvaloniaFact]
    public async Task ActualJarConflictKeepsCartAndPersistsBothFilesBeforeRetry()
    {
        using var ws = new TestWorkspace();
        using var fixture = new ModrinthIntegrationTests.Fixture(ws, conflictingConfig: true);
        using var services = new LauncherServices(ws.Paths, coreTransport: fixture.Handler);
        await services.Store.InitializeAsync();
        var profile = ws.Profile(ModLoader.Fabric);
        await services.Store.SaveProfileAsync(profile);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.OpenProfileWorkspace(profile.Id);
        vm.ChooseWorkspaceSection(ProfileSection.Mods);
        var window = new MainWindow { DataContext = vm, Width = 980, Height = 680 };
        window.Show();
        try
        {
            Click(window.FindControl<Button>("WorkspaceCatalogButton")!);
            var dialog = Assert.Single(window.OwnedWindows.OfType<ModCatalogDialog>());
            var query = dialog.FindControl<TextBox>("CatalogQuery")!;
            query.Text = "JEI";
            query.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await Until(() => vm.IsIdle && vm.SelectedRemoteVersion?.Id == "A1");
            Click(dialog.FindControl<Button>("CatalogAdd")!);
            dialog.FindControl<ListBox>("CatalogResults")!.SelectedIndex = 1;
            await Until(() => vm.IsIdle && vm.SelectedRemoteVersion?.Id == "B1");
            Click(dialog.FindControl<Button>("CatalogAdd")!);
            Click(dialog.FindControl<Button>("CatalogReview")!);
            var install = dialog.FindControl<Button>("InstallCombinedCartButton")!;
            await Until(() => install.IsEnabled);
            Click(install);
            await Until(() => vm.IsIdle && vm.HasCartConflict);
            Assert.True(dialog.IsVisible);
            Assert.False(install.IsEnabled);
            Assert.Equal(2, vm.CatalogPicks.Count);
            Assert.Empty(Directory.GetFiles(Path.Combine(ws.Paths.Game(profile.Id), "mods")));
            Assert.False(Directory.Exists(Path.Combine(ws.Paths.Profile(profile.Id), "transactions")));
            var conflict = dialog.FindControl<TextBlock>("CartConflict")!.Text;
            Assert.Contains("Mod A 1.0.0", conflict);
            Assert.Contains("Mod B 2.0.0", conflict);
            Assert.DoesNotContain("[Mod ID:", conflict);
            var reopened = new PTenoLauncher.Infrastructure.Storage.SqliteLauncherStore(ws.Paths);
            var failure = Assert.Single(await reopened.GetDownloadHistoryAsync());
            Assert.Equal("Failed", failure.Result);
            Assert.Contains("Mod ID: mezz_config", failure.Detail);
            Assert.Contains("A.jar", failure.Detail);
            Assert.Contains("B.jar", failure.Detail);
            Assert.DoesNotContain(ws.Paths.Root, failure.Detail);
            dialog.UpdateLayout();
            var remove = dialog.GetVisualDescendants().OfType<Button>().Single(button =>
                button.Name == "RemoveCatalogPick" && button.DataContext is MainViewModel.CatalogPick { Request.ProjectId: "B" });
            Click(remove);
            await Until(() => install.IsEnabled && !vm.HasCartConflict);
            Click(install);
            await Until(() => !dialog.IsVisible && vm.IsIdle && vm.WorkspaceModRows.SingleOrDefault()?.Name == "Example");
            Assert.Single(Directory.GetFiles(Path.Combine(ws.Paths.Game(profile.Id), "mods")));
            Assert.Contains(await reopened.GetDownloadHistoryAsync(), entry => entry.Result == "Succeeded");
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.OfType<ModCatalogDialog>().ToArray()) dialog.Close();
            window.Close();
        }

        static void Click(Button button)
        {
            Assert.True(button.IsEnabled, button.Name);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        async Task Until(Func<bool> check)
        {
            for (var i = 0; i < 500 && !check(); i++) await Task.Delay(10);
            Assert.True(check(), vm.Status);
        }
    }

    [AvaloniaFact]
    public async Task BackReturnsToOriginAndRestoresItsSelectedProfile()
    {
        using var ws = new TestWorkspace();
        using var services = new LauncherServices(ws.Paths);
        await services.Store.InitializeAsync();
        var previous = ws.Profile(ModLoader.Fabric) with { Name = "First" };
        var opened = ws.Profile(ModLoader.Fabric) with { Name = "Second" };
        await services.Store.SaveProfileAsync(previous);
        await services.Store.SaveProfileAsync(opened);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.SelectedProfile = previous;
        vm.Page = LauncherPage.Home;
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            vm.OpenProfileWorkspace(opened.Id);
            window.FindControl<Button>("WorkspaceBack")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(LauncherPage.Home, vm.Page);
            Assert.Equal(previous.Id, vm.SelectedProfile?.Id);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task WorkspaceKeepsModsFoldersAndProcessOutputBoundToTheirProfile()
    {
        using var ws = new TestWorkspace();
        using var services = new LauncherServices(ws.Paths);
        await services.Store.InitializeAsync();
        var first = ws.Profile(ModLoader.Fabric) with { Name = "Test1" };
        var second = ws.Profile(ModLoader.Fabric) with { Name = "Test2" };
        await services.Store.SaveProfileAsync(first);
        await services.Store.SaveProfileAsync(second);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.Offline = true;
        vm.OpenProfileWorkspace(first.Id);
        vm.ChooseWorkspaceSection(ProfileSection.Mods);
        vm.SelectedProfile = second;
        Assert.Equal(first.Id, vm.SelectedProfile?.Id);
        Assert.Equal(ws.Paths.Game(first.Id), vm.WorkspaceFolder("game"));
        Assert.Equal(Path.Combine(ws.Paths.Game(first.Id), "mods"), vm.WorkspaceFolder("mods"));
        Assert.Equal(Path.Combine(ws.Paths.Profile(first.Id), "launcher-logs"), vm.WorkspaceFolder("logs"));

        var jar = Path.Combine(ws.Paths.Root, "workspace-test.jar");
        SecurityAndModsTests.CreateJar(jar, "workspace-test");
        await vm.AddModsAsync([jar]);
        await vm.ResolveCartAsync();
        Assert.True(vm.LastOperationSucceeded, vm.Status);
        vm.UnknownSourceAccepted = true;
        var installation = vm.InstallCartCommand.ExecuteAsync();
        vm.SelectedProfile = second;
        await installation;
        Assert.True(vm.LastOperationSucceeded, vm.Status);
        Assert.Equal("workspace-test.jar", Assert.Single(vm.WorkspaceInstalledMods));
        Assert.Empty(Directory.GetFiles(Path.Combine(ws.Paths.Game(second.Id), "mods")));

        if (!OperatingSystem.IsWindows()) return;
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"));
        foreach (var arg in new[] { "/d", "/c", "echo workspace-synthetic-secret & exit /b 7" }) info.ArgumentList.Add(arg);
        var log = Path.Combine(vm.WorkspaceFolder("logs"), "controlled.log");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var running = vm.RunProfileConsoleAsync(first.Id,
            (output, started, logOpened, ct) => LiveProcessRunner.RunAsync(info, log, _ => { logOpened(log); started(); return Task.CompletedTask; },
                output, ["workspace-synthetic-secret"], ct), timeout.Token);
        vm.OpenProfileWorkspace(second.Id);
        vm.ChooseWorkspaceSection(ProfileSection.Console);
        Assert.Empty(vm.ConsoleLines);
        Assert.False(vm.IsSelectedProfileRunning);
        Assert.Equal(ws.Paths.Game(second.Id), vm.WorkspaceFolder("game"));
        Assert.Equal(7, await running);
        Assert.Empty(vm.ConsoleLines);
        vm.OpenProfileWorkspace(first.Id);
        for (var i = 0; vm.ConsoleLines.Count == 0 && i < 100; i++) await Task.Delay(10);
        Assert.Contains(vm.ConsoleLines, l => l.Contains("[REDACTED]", StringComparison.Ordinal));
        Assert.Contains("7", vm.ProfileConsoleStatus);
        vm.ClearProfileConsole();
        Assert.Empty(vm.ConsoleLines);
        Assert.Contains("[REDACTED]", await File.ReadAllTextAsync(log));
    }

    [AvaloniaFact]
    public async Task ImportCanBeCancelledBeforeCommitAndConfirmedImportOpensItsOwnWorkspace()
    {
        using var fixture = new MrpackFixture();
        using var services = new LauncherServices(fixture.Workspace.Paths);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.Offline = true;
        var window = new MainWindow { DataContext = vm, Width = 980, Height = 680 };
        window.Show();
        try
        {
            var pack = fixture.Pack(null, ("overrides/config/workspace.txt", [1]));
            window.FindControl<Button>("CreateProfileButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 100 && !window.OwnedWindows.OfType<ProfileEditor>().Any(); i++) await Task.Delay(10);
            var editor = Assert.Single(window.OwnedWindows.OfType<ProfileEditor>());
            editor.Width = 460; editor.Height = 430;
            editor.FindControl<Button>("NewImportMode")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var import = editor.FindControl<ProfileImportView>("NewProfileImport")!;
            Assert.True(import.IsVisible);
            await vm.ChoosePackAsync(pack);
            Assert.False(import.FindControl<Button>("ImportMrpackButton")!.IsEnabled);
            editor.Close(false);
            await Task.Yield();
            Assert.Empty(await services.Store.GetProfilesAsync());
            window.FindControl<Button>("CreateProfileButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 100 && !window.OwnedWindows.OfType<ProfileEditor>().Any(); i++) await Task.Delay(10);
            editor = Assert.Single(window.OwnedWindows.OfType<ProfileEditor>());
            editor.FindControl<Button>("NewImportMode")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            import = editor.FindControl<ProfileImportView>("NewProfileImport")!;
            await vm.ChoosePackAsync(pack);
            import.FindControl<CheckBox>("MrpackConsent")!.IsChecked = true;
            import.FindControl<Button>("ImportMrpackButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 300 && !vm.IsProfileWorkspace; i++) await Task.Delay(10);
            Assert.True(vm.IsProfileWorkspace, vm.Status);
            Assert.Equal(Assert.Single(await services.Store.GetProfilesAsync()).Id, vm.WorkspaceProfileId);
        }
        finally
        {
            foreach (var editor in window.OwnedWindows.OfType<ProfileEditor>().ToArray()) editor.Close();
            window.DataContext = null; window.Close();
        }
    }
}
