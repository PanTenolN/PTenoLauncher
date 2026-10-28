using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Authentication;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;
namespace PTenoLauncher.Tests;
public sealed class QuiltInstallerTests
{
    private sealed class Fixture:IDisposable
    {
        public readonly NetworkPolicy Network=new();public readonly StubHandler Handler;public readonly QuiltInstaller Installer;private readonly HttpClient http;
        public string Origin="https://maven.quiltmc.org/repository/release/";public string? BadCoordinate;public string Inherits="1.21.1";
        public bool Tamper;public bool OnlyPrerelease;public bool MissingLoader;public bool Sha512Missing;
        public Fixture(TestWorkspace ws,byte bytecode=52,bool missingMain=false)
        {
            static byte[] Jar(string name,byte major)
            {
                using var memory=new MemoryStream();using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true))
                using(var output=zip.CreateEntry(name).Open())output.Write(new byte[]{0xCA,0xFE,0xBA,0xBE,0,0,0,major});return memory.ToArray();
            }
            var loader=Jar(missingMain?"other.class":"org/quiltmc/loader/impl/launch/knot/KnotClient.class",bytecode);var intermediary=Jar("example.class",52);
            static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value))};
            Handler=new StubHandler(r=>{
                var uri=r.RequestUri!;
                if(uri.Host=="meta.quiltmc.org")
                {
                    if(!uri.AbsolutePath.EndsWith("/profile/json"))return Json((OnlyPrerelease?new[]{"0.31.0-beta.1"}:new[]{"0.9.9","0.31.0-beta.1","0.30.1"}).Select(v=>new{loader=new{version=v},launcherMeta=new{version=1}}));
                    var version=Uri.UnescapeDataString(uri.Segments[5].Trim('/'));
                    var libs=new List<object>{new{name="net.fabricmc:intermediary:1.21.1",url="https://maven.fabricmc.net/",size=intermediary.Length,sha256=Convert.ToHexString(SHA256.HashData(intermediary))}};
                    if(!MissingLoader)libs.Add(new{name=BadCoordinate??"org.quiltmc:quilt-loader:"+version,url=Origin});
                    return Json(new{inheritsFrom=Inherits,mainClass="org.quiltmc.loader.impl.launch.knot.KnotClient",libraries=libs,
                        arguments=new{jvm=new[]{"-Dquilt.test=true"},game=Array.Empty<string>()}});
                }
                if(uri.AbsolutePath.EndsWith(".sha512"))return Sha512Missing?new(HttpStatusCode.NotFound):new(HttpStatusCode.OK){Content=new StringContent(Convert.ToHexString(SHA512.HashData(loader)))};
                if(uri.AbsolutePath.EndsWith(".sha256"))return new(HttpStatusCode.OK){Content=new StringContent(Convert.ToHexString(SHA256.HashData(loader)))};
                var body=uri.AbsolutePath.Contains("quilt-loader")?loader.ToArray():intermediary.ToArray();if(Tamper)body[^1]^=1;
                return new(HttpStatusCode.OK){Content=new ByteArrayContent(body)};
            });
            http=new(Handler);var downloader=new VerifiedDownloader(http,Network);
            Installer=new(ws.Paths,downloader,Network,new(ws.Paths,new(ws.Paths,downloader),downloader,ws.Store),ws.Store);
        }
        public void Dispose()=>http.Dispose();
    }
    private static async Task<InstallationReceipt> Basis(TestWorkspace ws,GameProfile p)
    {
        await ws.Store.InitializeAsync();await ws.Store.SaveProfileAsync(p);var bytes="synthetic base"u8.ToArray();var index="{}"u8.ToArray();
        var file=new InstalledFile("objects/sha1/game.jar",Convert.ToHexString(SHA1.HashData(bytes)),"SHA1",bytes.Length);
        var asset=new InstalledFile("assets/indexes/test.json",Convert.ToHexString(SHA1.HashData(index)),"SHA1",index.Length);
        await AtomicFile.WriteAsync(SafePath.Join(ws.Paths.Cache,file.RelativePath),bytes);await AtomicFile.WriteAsync(SafePath.Join(ws.Paths.Cache,asset.RelativePath),index);
        var receipt=new InstallationReceipt(p.MinecraftVersion,21,"net.minecraft.client.main.Main",DateTimeOffset.UtcNow,[file,asset],[file.RelativePath],[],["-cp","$"+"{classpath}"],["--username","$"+"{auth_player_name}"],"test");
        await AtomicFile.WriteJsonAsync(ws.Paths.Receipt(p.Id),receipt);return receipt;
    }
    [Fact] public async Task SortsUnorderedCatalogNumericallyAndDefaultsToRelease()
    {
        using var ws=new TestWorkspace();var p=ws.Profile(ModLoader.Quilt);var basis=await Basis(ws,p);using var f=new Fixture(ws);
        var versions=await f.Installer.GetVersionsAsync(p.MinecraftVersion);Assert.Equal(new[]{"0.30.1","0.9.9","0.31.0-beta.1"},versions.Select(v=>v.Version));
        var receipt=await f.Installer.PrepareAsync(p,basis);Assert.Equal("0.30.1",receipt.LoaderVersion);Assert.Equal(ModLoader.Quilt,receipt.Loader);
        Assert.Equal(basis.GameArguments,receipt.GameArguments);Assert.Contains("-Dquilt.test=true",receipt.JvmArguments);Assert.Equal(4,receipt.Files.Count);
        ReceiptValidator.Validate(ws.Paths,receipt);
    }
    [Fact] public async Task ReusesVerifiedLibrariesOfflineAndNeverBypassesAccountCheck()
    {
        using var ws=new TestWorkspace();var p=ws.Profile(ModLoader.Quilt);var basis=await Basis(ws,p);using var f=new Fixture(ws);
        var first=await f.Installer.PrepareAsync(p,basis);int count=f.Handler.Requests;f.Network.Offline=true;
        var repeated=await f.Installer.PrepareAsync(p,basis,"0.30.1");Assert.Equal(count,f.Handler.Requests);Assert.Equal(first.LoaderVersion,repeated.LoaderVersion);
        using var handler=new StubHandler(_=>throw new Exception("No auth network"));using var http=new HttpClient(handler);
        var auth=new MicrosoftAuthentication(new(ws.Paths),http,new(){Offline=true});
        var report=await new OfflineReadiness(ws.Paths,new(),auth).CheckAsync(p);Assert.False(report.Ready);
        Assert.Contains(report.Issues,i=>i.Code=="account.missing");Assert.DoesNotContain(report.Issues,i=>i.Code=="loader.unsupported"||i.Code=="loader.mismatch");Assert.Equal(0,handler.Requests);
        var vanilla=await new OfflineReadiness(ws.Paths,new(),auth).CheckAsync(p with{Loader=ModLoader.Vanilla});Assert.Contains(vanilla.Issues,i=>i.Code=="loader.mismatch");
    }
    [Fact] public async Task DerivesHigherJavaRequirementFromLibraryBytecode()
    {
        using var ws=new TestWorkspace();var p=ws.Profile(ModLoader.Quilt);var basis=await Basis(ws,p);using var f=new Fixture(ws,69);
        Assert.Equal(25,(await f.Installer.PrepareAsync(p,basis)).JavaMajor);
    }
    [Fact] public async Task ExplicitPrereleaseIsRequiredWhenNoReleaseExists()
    {
        using var ws=new TestWorkspace();var p=ws.Profile(ModLoader.Quilt);var basis=await Basis(ws,p);using var f=new Fixture(ws){OnlyPrerelease=true};
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Installer.PrepareAsync(p,basis));
        Assert.Equal("0.31.0-beta.1",(await f.Installer.PrepareAsync(p,basis,"0.31.0-beta.1")).LoaderVersion);
    }
    [Theory]
    [InlineData("https://example.org/",null)]
    [InlineData("https://maven.quiltmc.org/repository/snapshots/",null)]
    [InlineData("http://maven.quiltmc.org/repository/release/",null)]
    [InlineData("https://maven.quiltmc.org/repository/release/","org.quiltmc:../escape:0.30.1")]
    [InlineData("https://maven.quiltmc.org/repository/release/","org.quiltmc:quilt-loader:0.9.9")]
    public async Task RejectsUnapprovedSourcesUnsafeCoordinatesAndWrongLoaderPin(string origin,string? coordinate)
    {
        using var ws=new TestWorkspace();var p=ws.Profile(ModLoader.Quilt);var basis=await Basis(ws,p);using var f=new Fixture(ws){Origin=origin,BadCoordinate=coordinate};
        var before=await File.ReadAllBytesAsync(ws.Paths.Receipt(p.Id));
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Installer.PrepareAsync(p,basis));Assert.Equal(before,await File.ReadAllBytesAsync(ws.Paths.Receipt(p.Id)));
    }
    [Fact] public async Task BadDownloadAndWrongInheritanceNeverCommitReceipt()
    {
        using var ws=new TestWorkspace();var p=ws.Profile(ModLoader.Quilt);var basis=await Basis(ws,p);var before=await File.ReadAllBytesAsync(ws.Paths.Receipt(p.Id));
        using(var f=new Fixture(ws){Tamper=true})await Assert.ThrowsAsync<InvalidDataException>(()=>f.Installer.PrepareAsync(p,basis));
        Assert.Equal(before,await File.ReadAllBytesAsync(ws.Paths.Receipt(p.Id)));
        using(var f=new Fixture(ws){Inherits="1.22"})await Assert.ThrowsAsync<InvalidDataException>(()=>f.Installer.PrepareAsync(p,basis,"0.9.9"));
        Assert.Equal(before,await File.ReadAllBytesAsync(ws.Paths.Receipt(p.Id)));Assert.Empty(Directory.GetFiles(ws.Paths.Cache,"*.part",SearchOption.AllDirectories));
    }
    [Fact] public async Task MissingMainClassOrMissingLoaderCannotBecomeReady()
    {
        using var ws=new TestWorkspace();var p=ws.Profile(ModLoader.Quilt);var basis=await Basis(ws,p);
        using(var f=new Fixture(ws,missingMain:true))await Assert.ThrowsAsync<InvalidDataException>(()=>f.Installer.PrepareAsync(p,basis));
        using(var f=new Fixture(ws){MissingLoader=true})await Assert.ThrowsAsync<InvalidDataException>(()=>f.Installer.PrepareAsync(p,basis,"0.9.9"));
    }
    [Fact] public async Task ActiveProfileAndColdOfflineCacheBlockWithoutNetwork()
    {
        using var ws=new TestWorkspace();var p=ws.Profile(ModLoader.Quilt);var basis=await Basis(ws,p);using var f=new Fixture(ws);
        using(var lease=new ProfileUsage(ws.Paths).Acquire(p.Id))await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Installer.PrepareAsync(p,basis));Assert.Equal(0,f.Handler.Requests);
        f.Network.Offline=true;await Assert.ThrowsAsync<InvalidDataException>(()=>f.Installer.PrepareAsync(p,basis));Assert.Equal(0,f.Handler.Requests);
    }
    [Fact] public async Task SupportsStrongSha256SidecarFallbackAndDetectsLaterTampering()
    {
        using var ws=new TestWorkspace();var p=ws.Profile(ModLoader.Quilt);var basis=await Basis(ws,p);using var f=new Fixture(ws){Sha512Missing=true};
        var receipt=await f.Installer.PrepareAsync(p,basis);Assert.All(receipt.Files.Where(x=>x.RelativePath.StartsWith("libraries/quilt")),x=>Assert.Equal("SHA256",x.Algorithm));
        int calls=f.Handler.Requests;f.Network.Offline=true;await f.Installer.PrepareAsync(p,basis,"0.30.1");Assert.Equal(calls,f.Handler.Requests);
        var loader=receipt.Files.Single(x=>x.RelativePath.Contains("/quilt-loader/"));await File.WriteAllTextAsync(SafePath.Join(ws.Paths.Cache,loader.RelativePath),"tampered");
        Assert.False(await VerifiedDownloader.VerifyAsync(SafePath.Join(ws.Paths.Cache,loader.RelativePath),loader.Hash,loader.Algorithm,loader.Size));
    }
}
