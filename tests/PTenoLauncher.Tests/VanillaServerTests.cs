using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class VanillaServerTests
{
    [Fact]
    public async Task MultipleStoppedServersReopenAndDeletingOneKeepsItsWorldCopyAndTheOtherServer()
    {
        using var ws = new TestWorkspace();
        using var services = new LauncherServices(ws.Paths);
        await SaveCatalogAsync(ws, new("1.21.1", "release", new("https://piston-meta.mojang.com/fixture/server.json"),
            new string('1', 40), DateTimeOffset.UtcNow));
        var port = FreePort();
        var first = await services.Servers.CreateAsync("First", "1.21.1", port);
        var second = await services.Servers.CreateAsync("Second", "1.21.1", port);
        var firstFolder = services.Servers.DirectoryFor(first.Id);
        var secondFolder = services.Servers.DirectoryFor(second.Id);
        foreach (var folder in new[] { firstFolder, secondFolder })
        {
            Directory.CreateDirectory(SafePath.Join(folder, "world"));
            Directory.CreateDirectory(SafePath.Join(folder, "mods"));
            Directory.CreateDirectory(SafePath.Join(folder, "plugins"));
            await File.WriteAllTextAsync(SafePath.Join(folder, "world/level.dat"), folder);
            await File.WriteAllTextAsync(SafePath.Join(folder, "mods/test.jar"), "mod");
            await File.WriteAllTextAsync(SafePath.Join(folder, "plugins/test.jar"), "plugin");
            await File.WriteAllTextAsync(SafePath.Join(folder, "whitelist.json"), "[]");
        }
        using var reopened = new LauncherServices(new LauncherPaths(ws.Paths.Root));
        Assert.Equal(new[] { first, second }, await reopened.Servers.ListAsync());
        var secondBefore = Directory.GetFiles(secondFolder, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllText);
        var plan = await reopened.Servers.PreviewDeletionAsync(first);
        Assert.True(plan.HasWorld);
        var copy = Assert.IsType<string>(await reopened.Servers.DeleteAsync(plan, true));
        Assert.Equal(firstFolder, await File.ReadAllTextAsync(SafePath.Join(copy, "world/level.dat")));
        Assert.False(Directory.Exists(firstFolder));
        Assert.Equal(second, Assert.Single(await reopened.Servers.ListAsync()));
        foreach (var (path, content) in secondBefore) Assert.Equal(content, await File.ReadAllTextAsync(path));

        using var different = new LauncherServices(new LauncherPaths(SafePath.Join(ws.Paths.Root, "different")));
        Assert.Empty(await different.Servers.ListAsync());
        await File.WriteAllTextAsync(SafePath.Join(secondFolder, "server.json"), "{");
        await Assert.ThrowsAsync<JsonException>(() => reopened.Servers.ListAsync());
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CardsKeepCommandsInTheOpenedServerWhileGameAndServerRunInBothOrders(bool gameFirst)
    {
        using var ws = new TestWorkspace();
        using var services = new LauncherServices(ws.Paths);
        await services.Store.InitializeAsync();
        var profile = ws.Profile();
        await services.Store.SaveProfileAsync(profile);
        var jar = "isolated server fixture"u8.ToArray();
        var hash = Convert.ToHexStringLower(SHA1.HashData(jar));
        var metadataUri = new Uri("https://piston-meta.mojang.com/fixture/server.json");
        var artifact = new Uri($"https://piston-data.mojang.com/v1/objects/{hash}/server.jar");
        var metadata = JsonSerializer.SerializeToUtf8Bytes(new { id = "1.21.1", javaVersion = new { majorVersion = 21 },
            downloads = new { server = new { url = artifact, sha1 = hash, size = jar.Length } } });
        await SaveCatalogAsync(ws, new("1.21.1", "release", metadataUri, Convert.ToHexStringLower(SHA1.HashData(metadata)), DateTimeOffset.UtcNow));
        using var client = new HttpClient(new StubHandler(request => new(HttpStatusCode.OK)
        { Content = new ByteArrayContent(request.RequestUri == metadataUri ? metadata : jar) }));
        var downloader = new VerifiedDownloader(client, new NetworkPolicy());
        var manager = new VanillaServerManager(ws.Paths, new(ws.Paths, downloader), downloader,
            (major, _, _) => Task.FromResult(new JavaRuntime("fixture", major, true)), (_, folder, _) => FixtureServer(folder));
        var first = await manager.CreateAsync("First", "1.21.1", FreePort());
        var second = await manager.CreateAsync("Second", "1.21.1", first.Port);
        await manager.AcceptSharedEulaAsync();
        var vm = new MainViewModel(services, manager);
        await vm.InitializeAsync();
        vm.Page = LauncherPage.Servers;
        await vm.LoadServersAsync();
        var window = new MainWindow { DataContext = vm, Width = gameFirst ? 980 : 1400 };
        window.Show();
        var card = vm.ServerCards.Single(row => row.Server == first);
        var other = vm.ServerCards.Single(row => row.Server == second);
        var gameExit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? game = null;
        Task? server = null;
        try
        {
            if (gameFirst) game = StartGame();
            server = vm.StartServerAsync(card);
            await WaitUntilAsync(() => card.CanSend);
            if (!gameFirst) game = StartGame();
            await WaitUntilAsync(() => vm.GameProcessStarted);
            Assert.True(vm.IsBusy);
            await vm.StartServerAsync(other);
            Assert.Equal(TextCatalog.Get("ServerPortBusy"), other.Status);
            await WaitUntilAsync(() => window.GetVisualDescendants().OfType<Button>().Count(b => b.DataContext is LocalServerCard) >= 2);
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.DataContext == card);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var detail = Assert.Single(window.OwnedWindows.OfType<LocalServerWindow>());
            detail.Width = gameFirst ? 640 : 1000;
            Assert.Same(card, detail.DataContext);
            detail.Close();
            Assert.True(card.CanSend);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            detail = Assert.Single(window.OwnedWindows.OfType<LocalServerWindow>());
            var input = detail.FindControl<TextBox>("ServerCommandInput")!;
            input.Text = "/op TestPlayer";
            Assert.True(input.Focus());
            detail.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            detail.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await WaitUntilAsync(() => card.Console.Contains("command op TestPlayer", StringComparison.Ordinal));
            foreach (var command in new[] { "list", "say hello  world", "deop TestPlayer" })
            {
                input.Text = command;
                detail.FindControl<Button>("ServerSendButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntilAsync(() => card.Console.Contains("command " + command, StringComparison.Ordinal));
            }
            Assert.Equal(1, card.Console.Split("command op TestPlayer", StringSplitOptions.None).Length - 1);
            Assert.Empty(other.Console);
            detail.Close();
            await vm.StopServerAsync(card);
            await server.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(vm.GameProcessStarted);
            Assert.False(card.IsActive);
        }
        finally
        {
            gameExit.TrySetResult();
            if (game is not null) await game.WaitAsync(TimeSpan.FromSeconds(15));
            card.Preparation?.Cancel();
            if (card.Session is { } session && await session.Ready) await session.StopAsync();
            if (server is not null) await server.WaitAsync(TimeSpan.FromSeconds(15));
            window.Close();
        }

        Task StartGame() => vm.RunAsync(ct => vm.RunProfileConsoleAsync(profile.Id, async (_, started, _, token) =>
        {
            started();
            await gameExit.Task.WaitAsync(token);
            return 0;
        }, ct));
    }

    private static async Task WaitUntilAsync(Func<bool> ready)
    {
        for (var attempt = 0; attempt < 500 && !ready(); attempt++) await Task.Delay(10);
        Assert.True(ready());
    }

    [Fact]
    public async Task FabricProfilePreviewExcludesClientAndUnknownModsAndBindsConfirmedFiles()
    {
        using var ws = new TestWorkspace();
        await ws.Store.InitializeAsync();
        var profile = ws.Profile(ModLoader.Fabric) with { Name = "Source", PreferredLoaderVersion = "0.20.1" };
        await ws.Store.SaveProfileAsync(profile);
        var metadataUri = new Uri("https://piston-meta.mojang.com/fixture/server.json");
        var metadata = """{"id":"1.21.1","javaVersion":{"majorVersion":21}}"""u8.ToArray();
        await SaveCatalogAsync(ws, new("1.21.1", "release", metadataUri,
            Convert.ToHexStringLower(SHA1.HashData(metadata)), DateTimeOffset.UtcNow));
        using var client = new HttpClient(new StubHandler(request =>
            request.RequestUri == metadataUri ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(metadata) } :
                throw new InvalidOperationException("Unexpected network request")));
        var downloads = new VerifiedDownloader(client, new NetworkPolicy());
        var manager = new VanillaServerManager(ws.Paths, new(ws.Paths, downloads), downloads,
            (_, _, _) => throw new InvalidOperationException(), (_, _, _) => throw new InvalidOperationException(), ws.Store);
        var mods = SafePath.Join(ws.Paths.Game(profile.Id), "mods");
        var clientMod = SafePath.Join(mods, "Client only.jar");
        var core = SafePath.Join(mods, "Example.jar");
        await File.WriteAllBytesAsync(clientMod, ModJar("""{"schemaVersion":1,"id":"display","version":"1.0.0","environment":"client"}"""));
        await File.WriteAllBytesAsync(SafePath.Join(mods, "Unknown.jar"), ModJar(null));
        await File.WriteAllBytesAsync(core, ModJar("""{"schemaVersion":1,"id":"example","version":"1.2.3","environment":"server","depends":{"display":"*"}}"""));
        var world = SafePath.Join(ws.Paths.Game(profile.Id), "saves/My world");
        Directory.CreateDirectory(world);
        await File.WriteAllTextAsync(SafePath.Join(world, "level.dat"), "fixture level data");
        await File.WriteAllTextAsync(SafePath.Join(world, "session.lock"), "original lock");
        var blocked = await manager.PreviewProfileAsync(profile, "My world");
        Assert.Contains("display", Assert.Single(blocked.Blockers));
        Assert.Equal("ServerProfileBlocked", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.CreateFromProfileAsync(blocked, "Dedicated", 25566, 3072))).Message);
        Assert.Empty(await manager.ListAsync());
        var payload = ModJar("""{"schemaVersion":1,"id":"example","version":"1.2.3","environment":"server","depends":{"minecraft":"1.21.1","fabricloader":">=0.20.0"}}""");
        await File.WriteAllBytesAsync(core, payload);
        var plan = await manager.PreviewProfileAsync(profile, "My world");
        Assert.Empty(plan.Blockers);
        Assert.Equal(ServerModDisposition.Client, plan.Mods.Single(mod => mod.File == "Client only.jar").Disposition);
        Assert.Equal(ServerModDisposition.Review, plan.Mods.Single(mod => mod.File == "Unknown.jar").Disposition);
        await File.WriteAllBytesAsync(core, ModJar("""{"schemaVersion":1,"id":"example","version":"1.2.4","environment":"server"}"""));
        Assert.Equal("ContentChanged", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.CreateFromProfileAsync(plan, "Dedicated", 25566, 3072))).Message);
        await File.WriteAllBytesAsync(core, payload);
        var created = await manager.CreateFromProfileAsync(plan, "Dedicated", 25566, 3072);
        Assert.Equal(ServerCore.Fabric, created.Core);
        Assert.Equal("0.20.1", created.Build);
        Assert.Equal(3072, created.MemoryMb);
        var directory = manager.DirectoryFor(created.Id);
        Assert.Equal("Dedicated", Path.GetFileName(directory));
        Assert.Equal(payload, await File.ReadAllBytesAsync(Assert.Single(Directory.GetFiles(SafePath.Join(directory, "mods")))));
        Assert.Equal("fixture level data", await File.ReadAllTextAsync(SafePath.Join(directory, "world/level.dat")));
        Assert.False(File.Exists(SafePath.Join(directory, "world/session.lock")));
        Assert.True(File.Exists(SafePath.Join(world, "session.lock")));
        Assert.True(File.Exists(clientMod));
        Assert.Equal(created, Assert.Single(await manager.ListAsync()));
    }

    [Fact]
    public async Task PaperUsesStableOfficialBuildAndPluginConfirmationAndRejectsOccupiedPort()
    {
        using var ws = new TestWorkspace();
        const string paper = "https://fill.papermc.io/v3/projects/paper";
        var entry = new VersionEntry("1.21.1", "release", new("https://piston-meta.mojang.com/fixture/server.json"),
            new string('0', 40), DateTimeOffset.UtcNow);
        await SaveCatalogAsync(ws, entry);
        var hash = new string('a', 64);
        var jar = new { name = "paper-1.21.1-133.jar", checksums = new { sha256 = hash }, size = 10,
            url = $"https://fill-data.papermc.io/v1/objects/{hash}/paper-1.21.1-133.jar" };
        var details = new { id = 133, channel = "STABLE", downloads = new Dictionary<string, object> { ["server:default"] = jar } };
        var unstable = new { id = 134, channel = "EXPERIMENTAL", downloads = details.downloads };
        using var client = new HttpClient(new StubHandler(request =>
        {
            object value = request.RequestUri!.AbsoluteUri switch
            {
                paper => new { versions = new Dictionary<string, string[]> { ["1.21"] = ["1.21.1", "1.21.4"] } },
                paper + "/versions/1.21.1/builds" => new[] { unstable, details },
                paper + "/versions/1.21.1/builds/133" => details,
                paper + "/versions/1.21.1/builds/134" => unstable,
                _ => throw new InvalidOperationException("Unexpected network request: " + request.RequestUri)
            };
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(value)) };
        }));
        var downloads = new VerifiedDownloader(client, new NetworkPolicy());
        var manager = new VanillaServerManager(ws.Paths, new(ws.Paths, downloads), downloads,
            (_, _, _) => throw new InvalidOperationException(), (_, _, _) => throw new InvalidOperationException());
        Assert.Equal(entry, Assert.Single(await manager.VersionsAsync(ServerCore.Paper)));
        Assert.Equal("133", Assert.Single(await manager.BuildsAsync(ServerCore.Paper, "1.21.1")));
        var portOwner = new TcpListener(IPAddress.Loopback, 0) { ExclusiveAddressUse = true };
        portOwner.Start();
        try
        {
            var port = ((IPEndPoint)portOwner.LocalEndpoint).Port;
            await Assert.ThrowsAsync<InvalidDataException>(() => manager.CreateAsync("Experimental", "1.21.1", port, 2048, ServerCore.Paper, "134"));
            var server = await manager.CreateAsync("Plugin server", "1.21.1", port, 2048, ServerCore.Paper, "133");
            await manager.AcceptEulaAsync(server);
            var folder = manager.DirectoryFor(server.Id);
            var spigot = SafePath.Join(folder, "spigot.yml");
            var paperConfig = SafePath.Join(folder, "config/paper-global.yml");
            Directory.CreateDirectory(Path.GetDirectoryName(paperConfig)!);
            await File.WriteAllTextAsync(spigot, "settings:\n  bungeecord: false\n");
            const string safePaper = "_version: 29\nproxies:\n  proxy-protocol: false\n  velocity:\n    enabled: false\n    secret: ''\n";
            await File.WriteAllTextAsync(paperConfig, safePaper);
            Assert.Equal("ServerPortBusy", (await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartAsync(server))).Message);
            foreach (var unsafePaper in new[]
            {
                safePaper.Replace("enabled: false", "enabled: true"),
                "  proxies:\n    velocity:\n      enabled: true\n",
                "proxies: {velocity: {enabled: true}}\n",
                "proxies:\n  velocity:\n    'enabled': true\n",
                "defaults: &shared\n  enabled: true\nproxies:\n  velocity:\n    <<: *shared\n"
            })
            {
                await File.WriteAllTextAsync(paperConfig, unsafePaper);
                Assert.Equal("ServerProxySettingsChanged",
                    (await Assert.ThrowsAsync<InvalidDataException>(() => manager.StartAsync(server))).Message);
            }
            await File.WriteAllTextAsync(paperConfig, safePaper);
            await File.WriteAllTextAsync(spigot, "settings:\n  bungeecord: true\n");
            Assert.Equal("ServerProxySettingsChanged",
                (await Assert.ThrowsAsync<InvalidDataException>(() => manager.StartAsync(server))).Message);
            await File.WriteAllTextAsync(spigot, "settings:\n  bungeecord: false\n");
            var file = SafePath.Join(ws.Paths.Root, "Example plugin.jar");
            var plugin = ModJar(null, plugin: true);
            await File.WriteAllBytesAsync(file, plugin);
            var plan = await manager.PreviewPluginAsync(server, file);
            Assert.False(File.Exists(plan.Target));
            await manager.AddPluginAsync(plan);
            Assert.Equal(plugin, await File.ReadAllBytesAsync(plan.Target));
            Assert.Equal(plugin, await File.ReadAllBytesAsync(file));
            Assert.Equal("Example plugin-2.jar", Path.GetFileName((await manager.PreviewPluginAsync(server, file)).Target));
        }
        finally { portOwner.Stop(); }
    }

    [Fact]
    public async Task PaperclipRequiresTheVerifiedMojangInputBeforeStartingJava()
    {
        using var ws = new TestWorkspace();
        var vanilla = "Mojang server fixture"u8.ToArray();
        var mojangHash = Convert.ToHexStringLower(SHA1.HashData(vanilla));
        var mojangSha256 = Convert.ToHexStringLower(SHA256.HashData(vanilla));
        var mojang = $"https://piston-data.mojang.com/v1/objects/{mojangHash}/server.jar";
        var metadataUrl = new Uri("https://piston-meta.mojang.com/fixture/server.json");
        var version = JsonSerializer.SerializeToUtf8Bytes(new { id = "1.21.1", javaVersion = new { majorVersion = 21 },
            downloads = new { server = new { url = mojang, sha1 = mojangHash, size = vanilla.Length } } });
        await SaveCatalogAsync(ws, new("1.21.1", "release", metadataUrl,
            Convert.ToHexStringLower(SHA1.HashData(version)), DateTimeOffset.UtcNow));
        var core = JarWithEntry("META-INF/download-context", $"{mojangSha256}\t{mojang}\tmojang_1.21.1.jar");
        var coreHash = Convert.ToHexStringLower(SHA256.HashData(core));
        var artifact = $"https://fill-data.papermc.io/v1/objects/{coreHash}/paper-1.21.1-133.jar";
        var details = JsonSerializer.SerializeToUtf8Bytes(new { id = 133, channel = "STABLE",
            downloads = new Dictionary<string, object> { ["server:default"] = new {
                name = "paper-1.21.1-133.jar", checksums = new { sha256 = coreHash }, size = core.Length, url = artifact } } });
        bool corrupt = true;
        using var client = new HttpClient(new StubHandler(request =>
        {
            var bytes = request.RequestUri!.AbsoluteUri switch
            {
                "https://fill.papermc.io/v3/projects/paper/versions/1.21.1/builds/133" => details,
                var url when url == metadataUrl.AbsoluteUri => version,
                var url when url == artifact => core,
                var url when url == mojang => corrupt ? new byte[vanilla.Length] : vanilla,
                _ => throw new InvalidOperationException("Unexpected request: " + request.RequestUri)
            };
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        var downloads = new VerifiedDownloader(client, new NetworkPolicy());
        var javaChecks = 0;
        var manager = new VanillaServerManager(ws.Paths, new(ws.Paths, downloads), downloads,
            (major, _, _) => { javaChecks++; Assert.Equal(21, major); return Task.FromResult(new JavaRuntime("fixture", major, true)); },
            (_, folder, _) =>
            {
                Assert.Equal(vanilla, File.ReadAllBytes(SafePath.Join(folder, "cache/mojang_1.21.1.jar")));
                return FixtureServer(folder);
            });
        var server = await manager.CreateAsync("Paper", "1.21.1", FreePort(), 1536, ServerCore.Paper, "133");
        await manager.AcceptEulaAsync(server);
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.StartAsync(server));
        Assert.Equal(0, javaChecks);
        corrupt = false;
        var session = await manager.StartAsync(server);
        try { Assert.True(await session.Ready.WaitAsync(TimeSpan.FromSeconds(12))); }
        finally { await session.StopAsync(); }
        Assert.Equal(0, await session.Completion);
        Assert.Equal(1, javaChecks);
    }

    [Theory]
    [InlineData(ServerCore.Fabric, "0.20.1", "fabric")]
    [InlineData(ServerCore.Forge, "52.1.0", "forge")]
    public async Task ExistingServerModsPreviewDependenciesAndPublishOnlyToTheSelectedServer(ServerCore core, string build, string loader)
    {
        using var ws = new TestWorkspace();
        var root = Jar("root_mod", "library_mod");
        var library = Jar("library_mod");
        var local = Jar("local_mod");
        var rootVersion = Version("root", root, "library");
        var libraryVersion = Version("library", library);
        using var http = new HttpClient(new StubHandler(request =>
        {
            var uri = request.RequestUri!;
            if (uri.Host == "cdn.modrinth.com")
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(uri.AbsolutePath == "/root.jar" ? root : library) };
            var route = uri.AbsolutePath;
            if (route.StartsWith("/v2/version_file/", StringComparison.Ordinal))
                return new(HttpStatusCode.NotFound);
            var body = route switch
            {
                "/v2/project/root" => Project("root", "required"),
                "/v2/project/library" => Project("library", "optional"),
                "/v2/project/client" => Project("client", "unsupported"),
                "/v2/version/rootVersion" => rootVersion,
                "/v2/version/libraryVersion" => libraryVersion,
                _ => throw new InvalidOperationException("Unexpected request " + uri)
            };
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body)) };
        }));
        var network = new NetworkPolicy();
        var downloads = new VerifiedDownloader(http, network);
        var catalog = new ModrinthClient(ws.Paths, downloads, network);
        var manager = new VanillaServerManager(ws.Paths, new(ws.Paths, downloads), downloads,
            (_, _, _) => throw new InvalidOperationException(), (_, _, _) => throw new InvalidOperationException());
        var target = await SavedServerAsync("Target");
        var other = await SavedServerAsync("Other");
        var folder = SafePath.Join(manager.DirectoryFor(target.Id), "mods");
        var otherFolder = SafePath.Join(manager.DirectoryFor(other.Id), "mods");
        var source = SafePath.Join(ws.Paths.Root, "Local.jar");
        var forge = core == ServerCore.Forge;
        await File.WriteAllBytesAsync(source, local);
        var localPlan = await manager.PreviewLocalModAsync(target, source);
        Assert.Equal(forge, Assert.Single(localPlan.Files).RequiresReview);
        Assert.False(Directory.Exists(folder)); // Closing the review requires no rollback.
        if (forge)
        {
            Assert.Equal("ServerModReview", (await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ApplyServerModsAsync(localPlan))).Message);
            var clientPath = SafePath.Join(ws.Paths.Root, "Client.jar");
            await File.WriteAllBytesAsync(clientPath, Jar("client_mod", clientOnly: true));
            var clientPlan = await manager.PreviewLocalModAsync(target, clientPath);
            Assert.Empty(clientPlan.Files);
            Assert.Equal(ServerModDisposition.Client, Assert.Single(clientPlan.Excluded).Disposition);
            var catalogClient = await manager.PreviewModrinthModAsync(target, catalog, "client", "unused");
            Assert.Equal(ServerModDisposition.Client, Assert.Single(catalogClient.Excluded).Disposition);
            var unknown = SafePath.Join(ws.Paths.Root, "Unknown.jar");
            await File.WriteAllBytesAsync(unknown, ModJar(null));
            Assert.Equal(ServerModDisposition.Review, Assert.Single((await manager.PreviewLocalModAsync(target, unknown)).Excluded).Disposition);
            var wrong = SafePath.Join(ws.Paths.Root, "Wrong.jar");
            await File.WriteAllBytesAsync(wrong, Jar("wrong_mod", "forge", range: "[53,)"));
            Assert.Contains("forge [53,)", (await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PreviewLocalModAsync(target, wrong))).Message);
        }
        var missing = SafePath.Join(ws.Paths.Root, "Root.jar");
        await File.WriteAllBytesAsync(missing, root);
        Assert.Contains("library_mod", (await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PreviewLocalModAsync(target, missing))).Message);
        await manager.ApplyServerModsAsync(localPlan, reviewedForgeMods: forge);
        Assert.Equal(local, await File.ReadAllBytesAsync(Assert.Single(Directory.GetFiles(folder))));
        Assert.Empty((await manager.PreviewLocalModAsync(target, source)).Files);

        var remotePlan = await manager.PreviewModrinthModAsync(target, catalog, "root", "rootVersion");
        Assert.Equal(2, remotePlan.Files.Count);
        Assert.All(remotePlan.Files, file => Assert.Equal(folder, Path.GetDirectoryName(file.Target)));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.ApplyServerModsAsync(remotePlan, cancelled.Token, forge));
        Assert.Single(Directory.GetFiles(folder));
        await manager.ApplyServerModsAsync(remotePlan, reviewedForgeMods: forge);
        Assert.Equal(root, await File.ReadAllBytesAsync(SafePath.Join(folder, "root.jar")));
        Assert.Equal(library, await File.ReadAllBytesAsync(SafePath.Join(folder, "library.jar")));
        Assert.Empty((await manager.PreviewModrinthModAsync(target, catalog, "root", "rootVersion")).Files);
        Assert.Equal(3, Directory.GetFiles(folder).Length);
        Assert.False(Directory.Exists(otherFolder));
        Assert.False(Directory.Exists(SafePath.Join(ws.Paths.Root, "instances/" + target.Id.ToString("N") + "/game/mods")));

        async Task<LocalVanillaServer> SavedServerAsync(string name)
        {
            var server = new LocalVanillaServer(Guid.NewGuid(), name, "1.21.1", 25565) { Core = core, Build = build };
            await AtomicFile.WriteJsonAsync(SafePath.Join(manager.DirectoryFor(server.Id), "server.json"), server);
            return server;
        }
        byte[] Jar(string id, string? dependency = null, bool clientOnly = false, string range = "[1,2)") =>
            core == ServerCore.Fabric ? ModJar(JsonSerializer.Serialize(new
            {
                schemaVersion = 1, id, version = "1.0", environment = "server",
                depends = dependency is null ? new Dictionary<string, string>() : new() { [dependency] = ">=1.0" }
            })) : JarWithEntry("META-INF/mods.toml", $"""
                modLoader="javafml"
                loaderVersion="[52,53)"
                license="MIT"
                clientSideOnly={clientOnly.ToString().ToLowerInvariant()}
                [[mods]]
                modId="{id}"
                version="1.0"
                [[dependencies.{id}]]
                modId="client_dependency"
                mandatory=true
                versionRange="[1,2)"
                side="CLIENT"
                """ + (dependency is null ? "" : $"""

                [[dependencies.{id}]]
                modId="{dependency}"
                mandatory=true
                versionRange="{range}"
                side="SERVER"
                """));
        static object Project(string id, string side) => new { id, slug = id, title = id, project_type = "mod", client_side = "optional", server_side = side };
        object Version(string id, byte[] payload, string? dependency = null) => new
        {
            id = id + "Version", project_id = id, name = id, version_number = "1.0",
            date_published = "2025-01-01T00:00:00Z", version_type = "release",
            game_versions = new[] { "1.21.1" }, loaders = new[] { loader },
            dependencies = dependency is null ? [] : new object[]
                { new { project_id = dependency, version_id = dependency + "Version", dependency_type = "required" } },
            files = new[] { new { filename = id + ".jar", url = "https://cdn.modrinth.com/" + id + ".jar", size = payload.Length,
                hashes = new { sha512 = Convert.ToHexStringLower(SHA512.HashData(payload)) }, primary = true } }
        };
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static byte[] ModJar(string? metadata, bool plugin = false) =>
        JarWithEntry(plugin ? "plugin.yml" : metadata is null ? "README.txt" : "fabric.mod.json",
            metadata ?? (plugin ? "name: Example\nmain: example.Plugin\nversion: 1.0" : "No metadata"));

    private static byte[] JarWithEntry(string name, string content)
    {
        using var output = new MemoryStream();
        using (var jar = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(jar.CreateEntry(name).Open()))
            writer.Write(content);
        return output.ToArray();
    }

    [AvaloniaFact]
    public async Task ServersPageSavesOneExplicitEulaAcceptanceAcrossServersAndRestarts()
    {
        using var ws = new TestWorkspace();
        using var services = new LauncherServices(ws.Paths);
        const string versionId = "1.21.1";
        var entry = new VersionEntry(versionId, "release", new Uri("https://piston-meta.mojang.com/fixture/server.json"),
            new string('1', 40), DateTimeOffset.UtcNow);
        await SaveCatalogAsync(ws, entry);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.Page = LauncherPage.Servers;
        await vm.LoadServersAsync();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            using var portOwner = new TcpListener(IPAddress.Loopback, 0) { ExclusiveAddressUse = true };
            portOwner.Start();
            vm.NewServerPort = ((IPEndPoint)portOwner.LocalEndpoint).Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            vm.NewServerName = "UI server";
            var memory = window.FindControl<TextBox>("NewServerMemoryInput")!;
            memory.Text = "6256";
            Assert.True(memory.Focus());
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal("6144", memory.Text);
            window.FindControl<Button>("CreateLocalServerButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var attempt = 0; attempt < 200 && vm.ServerOperationActive; attempt++) await Task.Delay(10);
            var server = Assert.IsType<LocalVanillaServer>(vm.SelectedServer);
            Assert.Equal(6144, server.MemoryMb);
            Assert.Equal("localhost:" + vm.NewServerPort, server.Address);
            var eulaPath = SafePath.Join(services.Servers.DirectoryFor(server.Id), "eula.txt");
            var accept = window.FindControl<Button>("AcceptLocalServerEulaButton")!;
            var open = window.FindControl<Button>("OpenLocalServerEulaButton")!;
            Assert.True(accept.IsEffectivelyVisible);
            Assert.True(open.IsEffectivelyVisible);
            accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(window.OwnedWindows.OfType<ContentActionDialog>()).Close();
            await Task.Yield();
            Assert.False(await services.Servers.HasSharedEulaAsync());
            Assert.False(File.Exists(eulaPath));
            Assert.Equal("ServerEulaNeeded", (await Assert.ThrowsAsync<InvalidOperationException>(() => services.Servers.StartAsync(server))).Message);
            accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var dialog = Assert.Single(window.OwnedWindows.OfType<ContentActionDialog>());
            dialog.FindControl<Button>("ConfirmAction")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var attempt = 0; attempt < 400 && (vm.ShowServerEula || vm.ServerOperationActive); attempt++) await Task.Delay(10);
            Assert.False(accept.IsEffectivelyVisible);
            Assert.False(open.IsEffectivelyVisible);
            Assert.True(await services.Servers.HasSharedEulaAsync());
            Assert.False(File.Exists(eulaPath));
            Assert.Equal("ServerPortBusy", (await Assert.ThrowsAsync<InvalidOperationException>(() => services.Servers.StartAsync(server))).Message);
            Assert.True(File.Exists(eulaPath), vm.ServerStatus);
            var firstAcceptance = await File.ReadAllTextAsync(eulaPath);
            Assert.Contains("eula=true", firstAcceptance);

            var reopened = new MainViewModel(services);
            await reopened.InitializeAsync();
            reopened.Page = LauncherPage.Servers;
            await reopened.LoadServersAsync();
            Assert.False(reopened.ShowServerEula);
            reopened.NewServerName = "Second server";
            reopened.NewServerPort = vm.NewServerPort;
            await reopened.CreateServerAsync();
            var second = Assert.IsType<LocalVanillaServer>(reopened.SelectedServer);
            Assert.NotEqual(server.Id, second.Id);
            var secondEula = SafePath.Join(services.Servers.DirectoryFor(second.Id), "eula.txt");
            await File.WriteAllTextAsync(secondEula, "eula=false\n");
            Assert.Equal("ServerEulaFileInvalid", (await Assert.ThrowsAsync<InvalidOperationException>(() => services.Servers.StartAsync(second))).Message);
            Assert.Equal("eula=false\n", await File.ReadAllTextAsync(secondEula));
            File.Delete(secondEula);
            Assert.Equal("ServerPortBusy", (await Assert.ThrowsAsync<InvalidOperationException>(() => services.Servers.StartAsync(second))).Message);
            Assert.Equal(firstAcceptance, await File.ReadAllTextAsync(secondEula));
            Assert.Empty(await ws.Store.GetProfilesAsync());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ServerMinecraftPickerLoadsMissingCatalogOnlineAndExplainsOfflineState()
    {
        using var ws = new TestWorkspace();
        var requests = 0;
        using var services = new LauncherServices(ws.Paths, coreTransport: new StubHandler(request =>
        {
            Assert.Equal(VersionManager.ManifestUri, request.RequestUri);
            requests++;
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent("""
                {"latest":{"release":"1.21.1"},"versions":[{"id":"1.21.1","type":"release",
                "url":"https://piston-meta.mojang.com/fixture/version.json","sha1":"1111111111111111111111111111111111111111",
                "releaseTime":"2024-08-08T00:00:00Z"}]}
                """u8.ToArray()) };
        }));
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.Offline = true;
        vm.Page = LauncherPage.Servers;
        await vm.LoadServersAsync();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            var versions = window.FindControl<ComboBox>("ServerMinecraftVersionPicker")!;
            var builds = window.FindControl<ComboBox>("ServerCoreBuildPicker")!;
            var create = window.FindControl<Button>("CreateLocalServerButton")!;
            Assert.False(builds.IsEffectivelyVisible);
            Assert.False(versions.IsEffectivelyEnabled);
            Assert.False(create.IsEffectivelyEnabled);
            Assert.Equal(TextCatalog.Get("Version"), versions.PlaceholderText);
            Assert.Equal(TextCatalog.Get("ServerCatalogOffline"), vm.ServerCatalogStatus);
            Assert.Equal(0, requests);

            vm.Offline = false;
            await vm.LoadServersAsync();
            Assert.True(versions.IsEffectivelyEnabled);
            Assert.True(create.IsEffectivelyEnabled);
            Assert.Equal("1.21.1", Assert.IsType<VersionEntry>(versions.SelectedItem).Id);
            Assert.False(vm.HasServerCatalogStatus);
            Assert.Equal(1, requests);

            vm.Offline = true;
            await vm.LoadServersAsync();
            Assert.Equal("1.21.1", vm.NewServerVersion?.Id);
            Assert.Equal(1, requests);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ForgeFormUsesTheExactBuildAfterCoreSwitchesAndFailedRefresh()
    {
        using var ws = new TestWorkspace();
        var uri = new Uri("https://piston-meta.mojang.com/fixture/version.json");
        await SaveCatalogAsync(ws, new("1.21.4", "release", uri, new string('2', 40), DateTimeOffset.UtcNow));
        var requests = 0;
        var fail = false;
        var delayedPaper = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var paperRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var paperReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var transport = new ServerCatalogTransport(async request =>
        {
            if (request.RequestUri!.Host == "fill.papermc.io")
            {
                paperRequested.TrySetResult();
                var response = await delayedPaper.Task;
                paperReturned.TrySetResult();
                return response;
            }
            Assert.Equal(VersionManager.ManifestUri, request.RequestUri);
            requests++;
            if (fail) return new(HttpStatusCode.ServiceUnavailable);
            return new(HttpStatusCode.OK) { Content = new StringContent("""
                {"latest":{"release":"1.21.4"},"versions":[
                    {"id":"1.21.4","type":"release","url":"https://piston-meta.mojang.com/fixture/new.json","sha1":"2222222222222222222222222222222222222222","releaseTime":"2024-12-03T00:00:00Z"},
                    {"id":"1.21.1","type":"release","url":"https://piston-meta.mojang.com/fixture/forge.json","sha1":"1111111111111111111111111111111111111111","releaseTime":"2024-08-08T00:00:00Z"}]}
                """) };
        });
        using var services = new LauncherServices(ws.Paths, coreTransport: transport);
        using var downloads = new HttpClient(transport, disposeHandler: false);
        var preparedJava = 0;
        var manager = new VanillaServerManager(ws.Paths, services.Versions, new(downloads, services.Network),
            (major, _, _) => { preparedJava = major; throw new IOException("Fixture reached Forge Java preparation."); },
            (_, _, _) => throw new InvalidOperationException("Fixture must not start a process."), services.Store);
        var vm = new MainViewModel(services, manager);
        await vm.InitializeAsync();
        var window = new MainWindow { DataContext = vm, Width = 980 };
        window.Show();
        try
        {
            var core = window.FindControl<ComboBox>("ServerCorePicker")!;
            var versions = window.FindControl<ComboBox>("ServerMinecraftVersionPicker")!;
            var builds = window.FindControl<ComboBox>("ServerCoreBuildPicker")!;
            var create = window.FindControl<Button>("CreateLocalServerButton")!;
            window.FindControl<Button>("ServersNavButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => !vm.ServerCatalogLoading && versions.SelectedItem is VersionEntry);
            Assert.Equal("1.21.4", Assert.IsType<VersionEntry>(versions.SelectedItem).Id);
            Assert.Equal(0, requests);
            window.FindControl<TextBox>("NewServerNameInput")!.Text = "Disposable Forge server";
            await SelectAsync(ServerCore.Forge, "52.1.0");
            await SelectAsync(ServerCore.Fabric, "0.20.1");
            await SelectAsync(ServerCore.Forge, "52.1.0");
            core.SelectedItem = vm.ServerCores.Single(option => option.Core == ServerCore.Paper);
            await paperRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await SelectAsync(ServerCore.Forge, "52.1.0");
            delayedPaper.SetResult(new(HttpStatusCode.OK) { Content = new StringContent("""{"versions":{"1.21":["1.21.4"]}}""") });
            await paperReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("1.21.1", Assert.IsType<VersionEntry>(versions.SelectedItem).Id);
            Assert.Equal("52.1.0", builds.SelectedItem);
            Assert.Equal(1, requests);
            fail = true;
            var refresh = window.FindControl<Button>("RefreshServerCatalogButton")!;
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => !vm.ServerCatalogLoading && requests == 2);
            Assert.Equal("1.21.1", Assert.IsType<VersionEntry>(versions.SelectedItem).Id);
            Assert.Equal("52.1.0", builds.SelectedItem);
            Assert.True(create.IsEffectivelyEnabled);
            Assert.Empty(vm.ServerCatalogStatus);
            create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => vm.CanEditServer && preparedJava != 0);
            Assert.Equal(21, preparedJava);
            Assert.Contains("Forge Java preparation", vm.ServerStatus);
            Assert.Empty(await manager.ListAsync());

            core.SelectedItem = vm.ServerCores.Single(option => option.Core == ServerCore.Quilt);
            await WaitUntilAsync(() => !vm.ServerCatalogLoading && vm.HasServerCatalogStatus);
            Assert.Equal(TextCatalog.Get("ServerBuildUnavailable"), vm.ServerCatalogStatus);
            Assert.False(create.IsEffectivelyEnabled);

            File.Delete(SafePath.Join(ws.Paths.Cache, "metadata/catalog.json"));
            core.SelectedItem = vm.ServerCores.Single(option => option.Core == ServerCore.Forge);
            await WaitUntilAsync(() => !vm.ServerCatalogLoading && requests == 3);
            Assert.Contains("503", vm.ServerCatalogStatus);
            Assert.False(create.IsEffectivelyEnabled);
            vm.Offline = true;
            await vm.LoadServersAsync();
            Assert.Equal(TextCatalog.Get("ServerCatalogOffline"), vm.ServerCatalogStatus);
            Assert.Equal(3, requests);

            async Task SelectAsync(ServerCore choice, string build)
            {
                core.SelectedItem = vm.ServerCores.Single(option => option.Core == choice);
                await WaitUntilAsync(() => !vm.ServerCatalogLoading && Equals(builds.SelectedItem, build));
                Assert.Equal("1.21.1", Assert.IsType<VersionEntry>(versions.SelectedItem).Id);
                Assert.True(create.IsEffectivelyEnabled);
            }
        }
        finally { delayedPaper.TrySetCanceled(); window.Close(); }
    }

    private sealed class ServerCatalogTransport(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }

    [Fact]
    public async Task SettingsRequireFreshPreviewPreserveUnrelatedPropertiesAndRecoverInterruptedWrite()
    {
        using var ws = new TestWorkspace();
        await SaveCatalogAsync(ws, new("1.21.1", "release", new("https://piston-meta.mojang.com/fixture/server.json"),
            new string('1', 40), DateTimeOffset.UtcNow));
        using var client = new HttpClient(new StubHandler(_ => throw new InvalidOperationException("Unexpected network request")));
        var downloads = new VerifiedDownloader(client, new NetworkPolicy());
        var manager = new VanillaServerManager(ws.Paths, new(ws.Paths, downloads), downloads,
            (_, _, _) => throw new InvalidOperationException(), (_, _, _) => throw new InvalidOperationException());
        var server = await manager.CreateAsync("Private server", "1.21.1", FreePort(), 2048, ServerCore.Vanilla, null, privateServer: true);
        var folder = manager.DirectoryFor(server.Id);
        var properties = SafePath.Join(folder, "server.properties");
        var original = await manager.ReadSettingsAsync(server);
        Assert.True(original.Settings.AllowList && original.Settings.EnforceAllowList);
        var desired = original.Settings with { Name = "New name", Motd = " Світ = \\ test 🌍", GameMode = "creative",
            Difficulty = "hard", Pvp = false, MaxPlayers = 7, MemoryMb = 3072, Port = FreePort() };
        await File.AppendAllTextAsync(properties, "view-distance=5\n");
        Assert.Equal("ContentChanged", (await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ApplySettingsAsync(original, desired))).Message);
        var fresh = await manager.ReadSettingsAsync(server);
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.ApplySettingsAsync(fresh, desired with { Motd = "test\nonline-mode=false" }));
        var updated = await manager.ApplySettingsAsync(fresh, desired);
        Assert.Equal(updated, Assert.Single(await manager.ListAsync()));
        var loaded = await manager.ReadSettingsAsync(updated);
        Assert.Equal(desired, loaded.Settings);
        Assert.Equal(fresh.Settings, loaded.Previous);
        var written = await File.ReadAllTextAsync(properties);
        Assert.Contains("view-distance=5", written);
        Assert.Contains("online-mode=true", written);
        Assert.Contains("server-ip=127.0.0.1", written);

        var pending = SafePath.Join(folder, ".settings-pending.json");
        File.Copy(SafePath.Join(folder, ".settings-previous.json"), pending);
        Assert.Equal("ServerSettingsPending", (await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartAsync(updated))).Message);
        var recovered = await manager.ReadSettingsAsync(updated);
        Assert.Equal(server, recovered.Server);
        Assert.Equal(fresh.Settings, recovered.Settings);
        Assert.False(File.Exists(pending));
        Assert.Equal(server, Assert.Single(await manager.ListAsync()));
    }

    [Fact]
    public async Task PlayerListsUseVerifiedIdentityAndKeepOperatorAndBanStateIndependent()
    {
        using var ws = new TestWorkspace();
        await SaveCatalogAsync(ws, new("1.21.1", "release", new("https://piston-meta.mojang.com/fixture/server.json"),
            new string('1', 40), DateTimeOffset.UtcNow));
        var uuid = Guid.NewGuid();
        using var handler = new StubHandler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("https://api.minecraftservices.com/minecraft/profile/lookup/name/Owner", request.RequestUri!.AbsoluteUri);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new { id = uuid.ToString("N"), name = "Owner" })) };
        });
        using var client = new HttpClient(handler);
        var policy = new NetworkPolicy();
        var downloads = new VerifiedDownloader(client, policy);
        var manager = new VanillaServerManager(ws.Paths, new(ws.Paths, downloads), downloads,
            (_, _, _) => throw new InvalidOperationException(), (_, _, _) => throw new InvalidOperationException());
        var server = await manager.CreateAsync("Independent lists", "1.21.1", FreePort());
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.AddPlayerAsync(server, ServerPlayerList.Allowed, "Owner\nop Other"));
        Assert.Equal(0, handler.Requests);
        await manager.AddPlayerAsync(server, ServerPlayerList.Allowed, "Owner");
        var initial = await manager.ReadPlayersAsync(server);
        Assert.Equal(new ServerPlayer(uuid, "Owner"), Assert.Single(initial.Allowed));
        Assert.Empty(initial.Operators);
        Assert.Empty(initial.Blocked);
        var folder = manager.DirectoryFor(server.Id);
        var allowFile = SafePath.Join(folder, "whitelist.json");
        var original = await File.ReadAllBytesAsync(allowFile);
        policy.Offline = true;
        Assert.Equal("ServerPlayerLookupFailed", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.AddPlayerAsync(server, ServerPlayerList.Allowed, "Owner"))).Message);
        Assert.Equal(original, await File.ReadAllBytesAsync(allowFile));
        policy.Offline = false;

        await manager.AddPlayerAsync(server, ServerPlayerList.Blocked, "Owner");
        await manager.AddPlayerAsync(server, ServerPlayerList.Operators, "Owner");
        var reopened = new VanillaServerManager(ws.Paths, new(ws.Paths, downloads), downloads,
            (_, _, _) => throw new InvalidOperationException(), (_, _, _) => throw new InvalidOperationException());
        var saved = await reopened.ReadPlayersAsync(server);
        Assert.Equal(uuid, Assert.Single(saved.Blocked).Uuid);
        Assert.Equal(uuid, Assert.Single(saved.Operators).Uuid);
        Assert.Contains("\"expires\": \"forever\"", await File.ReadAllTextAsync(SafePath.Join(folder, "banned-players.json")));
        Assert.Contains("\"level\": 4", await File.ReadAllTextAsync(SafePath.Join(folder, "ops.json")));
        await reopened.RemovePlayerAsync(server, ServerPlayerList.Blocked, uuid);
        await reopened.RemovePlayerAsync(server, ServerPlayerList.Allowed, uuid);
        Assert.Empty((await reopened.ReadPlayersAsync(server)).Blocked);
        Assert.Empty((await reopened.ReadPlayersAsync(server)).Allowed);
        Assert.Single((await reopened.ReadPlayersAsync(server)).Operators);
        await reopened.RemovePlayerAsync(server, ServerPlayerList.Operators, uuid);
        Assert.Empty((await reopened.ReadPlayersAsync(server)).Operators);
    }

    [Fact]
    public async Task OfficialArtifactAndJavaAreCheckedBeforeAProcessStartsAndStopWaitsForSaving()
    {
        using var ws = new TestWorkspace();
        const string versionId = "1.21.1";
        var jar = "official fixture server"u8.ToArray();
        var jarHash = Convert.ToHexStringLower(SHA1.HashData(jar));
        var artifact = new Uri($"https://piston-data.mojang.com/v1/objects/{jarHash}/server.jar");
        var metadataUri = new Uri("https://piston-meta.mojang.com/fixture/server.json");
        var metadata = JsonSerializer.SerializeToUtf8Bytes(new { id = versionId, javaVersion = new { majorVersion = 21 },
            downloads = new { server = new { url = artifact, sha1 = jarHash, size = jar.Length } } });
        var entry = new VersionEntry(versionId, "release", metadataUri, Convert.ToHexStringLower(SHA1.HashData(metadata)), DateTimeOffset.UtcNow);
        await SaveCatalogAsync(ws, entry);
        var urls = new List<Uri>();
        var supplyBadArtifact = true;
        using var client = new HttpClient(new StubHandler(request =>
        {
            urls.Add(request.RequestUri!);
            var bytes = request.RequestUri == metadataUri ? metadata : request.RequestUri == artifact
                ? (supplyBadArtifact ? new byte[jar.Length] : jar) : throw new InvalidOperationException("Unexpected server download");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        var downloads = new VerifiedDownloader(client, new NetworkPolicy());
        var versions = new VersionManager(ws.Paths, downloads);
        var javaChecks = 0;
        var wrongJava = true;
        var tamperDuringPreparation = false;
        var port = FreePort();
        var manager = new VanillaServerManager(ws.Paths, versions, downloads, (major, _, _) =>
        {
            javaChecks++;
            Assert.Equal(21, major);
            return Task.FromResult(new JavaRuntime("fixture", wrongJava ? 17 : major, true));
        }, (server, folder, _) =>
        {
            Assert.Equal($"localhost:{port}", server.Address);
            Assert.Equal(jar, File.ReadAllBytes(SafePath.Join(folder, "server.jar")));
            if (tamperDuringPreparation)
                File.WriteAllText(SafePath.Join(folder, "server.properties"), "online-mode=false\nserver-ip=0.0.0.0\n");
            return FixtureServer(folder);
        });
        var created = await manager.CreateAsync("My local server", versionId, port);
        Assert.Equal(created, Assert.Single(await manager.ListAsync()));
        var folder = manager.DirectoryFor(created.Id);
        Assert.Equal("My local server", Path.GetFileName(folder));
        Assert.Contains(Path.Combine(ws.Paths.Root, "servers"), folder);
        Assert.False(File.Exists(SafePath.Join(folder, "eula.txt")));
        Assert.Equal("ServerEulaNeeded", (await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartAsync(created))).Message);
        Assert.Empty(urls);
        await manager.AcceptEulaAsync(created);
        var eula = await File.ReadAllTextAsync(SafePath.Join(folder, "eula.txt"));
        Assert.Contains("eula=true", eula);
        Assert.Contains(VanillaServerManager.EulaUrl, eula);

        var properties = SafePath.Join(folder, "server.properties");
        var original = await File.ReadAllTextAsync(properties);
        Assert.Contains("online-mode=true", original);
        Assert.Contains("server-ip=127.0.0.1", original);
        await File.WriteAllTextAsync(properties, original + "online\\u002dmode=false\n");
        Assert.Equal("ServerPropertiesChanged", (await Assert.ThrowsAsync<InvalidDataException>(() => manager.StartAsync(created))).Message);
        Assert.Empty(urls);
        await File.WriteAllTextAsync(properties, original);

        await Assert.ThrowsAsync<InvalidDataException>(() => manager.StartAsync(created));
        Assert.Equal(0, javaChecks);
        Assert.False(File.Exists(SafePath.Join(folder, "server.jar")));
        supplyBadArtifact = false;
        Assert.Equal("ServerJavaMismatch", (await Assert.ThrowsAsync<InvalidDataException>(() => manager.StartAsync(created))).Message);
        wrongJava = false;
        var session = await manager.StartAsync(created);
        try
        {
            Assert.True(await session.Ready.WaitAsync(TimeSpan.FromSeconds(12)));
            Assert.Equal("ServerAlreadyRunning", (await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartAsync(created))).Message);
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ReadPlayersAsync(created));
            await Assert.ThrowsAsync<ArgumentException>(() => session.SendAsync("list\nstop"));
            await session.SendAsync("list");
        }
        finally { await session.StopAsync(); }
        Assert.Equal(0, await session.Completion);
        var log = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(SafePath.Join(folder, "launcher-logs"))));
        Assert.Contains("fixture online", log);
        Assert.Contains("command list", log);
        Assert.Contains("fixture saved world", log);
        Assert.Contains("Exit code: 0", log);
        Assert.Equal(new[] { metadataUri, artifact, artifact }, urls);
        Assert.Equal(2, javaChecks);
        tamperDuringPreparation = true;
        Assert.Equal("ServerPropertiesChanged", (await Assert.ThrowsAsync<InvalidDataException>(() => manager.StartAsync(created))).Message);
        Assert.Single(Directory.GetFiles(SafePath.Join(folder, "launcher-logs")));
    }

    [Theory]
    [InlineData("52.1.0", "ServerForgeIntegrity")]
    [InlineData("52.1.1", "ServerBuildUnavailable")]
    public async Task ExistingForgeServerChecksItsPinnedBuildAndPreparedFilesBeforeResolvingJava(string build, string failure)
    {
        using var ws = new TestWorkspace();
        const string version = "1.21.1";
        var metadataUri = new Uri("https://piston-meta.mojang.com/fixture/forge-server.json");
        var hash = new string('1', 40);
        var metadata = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = version,
            javaVersion = new { majorVersion = 21 },
            downloads = new { server = new { sha1 = hash, size = 1, url = $"https://piston-data.mojang.com/v1/objects/{hash}/server.jar" } }
        });
        await SaveCatalogAsync(ws, new(version, "release", metadataUri, Convert.ToHexStringLower(SHA1.HashData(metadata)), DateTimeOffset.UtcNow));
        using var handler = new StubHandler(request => request.RequestUri == metadataUri
            ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(metadata) }
            : throw new InvalidOperationException("Unexpected download."));
        using var client = new HttpClient(handler);
        var downloads = new VerifiedDownloader(client, new NetworkPolicy());
        var manager = new VanillaServerManager(ws.Paths, new(ws.Paths, downloads), downloads,
            (_, _, _) => throw new InvalidOperationException("Java must not be resolved for an invalid Forge installation."),
            (_, _, _) => throw new InvalidOperationException("Java must not be started for an invalid Forge installation."));
        var saved = await manager.CreateAsync("Existing server", version, FreePort(), 1536, ServerCore.Vanilla, null, privateServer: true);
        var folder = manager.DirectoryFor(saved.Id);
        var forge = saved with { Core = ServerCore.Forge, Build = build };
        await AtomicFile.WriteJsonAsync(SafePath.Join(folder, "server.json"), forge);
        await File.WriteAllTextAsync(SafePath.Join(folder, ".pteno-forge-installer.jar"), "corrupt installer");
        Assert.Equal(forge, Assert.Single(await manager.ListAsync()));
        Assert.Equal("ServerEulaNeeded", (await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartAsync(forge))).Message);
        await manager.AcceptEulaAsync(forge);

        var error = await Record.ExceptionAsync(() => manager.StartAsync(forge));
        Assert.Equal(failure, error?.Message);
        if (failure == "ServerForgeIntegrity") Assert.IsType<InvalidDataException>(error);
        else Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(0, handler.Requests);
        Assert.False(File.Exists(SafePath.Join(folder, "active-server.json")));
    }

    private static Task SaveCatalogAsync(TestWorkspace workspace, VersionEntry entry) =>
        AtomicFile.WriteJsonAsync(SafePath.Join(workspace.Paths.Cache, "metadata/catalog.json"),
            new VersionCatalog(DateTimeOffset.UtcNow, LatestRelease: entry.Id, Versions: [entry]));

    private static ProcessStartInfo FixtureServer(string folder)
    {
        var executable = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        const string script = """
            [Console]::WriteLine('fixture online')
            while ($null -ne ($command = [Console]::ReadLine())) {
                if ($command -ceq 'stop') {
                    [Console]::WriteLine('fixture saved world')
                    exit 0
                }
                [Console]::WriteLine('command ' + $command)
            }
            exit 1
            """;
        var info = new ProcessStartInfo(executable) { WorkingDirectory = folder };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            info.ArgumentList.Add(arg);
        return info;
    }
}
