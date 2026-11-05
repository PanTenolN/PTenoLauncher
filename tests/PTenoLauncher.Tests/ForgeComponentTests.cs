using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Authentication;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class ForgeComponentTests
{
    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
    private sealed class Sink(Action<DownloadProgress> action) : IProgress<DownloadProgress> { public void Report(DownloadProgress value) => action(value); }
    internal sealed class Fixture : IDisposable
    {
        public NetworkPolicy Network { get; } = new();
        public StubHandler Handler { get; }
        public HttpClient Http { get; }
        public ForgeCatalog Catalog { get; }
        public ForgeComponentPreparer Preparer { get; }
        public string Selected { get; }
        public ModLoader Loader { get; }
        public bool BadInstaller { get; set; }
        public bool BadRemoteLibrary { get; set; }
        public bool Sha1Only { get; set; }
        public bool BadSidecar { get; set; }
        public bool CatalogUnavailable { get; set; }
        public byte[] Jar { get; }
        public byte[] Xml { get; }
        public byte[] RemoteBytes { get; } = "synthetic remote library - not executable"u8.ToArray();
        public string Fml { get; }
        public Fixture(TestWorkspace ws, ModLoader loader = ModLoader.Forge, Action<JsonObject, JsonObject>? mutate = null, Action<ZipArchive>? extra = null, byte[]? processorJar = null, byte[]? runtimeJar = null)
        {
            Loader = loader; Selected = loader == ModLoader.Forge ? "1.21.1-52.0.1" : "21.1.1";
            var group = loader == ModLoader.Forge ? "net.minecraftforge" : "net.neoforged";
            var artifact = loader == ModLoader.Forge ? "forge" : "neoforge";
            var origin = loader == ModLoader.Forge ? "https://maven.minecraftforge.net/" : "https://maven.neoforged.net/releases/";
            var coordinate = group + ":" + artifact + ":" + Selected + ":universal";
            Fml = loader == ModLoader.Forge ? "net.minecraftforge:fmlloader:1.21.1-52.0.1" : "net.neoforged.fancymodloader:loader:4.0.42";
            var processor = "net.minecraftforge:installertools:2.1.2";
            var embedded = new Dictionary<string, byte[]> {
                [coordinate] = runtimeJar ?? "synthetic loader, not executed"u8.ToArray(),
                [Fml] = runtimeJar ?? "synthetic FML, not executed"u8.ToArray(),
                [processor] = processorJar ?? "synthetic processor, never executed"u8.ToArray()
            };
            object Library(string name, bool local)
            {
                var bytes = local ? embedded[name] : RemoteBytes; var path = ForgeCoordinate.Parse(name).Path;
                return new { name, downloads = new { artifact = new { path, sha1 = Sha(bytes), size = bytes.Length, url = local ? "" : origin + path } } };
            }
            var install = JsonSerializer.SerializeToNode(new {
                spec = 1, minecraft = "1.21.1", version = "test-loader-" + Selected, json = "/version.json",
                libraries = new object[] { Library(processor, true), Library("org.example:remote:1.0", false) },
                data = new { BINPATCH = new { client = "/data/client.lzma", server = "/data/server.lzma" }, PATCHED = new { client = "[net.minecraft:client:1.21.1:patched]", server = "[net.minecraft:server:1.21.1:patched]" } },
                processors = new object[] {
                    new { sides = new[] { "client" }, jar = processor, classpath = Array.Empty<string>(), args = new[] { "--input", "{MINECRAFT_JAR}", "--patch", "{BINPATCH}" }, outputs = new Dictionary<string,string> { ["{PATCHED}"] = "'" + Sha("expected synthetic output"u8.ToArray()) + "'" } },
                    new { sides = new[] { "server" }, jar = "not.a.client:ignored:1", args = Array.Empty<string>() }
                }
            })!.AsObject();
            var version = JsonSerializer.SerializeToNode(new {
                id = "test-loader-" + Selected, inheritsFrom = "1.21.1", mainClass = "cpw.mods.bootstraplauncher.BootstrapLauncher",
                libraries = new object[] { Library(coordinate, true), Library(Fml, true), Library("org.example:remote:1.0", false),
                    new { name = "net.minecraft:client:1.21.1:patched", downloads = new { artifact = new { path = "net/minecraft/client/1.21.1/client-1.21.1-patched.jar", sha1 = "", size = 0, url = "" } } } },
                arguments = new { jvm = new[] { "--module-path", "${library_directory}/example" }, game = new[] { "--launchTarget", "forgeclient" } }
            })!.AsObject();
            mutate?.Invoke(install, version);
            using (var memory = new MemoryStream())
            {
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                {
                    void Add(string name, byte[] bytes) { using var stream = zip.CreateEntry(name).Open(); stream.Write(bytes); }
                    Add("install_profile.json", Encoding.UTF8.GetBytes(install.ToJsonString())); Add("version.json", Encoding.UTF8.GetBytes(version.ToJsonString()));
                    foreach (var file in embedded) Add("maven/" + ForgeCoordinate.Parse(file.Key).Path, file.Value);
                    Add("data/client.lzma", "synthetic patches"u8.ToArray()); extra?.Invoke(zip);
                }
                Jar = memory.ToArray();
            }
            Xml = Encoding.UTF8.GetBytes($"<metadata><groupId>{group}</groupId><artifactId>{artifact}</artifactId><versioning><versions><version>{Selected}</version></versions></versioning></metadata>");
            Handler = new StubHandler(r => {
                var url = r.RequestUri!.AbsolutePath;
                if (url.EndsWith("maven-metadata.xml", StringComparison.Ordinal))
                { if (CatalogUnavailable) throw new HttpRequestException("Synthetic catalog connectivity failure"); return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Xml) }; }
                if (url.EndsWith(".sha512", StringComparison.Ordinal) || url.EndsWith(".sha256", StringComparison.Ordinal))
                {
                    if (Sha1Only) return new(HttpStatusCode.NotFound);
                    var hash = url.EndsWith(".sha512", StringComparison.Ordinal) ? Convert.ToHexString(SHA512.HashData(Jar)) : Convert.ToHexString(SHA256.HashData(Jar));
                    return new(HttpStatusCode.OK) { Content = new StringContent(BadSidecar ? "invalid hash" : hash) };
                }
                if (url.EndsWith(".sha1", StringComparison.Ordinal)) return new(HttpStatusCode.OK) { Content = new StringContent(Sha(Jar)) };
                if (url.EndsWith("-installer.jar", StringComparison.Ordinal)) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(BadInstaller ? new byte[Jar.Length] : Jar) };
                if (url.EndsWith("remote-1.0.jar", StringComparison.Ordinal)) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(BadRemoteLibrary ? new byte[RemoteBytes.Length] : RemoteBytes) };
                throw new InvalidOperationException("Unexpected fixture request: " + url);
            });
            Http = new(Handler); var downloads = new VerifiedDownloader(Http, Network);
            Catalog = new(ws.Paths, downloads, Network); Preparer = new(ws.Paths, Catalog, downloads, ws.Store);
        }
        public async Task<GameProfile> Profile(TestWorkspace ws)
        { await ws.Store.InitializeAsync(); var p = ws.Profile(Loader); await ws.Store.SaveProfileAsync(p); return p; }
        public void Dispose() => Http.Dispose();
    }
    [Theory] [InlineData(ModLoader.Forge)] [InlineData(ModLoader.NeoForge)]
    public async Task PreparesVerifiedInputsWithExactPinAndIndependentFmlWithoutClaimingGameInstallation(ModLoader loader)
    {
        using var ws = new TestWorkspace(); using var f = new Fixture(ws, loader); var p = await f.Profile(ws);
        var sentinel = SafePath.Join(ws.Paths.Game(p.Id), "saves/synthetic.txt"); await File.WriteAllTextAsync(sentinel, "NOT A REAL WORLD");
        var result = await f.Preparer.PrepareAsync(p, f.Selected);
        Assert.Equal(f.Selected, result.Plan.LoaderVersion); Assert.Equal("1.21.1", result.Plan.MinecraftVersion); Assert.Equal(f.Fml, result.Plan.FmlCoordinate);
        Assert.False(result.Plan.GameInstallationComplete); Assert.Single(result.Plan.ClientProcessors); Assert.Single(result.Plan.Libraries, l => l.Generated);
        Assert.Equal(5, result.Files.Count); Assert.False(File.Exists(ws.Paths.Receipt(p.Id)));
        Assert.True(File.Exists(SafePath.Join(ws.Paths.Profile(p.Id), "loader-components.json")));
        foreach (var file in result.Files) Assert.True(await VerifiedDownloader.VerifyAsync(SafePath.Join(ws.Paths.Cache, file.RelativePath), file.Hash, file.Algorithm, file.Size));
        var requests = f.Handler.Requests; f.Network.Offline = true;
        var repeated = await f.Preparer.PrepareAsync(p, f.Selected);
        Assert.Equal(f.Selected, repeated.Plan.LoaderVersion); Assert.Equal(requests, f.Handler.Requests); Assert.Equal("NOT A REAL WORLD", await File.ReadAllTextAsync(sentinel));
        var auth = new MicrosoftAuthentication(new(ws.Paths), f.Http, f.Network);
        var readiness = await new OfflineReadiness(ws.Paths, new(), auth).CheckAsync(p);
        Assert.False(readiness.Ready); Assert.Contains(readiness.Issues, i => i.Code == "loader.unsupported"); Assert.Contains(readiness.Issues, i => i.Code == "account.missing"); Assert.Equal(requests, f.Handler.Requests);
    }
    [Theory] [InlineData(ModLoader.Forge)] [InlineData(ModLoader.NeoForge)]
    public async Task CatalogIsCachedAndDoesNotInferNeoForgeMinecraftOrFml(ModLoader loader)
    {
        using var ws = new TestWorkspace(); using var f = new Fixture(ws, loader);
        var versions = await f.Catalog.GetVersionsAsync(loader, "1.21.1"); var item = Assert.Single(versions);
        Assert.Equal(loader == ModLoader.Forge ? "1.21.1" : null, item.MinecraftVersion);
        var requests = f.Handler.Requests; f.Network.Offline = true;
        Assert.Single(await f.Catalog.GetVersionsAsync(loader, "1.21.1")); Assert.True(f.Catalog.UsedCachedMetadata); Assert.Equal(requests, f.Handler.Requests);
    }
    [Theory]
    [InlineData("<!DOCTYPE metadata [<!ENTITY e SYSTEM 'file:///C:/Windows/win.ini'>]><metadata>&e;</metadata>")]
    [InlineData("<metadata><groupId>evil</groupId><artifactId>forge</artifactId><versioning><versions><version>1.21.1-52.0.1</version></versions></versioning></metadata>")]
    [InlineData("<metadata><groupId>net.minecraftforge</groupId><artifactId>forge</artifactId><versioning><versions><version>../escape</version></versions></versioning></metadata>")]
    [InlineData("<metadata><groupId>net.minecraftforge</groupId><artifactId>forge</artifactId><versioning><versions><version>1.21.1-52.0.1</version><version>1.21.1-52.0.1</version></versions></versioning></metadata>")]
    public void UnsafeXmlAndIdentityAreRejected(string xml) => Assert.Throws<InvalidDataException>(() => ForgeCatalog.Parse(ModLoader.Forge, Encoding.UTF8.GetBytes(xml)));
    [Theory] [InlineData("x:y:../escape")] [InlineData("a..b:c:1")] [InlineData("a:b:1@../jar")] [InlineData("a:b:1:../../escape")]
    public void MavenCoordinatesCannotEscapeTheirArtifactPath(string value) => Assert.Throws<InvalidDataException>(() => ForgeCoordinate.Parse(value));
    [Fact] public async Task NetworkFailureUsesVerifiedCatalogCacheButDoesNotIgnoreCancellation()
    {
        using var ws = new TestWorkspace(); using var f = new Fixture(ws); await f.Catalog.GetVersionsAsync(ModLoader.Forge);
        f.CatalogUnavailable = true;
        Assert.Single(await f.Catalog.GetVersionsAsync(ModLoader.Forge, refresh: true)); Assert.True(f.Catalog.UsedCachedMetadata);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Catalog.GetVersionsAsync(ModLoader.Forge, refresh: true, ct: stop.Token));
    }
    [Theory] [InlineData("spec")][InlineData("version")]
    public async Task MissingOrLegacyInstallerFieldsDoNotProduceACompletedPointer(string field)
    {
        using var ws = new TestWorkspace(); using var f = new Fixture(ws, mutate: (i, _) => i.Remove(field)); var p = await f.Profile(ws);
        if (field == "spec") await Assert.ThrowsAsync<NotSupportedException>(() => f.Preparer.PrepareAsync(p, f.Selected));
        else await Assert.ThrowsAsync<InvalidDataException>(() => f.Preparer.PrepareAsync(p, f.Selected));
        Assert.False(File.Exists(SafePath.Join(ws.Paths.Profile(p.Id), "loader-components.json")));
    }
    [Fact] public async Task ReservedDataVariablesAndMismatchedMavenPathsAreRejected()
    {
        using var ws = new TestWorkspace(); using var f = new Fixture(ws, mutate: (i, _) => i["data"]!["ROOT"] = new JsonObject { ["client"] = "'/outside'", ["server"] = "'/outside'" }); var p = await f.Profile(ws);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Preparer.PrepareAsync(p, f.Selected));
        using var other = new TestWorkspace(); using var g = new Fixture(other, mutate: (i, _) => i["libraries"]![0]!["downloads"]!["artifact"]!["path"] = "../elsewhere.jar"); var q = await g.Profile(other);
        await Assert.ThrowsAsync<InvalidDataException>(() => g.Preparer.PrepareAsync(q, g.Selected));
    }
    [Fact] public async Task ColdOfflineAndUnknownVersionNeverSendHttpOrChangeProfile()
    {
        using var ws = new TestWorkspace(); using var f = new Fixture(ws); var p = await f.Profile(ws); f.Network.Offline = true;
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Preparer.PrepareAsync(p, f.Selected)); Assert.Equal(0, f.Handler.Requests);
        f.Network.Offline = false; await f.Catalog.GetVersionsAsync(ModLoader.Forge); var requests = f.Handler.Requests;
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Preparer.PrepareAsync(p, "1.21.1-99.99.99")); Assert.Equal(requests, f.Handler.Requests);
        Assert.False(File.Exists(SafePath.Join(ws.Paths.Profile(p.Id), "loader-components.json")));
    }
    [Theory] [InlineData(true, false)] [InlineData(false, true)]
    public async Task TamperedInstallerOrSidecarCannotBecomeAPreparationReceipt(bool installer, bool sidecar)
    {
        using var ws = new TestWorkspace(); using var f = new Fixture(ws) { BadInstaller = installer, BadSidecar = sidecar }; var p = await f.Profile(ws);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Preparer.PrepareAsync(p, f.Selected));
        Assert.False(File.Exists(SafePath.Join(ws.Paths.Profile(p.Id), "loader-components.json"))); Assert.False(File.Exists(ws.Paths.Receipt(p.Id)));
        Assert.Equal(installer ? 3 : 2, f.Handler.Requests);
    }
    [Fact] public async Task SupportedMissingStrongSidecarsFallBackToOfficialSha1OnlyOn404()
    {
        using var ws = new TestWorkspace(); using var f = new Fixture(ws) { Sha1Only = true }; var p = await f.Profile(ws);
        var result = await f.Preparer.PrepareAsync(p, f.Selected); Assert.Equal("SHA1", result.Installer.Algorithm);
    }
    [Fact] public async Task WrongMinecraftBindingAndSelectedLoaderIdentityAreRejected()
    {
        using var ws = new TestWorkspace(); using var f = new Fixture(ws, ModLoader.NeoForge); var p = await f.Profile(ws);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Preparer.PrepareAsync(p with { MinecraftVersion = "1.21.4" }, f.Selected));
        Assert.False(File.Exists(SafePath.Join(ws.Paths.Profile(p.Id), "loader-components.json")));
        // Use a separate workspace to avoid legitimately reusing the first installer's verified cache.
        using var fresh = new TestWorkspace(); using var wrong = new Fixture(fresh, ModLoader.NeoForge, (_, v) => v["inheritsFrom"] = "1.21.4"); var q = await wrong.Profile(fresh);
        await Assert.ThrowsAsync<InvalidDataException>(() => wrong.Preparer.PrepareAsync(q, wrong.Selected));
    }
    [Theory] [InlineData("https://example.org/org/example/remote/1.0/remote-1.0.jar")]
    [InlineData("http://maven.minecraftforge.net/org/example/remote/1.0/remote-1.0.jar")]
    [InlineData("https://maven.minecraftforge.net/wrong.jar")]
    [InlineData("https://maven.minecraftforge.net/org/example/remote/1.0/remote-1.0.jar?secret=x")]
    [InlineData("https://maven.minecraftforge.net:444/org/example/remote/1.0/remote-1.0.jar")]
    [InlineData("https://user:password@maven.minecraftforge.net/org/example/remote/1.0/remote-1.0.jar")]
    public async Task UnapprovedOrMismatchedArtifactOriginsAreRejectedBeforeLibraryDownload(string origin)
    {
        using var ws = new TestWorkspace(); using var f = new Fixture(ws, mutate: (i, _) => i["libraries"]![1]!["downloads"]!["artifact"]!["url"] = origin); var p = await f.Profile(ws);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Preparer.PrepareAsync(p, f.Selected));
        Assert.False(File.Exists(SafePath.Join(ws.Paths.Profile(p.Id), "loader-components.json")));
    }
    [Fact] public async Task ZipSlipAndSymlinkEntriesAreRejectedWithoutExtraction()
    {
        using var ws = new TestWorkspace(); using var f = new Fixture(ws, extra: z => z.CreateEntry("../escape")); var p = await f.Profile(ws);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Preparer.PrepareAsync(p, f.Selected));
        Assert.False(File.Exists(SafePath.Join(ws.Paths.Cache, "escape")));
        using var second = new TestWorkspace(); using var g = new Fixture(second, extra: z => z.CreateEntry("link").ExternalAttributes = unchecked((int)0xA1FF0000)); var q = await g.Profile(second);
        await Assert.ThrowsAsync<InvalidDataException>(() => g.Preparer.PrepareAsync(q, g.Selected));
    }
    [Theory] [InlineData("../../../output")][InlineData("C:/outside")]
    public async Task EmbeddedDataCannotReferenceOutsideTheInstaller(string path)
    {
        using var ws = new TestWorkspace(); using var f = new Fixture(ws, mutate: (i, _) => i["data"]!["BINPATCH"]!["client"] = path); var p = await f.Profile(ws);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Preparer.PrepareAsync(p, f.Selected));
    }
    [Fact] public async Task MissingFmlStaysUnknownAndDoesNotInheritTheNeoForgeVersion()
    {
        using var ws = new TestWorkspace(); using var f = new Fixture(ws, ModLoader.NeoForge, (_, v) => v["libraries"]!.AsArray().RemoveAt(1)); var p = await f.Profile(ws);
        var result = await f.Preparer.PrepareAsync(p, f.Selected); Assert.Null(result.Plan.FmlCoordinate); Assert.False(result.Plan.GameInstallationComplete);
    }
    [Fact] public async Task CorruptEmbeddedInputAndBadRemoteLibraryDoNotCommit()
    {
        using var ws = new TestWorkspace(); using var f = new Fixture(ws) { BadRemoteLibrary = true }; var p = await f.Profile(ws);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Preparer.PrepareAsync(p, f.Selected));
        Assert.False(File.Exists(SafePath.Join(ws.Paths.Profile(p.Id), "loader-components.json")));
        f.BadRemoteLibrary = false; await f.Preparer.PrepareAsync(p, f.Selected);
    }
    [Fact] public async Task EmbeddedChecksumMismatchAndUndeclaredProcessorAreRejected()
    {
        using var ws = new TestWorkspace(); using var f = new Fixture(ws, mutate: (i, _) => i["libraries"]![0]!["downloads"]!["artifact"]!["sha1"] = new string('a', 40)); var p = await f.Profile(ws);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Preparer.PrepareAsync(p, f.Selected));
        Assert.False(File.Exists(SafePath.Join(ws.Paths.Profile(p.Id), "loader-components.json")));
        using var other = new TestWorkspace(); using var g = new Fixture(other, mutate: (i, _) => i["processors"]![0]!["jar"] = "net.example:undeclared:1.0"); var q = await g.Profile(other);
        await Assert.ThrowsAsync<InvalidDataException>(() => g.Preparer.PrepareAsync(q, g.Selected));
    }
    [Fact] public async Task OfflineEmbeddedRepairCancellationPreservesPreviousPointerAndGameReceipt()
    {
        using var ws = new TestWorkspace(); using var f = new Fixture(ws); var p = await f.Profile(ws); var result = await f.Preparer.PrepareAsync(p, f.Selected);
        var marker = "previous real installation receipt sentinel, not a game"u8.ToArray(); await File.WriteAllBytesAsync(ws.Paths.Receipt(p.Id), marker);
        var pointer = SafePath.Join(ws.Paths.Profile(p.Id), "loader-components.json"); var before = await File.ReadAllBytesAsync(pointer);
        var embedded = result.Files.Single(x => x.RelativePath.EndsWith("installertools-2.1.2.jar", StringComparison.Ordinal));
        await File.WriteAllTextAsync(SafePath.Join(ws.Paths.Cache, embedded.RelativePath), "corrupt owned cache input"); f.Network.Offline = true; var requests = f.Handler.Requests;
        using var stop = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Preparer.PrepareAsync(p, f.Selected, new Sink(_ => stop.Cancel()), stop.Token));
        Assert.Equal(before, await File.ReadAllBytesAsync(pointer)); Assert.Equal(marker, await File.ReadAllBytesAsync(ws.Paths.Receipt(p.Id)));
        await f.Preparer.PrepareAsync(p, f.Selected); Assert.Equal(requests, f.Handler.Requests);
    }
    [Fact] public async Task ActiveProfileLeaseBlocksPreparationBeforeAnyNetworkOrFileCommit()
    {
        using var ws = new TestWorkspace(); using var f = new Fixture(ws); var p = await f.Profile(ws); using var lease = new ProfileUsage(ws.Paths).Acquire(p.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Preparer.PrepareAsync(p, f.Selected)); Assert.Equal(0, f.Handler.Requests);
    }
    [Fact] public async Task AlreadyCancelledPreparationDoesNotTouchTheProfileOrSendHttp()
    {
        using var ws = new TestWorkspace(); using var f = new Fixture(ws); var p = ws.Profile(ModLoader.Forge);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Preparer.PrepareAsync(p, f.Selected, ct: stop.Token));
        Assert.Equal(0, f.Handler.Requests); Assert.False(Directory.Exists(ws.Paths.Profile(p.Id)));
    }
    [AvaloniaFact] public async Task ActualForgePanelPreparesCachedComponentsOfflineAndResetsForVanilla()
    {
        using var ws = new TestWorkspace(); using var f = new Fixture(ws, ModLoader.NeoForge); var p = await f.Profile(ws); await f.Preparer.PrepareAsync(p, f.Selected);
        var vanilla = p with { Id = Guid.NewGuid(), Name = "Synthetic Vanilla profile", Loader = ModLoader.Vanilla }; await ws.Store.SaveProfileAsync(vanilla);
        using var services = new LauncherServices(ws.Paths); var vm = new MainViewModel(services); await vm.InitializeAsync(); vm.Offline = true;
        TextCatalog.Apply("uk"); vm.SelectedProfile = vm.Profiles.Single(x => x.Id == p.Id); var window = new MainWindow { DataContext = vm }; window.Show();
        try
        {
            Assert.True(window.FindControl<Border>("ForgeLoaderPanel")!.IsVisible); Assert.False(window.FindControl<Button>("InstallGameButton")!.IsEnabled);
            await vm.LoadForgeVersionsAsync(); Assert.Single(vm.ForgeVersions); Assert.Equal(f.Selected, vm.SelectedForgeVersion!.Version);
            await vm.PrepareForgeComponentsAsync(); Assert.Contains(f.Fml, vm.ForgeStatus); Assert.Contains("ще не завершено", vm.ForgeStatus);
            vm.SelectedLanguage = TextCatalog.Languages.Single(l => l.Code == "en"); Assert.Contains("not complete", vm.ForgeStatus);
            vm.SelectedProfile = vm.Profiles.Single(x => x.Id == vanilla.Id); Assert.Empty(vm.ForgeVersions); Assert.False(vm.IsForgeProfile);
            Assert.True(window.FindControl<Button>("InstallGameButton")!.IsEnabled); Assert.False(window.FindControl<Border>("ForgeLoaderPanel")!.IsVisible);
        }
        finally { window.Close(); TextCatalog.Apply("uk"); }
    }
}
