using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Storage;
using PTenoLauncher.Testing;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class ProfileContentTests
{
    [AvaloniaFact]
    public async Task IncompatibleResourcePackNeedsConfirmationBeforeChangingOptions()
    {
        using var ws = new TestWorkspace();
        using var services = new LauncherServices(ws.Paths);
        await ws.Store.InitializeAsync();
        var profile = ws.Profile() with { MinecraftVersion = "1.21" };
        await services.Store.SaveProfileAsync(profile);
        WriteZip(SafePath.Join(services.Content.Folder(profile.Id, ProfileContentKind.ResourcePacks), "older.zip"), "pack.mcmeta",
            """{"pack":{"pack_format":12,"description":"Older pack"}}""");
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.OpenProfileWorkspace(profile.Id);
        vm.ChooseWorkspaceSection(ProfileSection.ResourcePacks);
        await vm.WaitForWorkspaceSnapshotAsync();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            vm.WorkspaceContentSelection = Assert.Single(vm.WorkspaceContentRows);
            Assert.Equal(ResourcePackCompatibility.Incompatible, vm.WorkspaceContentSelection.Compatibility);
            var options = SafePath.Join(ws.Paths.Game(profile.Id), "options.txt");
            var toggle = window.FindControl<Button>("WorkspaceTogglePackButton")!;
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(File.Exists(options));
            Assert.Single(window.OwnedWindows.OfType<ContentActionDialog>()).Close();
            await WaitForAsync(() => window.OwnedWindows.Count == 0);
            Assert.False(File.Exists(options));
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(window.OwnedWindows.OfType<ContentActionDialog>()).Close(false);
            await WaitForAsync(() => vm.WorkspaceContentSelection?.Enabled == true);
            Assert.Contains("incompatibleResourcePacks:[\"file/older.zip\"]", await File.ReadAllTextAsync(options));
        }
        finally { window.Close(); }
    }

    [Fact]
    public async Task ResourcePackFormatAndTogglePreserveUnrelatedMinecraftOptions()
    {
        using var ws = new TestWorkspace();
        await ws.Store.InitializeAsync();
        var profile = ws.Profile() with { MinecraftVersion = "1.21" };
        await ws.Store.SaveProfileAsync(profile);
        var content = new ProfileContent(ws.Paths);
        var folder = content.Folder(profile.Id, ProfileContentKind.ResourcePacks);
        using (var zip = ZipFile.Open(Path.Combine(folder, "test.zip"), ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("pack.mcmeta").Open()))
            await writer.WriteAsync("""{"pack":{"pack_format":33,"supported_formats":[33,34],"description":"Fixture pack"}}""");
        var options = Path.Combine(ws.Paths.Game(profile.Id), "options.txt");
        await File.WriteAllTextAsync(options, "renderDistance:8\r\nresourcePacks:[\"vanilla\",\"file/old.zip\"]\r\n");
        var pack = Assert.Single(await content.ListAsync(profile.Id, ProfileContentKind.ResourcePacks, profileInfo: profile));
        Assert.Equal(ResourcePackCompatibility.Compatible, pack.Compatibility);
        Assert.False(pack.Enabled);
        await content.SetResourcePackEnabledAsync(profile.Id, pack, true);
        Assert.True(Assert.Single(await content.ListAsync(profile.Id, ProfileContentKind.ResourcePacks, profileInfo: profile)).Enabled);
        await content.SetResourcePackEnabledAsync(profile.Id, pack, false);
        Assert.Contains("renderDistance:8\r\nresourcePacks:[\"vanilla\",\"file/old.zip\"]", await File.ReadAllTextAsync(options));
        Assert.False(Assert.Single(await content.ListAsync(profile.Id, ProfileContentKind.ResourcePacks, profileInfo: profile)).Enabled);
        await File.WriteAllTextAsync(options, "resourcePacks:[");
        await Assert.ThrowsAsync<InvalidDataException>(() => content.SetResourcePackEnabledAsync(profile.Id, pack, true));
        Assert.Equal("resourcePacks:[", await File.ReadAllTextAsync(options));
    }

    [Fact]
    public async Task LauncherGeneratedTranslationPackAppearsOnlyInItsProfile()
    {
        using var f = new AiUxFixture();
        var ws = f.Workspace;
        await ws.Store.InitializeAsync();
        var profile = ws.Profile() with { MinecraftVersion = "1.21" };
        var other = ws.Profile();
        await ws.Store.SaveProfileAsync(profile);
        await ws.Store.SaveProfileAsync(other);
        await f.Translations.ImportAsync(profile, [f.Jar()], "uk_ua", sourceLocale: "fr_fr");
        var draft = (await f.Translations.ReadAsync(profile.Id, "uk_ua", "demo", sourceLocale: "fr_fr"))!;
        await f.Translations.SaveManualAsync(profile, "uk_ua", "demo", new Dictionary<string, string> { ["hello"] = "Hello %s" },
            sourceLocale: "fr_fr", expectedFingerprint: PTenoLauncher.Infrastructure.AI.TranslationWorkspace.Fingerprint(draft));
        var pack = await f.Translations.ExportAsync(profile, "uk_ua", 34, sourceLocale: "fr_fr");
        var content = new ProfileContent(ws.Paths);
        var listed = Assert.Single(await content.ListAsync(profile.Id, ProfileContentKind.ResourcePacks, profileInfo: profile));
        Assert.Equal(Path.GetFileName(pack.Path), listed.FileName);
        Assert.Equal(ResourcePackCompatibility.Compatible, listed.Compatibility);
        Assert.Empty(await content.ListAsync(other.Id, ProfileContentKind.ResourcePacks, profileInfo: other));
    }

    [AvaloniaFact]
    public async Task ShaderWorkspaceReportsMissingAndEnabledComponent()
    {
        using var ws = new TestWorkspace();
        using var services = new LauncherServices(ws.Paths);
        await services.Store.InitializeAsync();
        var profile = ws.Profile(ModLoader.Fabric);
        await services.Store.SaveProfileAsync(profile);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.OpenProfileWorkspace(profile.Id);
        vm.ChooseWorkspaceSection(ProfileSection.Shaders);
        await vm.WaitForWorkspaceSnapshotAsync();
        Assert.Contains("Iris", vm.WorkspaceShaderSupport);
        Assert.True(vm.WorkspaceFindShaderComponent);
        var jar = Path.Combine(ws.Paths.Game(profile.Id), "mods", "iris.jar");
        SecurityAndModsTests.CreateJar(jar, "iris");
        await vm.RefreshVisibleWorkspaceAsync();
        Assert.Contains("Example", vm.WorkspaceShaderSupport);
        Assert.False(vm.WorkspaceFindShaderComponent);
    }

    [AvaloniaFact]
    public async Task ProfileTabsFollowExactVersionsAndLoadersWhileOffline()
    {
        using var ws = new TestWorkspace();
        using var services = new LauncherServices(ws.Paths);
        await services.Store.InitializeAsync();
        var vanilla = ws.Profile() with { Name = "Vanilla" };
        var noShader = ws.Profile(ModLoader.NeoForge) with { Name = "No shader component", MinecraftVersion = "1.20.6" };
        var iris = ws.Profile(ModLoader.NeoForge) with { Name = "Iris available" };
        var unknown = ws.Profile(ModLoader.Fabric) with { Name = "Unconfirmed snapshot", MinecraftVersion = "1.21.1-pre1" };
        foreach (var profile in new[] { vanilla, noShader, iris, unknown })
            await services.Store.SaveProfileAsync(profile);
        var resourceZip = SafePath.Join(ws.Paths.Root, "resource.zip");
        WriteZip(resourceZip, "pack.mcmeta", """{"pack":{"pack_format":34,"description":"Fixture"}}""");
        var shaderZip = SafePath.Join(ws.Paths.Root, "shader.zip");
        WriteZip(shaderZip, "shaders/final.fsh", "void main() {}");
        var existingShader = SafePath.Join(ws.Paths.Game(noShader.Id), "shaderpacks/existing.zip");
        File.Copy(shaderZip, existingShader);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.Offline = true;
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            var mods = window.FindControl<Button>("ProfileModsSection")!;
            var shaders = window.FindControl<Button>("ProfileShadersSection")!;
            var resources = window.FindControl<Button>("ProfileResourcePacks")!;
            var addPack = window.FindControl<Button>("WorkspaceAddPack")!;
            var findComponent = window.FindControl<Button>("WorkspaceFindShaderComponentButton")!;
            vm.OpenProfileWorkspace(vanilla.Id);
            Assert.False(mods.IsVisible);
            Assert.False(shaders.IsVisible);
            Assert.True(resources.IsVisible);
            vm.ChooseWorkspaceSection(ProfileSection.Mods);
            Assert.Equal(ProfileSection.Overview, vm.WorkspaceSection);
            vm.ChooseWorkspaceSection(ProfileSection.ResourcePacks);
            Assert.True(vm.WorkspacePackCatalogOffline);
            Assert.True(addPack.IsEffectivelyEnabled);
            await vm.ImportWorkspacePackAsync(vanilla.Id, ProfileContentKind.ResourcePacks, resourceZip);
            Assert.Equal("resource.zip", Assert.Single(vm.WorkspaceContentRows).FileName);

            vm.OpenProfileWorkspace(noShader.Id);
            Assert.True(mods.IsVisible);
            Assert.False(shaders.IsVisible);
            vm.ChooseWorkspaceSection(ProfileSection.Folders);
            Assert.True(File.Exists(Path.Combine(vm.WorkspaceFolder("shaderpacks"), "existing.zip")));

            vm.OpenProfileWorkspace(iris.Id);
            Assert.True(shaders.IsVisible);
            vm.ChooseWorkspaceSection(ProfileSection.Shaders);
            await vm.WaitForWorkspaceSnapshotAsync();
            Assert.Equal("Iris", vm.WorkspaceShaderSuggestion);
            Assert.Contains("Iris", vm.WorkspaceShaderSupport);
            Assert.True(findComponent.IsVisible);
            Assert.False(findComponent.IsEffectivelyEnabled);
            Assert.True(addPack.IsEffectivelyEnabled);
            await vm.ImportWorkspacePackAsync(iris.Id, ProfileContentKind.Shaders, shaderZip);
            Assert.Equal("shader.zip", Assert.Single(vm.WorkspaceContentRows).FileName);

            vm.SelectedProfile = iris with { MinecraftVersion = noShader.MinecraftVersion };
            Assert.False(shaders.IsVisible);
            Assert.True(mods.IsVisible);
            Assert.Equal(ProfileSection.Overview, vm.WorkspaceSection);
            vm.ChooseWorkspaceSection(ProfileSection.Mods);
            vm.SelectedProfile = iris with { MinecraftVersion = noShader.MinecraftVersion, Loader = ModLoader.Vanilla };
            Assert.False(mods.IsVisible);
            Assert.Equal(ProfileSection.Overview, vm.WorkspaceSection);
            Assert.True(File.Exists(Path.Combine(vm.WorkspaceFolder("shaderpacks"), "shader.zip")));

            vm.OpenProfileWorkspace(unknown.Id);
            Assert.True(mods.IsVisible);
            Assert.True(shaders.IsVisible);
            vm.ChooseWorkspaceSection(ProfileSection.Mods);
            Assert.True(vm.WorkspaceCompatibilityUnknown);
            Assert.False(vm.CanUseOnlineCatalog);
            vm.ChooseWorkspaceSection(ProfileSection.Shaders);
            await vm.WaitForWorkspaceSnapshotAsync();
            Assert.Equal(TextCatalog.Get("WorkspaceCompatibilityUnknown"), vm.WorkspaceShaderSupport);
            Assert.True(addPack.IsEffectivelyEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ShaderSearchReplacesAnOlderRequestAndShowsOfflineSeparately()
    {
        using var ws = new TestWorkspace();
        using var transport = new ShaderSearchTransport();
        using var services = new LauncherServices(ws.Paths, coreTransport: transport);
        await services.Store.InitializeAsync();
        var profile = ws.Profile(ModLoader.Fabric);
        await services.Store.SaveProfileAsync(profile);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.OpenProfileWorkspace(profile.Id);
        vm.ChooseWorkspaceSection(ProfileSection.Shaders);
        var dialog = new ContentCatalogDialog(vm, profile, ProfileContentKind.Shaders);
        dialog.Show();
        try
        {
            var query = dialog.FindControl<TextBox>("ContentQuery")!;
            query.Text = "older";
            await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(6));
            query.Text = "current";
            var results = dialog.FindControl<ListBox>("ContentResults")!;
            await WaitForAsync(() => results.ItemCount == 1);
            Assert.True(transport.Cancelled);
            Assert.Equal("Current shader", Assert.IsType<CatalogSearchHit>(Assert.Single(results.Items)).Title);
            query.Text = "empty";
            await WaitForAsync(() => dialog.FindControl<TextBlock>("ContentSearchStatus")!.Text == TextCatalog.Get("CatalogSearchEmpty"));
            Assert.True(vm.WorkspaceShaders && vm.WorkspaceCanImportPack);
            query.Text = "unavailable";
            await WaitForAsync(() => dialog.FindControl<TextBlock>("ContentSearchStatus")!.Text == TextCatalog.Get("CatalogSearchFailed"));
            Assert.True(vm.WorkspaceShaders && vm.WorkspaceCanImportPack);
            vm.Offline = true;
            query.Text = "offline";
            await WaitForAsync(() => dialog.FindControl<TextBlock>("ContentSearchStatus")!.Text ==
                PTenoLauncher.Desktop.Localization.TextCatalog.Get("CatalogSearchOffline"));
            Assert.Equal(4, transport.Requests);
            Assert.True(vm.WorkspaceShaders && vm.WorkspaceCanImportPack);
        }
        finally { dialog.Close(); }
    }

    private sealed class ShaderSearchTransport : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled { get; private set; }
        public int Requests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            Assert.Contains("project_type:shader", query);
            Assert.Contains("versions:1.21.1", query);
            if (query.Contains("query=older", StringComparison.Ordinal))
            {
                Started.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { Cancelled = true; throw; }
            }
            if (query.Contains("query=empty", StringComparison.Ordinal)) return Response("""{"hits":[]}""");
            if (query.Contains("query=unavailable", StringComparison.Ordinal)) return new(HttpStatusCode.ServiceUnavailable);
            return Response("""{"hits":[{"project_id":"Current","slug":"current","title":"Current shader","description":"fixture"}]}""");
        }
    }

    [AvaloniaFact]
    public async Task ResourceCatalogInstallsTheSelectedZipInOnlyTheOpenedProfile()
    {
        using var ws = new TestWorkspace();
        var zip = SafePath.Join(ws.Paths.Root, "fixture.zip");
        WriteZip(zip, "pack.mcmeta", """{"pack":{"pack_format":34,"description":"Verified catalog pack"}}""");
        var bytes = await File.ReadAllBytesAsync(zip);
        var hash = Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant();
        const string download = "https://cdn.modrinth.com/data/Pack1234/versions/File1234/fixture.zip";
        var routes = new List<string>();
        using var transport = new StubHandler(request =>
        {
            var uri = request.RequestUri!;
            routes.Add(uri.AbsolutePath);
            return uri.AbsolutePath switch
            {
                "/v2/search" => Response("""{"hits":[{"project_id":"Pack1234","slug":"fixture-pack","title":"Fixture pack","description":"A test pack"}]}"""),
                "/v2/project/Pack1234" => Response("""{"id":"Pack1234","project_type":"resourcepack"}"""),
                "/v2/project/Pack1234/version" => Response(JsonSerializer.Serialize(new[]
                {
                    new { id = "File1234", project_id = "Pack1234", version_number = "4.2", name = "Fixture",
                        date_published = "2026-01-01T00:00:00Z", version_type = "release", game_versions = new[] { "1.21.1" },
                        loaders = new[] { "minecraft" }, dependencies = Array.Empty<object>(),
                        files = new[] { new { filename = "fixture.zip", url = download, hashes = new { sha512 = hash }, size = bytes.Length } } }
                })),
                "/data/Pack1234/versions/File1234/fixture.zip" => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) },
                _ => throw new InvalidOperationException("Unexpected test route: " + uri.AbsolutePath)
            };
        });
        using var services = new LauncherServices(ws.Paths, coreTransport: transport);
        await services.Store.InitializeAsync();
        var first = ws.Profile() with { Name = "First" };
        var second = ws.Profile() with { Name = "Second" };
        await services.Store.SaveProfileAsync(first);
        await services.Store.SaveProfileAsync(second);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.OpenProfileWorkspace(first.Id);
        vm.ChooseWorkspaceSection(ProfileSection.ResourcePacks);
        var dialog = new ContentCatalogDialog(vm, first, ProfileContentKind.ResourcePacks);
        dialog.Show();
        try
        {
            var source = dialog.FindControl<ComboBox>("ContentSource")!;
            source.SelectedIndex = 1;
            Assert.True(dialog.FindControl<TextBlock>("ContentSourceUnavailable")!.IsVisible);
            Assert.Empty(routes);
            source.SelectedIndex = 0;
            dialog.FindControl<TextBox>("ContentQuery")!.Text = "fixture";
            var results = dialog.FindControl<ListBox>("ContentResults")!;
            await WaitForAsync(() => results.ItemCount == 1 && vm.IsIdle);
            results.SelectedIndex = 0;
            var add = dialog.FindControl<Button>("ContentAdd")!;
            await WaitForAsync(() => add.IsEnabled && vm.IsIdle);
            Assert.True(dialog.FindControl<Button>("ContentProjectPage")!.IsVisible);
            Assert.Equal("4.2", Assert.IsType<CatalogModVersion>(dialog.FindControl<ComboBox>("ContentVersions")!.SelectedItem).Number);
            Assert.Equal("fixture.zip", dialog.FindControl<TextBlock>("ContentFile")!.Text);
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.DoesNotContain("/data/Pack1234/versions/File1234/fixture.zip", routes);
            dialog.FindControl<Button>("ContentDone")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialog.FindControl<Button>("ContentInstall")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => vm.IsIdle && vm.WorkspaceContentRows.Count == 1);
            Assert.True(vm.LastOperationSucceeded, vm.Status);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(SafePath.Join(services.Content.Folder(first.Id, ProfileContentKind.ResourcePacks), "fixture.zip")));
            Assert.Empty(Directory.GetFiles(services.Content.Folder(second.Id, ProfileContentKind.ResourcePacks)));
            Assert.Equal(new[] { "/v2/search", "/v2/project/Pack1234", "/v2/project/Pack1234/version", "/data/Pack1234/versions/File1234/fixture.zip" }, routes);
        }
        finally { if (dialog.IsVisible) dialog.Close(); }
    }

    private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    [Fact]
    public async Task PhysicalZipNamesUseTheOriginalAndShortCollisionSuffix()
    {
        using var ws = new TestWorkspace();
        var profile = ws.Profile() with { Name = "T1" };
        await ws.Store.InitializeAsync();
        await ws.Store.SaveProfileAsync(profile);
        var pack = SafePath.Join(ws.Paths.Root, "Bare Bones.zip");
        WriteZip(pack, "pack.mcmeta", """{"pack":{"pack_format":34,"description":"Bare Bones"}}""");
        var content = new ProfileContent(ws.Paths);
        var kind = ProfileContentKind.ResourcePacks;
        Assert.Equal("Bare Bones.zip", (await content.ImportAsync(profile.Id, kind, pack)).FileName);
        Assert.Equal("Bare Bones-2.zip", (await content.ImportAsync(profile.Id, kind, pack)).FileName);
        var bytes = await File.ReadAllBytesAsync(pack);
        var oldName = Convert.ToHexStringLower(SHA256.HashData(bytes)) + "-Bare Bones.zip";
        var oldPath = SafePath.Join(content.Folder(profile.Id, kind), oldName);
        await File.WriteAllBytesAsync(oldPath, bytes);
        var renamer = new ProfileFileRenamer(ws.Paths, ws.Store, new(ws.Paths, ws.Store), content);
        var plan = await renamer.PreviewAsync(profile.Id, "resourcepacks/" + oldName);
        Assert.Equal("Bare Bones-3.zip", Path.GetFileName(plan.Target));
        await renamer.ApplyAsync(plan);
        Assert.False(File.Exists(oldPath));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(plan.Target));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DoubleTapCartKeepsExactVersionsAcrossQueriesAndRetriesOnlyFailedPacks(bool shaders)
    {
        using var ws = new TestWorkspace();
        var kind = shaders ? ProfileContentKind.Shaders : ProfileContentKind.ResourcePacks;
        var type = shaders ? "shader" : "resourcepack";
        var firstFile = shaders ? "First Shader.zip" : "Bare Bones.zip";
        var zip = SafePath.Join(ws.Paths.Root, "fixture.zip");
        WriteZip(zip, shaders ? "shaders/final.fsh" : "pack.mcmeta",
            shaders ? "void main() {}" : """{"pack":{"pack_format":34,"description":"Fixture"}}""");
        var bytes = await File.ReadAllBytesAsync(zip);
        var digest = Convert.ToHexStringLower(SHA512.HashData(bytes));
        var requests = new List<string>();
        var failSecond = true;
        object Version(string project, string suffix, string file) => new
        {
            id = project + suffix, project_id = project, version_number = suffix, name = "Fixture " + suffix,
            date_published = suffix == "2" ? "2026-01-02T00:00:00Z" : "2026-01-01T00:00:00Z",
            version_type = "release", game_versions = new[] { "1.21.1" },
            loaders = new[] { shaders ? "iris" : "minecraft" }, dependencies = Array.Empty<object>(),
            files = new[] { new { filename = file, size = bytes.Length, url = $"https://cdn.modrinth.com/data/{project}/{suffix}/{Uri.EscapeDataString(file)}", hashes = new { sha512 = digest } } }
        };
        using var transport = new StubHandler(request =>
        {
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath;
            if (path.StartsWith("/data/", StringComparison.Ordinal))
            {
                requests.Add(path);
                if (path.Contains("/Second/", StringComparison.Ordinal) && failSecond) return new(HttpStatusCode.ServiceUnavailable);
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            }
            if (path == "/v2/search")
            {
                var id = uri.Query.Contains("query=second", StringComparison.Ordinal) ? "Second" : "First";
                return Response(JsonSerializer.Serialize(new { hits = new[] { new { project_id = id, slug = id, title = id, description = "Fixture" } } }));
            }
            if (path.EndsWith("/version", StringComparison.Ordinal))
                return Response(JsonSerializer.Serialize(path.Contains("/First/") ? new[] { Version("First", "1", firstFile), Version("First", "2", "Latest.zip") } : new[] { Version("Second", "1", "Second Pack.zip") }));
            return Response(JsonSerializer.Serialize(new { id = path.Split('/')[^1], project_type = type }));
        });
        using var services = new LauncherServices(ws.Paths, coreTransport: transport);
        await services.Store.InitializeAsync();
        var profile = ws.Profile(shaders ? ModLoader.Fabric : ModLoader.Vanilla) with { Name = "T1" };
        var other = ws.Profile() with { Name = "T2" };
        await services.Store.SaveProfileAsync(profile);
        await services.Store.SaveProfileAsync(other);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.OpenProfileWorkspace(profile.Id);
        vm.ChooseWorkspaceSection(shaders ? ProfileSection.Shaders : ProfileSection.ResourcePacks);
        var dialog = new ContentCatalogDialog(vm, profile, kind);
        dialog.Show();
        try
        {
            var query = dialog.FindControl<TextBox>("ContentQuery")!;
            var hits = dialog.FindControl<ListBox>("ContentResults")!;
            var rows = dialog.FindControl<ListBox>("ContentCartRows")!;
            var versions = dialog.FindControl<ComboBox>("ContentVersions")!;
            var install = dialog.FindControl<Button>("ContentInstall")!;
            query.Text = "first";
            await WaitForAsync(() => hits.ItemCount == 1);
            hits.SelectedIndex = 0;
            DoubleTap();
            await WaitForAsync(() => rows.ItemCount == 1);
            Assert.Equal("First2", Assert.IsType<ContentCartPick>(rows.Items[0]).Version.Id);
            DoubleTap();
            Assert.Equal(1, rows.ItemCount);
            versions.SelectedIndex = 1;
            DoubleTap();
            Assert.Equal("First1", Assert.IsType<ContentCartPick>(rows.Items[0]).Version.Id);
            query.Text = "second";
            await WaitForAsync(() => hits.Items.OfType<CatalogSearchHit>().SingleOrDefault()?.Id == "Second");
            hits.SelectedIndex = 0;
            DoubleTap();
            await WaitForAsync(() => rows.ItemCount == 2);
            Click("ContentDone");
            Click("ContentBack");
            Assert.Equal("second", query.Text);
            Assert.Empty(requests);
            Click("ContentDone");
            Click("ContentInstall");
            await WaitForAsync(() => vm.IsIdle && rows.Items.OfType<ContentCartPick>().Any(pick => pick.Installed));
            Assert.True(File.Exists(SafePath.Join(services.Content.Folder(profile.Id, kind), firstFile)));
            Assert.False(File.Exists(SafePath.Join(services.Content.Folder(profile.Id, kind), "Latest.zip")));
            Assert.True(install.IsEnabled);
            failSecond = false;
            Click("ContentInstall");
            await WaitForAsync(() => vm.IsIdle && rows.Items.OfType<ContentCartPick>().All(pick => pick.Installed));
            Assert.Equal(1, requests.Count(path => path.Contains("/First/1/")));
            Assert.Equal(2, requests.Count(path => path.Contains("/Second/1/")));
            Assert.True(File.Exists(SafePath.Join(services.Content.Folder(profile.Id, kind), "Second Pack.zip")));
            Assert.Empty(Directory.EnumerateFiles(services.Content.Folder(other.Id, kind)));
        }
        finally { dialog.Close(); }

        void Click(string name) => dialog.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void DoubleTap()
        {
            dialog.UpdateLayout();
            var list = dialog.FindControl<ListBox>("ContentResults")!;
            var item = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
            using var pointer = new Pointer(1, PointerType.Mouse, true);
            var press = new PointerEventArgs(InputElement.PointerPressedEvent, item, pointer, dialog, default, 0,
                new PointerPointProperties(), KeyModifiers.None);
            item.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, press));
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 250 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task ShaderAndWorldActionsLeaveTheOtherProfileAndOriginalCopyUntouched()
    {
        using var ws = new TestWorkspace();
        using var destination = new OwnedTestRoot("world-copy");
        var first = ws.Profile();
        var second = ws.Profile();
        ws.Paths.CreateProfile(first);
        ws.Paths.CreateProfile(second);
        var content = new ProfileContent(ws.Paths);
        var source = SafePath.Join(ws.Paths.Root, "shader.zip");
        WriteZip(source, "shaders/final.fsh", "void main() {}");
        var bytes = await File.ReadAllBytesAsync(source);
        await Assert.ThrowsAsync<InvalidDataException>(() => content.ImportAsync(first.Id, ProfileContentKind.Shaders,
            source, expectedSha512: new string('0', 128)));
        var shader = await content.ImportAsync(first.Id, ProfileContentKind.Shaders, source);
        Assert.Equal("shader.zip", shader.FileName);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(source));
        Assert.Equal("shader-2.zip", (await content.ImportAsync(first.Id, ProfileContentKind.Shaders, source)).FileName);
        Assert.Empty(await content.ListAsync(second.Id, ProfileContentKind.Shaders));

        var world = SafePath.Join(content.Folder(first.Id, ProfileContentKind.Worlds), "Fixture World");
        Directory.CreateDirectory(world);
        await File.WriteAllTextAsync(SafePath.Join(world, "level.dat"), "synthetic world");
        var entry = Assert.Single(await content.ListAsync(first.Id, ProfileContentKind.Worlds));
        var copy = content.PlanCopy(first.Id, ProfileContentKind.Worlds, entry, destination.Root);
        using (var cancellation = new CancellationTokenSource())
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => content.CopyAsync(copy, cancellation.Token));
        }
        Assert.False(Directory.Exists(copy.Target));
        await content.CopyAsync(copy);
        Assert.Equal("synthetic world", await File.ReadAllTextAsync(SafePath.Join(copy.Target, "level.dat")));
        Assert.Throws<IOException>(() => content.PlanCopy(first.Id, ProfileContentKind.Worlds, entry, destination.Root));
        var removal = content.PlanRemove(first.Id, ProfileContentKind.Worlds, entry);
        using (new ProfileUsage(ws.Paths).Acquire(first.Id))
            await Assert.ThrowsAnyAsync<Exception>(() => content.RemoveAsync(removal, true));
        Assert.True(Directory.Exists(world));
        await content.RemoveAsync(removal, true);
        Assert.False(Directory.Exists(world));
        Assert.Equal("synthetic world", await File.ReadAllTextAsync(SafePath.Join(removal.Backup!, "level.dat")));
        Assert.Equal("synthetic world", await File.ReadAllTextAsync(SafePath.Join(removal.Target, "level.dat")));
        Assert.Empty(await content.ListAsync(second.Id, ProfileContentKind.Worlds));
    }

    [AvaloniaFact]
    public async Task ResourcePackImportsOfflineIntoOpenedProfileAndRefreshesItsList()
    {
        using var ws = new TestWorkspace();
        using var services = new LauncherServices(ws.Paths, coreTransport: new StubHandler(_ => throw new IOException("Offline")));
        await services.Store.InitializeAsync();
        var target = ws.Profile() with { Name = "Resource target" };
        var other = ws.Profile() with { Name = "Other profile" };
        await services.Store.SaveProfileAsync(target);
        await services.Store.SaveProfileAsync(other);
        await services.Store.SetSettingAsync("network.offline", "True");
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.OpenProfileWorkspace(target.Id);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            window.FindControl<Button>("ProfileResourcePacks")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; vm.IsBusy && i < 200; i++) await Task.Delay(10);
            Assert.True(vm.WorkspaceResourcePacks);
            Assert.True(window.FindControl<Button>("WorkspaceAddPack")!.IsVisible);
            var source = SafePath.Join(ws.Paths.Root, "my pack.zip");
            WriteZip(source, "pack.mcmeta", """{"pack":{"pack_format":34,"description":{"text":"Clear ","extra":[{"text":"Sky"}]}}}""");
            var original = await File.ReadAllBytesAsync(source);
            await vm.ImportWorkspacePackAsync(target.Id, ProfileContentKind.ResourcePacks, source);
            Assert.True(vm.LastOperationSucceeded, vm.Status);
            Assert.Equal("Clear Sky", Assert.Single(vm.WorkspaceContentRows).Name);
            Assert.Equal("my pack.zip", vm.WorkspaceContentSelection?.FileName);
            Assert.Equal(original, await File.ReadAllBytesAsync(source));
            Assert.Equal(original, await File.ReadAllBytesAsync(SafePath.Join(ws.Paths.Game(target.Id), "resourcepacks/my pack.zip")));
            Assert.Empty(Directory.GetFiles(services.Content.Folder(other.Id, ProfileContentKind.ResourcePacks)));
        }
        finally { window.DataContext = null; window.Close(); }
    }

    [Theory]
    [InlineData("wrapper/pack.mcmeta")]
    [InlineData("../pack.mcmeta")]
    public async Task InvalidZipLayoutDoesNotLeaveAnInstalledPack(string entry)
    {
        using var ws = new TestWorkspace();
        var profile = ws.Profile();
        ws.Paths.CreateProfile(profile);
        var source = SafePath.Join(ws.Paths.Root, "invalid.zip");
        WriteZip(source, entry, """{"pack":{"pack_format":34,"description":"Example"}}""");
        var content = new ProfileContent(ws.Paths);
        var original = await File.ReadAllBytesAsync(source);
        await Assert.ThrowsAnyAsync<Exception>(() => content.ImportAsync(profile.Id, ProfileContentKind.ResourcePacks, source));
        Assert.Empty(Directory.GetFiles(content.Folder(profile.Id, ProfileContentKind.ResourcePacks)));
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
    }

    internal static void WriteZip(string path, string entry, string value)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var output = new StreamWriter(zip.CreateEntry(entry).Open());
        output.Write(value);
    }
}
