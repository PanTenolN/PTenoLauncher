using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Authentication;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;
namespace PTenoLauncher.Tests;
public sealed class FabricInstallerTests
{
    private sealed class Fixture : IDisposable
    {
        public NetworkPolicy Network { get; } = new();
        public StubHandler Handler { get; }
        public FabricInstaller Installer { get; }
        private readonly HttpClient http;
        public string LibraryName { get; set; } = "net.fabricmc:example:1.0";
        public string Origin { get; set; } = "https://maven.fabricmc.net/";
        public string Inherits { get; set; } = "1.21.1";
        public bool Tamper { get; set; }
        public int MinimumJava { get; set; } = 8;
        public int LibraryRequests { get; private set; }
        public Fixture(TestWorkspace ws)
        {
            static byte[] Jar(string entry)
            {
                using var memory = new MemoryStream();
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                { using var output = zip.CreateEntry(entry).Open(); output.Write(new byte[] {0xCA,0xFE,0xBA,0xBE}); }
                return memory.ToArray();
            }
            var first = Jar("synthetic.class"); var second = Jar("net/fabricmc/loader/impl/launch/knot/KnotClient.class");
            HttpResponseMessage Json(object v) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(v)) };
            Handler = new StubHandler(r => {
                var uri = r.RequestUri!;
                if (uri.Host == "meta.fabricmc.net")
                {
                    if (!uri.AbsolutePath.EndsWith("/profile/json"))
                        return Json(new[] { new { loader = new { version = "0.20.1", stable = true }, launcherMeta = new { min_java_version = MinimumJava } } });
                    return Json(new { inheritsFrom = Inherits, mainClass = "net.fabricmc.loader.impl.launch.knot.KnotClient",
                        arguments = new { jvm = new[] { "-Dfabric.test=true" }, game = Array.Empty<string>() },
                        libraries = new object[] {
                            new {name=LibraryName,url=Origin,sha512=Convert.ToHexString(SHA512.HashData(first)),size=first.Length},
                            new {name="net.fabricmc:fabric-loader:0.20.1",url="https://maven.fabricmc.net/"}
                        } });
                }
                if (uri.AbsolutePath.EndsWith(".sha512"))
                    return new(HttpStatusCode.OK) { Content = new StringContent(Convert.ToHexString(SHA512.HashData(second))) };
                LibraryRequests++;
                var bytes = uri.AbsolutePath.Contains("/example/") ? first.ToArray() : second.ToArray();
                if (Tamper) bytes[^1] ^= 1;
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            });
            http = new(Handler); var downloader = new VerifiedDownloader(http, Network);
            Installer = new(ws.Paths, downloader, Network, new(ws.Paths, new(ws.Paths, downloader), downloader, ws.Store), ws.Store);
        }
        public void Dispose() => http.Dispose();
    }
    private static async Task<InstallationReceipt> Basis(TestWorkspace ws, GameProfile profile)
    {
        await ws.Store.InitializeAsync(); await ws.Store.SaveProfileAsync(profile);
        var data = Encoding.UTF8.GetBytes("synthetic base"); var index = "{}"u8.ToArray();
        var game = new InstalledFile("objects/sha1/game.jar", Convert.ToHexString(SHA1.HashData(data)), "SHA1", data.Length);
        var assets = new InstalledFile("assets/indexes/test.json", Convert.ToHexString(SHA1.HashData(index)), "SHA1", index.Length);
        await AtomicFile.WriteAsync(SafePath.Join(ws.Paths.Cache, game.RelativePath), data);
        await AtomicFile.WriteAsync(SafePath.Join(ws.Paths.Cache, assets.RelativePath), index);
        var receipt = new InstallationReceipt(profile.MinecraftVersion, 21, "net.minecraft.client.main.Main", DateTimeOffset.UtcNow,
            [game,assets], [game.RelativePath], [], ["-cp", "$"+"{classpath}"], ["--username","$"+"{auth_player_name}"], "test");
        await AtomicFile.WriteJsonAsync(ws.Paths.Receipt(profile.Id), receipt);
        return receipt;
    }
    [Fact] public async Task ComposesOfficialProfileAndReusesVerifiedLibrariesOffline()
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(ModLoader.Fabric); var basis = await Basis(ws,p);
        using var fixture = new Fixture(ws);
        var installed = await fixture.Installer.PrepareAsync(p,basis);
        Assert.Equal(ModLoader.Fabric,installed.Loader); Assert.Equal("0.20.1",installed.LoaderVersion);
        Assert.Equal("net.fabricmc.loader.impl.launch.knot.KnotClient",installed.MainClass);
        Assert.Equal(basis.GameArguments,installed.GameArguments); Assert.Contains("-Dfabric.test=true",installed.JvmArguments);
        Assert.Equal(4,installed.Files.Count); ReceiptValidator.Validate(ws.Paths,installed);
        var requests = fixture.Handler.Requests; fixture.Network.Offline = true;
        var second = await fixture.Installer.PrepareAsync(p,basis,"0.20.1");
        Assert.Equal(installed.LoaderVersion,second.LoaderVersion); Assert.Equal(requests,fixture.Handler.Requests);
        using var http = new HttpClient(new StubHandler(_=>throw new Exception("No auth network")));
        var auth = new MicrosoftAuthentication(new(ws.Paths),http,new(){Offline=true});
        var report = await new OfflineReadiness(ws.Paths,new(),auth).CheckAsync(p);
        Assert.Contains(report.Issues,i=>i.Code=="account.missing"); Assert.False(report.Ready);
        Assert.DoesNotContain(report.Issues,i=>i.Code=="loader.unsupported" || i.Code=="loader.mismatch");
        var vanilla = await new OfflineReadiness(ws.Paths,new(),auth).CheckAsync(p with {Loader=ModLoader.Vanilla});
        Assert.Contains(vanilla.Issues,i=>i.Code=="loader.mismatch");
    }
    [Theory]
    [InlineData("net.fabricmc:../escape:1.0","https://maven.fabricmc.net/")]
    [InlineData("net.fabricmc:example:1.0","https://example.org/")]
    [InlineData("net.fabricmc:example:1.0","http://maven.fabricmc.net/")]
    public async Task RejectsUnsafeCoordinatesAndSourcesBeforeCommitting(string coordinate,string origin)
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(ModLoader.Fabric); var basis = await Basis(ws,p);
        var before = await File.ReadAllBytesAsync(ws.Paths.Receipt(p.Id));
        using var fixture = new Fixture(ws){LibraryName=coordinate,Origin=origin};
        await Assert.ThrowsAsync<InvalidDataException>(()=>fixture.Installer.PrepareAsync(p,basis));
        Assert.Equal(before,await File.ReadAllBytesAsync(ws.Paths.Receipt(p.Id)));
    }
    [Fact] public async Task WrongInheritancePreservesPriorReceipt()
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(ModLoader.Fabric); var basis = await Basis(ws,p);
        var before = await File.ReadAllBytesAsync(ws.Paths.Receipt(p.Id));
        using(var fixture = new Fixture(ws){Inherits="1.20.1"})
            await Assert.ThrowsAsync<InvalidDataException>(()=>fixture.Installer.PrepareAsync(p,basis));

        Assert.Equal(before,await File.ReadAllBytesAsync(ws.Paths.Receipt(p.Id)));
    }
    [Fact] public async Task UncachedOfflineAndUnknownPinnedVersionAreBlocked()
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(ModLoader.Fabric); var basis = await Basis(ws,p);
        using var fixture = new Fixture(ws); fixture.Network.Offline = true;
        await Assert.ThrowsAsync<InvalidDataException>(()=>fixture.Installer.PrepareAsync(p,basis));
        Assert.Equal(0,fixture.Handler.Requests); fixture.Network.Offline = false;
        await Assert.ThrowsAsync<InvalidDataException>(()=>fixture.Installer.PrepareAsync(p,basis,"99.0.0"));
    }
    [Fact] public async Task ActiveProfileLeasePreventsFabricPreparationBeforeAnyRequest()
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(ModLoader.Fabric); var basis = await Basis(ws,p);
        using var fixture = new Fixture(ws); using var active = new ProfileUsage(ws.Paths).Acquire(p.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Installer.PrepareAsync(p,basis));
        Assert.Equal(0,fixture.Handler.Requests);
    }
    [Theory]
    [InlineData(8,21)]
    [InlineData(25,25)]
    public async Task JavaRequirementNeverDropsBelowMinecraftOrLoader(int minimum,int expected)
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(ModLoader.Fabric); var basis = await Basis(ws,p);
        using var fixture = new Fixture(ws){MinimumJava=minimum};
        Assert.Equal(expected,(await fixture.Installer.PrepareAsync(p,basis)).JavaMajor);
    }
    [Fact] public async Task OldVanillaReceiptRemainsBackwardCompatible()
    {
        using var ws = new TestWorkspace();var p = ws.Profile();var basis = await Basis(ws,p);
        var json = JsonSerializer.SerializeToNode(basis,AtomicFile.JsonOptions)!.AsObject();
        json.Remove("loader");json.Remove("loaderVersion");
        var legacy = JsonSerializer.Deserialize<InstallationReceipt>(json.ToJsonString(),AtomicFile.JsonOptions)!;
        Assert.Equal(ModLoader.Vanilla,legacy.Loader);Assert.Null(legacy.LoaderVersion);ReceiptValidator.Validate(ws.Paths,legacy);
    }
    [Fact] public async Task CorruptLibraryDownloadCannotCommitProfileMetadata()
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(ModLoader.Fabric); var basis = await Basis(ws,p);
        var before = await File.ReadAllBytesAsync(ws.Paths.Receipt(p.Id));
        using var fixture = new Fixture(ws){Tamper=true};
        await Assert.ThrowsAsync<InvalidDataException>(()=>fixture.Installer.PrepareAsync(p,basis));
        Assert.Equal(1,fixture.LibraryRequests);
        Assert.Equal(before,await File.ReadAllBytesAsync(ws.Paths.Receipt(p.Id)));
        Assert.Empty(Directory.EnumerateFiles(ws.Paths.Cache,"*.part",SearchOption.AllDirectories));
    }
}
