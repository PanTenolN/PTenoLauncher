using System.IO.Compression;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
using Xunit;
namespace PTenoLauncher.Tests;
public sealed class EmbeddedFabricTests
{
    private static void Create(string path, bool missingDependency=false, bool traversal=false)
    {
        using var memory=new MemoryStream();
        using(var inner=new ZipArchive(memory,ZipArchiveMode.Create,true))
        {
            using(var text=new StreamWriter(inner.CreateEntry("fabric.mod.json").Open()))
                text.Write(JsonSerializer.Serialize(new {id="bundled",version="1.2.0",depends=missingDependency ? new Dictionary<string,string>{{"still_missing","*"}} : new()}));
            if(traversal)inner.CreateEntry("../outside.txt");
        }
        using var root=ZipFile.Open(path,ZipArchiveMode.Create);
        using(var meta=new StreamWriter(root.CreateEntry("fabric.mod.json").Open()))
            meta.Write("""{"id":"parent","version":"1.0.0","depends":{"bundled":">=1.0"},"jars":[{"file":"META-INF/jars/child.jar"}]}""");
        using var output=root.CreateEntry("META-INF/jars/child.jar").Open();output.Write(memory.ToArray());
    }
    [Fact] public async Task BundledFabricModuleSatisfiesDependencyWithoutExternalDownload()
    {
        using var ws=new TestWorkspace();await ws.Store.InitializeAsync();var p=ws.Profile(ModLoader.Fabric);await ws.Store.SaveProfileAsync(p);
        var file=Path.Combine(ws.Paths.Root,"parent.jar");Create(file);
        var mods=new LocalModManager(ws.Paths,ws.Store);var info=await mods.InspectAsync(file);
        Assert.Contains(info.ProvidedMods!,m=>m.Id=="bundled" && m.Version=="1.2.0");
        var cart=new[]{new ModCartItem(file,info)};Assert.Empty((await mods.ReviewAsync(p,cart)).Blockers);
        await mods.InstallAsync(p,cart,true);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(ws.Paths.Game(p.Id),"mods"),"*.jar"));
    }
    [Fact] public async Task NestedRequiredDependencyIsStillChecked()
    {
        using var ws=new TestWorkspace();await ws.Store.InitializeAsync();var p=ws.Profile(ModLoader.Fabric);await ws.Store.SaveProfileAsync(p);
        var file=Path.Combine(ws.Paths.Root,"parent.jar");Create(file,true);
        var mods=new LocalModManager(ws.Paths,ws.Store);var info=await mods.InspectAsync(file);
        Assert.Contains((await mods.ReviewAsync(p,[new(file,info)])).Blockers,b=>b.Contains("still_missing"));
    }
    [Fact] public async Task TraversalInsideNestedJarIsRejectedWithoutExtraction()
    {
        using var ws=new TestWorkspace();var file=Path.Combine(ws.Paths.Root,"parent.jar");Create(file,traversal:true);
        await Assert.ThrowsAsync<InvalidDataException>(()=>new LocalModManager(ws.Paths,ws.Store).InspectAsync(file));
        Assert.False(File.Exists(Path.Combine(ws.Paths.Root,"outside.txt")));
    }
}
