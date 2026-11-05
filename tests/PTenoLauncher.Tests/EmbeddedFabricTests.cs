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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StandaloneMezzConfigSupersedesJeiEmbeddedCandidateRegardlessOfFileOrder(bool standaloneFirst)
    {
        using var ws = new TestWorkspace();
        await ws.Store.InitializeAsync();
        var profile = ws.Profile(ModLoader.Fabric);
        await ws.Store.SaveProfileAsync(profile);
        var mods = new LocalModManager(ws.Paths, ws.Store);
        var jei = Path.Combine(ws.Paths.Root, "jei.jar");
        using (var jar = ZipFile.Open(jei, ZipArchiveMode.Create))
        {
            using (var metadata = new StreamWriter(jar.CreateEntry("fabric.mod.json").Open()))
                metadata.Write("""{"schemaVersion":1,"id":"jei","name":"Just Enough Items","version":"19.64.0.455","depends":{"mezz_config":">=0.6.3 <1.0.0"},"jars":[{"file":"META-INF/jars/mezz_config-0.6.6.jar"}]}""");
            using var output = jar.CreateEntry("META-INF/jars/mezz_config-0.6.6.jar").Open();
            using var nested = new ZipArchive(output, ZipArchiveMode.Create);
            using var child = new StreamWriter(nested.CreateEntry("fabric.mod.json").Open());
            child.Write("""{"schemaVersion":1,"id":"mezz_config","name":"MezzConfig","version":"0.6.6","provides":["embedded_alias"],"depends":{"unused_embedded_dependency":"*"}}""");
        }
        var config = Path.Combine(ws.Paths.Root, "mezz_config.jar");
        using (var jar = ZipFile.Open(config, ZipArchiveMode.Create))
        using (var metadata = new StreamWriter(jar.CreateEntry("fabric.mod.json").Open()))
            metadata.Write("""{"schemaVersion":1,"id":"mezz_config","name":"MezzConfig","version":"0.6.10"}""");
        var jeiItem = new ModCartItem(jei, await mods.InspectAsync(jei));
        var configItem = new ModCartItem(config, await mods.InspectAsync(config));
        var cart = standaloneFirst ? new[] { configItem, jeiItem } : [jeiItem, configItem];
        var review = await mods.ReviewAsync(profile, cart);
        Assert.Empty(review.Blockers);
        Assert.DoesNotContain(review.Warnings, w => w.StartsWith("mezz_config:", StringComparison.Ordinal));
        await mods.InstallAsync(profile, cart, true);
        var files = Directory.GetFiles(Path.Combine(ws.Paths.Game(profile.Id), "mods"), "*.jar");
        Assert.Equal(2, files.Length);
        var installed = await Task.WhenAll(files.Select(f => mods.InspectAsync(f)));
        Assert.Empty(InstalledModReadiness.CheckFabricSet(profile, null, installed.ToList()));
    }
    [Fact] public async Task TopLevelAliasConflictsWithAnInstalledPrimaryId()
    {
        using var ws = new TestWorkspace();
        await ws.Store.InitializeAsync();
        var profile = ws.Profile(ModLoader.Fabric);
        await ws.Store.SaveProfileAsync(profile);
        var installed = Path.Combine(ws.Paths.Game(profile.Id), "mods", "existing.jar");
        SecurityAndModsTests.CreateJar(installed, "mezz_config");
        var replacement = Path.Combine(ws.Paths.Root, "replacement.jar");
        using (var jar = ZipFile.Open(replacement, ZipArchiveMode.Create))
        using (var metadata = new StreamWriter(jar.CreateEntry("fabric.mod.json").Open()))
            metadata.Write("""{"schemaVersion":1,"id":"other_config","name":"Other config","version":"2.0","provides":["mezz_config"]}""");
        var manager = new LocalModManager(ws.Paths, ws.Store);
        var item = new ModCartItem(replacement, await manager.InspectAsync(replacement));
        var conflict = await Assert.ThrowsAsync<ModIdentityConflictException>(() => manager.InstallAsync(profile, [item], true));
        Assert.Contains("existing.jar", conflict.Detail);
        Assert.Contains("Other config 2.0", conflict.Detail);
        Assert.Contains("mezz_config", conflict.Detail);
        Assert.Equal(installed, Assert.Single(Directory.GetFiles(Path.GetDirectoryName(installed)!)));
    }
    [Fact] public async Task TraversalInsideNestedJarIsRejectedWithoutExtraction()
    {
        using var ws=new TestWorkspace();var file=Path.Combine(ws.Paths.Root,"parent.jar");Create(file,traversal:true);
        await Assert.ThrowsAsync<InvalidDataException>(()=>new LocalModManager(ws.Paths,ws.Store).InspectAsync(file));
        Assert.False(File.Exists(Path.Combine(ws.Paths.Root,"outside.txt")));
    }
}
