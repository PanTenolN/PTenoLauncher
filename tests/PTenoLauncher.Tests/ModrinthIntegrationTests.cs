using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Network;
using Xunit;
namespace PTenoLauncher.Tests;
public sealed class ModrinthIntegrationTests
{
    private sealed class Fixture : IDisposable
    {
        public NetworkPolicy Network { get; } = new();
        public StubHandler Handler { get; }
        private readonly HttpClient http;
        public ModrinthClient Catalog { get; }
        public RemoteModInstaller Installer { get; }
        public bool Tamper { get; set; }
        public string? BadFileName { get; set; }
        public string? BadUrl { get; set; }
        public int CdnRequests { get; private set; }
        private readonly Dictionary<string,byte[]> bytes = new();
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) {Content=new StringContent(JsonSerializer.Serialize(value))};
        public Fixture(TestWorkspace ws)
        {
            foreach(var id in new[]{"A","B"})
            {
                var path=Path.Combine(ws.Paths.Root,id+".jar");
                SecurityAndModsTests.CreateJar(path,id.ToLowerInvariant(),id=="A" ? """{"b":">=1.0.0"}""" : "{}");
                bytes[id]=File.ReadAllBytes(path);
            }
            object Version(string id) => new {
                id=id+"1",project_id=id,version_number="1.0.0",name=id+" release",date_published="2026-01-01T00:00:00Z",version_type="release",status="listed",
                game_versions=new[]{"1.21.1"},loaders=new[]{"fabric"},
                dependencies=id=="A" ? new[]{new {project_id="B",version_id=(string?)null,dependency_type="required"}} : [],
                files=new[]{new {filename=BadFileName??id+".jar",url=BadUrl??"https://cdn.modrinth.com/data/"+id+".jar",size=bytes[id].Length,primary=true,hashes=new{sha512=Convert.ToHexString(SHA512.HashData(bytes[id])).ToLowerInvariant()}}}
            };
            Handler=new StubHandler(request => {
                var path=request.RequestUri!.AbsolutePath;
                if(request.RequestUri.Host=="cdn.modrinth.com")
                {
                    CdnRequests++;var body=bytes[Path.GetFileNameWithoutExtension(path)].ToArray();if(Tamper)body[^1]^=1;
                    return new(HttpStatusCode.OK){Content=new ByteArrayContent(body)};
                }
                foreach(var id in bytes.Keys)
                {
                    if(path=="/v2/project/"+id) return Json(new{id,slug=id,title="Mod "+id,project_type="mod",client_side="required"});
                    if(path=="/v2/project/"+id+"/version") return Json(new[]{Version(id)});
                    if(path=="/v2/version/"+id+"1") return Json(Version(id));
                    if(path=="/v2/version_file/"+Convert.ToHexString(SHA512.HashData(bytes[id])).ToLowerInvariant()) return Json(Version(id));
                }
                throw new InvalidOperationException("Unexpected request: "+request.RequestUri);
            });
            http=new(Handler);var download=new VerifiedDownloader(http,Network);Catalog=new(ws.Paths,download,Network);
            Installer=new(ws.Paths,ws.Store,Catalog,download,new(ws.Paths,ws.Store));
        }
        public void Dispose()=>http.Dispose();
    }
    [Fact] public async Task PreviewDownloadInstallIdentityAndOfflineReuseWorkWithoutAi()
    {
        using var ws=new TestWorkspace();await ws.Store.InitializeAsync();var p=ws.Profile(ModLoader.Fabric);await ws.Store.SaveProfileAsync(p);
        using var fixture=new Fixture(ws);
        var plan=await fixture.Installer.PreviewAsync(p,[new("A")]);
        Assert.True(plan.Resolution.CanInstall);Assert.Equal(2,plan.Resolution.Entries.Count);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(ws.Paths.Game(p.Id),"mods")));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Installer.InstallAsync(p,plan,false));
        await fixture.Installer.InstallAsync(p,plan,true);
        Assert.Equal(2,Directory.EnumerateFiles(Path.Combine(ws.Paths.Game(p.Id),"mods"),"*.jar").Count());
        var identified=await fixture.Installer.PreviewAsync(p,[new("A")]);
        Assert.All(identified.Resolution.Entries,e=>Assert.True(e.AlreadyInstalled));
        var count=fixture.Handler.Requests;fixture.Network.Offline=true;
        var p2=p with {Id=Guid.NewGuid(),Name="Offline target"};await ws.Store.SaveProfileAsync(p2);
        var offline=await fixture.Installer.PreviewAsync(p2,[new("A")]);
        Assert.True(offline.CachedMetadata);Assert.True(offline.Resolution.CanInstall);
        await fixture.Installer.InstallAsync(p2,offline,true);
        Assert.Equal(count,fixture.Handler.Requests);
        Assert.Equal(2,Directory.EnumerateFiles(Path.Combine(ws.Paths.Game(p2.Id),"mods"),"*.jar").Count());
    }
    [Fact] public async Task ChangedInstalledSetRejectsPlanBeforeDownloading()
    {
        using var ws=new TestWorkspace();await ws.Store.InitializeAsync();var p=ws.Profile(ModLoader.Fabric);await ws.Store.SaveProfileAsync(p);
        using var fixture=new Fixture(ws);var plan=await fixture.Installer.PreviewAsync(p,[new("A")]);
        var changed=Path.Combine(ws.Paths.Game(p.Id),"mods","manual.jar");SecurityAndModsTests.CreateJar(changed,"manual");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Installer.InstallAsync(p,plan,true));
        Assert.Equal(0,fixture.CdnRequests);Assert.True(File.Exists(changed));
    }
    [Fact] public async Task FailedHashNeverCommitsAnyProfileMods()
    {
        using var ws=new TestWorkspace();await ws.Store.InitializeAsync();var p=ws.Profile(ModLoader.Fabric);await ws.Store.SaveProfileAsync(p);
        using var fixture=new Fixture(ws){Tamper=true};var plan=await fixture.Installer.PreviewAsync(p,[new("A")]);
        await Assert.ThrowsAsync<InvalidDataException>(()=>fixture.Installer.InstallAsync(p,plan,true));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(ws.Paths.Game(p.Id),"mods")));
        Assert.Empty(Directory.EnumerateFiles(ws.Paths.Cache,"*.part",SearchOption.AllDirectories));
    }
    [Theory]
    [InlineData("../escape.jar",null)]
    [InlineData("safe.jar","https://example.org/evil.jar")]
    [InlineData("safe.jar","http://cdn.modrinth.com/evil.jar")]
    public async Task PinnedVersionRejectsTraversalOrUnapprovedDownloadHost(string filename,string? url)
    {
        using var ws=new TestWorkspace();using var fixture=new Fixture(ws){BadFileName=filename,BadUrl=url};
        await Assert.ThrowsAsync<InvalidDataException>(()=>fixture.Catalog.GetVersionAsync("A1"));
    }
    [Fact] public async Task ColdOfflineMetadataBlocksWithoutAnyNetworkRequest()
    {
        using var ws=new TestWorkspace();await ws.Store.InitializeAsync();var p=ws.Profile(ModLoader.Fabric);await ws.Store.SaveProfileAsync(p);
        using var fixture=new Fixture(ws);fixture.Network.Offline=true;
        var plan=await fixture.Installer.PreviewAsync(p,[new("A")]);
        Assert.False(plan.Resolution.CanInstall);Assert.NotEmpty(plan.Resolution.Blockers);Assert.Equal(0,fixture.Handler.Requests);
    }
}
