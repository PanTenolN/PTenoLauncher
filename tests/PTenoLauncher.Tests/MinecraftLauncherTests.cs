using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Modpacks;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;
namespace PTenoLauncher.Tests;

public sealed class MinecraftLauncherTests
{
    internal static (string File,string Content) Source(MrpackFixture f,string? main=null,string? coordinate=null,bool external=false,string filename="launcher_profiles.json")
    {
        var installation=Path.Combine(f.Workspace.Paths.Root,"minecraft-source-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(installation);
        var content=external?Path.Combine(f.Workspace.Paths.Root,"explicit-content-"+Guid.NewGuid().ToString("N")):installation;Directory.CreateDirectory(content);
        InstancePackTests.Put(content,"config/example.txt","LOCAL CONTENT"u8.ToArray());InstancePackTests.Put(content,"saves/world/level.dat","SYNTHETIC WORLD"u8.ToArray());InstancePackTests.Put(content,"launcher_accounts.json","PRIVATE FIXTURE ACCOUNT DATA"u8.ToArray());
        var id=main is null?"1.21.1":"custom-version-id";
        var profiles=new{profiles=new Dictionary<string,object>{{"selected",new{name="Selected source profile",type="custom",lastVersionId=id,gameDir=external?content:null,javaArgs="-Xmx8G"}},{"other",new{name="Other profile",type="custom",lastVersionId="latest-release"}}},authenticationDatabase=new{privateValue="PRIVATE FIXTURE ACCOUNT DATA"},clientToken="PRIVATE FIXTURE CLIENT DATA"};
        var file=Path.Combine(installation,filename);File.WriteAllBytes(file,JsonSerializer.SerializeToUtf8Bytes(profiles,new JsonSerializerOptions{DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull}));
        InstancePackTests.Put(installation,"versions/1.21.1/1.21.1.json",JsonSerializer.SerializeToUtf8Bytes(new{id="1.21.1",mainClass="net.minecraft.client.main.Main",type="release",downloads=new{client=new{sha1=new string('a',40),url="https://piston-data.mojang.com/v1/objects/"+new string('a',40)+"/client.jar"}}}));
        if(main is not null)InstancePackTests.Put(installation,"versions/"+id+"/"+id+".json",JsonSerializer.SerializeToUtf8Bytes(new{id,inheritsFrom="1.21.1",mainClass=main,libraries=new[]{new{name=coordinate??"net.fabricmc:fabric-loader:0.16.10"},new{name="net.fabricmc:intermediary:1.21.1"}},arguments=new{game=Array.Empty<string>(),jvm=Array.Empty<string>()}}));
        return(file,content);
    }
    internal static MinecraftLauncherRequest Request(string content)=>new("selected","Imported independently",content,true);
    [Theory][InlineData("launcher_profiles.json")][InlineData("launcher_profiles_microsoft_store.json")]
    public async Task ExplicitVanillaProfileImportsOnlyPermittedContentOfflineAndKeepsSourceAccountsWorldsUntouched(string filename)
    {
        using var f=new MrpackFixture();await f.Workspace.Store.InitializeAsync();f.Network.Offline=true;var source=Source(f,filename:filename);var before=Directory.GetFiles(Path.GetDirectoryName(source.File)!,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>File.ReadAllBytes(p));
        var p=await f.Importer.MinecraftReader.PreviewAsync(source.File,Request(source.Content));Assert.True(p.Plan.CanImport,string.Join(",",p.Plan.Blockers));Assert.Equal(2,p.Choices.Count);Assert.Equal("1.21.1",p.Plan.Minecraft);
        var result=await f.Importer.ImportMinecraftAsync(p,true);Assert.Equal(ModLoader.Vanilla,result.Profile.Loader);Assert.Equal(4096,result.Profile.MemoryMb);Assert.Null(result.Profile.JavaPath);Assert.Single(result.Receipt.Files);
        Assert.Equal("local-minecraft-launcher",(await MrpackRequirements.ReadAsync(f.Workspace.Paths,result.Profile))!.SourceKind);Assert.False(File.Exists(f.Workspace.Paths.Receipt(result.Profile.Id)));Assert.Equal(0,f.Requests);
        Assert.All(before,pair=>Assert.Equal(pair.Value,File.ReadAllBytes(pair.Key)));
        foreach(var file in Directory.GetFiles(f.Workspace.Paths.Profile(result.Profile.Id),"*.json",SearchOption.AllDirectories))Assert.DoesNotContain("PRIVATE FIXTURE",File.ReadAllText(file));
        Assert.Empty(Directory.GetFiles(Path.Combine(f.Workspace.Paths.Game(result.Profile.Id),"saves")));Assert.Empty(await f.Importer.PendingAsync());
    }
    [Theory][InlineData("net.fabricmc.loader.impl.launch.knot.KnotClient","net.fabricmc:fabric-loader:0.16.10",ModLoader.Fabric,"0.16.10")][InlineData("org.quiltmc.loader.impl.launch.knot.KnotClient","org.quiltmc:quilt-loader:0.30.1",ModLoader.Quilt,"0.30.1")]
    public async Task OpaqueVersionIdUsesInheritedMetadataAndLoaderCoordinateNeverFilenameGuessing(string main,string coordinate,ModLoader loader,string version)
    {
        using var f=new MrpackFixture();await f.Workspace.Store.InitializeAsync();var s=Source(f,main,coordinate);var p=await f.Importer.MinecraftReader.PreviewAsync(s.File,Request(s.Content));Assert.True(p.Plan.CanImport,string.Join(",",p.Plan.Blockers));Assert.Equal("1.21.1",p.Plan.Minecraft);Assert.Equal(loader,p.Plan.Loader);
        var result=await f.Importer.ImportMinecraftAsync(p,true);Assert.Equal(version,await MrpackRequirements.RequestedAsync(f.Workspace.Paths,result.Profile));await Assert.ThrowsAsync<MrpackException>(()=>MrpackRequirements.RequestedAsync(f.Workspace.Paths,result.Profile,"9.9.9"));Assert.False(File.Exists(f.Workspace.Paths.Receipt(result.Profile.Id)));
    }
    [Fact]public async Task ExternalContentIsNotTraversedWithoutExplicitMatchingDirectoryChoice()
    {
        using var f=new MrpackFixture();var s=Source(f,external:true);var noChoice=await f.Importer.MinecraftReader.PreviewAsync(s.File,new("selected"));Assert.Contains(noChoice.Plan.Blockers,b=>b.Code=="MinecraftImportContent");Assert.Empty(noChoice.Snapshot.Content);Assert.Equal(s.Content,noChoice.RequiredContentDirectory);
        var wrong=await f.Importer.MinecraftReader.PreviewAsync(s.File,new("selected",ContentDirectory:Path.GetDirectoryName(s.File)));Assert.False(wrong.Plan.CanImport);Assert.Empty(wrong.Snapshot.Content);
        Assert.True((await f.Importer.MinecraftReader.PreviewAsync(s.File,Request(s.Content))).Plan.CanImport);Assert.Equal(0,f.Requests);
    }
    [Fact]public async Task ProfileMustBeSelectedAndLatestIsNeverResolvedSilently()
    {
        using var f=new MrpackFixture();var s=Source(f);var choose=await f.Importer.MinecraftReader.PreviewAsync(s.File,new());Assert.Contains(choose.Plan.Blockers,b=>b.Code=="MinecraftImportChoose");Assert.Empty(choose.Snapshot.Content);
        var latest=await f.Importer.MinecraftReader.PreviewAsync(s.File,new("other",ContentDirectory:s.Content));Assert.Contains(latest.Plan.Blockers,b=>b.Code=="MinecraftImportUnsupported");Assert.False(latest.Plan.CanImport);
    }
    [Theory][InlineData("forge.Bootstrap","net.minecraftforge:fmlloader:52.0.1")][InlineData("net.fabricmc.loader.impl.launch.knot.KnotClient","net.fabricmc:fabric-loader:latest")][InlineData("org.unknown.CustomClient","net.fabricmc:fabric-loader:0.16.10")]
    public async Task UnsupportedOrAmbiguousLoadersRemainExplicitBlockers(string main,string coordinate)
    {using var f=new MrpackFixture();var s=Source(f,main,coordinate);var p=await f.Importer.MinecraftReader.PreviewAsync(s.File,Request(s.Content));Assert.False(p.Plan.CanImport);Assert.Contains(p.Plan.Blockers,b=>b.Code=="MinecraftImportUnsupported");}
    [Theory][InlineData("command")][InlineData("argument")][InlineData("base")][InlineData("library")]
    public async Task CustomCommandsVersionOverridesMissingBaseAndUnknownLibrariesAreRejected(string kind)
    {
        using var f=new MrpackFixture();var s=Source(f,"net.fabricmc.loader.impl.launch.knot.KnotClient");var dir=Path.GetDirectoryName(s.File)!;
        if(kind=="command")File.WriteAllText(s.File,"{\"profiles\":{\"selected\":{\"name\":\"X\",\"lastVersionId\":\"1.21.1\",\"preLaunchCommand\":\"do not execute\"}}}");
        if(kind=="base")File.Delete(Path.Combine(dir,"versions/1.21.1/1.21.1.json"));
        if(kind=="argument"||kind=="library")
        {
            var file=Path.Combine(dir,"versions/custom-version-id/custom-version-id.json");var text=File.ReadAllText(file);
            text=kind=="argument"?text.Replace("\"jvm\":[]","\"jvm\":[\"-javaagent:unknown.jar\"]"):text.Replace("net.fabricmc:intermediary:1.21.1","unknown.group:injector:1.0");File.WriteAllText(file,text);
        }
        var p=await f.Importer.MinecraftReader.PreviewAsync(s.File,Request(s.Content));Assert.False(p.Plan.CanImport);Assert.Contains(p.Plan.Blockers,b=>b.Code=="MinecraftImportUnsupported");Assert.Equal(0,f.Requests);
    }
    [Theory][InlineData("{broken")][InlineData("{\"profiles\":null}")][InlineData("{\"profiles\":{\"x\":null}}")][InlineData("{\"profiles\":{},\"profiles\":{}}")]
    public async Task MalformedMetadataIsSafeAndDoesNotExposeForeignData(string text)
    {using var f=new MrpackFixture();var s=Source(f);File.WriteAllText(s.File,text);var error=await Assert.ThrowsAsync<MrpackException>(()=>f.Importer.MinecraftReader.PreviewAsync(s.File,new()));Assert.DoesNotContain("PRIVATE FIXTURE",error.ToString());}
    [Theory][InlineData("profiles")][InlineData("version")][InlineData("content")][InlineData("choice")]
    public async Task ChangedSourceOrProfileChoiceInvalidatesPlanBeforeOperation(string kind)
    {
        using var f=new MrpackFixture();await f.Workspace.Store.InitializeAsync();var s=Source(f);var p=await f.Importer.MinecraftReader.PreviewAsync(s.File,Request(s.Content));
        if(kind=="profiles")File.AppendAllText(s.File," ");if(kind=="version")File.AppendAllText(Path.Combine(Path.GetDirectoryName(s.File)!,"versions/1.21.1/1.21.1.json")," ");if(kind=="content")InstancePackTests.Put(s.Content,"config/new",[1]);
        if(kind=="choice")p=p with{Request=p.Request with{ProfileKey="other"}};
        Assert.Equal("PackChanged",(await Assert.ThrowsAsync<MrpackException>(()=>f.Importer.ImportMinecraftAsync(p,true))).Code);Assert.Empty(await f.Importer.PendingAsync());Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
    }
    [Theory][InlineData("preparing",false)][InlineData("ready",true)][InlineData("moved",true)][InlineData("before-database",true)][InlineData("database",true)]
    public async Task RecoveryPublishesOnlyVerifiedSnapshotAndSqliteState(string boundary,bool publishes)
    {
        using var f=new MrpackFixture();await f.Workspace.Store.InitializeAsync();var s=Source(f);var p=await f.Importer.MinecraftReader.PreviewAsync(s.File,Request(s.Content));f.Importer.Fault=stage=>{if(stage==boundary)throw new IOException("Controlled interruption");};
        await Assert.ThrowsAsync<IOException>(()=>f.Importer.ImportMinecraftAsync(p,true));if(boundary!="database")Assert.Empty(await f.Workspace.Store.GetProfilesAsync());File.Delete(s.File);f.Importer.Fault=null;await f.Importer.RecoverAsync(true);await f.Importer.RecoverAsync(true);Assert.Equal(publishes?1:0,(await f.Workspace.Store.GetProfilesAsync()).Count);Assert.Empty(await f.Importer.PendingAsync());Assert.Equal(0,f.Requests);
    }
    [Theory][InlineData("ready")][InlineData("moved")]
    public async Task RecoveryNeverAdoptsForeignOrNewerBytes(string boundary)
    {
        using var f=new MrpackFixture();await f.Workspace.Store.InitializeAsync();var s=Source(f);var p=await f.Importer.MinecraftReader.PreviewAsync(s.File,Request(s.Content));f.Importer.Fault=stage=>{if(stage==boundary)throw new IOException();};await Assert.ThrowsAsync<IOException>(()=>f.Importer.ImportMinecraftAsync(p,true));f.Importer.Fault=null;
        var id=Assert.Single(await f.Importer.PendingAsync());var target=f.Workspace.Paths.Profile(id);Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"foreign.txt"),"NEWER");await Assert.ThrowsAsync<MrpackException>(()=>f.Importer.RecoverAsync(true));Assert.Equal("NEWER",File.ReadAllText(Path.Combine(target,"foreign.txt")));Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
    }
    [Fact]public async Task CancellationLeavesRecoverableStateAndSourceIsUnchanged()
    {
        using var f=new MrpackFixture();await f.Workspace.Store.InitializeAsync();var s=Source(f);var bytes=File.ReadAllBytes(s.File);var p=await f.Importer.MinecraftReader.PreviewAsync(s.File,Request(s.Content));using var stop=new CancellationTokenSource();f.Importer.Fault=stage=>{if(stage=="preparing")stop.Cancel();};await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Importer.ImportMinecraftAsync(p,true,ct:stop.Token));f.Importer.Fault=null;await f.Importer.RecoverAsync(true);Assert.Empty(await f.Workspace.Store.GetProfilesAsync());Assert.Equal(bytes,File.ReadAllBytes(s.File));
    }
    [Fact]public async Task ActualJarsAreStaticallyReviewedWithoutRunningOrInventingProviderIdentity()
    {
        using var f=new MrpackFixture();await f.Workspace.Store.InitializeAsync();f.Network.Offline=true;var s=Source(f,"net.fabricmc.loader.impl.launch.knot.KnotClient");InstancePackTests.Put(s.Content,"mods/demo.jar",MrpackFixture.Jar(dependency:"missing-required"));
        var blocked=await f.Importer.MinecraftReader.PreviewAsync(s.File,Request(s.Content));Assert.Contains(blocked.Plan.Blockers,b=>b.Code=="PackCompatibility");InstancePackTests.Put(s.Content,"mods/demo.jar",MrpackFixture.Jar());var p=await f.Importer.MinecraftReader.PreviewAsync(s.File,Request(s.Content));Assert.True(p.Plan.CanImport,string.Join(",",p.Plan.Blockers));var result=await f.Importer.ImportMinecraftAsync(p,true);
        var pins=await AtomicFile.ReadJsonAsync<PTenoLauncher.Infrastructure.Mods.ModPinSet>(Path.Combine(f.Workspace.Paths.Profile(result.Profile.Id),"mod-pins.json"));Assert.Null(Assert.Single(pins!.Pins).Provider);Assert.Equal(0,f.Requests);
    }
}
