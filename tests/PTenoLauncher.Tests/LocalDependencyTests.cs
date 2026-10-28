using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;
namespace PTenoLauncher.Tests;
public sealed class LocalDependencyTests
{
    internal sealed class Fixture : IDisposable
    {
        public readonly NetworkPolicy Network = new();
        public readonly StubHandler Handler;
        public readonly RemoteModInstaller Installer;
        public readonly LocalModManager Local;
        private readonly HttpClient http;
        private readonly TestWorkspace ws;
        private readonly Dictionary<string,byte[]> bytes = new();
        public readonly List<string> Routes = [];
        public int Downloads { get; private set; }
        public bool Tamper { get; set; }
        public bool WrongGame { get; set; }
        public Fixture(TestWorkspace ws, bool badDependencyId = false)
        {
            this.ws=ws;
            foreach(var id in new[]{"A","B","C","D","primary"})
            {
                using var memory=new MemoryStream();
                using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true))
                using(var output=new StreamWriter(zip.CreateEntry("fabric.mod.json").Open()))
                    output.Write(JsonSerializer.Serialize(new { schemaVersion=1, id=badDependencyId && id=="B" ? "wrong" : id.ToLowerInvariant(), version="1.0.0",
                        depends=id=="A" ? new Dictionary<string,string>{{"b",">=1.0"}} : id=="B" ? new Dictionary<string,string>{{"c","*"}} : new() }));
                bytes[id]=memory.ToArray();
                File.WriteAllBytes(Path.Combine(ws.Paths.Root,id+".jar"),bytes[id]);
            }
            object ModFile(string key,bool primary)=>new { filename=key+".jar",url="https://cdn.modrinth.com/data/"+key+".jar",size=bytes[key].Length,primary,hashes=new { sha512=Hash(key) } };
            object Version(string id,string? version=null)=>new {
                id=version??id+"1",project_id=id,version_number="1.0.0",name=id+" release",date_published="2026-01-01T00:00:00Z",version_type="release",status="listed",
                game_versions=new[]{WrongGame && id=="A" ? "1.22" : "1.21.1"},loaders=new[]{"fabric"},
                dependencies=id=="A" ? new[]{new {project_id="B",version_id=(string?)null,dependency_type="required"},new {project_id="D",version_id=(string?)null,dependency_type="optional"}} :
                    id=="B" ? new[]{new {project_id="C",version_id=(string?)null,dependency_type="required"}} : [],
                files=id=="A" ? new[]{ModFile("primary",true),ModFile("A",false)} : new[]{ModFile(id,true)}
            };
            static HttpResponseMessage Json(object? value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value))};
            Handler=new StubHandler(request=>{
                Assert.Equal(HttpMethod.Get,request.Method);Assert.Null(request.Content);
                var path=request.RequestUri!.AbsolutePath;Routes.Add(path);
                if(request.RequestUri.Host=="cdn.modrinth.com")
                {
                    Downloads++; var body=bytes[Path.GetFileNameWithoutExtension(path)].ToArray(); if(Tamper)body[^1]^=1;
                    return new(HttpStatusCode.OK){Content=new ByteArrayContent(body)};
                }
                foreach(var id in new[]{"A","B","C","D"})
                {
                    if(path=="/v2/project/"+id)return Json(new{id,slug=id,title="Mod "+id,project_type="mod",client_side="required"});
                    if(path=="/v2/project/"+id+"/version")return Json(new[]{Version(id)});
                    if(path=="/v2/version/"+id+"1")return Json(Version(id));
                    if(path=="/v2/version_file/"+Hash(id))return Json(Version(id));
                }
                if(path=="/v2/version/A2")return Json(Version("A","A2"));
                if(path.StartsWith("/v2/version_file/"))return Json(null);
                throw new InvalidOperationException("Unexpected route: "+request.RequestUri);
            });
            http=new(Handler);var downloader=new VerifiedDownloader(http,Network);Local=new(ws.Paths,ws.Store);
            Installer=new(ws.Paths,ws.Store,new(ws.Paths,downloader,Network),downloader,Local);
        }
        private string Hash(string id)=>Convert.ToHexString(SHA512.HashData(bytes[id])).ToLowerInvariant();
        public async Task<ModCartItem> Input(string id="A")
        {
            var file=Path.Combine(ws.Paths.Root,id+".jar");return new(file,await Local.InspectAsync(file));
        }
        public void Dispose()=>http.Dispose();
    }
    private static async Task<GameProfile> Profile(TestWorkspace ws)
    { await ws.Store.InitializeAsync();var p=ws.Profile(ModLoader.Fabric);await ws.Store.SaveProfileAsync(p);return p; }
    private static string Mods(TestWorkspace ws,GameProfile p)=>Path.Combine(ws.Paths.Game(p.Id),"mods");
    [Fact] public async Task ExactLocalSecondaryJarResolvesTransitiveDependenciesWithoutRedownloadingRoot()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws);using var f=new Fixture(ws);var input=await f.Input();
        var source=await File.ReadAllBytesAsync(input.SourcePath);var plan=await f.Installer.PreviewLocalAsync(p,[input]);
        Assert.True(plan.CanInstall);Assert.False(plan.RequiresUnknownAcceptance);Assert.Equal(3,plan.Remote.Resolution.Entries.Count);
        var root=Assert.Single(plan.Remote.Resolution.Entries,e=>e.FromLocalFile);Assert.Equal("A.jar",root.Version.File.Name);
        Assert.DoesNotContain("/v2/version/A1",f.Routes);Assert.Contains(plan.Remote.Resolution.Optional,o=>o.Dependency.ProjectId=="D");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Installer.InstallLocalAsync(p,plan,false,false));Assert.Equal(0,f.Downloads);
        await f.Installer.InstallLocalAsync(p,plan,true,false);
        Assert.Equal(2,f.Downloads);Assert.Equal(3,Directory.GetFiles(Mods(ws,p)).Length);Assert.Equal(source,await File.ReadAllBytesAsync(input.SourcePath));
        Assert.Single(await ws.Store.GetHistoryAsync(p.Id),h=>h.Action=="mods.installed");
        Assert.Equal(3,(await new ModInventoryStore(ws.Paths).ReadAsync(p.Id))!.Files.Count);
        Assert.Empty(Directory.GetFiles(ws.Paths.Profile(p.Id),"mods-*.json",SearchOption.AllDirectories));
    }
    [Fact] public async Task ChangedInputIsRejectedBeforeAnyDownload()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws);using var f=new Fixture(ws);var input=await f.Input();
        var plan=await f.Installer.PreviewLocalAsync(p,[input]);await File.AppendAllTextAsync(input.SourcePath,"changed");
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Installer.InstallLocalAsync(p,plan,true,false));
        Assert.Equal(0,f.Downloads);Assert.Empty(Directory.GetFiles(Mods(ws,p)));
    }
    [Fact] public async Task InstalledSetChangeInvalidatesCombinedPlan()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws);using var f=new Fixture(ws);
        var plan=await f.Installer.PreviewLocalAsync(p,[await f.Input()]);var manual=Path.Combine(Mods(ws,p),"manual.jar");SecurityAndModsTests.CreateJar(manual,"manual");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Installer.InstallLocalAsync(p,plan,true,false));Assert.Equal(0,f.Downloads);Assert.True(File.Exists(manual));
    }
    [Fact] public async Task UnknownLocalIdIsNeverGuessedAsProjectSlug()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws);using var f=new Fixture(ws);
        var file=Path.Combine(ws.Paths.Root,"unknown.jar");SecurityAndModsTests.CreateJar(file,"unknown", "{\"missing_library\":\"*\"}");
        var plan=await f.Installer.PreviewLocalAsync(p,[new(file,await f.Local.InspectAsync(file))]);
        Assert.False(plan.CanInstall);Assert.True(plan.RequiresUnknownAcceptance);Assert.Contains(plan.Blockers,b=>b.Contains("missing_library"));
        Assert.All(f.Routes,r=>Assert.StartsWith("/v2/version_file/",r));Assert.Equal(0,f.Downloads);
    }
    [Fact] public async Task ColdOfflineUnknownLocalWorksOnlyAfterBothExplicitConfirmations()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws);using var f=new Fixture(ws);f.Network.Offline=true;
        var file=Path.Combine(ws.Paths.Root,"unknown.jar");SecurityAndModsTests.CreateJar(file,"unknown");
        var plan=await f.Installer.PreviewLocalAsync(p,[new(file,await f.Local.InspectAsync(file))]);
        Assert.True(plan.CanInstall);Assert.True(plan.RequiresUnknownAcceptance);Assert.True(plan.Remote.CachedMetadata==false);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Installer.InstallLocalAsync(p,plan,true,false));
        await f.Installer.InstallLocalAsync(p,plan,true,true);Assert.Single(Directory.GetFiles(Mods(ws,p)));Assert.Equal(0,f.Handler.Requests);
    }
    [Fact] public async Task ColdOfflineMissingDependencyRemainsUnresolvedWithoutNetwork()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws);using var f=new Fixture(ws);f.Network.Offline=true;
        var plan=await f.Installer.PreviewLocalAsync(p,[await f.Input()]);Assert.False(plan.CanInstall);Assert.Equal(0,f.Handler.Requests);
        Assert.Contains(plan.Blockers,b=>b.Contains("b"));
    }
    [Fact] public async Task WarmCacheReusesLocalIdentityAndDownloadsEntirelyOffline()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws);using var f=new Fixture(ws);var input=await f.Input();
        var online=await f.Installer.PreviewLocalAsync(p,[input]);await f.Installer.InstallLocalAsync(p,online,true,false);
        int calls=f.Handler.Requests;f.Network.Offline=true;var second=p with{Id=Guid.NewGuid(),Name="Offline target"};await ws.Store.SaveProfileAsync(second);
        var offline=await f.Installer.PreviewLocalAsync(second,[input]);Assert.True(offline.CanInstall);Assert.True(offline.Remote.CachedMetadata);
        await f.Installer.InstallLocalAsync(second,offline,true,false);Assert.Equal(calls,f.Handler.Requests);Assert.Equal(3,Directory.GetFiles(Mods(ws,second)).Length);
    }
    [Fact] public async Task CorruptDependencyNeverCommitsLocalRootOrAnyOtherMod()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws);using var f=new Fixture(ws){Tamper=true};
        var plan=await f.Installer.PreviewLocalAsync(p,[await f.Input()]);
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Installer.InstallLocalAsync(p,plan,true,false));
        Assert.Empty(Directory.GetFiles(Mods(ws,p)));Assert.Null(await new ModInventoryStore(ws.Paths).ReadAsync(p.Id));
        Assert.Empty(Directory.GetFiles(ws.Paths.Cache,"*.part",SearchOption.AllDirectories));
    }
    [Fact] public async Task ActualJarIdsAreCheckedTogetherBeforeAnyProfileMutation()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws);using var f=new Fixture(ws,true);
        var plan=await f.Installer.PreviewLocalAsync(p,[await f.Input()]);Assert.True(plan.CanInstall);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Installer.InstallLocalAsync(p,plan,true,false));
        Assert.Contains("b",error.Message);Assert.Empty(Directory.GetFiles(Mods(ws,p)));Assert.Equal(2,f.Downloads);
    }
    [Fact] public async Task MultipleLocalFilesAndRemoteRootShareOneInstallAndSkipOptionalByDefault()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws);using var f=new Fixture(ws);
        var plan=await f.Installer.PreviewLocalAsync(p,[await f.Input("A"),await f.Input("B")],[new("D","D1")]);
        Assert.True(plan.CanInstall);Assert.Equal(2,plan.Remote.Resolution.Entries.Count(e=>e.FromLocalFile));
        await f.Installer.InstallLocalAsync(p,plan,true,false);Assert.Equal(2,f.Downloads);Assert.Equal(4,Directory.GetFiles(Mods(ws,p)).Length);
        Assert.Single(await ws.Store.GetHistoryAsync(p.Id),h=>h.Action=="mods.installed");
    }
    [Fact] public async Task DuplicateLocalInputsAreBlocked()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws);using var f=new Fixture(ws);var input=await f.Input();
        var plan=await f.Installer.PreviewLocalAsync(p,[input,input]);Assert.False(plan.CanInstall);Assert.NotEmpty(plan.Blockers);Assert.Equal(0,f.Downloads);
    }
    [Fact] public async Task IncompatibleMinecraftAndConflictingExplicitPinCannotReplaceLocalVersion()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws);using var f=new Fixture(ws);
        var pinned=await f.Installer.PreviewLocalAsync(p,[await f.Input()],[new("A","A2")]);Assert.False(pinned.CanInstall);
        Assert.NotEmpty(pinned.Remote.Resolution.Blockers);Assert.Equal(0,f.Downloads);
        using var other=new TestWorkspace();var otherP=await Profile(other);using var bad=new Fixture(other){WrongGame=true};
        Assert.False((await bad.Installer.PreviewLocalAsync(otherP,[await bad.Input()])).CanInstall);
    }
    [Fact] public async Task ActiveProfileBlocksPreviewBeforeAnyNetwork()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws);using var f=new Fixture(ws);var input=await f.Input();
        using var lease=new ProfileUsage(ws.Paths).Acquire(p.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Installer.PreviewLocalAsync(p,[input]));Assert.Equal(0,f.Handler.Requests);
    }
    [Fact] public async Task AlreadyInstalledExactLocalRootIsReusedWithoutDuplicateCopy()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws);using var f=new Fixture(ws);var input=await f.Input();
        var first=await f.Installer.PreviewLocalAsync(p,[input]);await f.Installer.InstallLocalAsync(p,first,true,false);
        var second=await f.Installer.PreviewLocalAsync(p,[input]);Assert.True(second.CanInstall);Assert.All(second.Remote.Resolution.Entries,e=>Assert.True(e.AlreadyInstalled));
        await f.Installer.InstallLocalAsync(p,second,true,false);Assert.Equal(2,f.Downloads);Assert.Equal(3,Directory.GetFiles(Mods(ws,p)).Length);
    }
    [Fact] public async Task DefiniteMinecraftMismatchBlocksBeforeDownloadingOtherDependencies()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws);using var f=new Fixture(ws);
        var local=Path.Combine(ws.Paths.Root,"future.jar");
        SecurityAndModsTests.CreateJar(local,"future", "{\"minecraft\":\">=1.22\"}");
        var plan=await f.Installer.PreviewLocalAsync(p,[await f.Input(),new(local,await f.Local.InspectAsync(local))]);
        Assert.False(plan.CanInstall);Assert.Contains(plan.Blockers,b=>b.Contains("1.22"));Assert.Equal(0,f.Downloads);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Installer.InstallLocalAsync(p,plan,true,true));
        Assert.Empty(Directory.GetFiles(Mods(ws,p)));Assert.Equal(0,f.Downloads);
    }
    [Fact] public async Task UnknownMissingDependencyCanBeResolvedByDownloadedJar()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws);using var f=new Fixture(ws);
        var local=Path.Combine(ws.Paths.Root,"extra.jar");
        SecurityAndModsTests.CreateJar(local,"extra", "{\"b\":\"*\"}");
        var plan=await f.Installer.PreviewLocalAsync(p,[await f.Input(),new(local,await f.Local.InspectAsync(local))]);
        Assert.True(plan.CanInstall);Assert.Contains(plan.Warnings,w=>w.Contains("b"));
        await f.Installer.InstallLocalAsync(p,plan,true,true);
        Assert.Equal(4,Directory.GetFiles(Mods(ws,p)).Length);Assert.Equal(2,f.Downloads);
    }
    [Fact] public async Task WrongLoaderBlocksBeforeDownloadingCatalogDependencies()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws);using var f=new Fixture(ws);
        var local=Path.Combine(ws.Paths.Root,"forge.jar");
        using(var archive=ZipFile.Open(local,ZipArchiveMode.Create))
        using(var text=new StreamWriter(archive.CreateEntry("META-INF/mods.toml").Open()))
            text.Write("modLoader=\"javafml\"\nloaderVersion=\"[52,)\"\nlicense=\"MIT\"\n[[mods]]\nmodId=\"forge_example\"\nversion=\"1.0\"\n");
        var plan=await f.Installer.PreviewLocalAsync(p,[await f.Input(),new(local,await f.Local.InspectAsync(local))]);
        Assert.False(plan.CanInstall);Assert.Contains(plan.Blockers,b=>b.Contains("Forge"));Assert.Equal(0,f.Downloads);
    }
    [Fact] public async Task SelectedOptionalUsesSameTransactionAndWorksFromCacheOffline()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws);using var f=new Fixture(ws);var input=await f.Input();
        var initial=await f.Installer.PreviewLocalAsync(p,[input]);var key=Assert.Single(initial.Remote.Resolution.Optional).Key;
        var selected=await f.Installer.PreviewLocalAsync(p,[input],optionalSelections:[key]);Assert.True(selected.CanInstall);
        Assert.Contains(selected.Remote.Resolution.Entries,e=>e.Version.ProjectId=="D");
        await f.Installer.InstallLocalAsync(p,selected,true,false);Assert.Equal(4,Directory.GetFiles(Mods(ws,p)).Length);Assert.Equal(3,f.Downloads);
        Assert.Single(await ws.Store.GetHistoryAsync(p.Id),h=>h.Action=="mods.installed");
        int calls=f.Handler.Requests;f.Network.Offline=true;var other=p with{Id=Guid.NewGuid(),Name="Offline optional"};await ws.Store.SaveProfileAsync(other);
        var cached=await f.Installer.PreviewLocalAsync(other,[input],optionalSelections:[key]);Assert.True(cached.CanInstall);Assert.True(cached.Remote.CachedMetadata);
        await f.Installer.InstallLocalAsync(other,cached,true,false);Assert.Equal(calls,f.Handler.Requests);Assert.Equal(4,Directory.GetFiles(Mods(ws,other)).Length);
    }
}
