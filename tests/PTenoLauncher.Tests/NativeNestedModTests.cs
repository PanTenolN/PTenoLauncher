using System.IO.Compression;
using System.Text;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class NativeNestedModTests
{
    [Theory]
    [InlineData("1.0.0-rc.01", "\">1.0.0-rc.2\"", ConstraintMatch.Match)]
    [InlineData("1.0.0-rc.01", "\"=1.0.0-rc.1\"", ConstraintMatch.Mismatch)]
    [InlineData("1.0.0-alpha.2", "\"<1.0.0-alpha.11\"", ConstraintMatch.Match)]
    [InlineData("1.0.0+build.1", "\"=1.0.0+build.2\"", ConstraintMatch.Match)]
    [InlineData("1.0.0", "\"1.0.0+\"", ConstraintMatch.Unknown)]
    [InlineData("1.0.0", "\"1.0.0-\"", ConstraintMatch.Unknown)]
    public void QuiltOrderingFollowsItsNativeComparatorIncludingLeadingZeroPrereleaseTokens(string actual,string expression,ConstraintMatch expected)
        =>Assert.Equal(expected,QuiltVersionConstraints.Match(actual,expression));
    // Fixtures use Quilt Loader 0.30.1 StandardQuiltPlugin/OptionalModIdDefintion/QuiltRuleDepOnly
    // and Forge 1.21.1 / NeoForge JarSelector + locator contracts. No fixture JAR is executed.
    private static byte[] Archive(Dictionary<string, byte[]> entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            foreach (var e in entries) { var entry = zip.CreateEntry(e.Key); entry.LastWriteTime = new DateTimeOffset(2024,1,1,0,0,0,TimeSpan.Zero); using var stream = entry.Open(); stream.Write(e.Value); }
        return memory.ToArray();
    }
    private static byte[] Quilt(string id, string version = "1.0.0", string depends = "[]", string breaks = "[]", string provides = "[]",
        string? type = null, string environment = "*", Dictionary<string, byte[]>? children = null, string extra = "")
    {
        children ??= new();
        var jars = JsonSerializer.Serialize(children.Keys.ToArray());
        var metadata = "{\"schema_version\":1,\"quilt_loader\":{\"id\":\""+id+"\",\"group\":\"test.mods\",\"version\":\""+version+"\",\"intermediate_mappings\":\"net.fabricmc:intermediary\",\"depends\":"+depends+",\"breaks\":"+breaks+",\"provides\":"+provides+",\"jars\":"+jars+
            (type is null ? "" : ",\"load_type\":\""+type+"\"")+extra+"},\"minecraft\":{\"environment\":\""+environment+"\"}}";
        var entries = new Dictionary<string, byte[]>(children) { ["quilt.mod.json"] = Encoding.UTF8.GetBytes(metadata) };
        return Archive(entries);
    }
    private sealed record Embedded(string Path, byte[] Bytes, string Group = "test.library", string Artifact = "shared", string Version = "1.0.0", string Range = "[1,3)", bool Obfuscated = false);
    private static byte[] Forge(ModLoader loader, string? id, string modVersion = "1.0.0", string? dependency = null, IReadOnlyList<Embedded>? children = null, string? rawMetadata = null, string module = "test.library.shared", string? extraEntry = null)
    {
        children ??= []; var entries = new Dictionary<string, byte[]>();
        if (id is not null)
        {
            var content = "modLoader=\"javafml\"\nloaderVersion=\""+(loader==ModLoader.Forge?"[52,53)":"[4,5)")+"\"\nlicense=\"MIT\"\n[[mods]]\nmodId=\""+id+"\"\nversion=\""+modVersion+"\"\n";
            if (dependency is not null) content += "[[dependencies."+id+"]]\nmodId=\""+dependency+"\"\n"+(loader==ModLoader.Forge?"mandatory=true":"type=\"required\"")+"\nversionRange=\"[9,10)\"\n";
            entries[loader==ModLoader.Forge?"META-INF/mods.toml":"META-INF/neoforge.mods.toml"] = Encoding.UTF8.GetBytes(content);
        }
        else entries["META-INF/MANIFEST.MF"] = Encoding.UTF8.GetBytes("Manifest-Version: 1.0\r\nFMLModType: LIBRARY\r\nAutomatic-Module-Name: "+module+"\r\n\r\n");
        if (children.Count > 0 || rawMetadata is not null)
        {
            entries["META-INF/jarjar/metadata.json"] = Encoding.UTF8.GetBytes(rawMetadata ?? JsonSerializer.Serialize(new { jars = children.Select(c => new {
                identifier = new { group = c.Group, artifact = c.Artifact }, version = new { artifactVersion = c.Version, range = c.Range }, path = c.Path, isObfuscated = c.Obfuscated
            }) }));
            foreach (var c in children) entries.Add(c.Path, c.Bytes);
        }
        if (extraEntry is not null) entries.Add(extraEntry, "different fixture bytes"u8.ToArray());
        return Archive(entries);
    }
    private static InstallationReceipt Receipt(GameProfile p) => new(p.MinecraftVersion,21,"fixture.Main",DateTimeOffset.UtcNow,[],[],[],[],[],"fixture",p.Loader,
        p.Loader==ModLoader.Quilt?"0.30.1":p.Loader==ModLoader.Forge?"52.0.1":"21.1.1",
        Forge:p.Loader is ModLoader.Forge or ModLoader.NeoForge ? new(1,"fixture",p.Loader==ModLoader.Forge?"net.minecraftforge:fmlloader:1.21.1-52.0.1":"net.neoforged.fancymodloader:loader:4.0.42",Guid.NewGuid().ToString("N"),new("fixture","","SHA1",0),[]) : null);
    private static async Task<GameProfile> Profile(TestWorkspace ws, ModLoader loader)
    { await ws.Store.InitializeAsync(); var p=ws.Profile(loader); await ws.Store.SaveProfileAsync(p); await AtomicFile.WriteJsonAsync(ws.Paths.Receipt(p.Id),Receipt(p)); await File.WriteAllTextAsync(SafePath.Join(ws.Paths.Game(p.Id),"saves/synthetic-world.txt"),"NOT A REAL WORLD"); return p; }
    private static async Task<ModCartItem> Item(TestWorkspace ws, string name, byte[] bytes)
    { var path=SafePath.Join(ws.Paths.Root,name+".jar");await File.WriteAllBytesAsync(path,bytes);return new(path,await new LocalModManager(ws.Paths,ws.Store).InspectAsync(path)); }
    private static NativeModSelection Select(GameProfile p,params ModCartItem[] items) => NativeNestedResolver.Analyze(p,Receipt(p),items.Select(i=>i.Inspection).ToArray());

    [Fact] public async Task QuiltRequiredNestedCandidateResolvesOfflineWithoutInventingProvidesOrExtractingJars()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws,ModLoader.Quilt);
        var source=Quilt("parent",depends:"[\"bundled\"]",children:new() { ["META-INF/jars/child.jar"]=Quilt("bundled") });
        var item=await Item(ws,"parent",source);Assert.Empty(item.Inspection.ProvidedMods!);Assert.Single(item.Inspection.Nested!.Candidates);
        var manager=new LocalModManager(ws.Paths,ws.Store);var review=await manager.ReviewAsync(p,[item]);Assert.Empty(review.Blockers);Assert.True(review.NativeSelection!.Confirmed);Assert.Single(review.NativeSelection.Artifacts);
        await manager.InstallAsync(p,[item],true);var report=await new InstalledModReadiness(ws.Paths,manager).CheckAsync(p,Receipt(p));Assert.Empty(report.Issues);Assert.Equal(1,report.VerifiedFiles);Assert.Equal(2,report.NativeSelection!.ActiveMods.Count);
        Assert.Single(Directory.GetFiles(SafePath.Join(ws.Paths.Game(p.Id),"mods")));Assert.False(Directory.Exists(SafePath.Join(ws.Paths.Cache,"META-INF")));
        Assert.Equal(source,await File.ReadAllBytesAsync(item.SourcePath));Assert.Equal("NOT A REAL WORLD",await File.ReadAllTextAsync(SafePath.Join(ws.Paths.Game(p.Id),"saves/synthetic-world.txt")));
    }
    [Theory][InlineData(null,0)][InlineData("if_required",0)][InlineData("if_possible",1)][InlineData("always",1)]
    public async Task QuiltLoadTypesDoNotMakeEveryDiscoveredJarActive(string? loadType,int count)
    { using var ws=new TestWorkspace();var p=await Profile(ws,ModLoader.Quilt);var root=await Item(ws,"parent",Quilt("parent",children:new() { ["lib.jar"]=Quilt("library",type:loadType) }));var result=Select(p,root);Assert.True(result.Confirmed);Assert.Equal(count,result.Artifacts.Count); }
    [Fact] public async Task QuiltIfPossibleCanBeExcludedByAConflict()
    { using var ws=new TestWorkspace();var p=await Profile(ws,ModLoader.Quilt);var root=await Item(ws,"parent",Quilt("parent",breaks:"[\"library\"]",children:new() { ["lib.jar"]=Quilt("library",type:"if_possible") }));var result=Select(p,root);Assert.True(result.Confirmed);Assert.Empty(result.Artifacts); }
    [Fact] public async Task QuiltAlwaysAppliesToTheWholeIdGroupAndVersionPreferenceIsLoaderSpecific()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws,ModLoader.Quilt);
        var root=await Item(ws,"parent",Quilt("parent",depends:"[{\"id\":\"library\",\"versions\":\"=1.0.0\"}]",children:new() {
            ["old.jar"]=Quilt("library","1.0.0"),["new.jar"]=Quilt("library","2.0.0",type:"always") }));
        var constrained=Select(p,root);Assert.True(constrained.Confirmed);Assert.Equal("1.0.0",Assert.Single(constrained.Artifacts).Version);
        var other=await Item(ws,"newer",Quilt("newer",depends:"[\"library\"]",children:new() { ["old.jar"]=Quilt("library","1.0.0"),["new.jar"]=Quilt("library","2.0.0") }));
        Assert.Equal("2.0.0",Assert.Single(Select(p,other).Artifacts).Version);
    }
    [Fact] public async Task QuiltOptionalDependencyUsesDiscoveredValidOptionsAndUnlessCanActivateAReplacement()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws,ModLoader.Quilt);
        var optional=await Item(ws,"optional",Quilt("optional",depends:"[{\"id\":\"library\",\"optional\":true}]",children:new() { ["lib.jar"]=Quilt("library") }));
        Assert.Single(Select(p,optional).Artifacts); // QuiltRuleDepOnly: discovered valid options participate even for optional dependencies.
        var conditional=await Item(ws,"conditional",Quilt("conditional",depends:"[{\"id\":\"absent\",\"unless\":\"compatibility\"}]",children:new() { ["compat.jar"]=Quilt("compatibility") }));
        Assert.Equal("test.mods:compatibility",Assert.Single(Select(p,conditional).Artifacts).Identity);
    }
    [Fact] public async Task QuiltOptionalIncompatibleCandidateCanRemainAbsentAndServerOnlyOptionsDoNotBecomeClientMods()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws,ModLoader.Quilt);
        var root=await Item(ws,"parent",Quilt("parent",depends:"[{\"id\":\"library\",\"versions\":\"=2.0.0\",\"optional\":true},\"server_lib\"]",children:new() {
            ["wrong.jar"]=Quilt("library","1.0.0"),["server.jar"]=Quilt("server_lib",environment:"dedicated_server") }));
        var result=Select(p,root);Assert.True(result.Confirmed);Assert.Empty(result.Artifacts);
    }
    [Fact] public async Task QuiltOptionalQualifiedDependencyAlsoExcludesWrongGroupCandidates()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws,ModLoader.Quilt);
        var item=await Item(ws,"parent",Quilt("parent",depends:"[{\"id\":\"other.group:library\",\"optional\":true}]",children:new() { ["lib.jar"]=Quilt("library",type:"if_possible") }));
        var result=Select(p,item);Assert.True(result.Confirmed);Assert.Empty(result.Artifacts); // QuiltRuleDepOnly invalidOptions includes same ID with wrong group.
    }
    [Fact] public async Task QuiltRootIdentityWinsAndNestedProvidesCanSatisfyQualifiedDependencies()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws,ModLoader.Quilt);
        var root=await Item(ws,"parent",Quilt("parent",depends:"[\"library\"]",children:new() { ["lib.jar"]=Quilt("library","2.0.0") }));
        var direct=await Item(ws,"direct",Quilt("library","1.0.0",type:"if_required"));var result=Select(p,root,direct);Assert.True(result.Confirmed);Assert.Empty(result.Artifacts);Assert.Contains(result.ActiveMods,m=>m.Id=="library"&&m.Version=="1.0.0");
        var aliases=await Item(ws,"aliases",Quilt("aliases",depends:"[{\"id\":\"api.group:api_alias\",\"versions\":\"=9.0.0\"}]",children:new() { ["provided.jar"]=Quilt("provider",provides:"[{\"id\":\"api.group:api_alias\",\"version\":\"9.0.0\"}]") }));
        Assert.True(Select(p,aliases).Confirmed);Assert.Equal("test.mods:provider",Assert.Single(Select(p,aliases).Artifacts).Identity);
    }
    [Fact] public async Task QuiltDiscoveryIncludesGrandchildrenEvenIfTheirContainerIsNotSelectedAndIdenticalCopiesAreEquivalent()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws,ModLoader.Quilt);var child=Quilt("library");var container=Quilt("unused_container",children:new() { ["child.jar"]=child });
        var root=await Item(ws,"parent",Quilt("parent",depends:"[\"library\"]",children:new() { ["container.jar"]=container,["copy.jar"]=child }));
        var result=Select(p,root);Assert.True(result.Confirmed);Assert.Single(result.Artifacts);Assert.DoesNotContain(result.ActiveMods,m=>m.Id=="unused_container");
    }
    [Fact] public async Task QuiltUnsatisfiedAndAmbiguousAssignmentsAreReportedWithoutMutatingProfiles()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws,ModLoader.Quilt);
        var bad=await Item(ws,"bad",Quilt("bad",depends:"[{\"id\":\"library\",\"versions\":\"=3.0.0\"}]",children:new() { ["lib.jar"]=Quilt("library") }));
        var manager=new LocalModManager(ws.Paths,ws.Store);Assert.Contains((await manager.ReviewAsync(p,[bad])).Blockers,b=>b.Contains("library"));await Assert.ThrowsAsync<InvalidOperationException>(()=>manager.InstallAsync(p,[bad],true));
        var ambiguous=await Item(ws,"ambiguous",Quilt("ambiguous",depends:"[[\"left_lib\",\"right_lib\"]]",children:new() { ["left.jar"]=Quilt("left_lib"),["right.jar"]=Quilt("right_lib") }));
        var result=Select(p,ambiguous);Assert.False(result.Confirmed);Assert.Contains(result.Issues,i=>i.Code.Contains("unknown")&&i.Message.Contains("minimum-weight"));
        Assert.Empty(Directory.GetFiles(SafePath.Join(ws.Paths.Game(p.Id),"mods")));Assert.Equal("NOT A REAL WORLD",await File.ReadAllTextAsync(SafePath.Join(ws.Paths.Game(p.Id),"saves/synthetic-world.txt")));
    }
    [Theory][InlineData("custom_type")][InlineData("breaks_unless")][InlineData("extension")][InlineData("same_version_bytes")]
    public async Task QuiltUnconfirmedSemanticsStayUnknown(string mode)
    {
        using var ws=new TestWorkspace();var p=await Profile(ws,ModLoader.Quilt);
        var child=Quilt("library",type:mode=="custom_type"?"plugin_type":null,extra:mode=="same_version_bytes"?",\"metadata\":{\"name\":\"different\"}":"");
        var children=new Dictionary<string,byte[]> { ["child.jar"]=child };if(mode=="same_version_bytes")children["other.jar"]=Quilt("library");
        var root=await Item(ws,"parent",Quilt("parent",depends:mode=="extension"?"[{\"id\":\"library\",\"custom_rule\":true}]":"[\"library\"]",breaks:mode=="breaks_unless"?"[{\"id\":\"library\",\"unless\":\"compat\"}]":"[]",children:children));
        var result=Select(p,root);Assert.False(result.Confirmed);Assert.Contains(result.Issues,i=>i.Code.Contains("unknown"));
    }
    [Fact] public async Task QuiltSemanticVersionTiesDoNotTurnDiscoveryOrderIntoAPinnedChoice()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws,ModLoader.Quilt);
        var item=await Item(ws,"parent",Quilt("parent",depends:"[\"library\"]",children:new() { ["one.jar"]=Quilt("library","1.0.0+build.1"),["two.jar"]=Quilt("library","1.0.0+build.2") }));
        Assert.Contains(Select(p,item).Issues,i=>i.Code.Contains("unknown")&&i.Message.Contains("Semantically equal"));
    }
    [Theory][InlineData(19)][InlineData(65)]
    public async Task QuiltCandidateAndSearchLimitsStayUnknown(int count)
    { using var ws=new TestWorkspace();var p=await Profile(ws,ModLoader.Quilt);var children=Enumerable.Range(0,count).ToDictionary(i=>"lib"+i+".jar",i=>Quilt("library_"+i));var root=await Item(ws,"parent",Quilt("parent",children:children));Assert.Contains(Select(p,root).Issues,i=>i.Code.Contains("unknown")&&i.Message.Contains(count==65?"64":"18")); }
    [Fact] public async Task NativeDepthLimitStaysUnknownAndDoesNotExtractAnything()
    { using var ws=new TestWorkspace();var p=await Profile(ws,ModLoader.Quilt);var bytes=Quilt("leaf");for(int i=0;i<5;i++)bytes=Quilt("container_"+i,children:new() { ["child.jar"]=bytes });var item=await Item(ws,"deep",bytes);Assert.Contains(Select(p,item).Issues,i=>i.Code.Contains("unknown")&&i.Message.Contains("depth"));Assert.Empty(Directory.GetFiles(ws.Paths.Cache,"*.jar",SearchOption.AllDirectories)); }
    [Theory][InlineData(ModLoader.Forge)][InlineData(ModLoader.NeoForge)]
    public async Task JarJarArtifactIdentityIsSeparateFromModIdAndVersion(ModLoader loader)
    {
        using var ws=new TestWorkspace();var p=await Profile(ws,loader);var nested=Forge(loader,"nested_mod","9.0.0");
        var item=await Item(ws,"parent",Forge(loader,"parent",dependency:"nested_mod",children:[new("META-INF/jarjar/library.jar",nested,Artifact:"not_a_mod_id",Version:"2.0.0",Range:"[2,3)")]));
        var result=Select(p,item);Assert.True(result.Confirmed);Assert.Equal("test.library:not_a_mod_id",Assert.Single(result.Artifacts).Identity);Assert.Contains(result.ActiveMods,m=>m.Id=="nested_mod"&&m.Version=="9.0.0");Assert.DoesNotContain(result.ActiveMods,m=>m.Id=="not_a_mod_id");
        var manager=new LocalModManager(ws.Paths,ws.Store);await manager.InstallAsync(p,[item],true);var report=await new InstalledModReadiness(ws.Paths,manager).CheckAsync(p,Receipt(p));Assert.Empty(report.Issues);Assert.Single(Directory.GetFiles(SafePath.Join(ws.Paths.Game(p.Id),"mods")));
    }
    [Theory][InlineData(ModLoader.Forge)][InlineData(ModLoader.NeoForge)]
    public async Task JarJarPlainLibrariesAreNotModsAndIdenticalCopiesAreEquivalent(ModLoader loader)
    {
        using var ws=new TestWorkspace();var p=await Profile(ws,loader);var library=Forge(loader,null);
        var first=await Item(ws,"first",Forge(loader,"first",children:[new("lib.jar",library)]));var second=await Item(ws,"second",Forge(loader,"second",children:[new("other.jar",library)]));
        var result=Select(p,first,second);Assert.True(result.Confirmed);Assert.True(Assert.Single(result.Artifacts).Library);Assert.Equal(2,result.ActiveMods.Count);Assert.Null(first.Inspection.Nested!.Candidates[0].Inspection.Id);
    }
    [Theory][InlineData(ModLoader.Forge)][InlineData(ModLoader.NeoForge)]
    public async Task JarJarUsesAllRangesAndReportsMultipleEligibleArtifactsWithoutGuessingNewest(ModLoader loader)
    {
        using var ws=new TestWorkspace();var p=await Profile(ws,loader);var a=Forge(loader,null,module:"library.one");var b=Forge(loader,null,module:"library.two");
        var first=await Item(ws,"first",Forge(loader,"first",children:[new("a.jar",a,Version:"1.0.0",Range:"[1,3)")]));
        var narrow=await Item(ws,"narrow",Forge(loader,"narrow",children:[new("b.jar",b,Version:"2.0.0",Range:"[2,3)")]));
        Assert.Equal("2.0.0",Assert.Single(Select(p,first,narrow).Artifacts).Version);
        var broad=await Item(ws,"broad",Forge(loader,"broad",children:[new("b.jar",b,Version:"2.0.0",Range:"[1,3)")]));
        Assert.Contains(Select(p,first,broad).Issues,i=>i.Code=="mods.nested.ambiguous.unknown");
        var incompatible=await Item(ws,"incompatible",Forge(loader,"incompatible",children:[new("b.jar",b,Version:"2.0.0",Range:"[2,3)")]));
        var restrictive=await Item(ws,"restrictive",Forge(loader,"restrictive",children:[new("a.jar",a,Range:"[1,2)")]));Assert.Contains(Select(p,restrictive,incompatible).Issues,i=>i.Code=="mods.nested.unsatisfied");
    }
    [Theory][InlineData(ModLoader.Forge)][InlineData(ModLoader.NeoForge)]
    public async Task JarJarRootModIdentityOverridesNestedAndTransitiveDiscoveryIsIndependentOfContainerChoice(ModLoader loader)
    {
        using var ws=new TestWorkspace();var p=await Profile(ws,loader);var child=Forge(loader,"nested_mod","9.0.0");var container=Forge(loader,null,children:[new("child.jar",child,Artifact:"child")]);
        var parent=await Item(ws,"parent",Forge(loader,"parent",dependency:"nested_mod",children:[new("container.jar",container)]));var direct=await Item(ws,"direct",Forge(loader,"nested_mod","9.1.0"));
        var result=Select(p,parent,direct);Assert.True(result.Confirmed);Assert.Equal(2,result.ActiveMods.Count);Assert.DoesNotContain(result.Artifacts,a=>!a.Library);Assert.Single(result.Artifacts);
    }
    [Theory][InlineData("malformed")][InlineData("unsupported_range")][InlineData("obfuscated")][InlineData("missing_module_identity")]
    public async Task JarJarUnsupportedDescriptorsHaveSpecificUnknownReasons(string mode)
    {
        using var ws=new TestWorkspace();var p=await Profile(ws,ModLoader.NeoForge);var bytes=mode=="missing_module_identity"?Archive(new() { ["fixture.txt"]="fixture"u8.ToArray() }):Forge(p.Loader,null);
        var item=await Item(ws,"parent",Forge(p.Loader,"parent",children:[new("lib.jar",bytes,Range:mode=="unsupported_range"?"[1-nightly,2)":"[1,3)",Obfuscated:mode=="obfuscated")],rawMetadata:mode=="malformed"?"{broken":null));
        var result=Select(p,item);Assert.False(result.Confirmed);Assert.Contains(result.Issues,i=>i.Code.Contains("unknown"));
    }
    [Theory][InlineData(ModLoader.Quilt)][InlineData(ModLoader.Forge)][InlineData(ModLoader.NeoForge)]
    public async Task NativeUnsafePathsAndArchiveSymlinksFailClosedWithoutWriting(ModLoader loader)
    {
        using var ws=new TestWorkspace();var manager=new LocalModManager(ws.Paths,ws.Store);
        var invalid=loader==ModLoader.Quilt?Quilt("parent",children:new() { ["../escape.jar"]=Quilt("child") }):Forge(loader,"parent",children:[new("../escape.jar",Forge(loader,null))]);
        var path=SafePath.Join(ws.Paths.Root,"unsafe.jar");await File.WriteAllBytesAsync(path,invalid);await Assert.ThrowsAsync<InvalidDataException>(()=>manager.InspectAsync(path));
        using var m=new MemoryStream();using(var z=new ZipArchive(m,ZipArchiveMode.Create,true)){var e=z.CreateEntry("link");e.ExternalAttributes=unchecked((int)0xA0000000);}
        var linked=loader==ModLoader.Quilt?Quilt("parent",children:new() { ["linked.jar"]=m.ToArray() }):Forge(loader,"parent",children:[new("linked.jar",m.ToArray())]);
        await File.WriteAllBytesAsync(path,linked);await Assert.ThrowsAsync<InvalidDataException>(()=>manager.InspectAsync(path));Assert.Single(Directory.GetFiles(ws.Paths.Root,"*.jar"));Assert.Empty(Directory.GetFiles(ws.Paths.Cache,"*",SearchOption.AllDirectories));
    }
    [Theory][InlineData(ModLoader.Quilt)][InlineData(ModLoader.Forge)][InlineData(ModLoader.NeoForge)]
    public async Task NativeResolutionIsDeterministicAcrossRootOrderAndIncludesTheInstalledSet(ModLoader loader)
    {
        using var ws=new TestWorkspace();var p=await Profile(ws,loader);
        byte[] Child()=>loader==ModLoader.Quilt?Quilt("library"):Forge(loader,null);
        byte[] Root(string id,byte[] lib)=>loader==ModLoader.Quilt?Quilt(id,depends:"[\"library\"]",children:new() { ["child.jar"]=lib }):Forge(loader,id,children:[new("lib.jar",lib)]);
        var child=Child();var a=await Item(ws,"first",Root("first",child));var b=await Item(ws,"second",Root("second",child));
        Assert.Equal(JsonSerializer.Serialize(Select(p,a,b)),JsonSerializer.Serialize(Select(p,b,a)));
        var manager=new LocalModManager(ws.Paths,ws.Store);await manager.InstallAsync(p,[a],true);var preview=await manager.ReviewAsync(p,[b]);Assert.Empty(preview.Blockers);Assert.True(preview.NativeSelection!.Confirmed);
        using var stop=new CancellationTokenSource();stop.Cancel();Assert.Throws<OperationCanceledException>(()=>NativeNestedResolver.Analyze(p,Receipt(p),[a.Inspection],stop.Token));
    }
    [Fact] public async Task ForeignNestedMetadataCannotBecomeNativeSupportOrACrossLoaderBridge()
    {
        using var ws=new TestWorkspace();var foreign=Archive(new() { ["fabric.mod.json"]=Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"fabric_only\",\"version\":\"1.0.0\"}") });
        foreach(var loader in new[] { ModLoader.Quilt, ModLoader.Forge, ModLoader.NeoForge })
        {
            var p=await Profile(ws,loader);
            var bytes=loader==ModLoader.Quilt?Quilt("parent",children:new() { ["foreign.jar"]=foreign }):Forge(loader,"parent",children:[new("foreign.jar",foreign)]);
            var item=await Item(ws,"parent_"+loader,bytes);var result=Select(p,item);
            Assert.False(result.Confirmed);Assert.Contains(result.Issues,i=>i.Code=="mods.nested.loader.unknown");
        }
        Assert.False(NativeNestedResolver.Analyze(ws.Profile(ModLoader.Fabric),null,[]).Confirmed);
    }
    [Fact] public async Task NativeSelectionAndDiagnosticsReachTheCombinedCartOfflineWithoutAnyHttp()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws,ModLoader.Quilt);
        var item=await Item(ws,"parent",Quilt("parent",depends:"[\"library\"]",children:new() { ["child.jar"]=Quilt("library") }));
        using var handler=new StubHandler(_=>throw new InvalidOperationException("Offline analysis must not issue HTTP."));using var http=new HttpClient(handler);
        var network=new NetworkPolicy { Offline=true };var downloads=new VerifiedDownloader(http,network);var catalog=new ModrinthClient(ws.Paths,downloads,network);var manager=new LocalModManager(ws.Paths,ws.Store);
        var remote=new RemoteModInstaller(ws.Paths,ws.Store,catalog,downloads,manager);
        var plan=await remote.PreviewLocalAsync(p,[item]);Assert.True(plan.CanInstall);Assert.Contains(plan.Warnings,w=>w.Contains("test.mods:library"));Assert.Equal(0,handler.Requests);
        var oldReceipt=await File.ReadAllBytesAsync(ws.Paths.Receipt(p.Id));await ws.Store.SetSettingAsync("ui.language","uk");
        var other=ws.Profile();await ws.Store.SaveProfileAsync(other);var sentinel=SafePath.Join(ws.Paths.Game(other.Id),"config/sentinel.txt");await File.WriteAllTextAsync(sentinel,"OTHER PROFILE");
        await remote.InstallLocalAsync(p,plan,true,true);
        Assert.Equal(oldReceipt,await File.ReadAllBytesAsync(ws.Paths.Receipt(p.Id)));Assert.Equal("uk",await ws.Store.GetSettingAsync("ui.language"));Assert.Equal("OTHER PROFILE",await File.ReadAllTextAsync(sentinel));Assert.Equal(0,handler.Requests);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task QuiltOverridesCannotSilentlyChangeNestedDecisions(bool populated)
    {
        using var ws=new TestWorkspace();var p=await Profile(ws,ModLoader.Quilt);
        var item=await Item(ws,"parent",Quilt("parent",depends:"[\"library\"]",children:new() { ["child.jar"]=Quilt("library") }));
        var file=SafePath.Join(ws.Paths.Game(p.Id),"config/quilt-loader-overrides.json");
        var text=populated?"{\"schema_version\":1,\"overrides\":[{\"id\":\"library\",\"version\":\"99.0.0\"}]}":"{\"schema_version\":1,\"overrides\":[]}";
        await File.WriteAllTextAsync(file,text);var manager=new LocalModManager(ws.Paths,ws.Store);var review=await manager.ReviewAsync(p,[item]);Assert.Equal(!populated,review.NativeSelection!.Confirmed);
        if(populated)Assert.Contains(review.Warnings,w=>w.Contains("overrides"));
        await manager.InstallAsync(p,[item],true);var report=await new InstalledModReadiness(ws.Paths,manager).CheckAsync(p,Receipt(p));Assert.Equal(!populated,report.NativeSelection!.Confirmed);
        Assert.Equal(text,await File.ReadAllTextAsync(file));
    }
    [Fact] public async Task BoundedSearchDoesNotReturnAnEarlyPartialSolutionAsConfirmed()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws,ModLoader.Quilt);
        var children=Enumerable.Range(0,18).ToDictionary(i=>"lib"+i+".jar",i=>Quilt("library_"+i));var root=await Item(ws,"parent",Quilt("parent",children:children));
        var result=Select(p,root);Assert.False(result.Confirmed);Assert.Contains(result.Issues,i=>i.Message.Contains("65,536"));Assert.Empty(result.Artifacts);
    }
    [Fact] public async Task NestedZipBombAndMissingDeclaredFileCannotBeAccepted()
    {
        using var ws=new TestWorkspace();var manager=new LocalModManager(ws.Paths,ws.Store);
        var bomb=Archive(new() { ["huge.txt"]=new byte[4*1024*1024] });var file=SafePath.Join(ws.Paths.Root,"bomb.jar");await File.WriteAllBytesAsync(file,Quilt("parent",children:new() { ["bomb.jar"]=bomb }));
        await Assert.ThrowsAsync<InvalidDataException>(()=>manager.InspectAsync(file));
        var missing=Forge(ModLoader.Forge,"parent",rawMetadata:"{\"jars\":[{\"identifier\":{\"group\":\"test\",\"artifact\":\"library\"},\"version\":{\"range\":\"[1,2)\",\"artifactVersion\":\"1.0.0\"},\"path\":\"missing.jar\"}]}");
        await File.WriteAllBytesAsync(file,missing);await Assert.ThrowsAsync<InvalidDataException>(()=>manager.InspectAsync(file));Assert.Empty(Directory.GetFiles(ws.Paths.Cache,"*",SearchOption.AllDirectories));
    }
    [Fact] public async Task MalformedNestedTomlStaysUnknownAndDifferentLibraryModuleIdentitiesAreNotInvented()
    {
        using var ws=new TestWorkspace();var p=await Profile(ws,ModLoader.NeoForge);
        var malformed=Archive(new() { ["META-INF/neoforge.mods.toml"]=Encoding.UTF8.GetBytes("modLoader=\"unterminated") });
        var root=await Item(ws,"malformed",Forge(p.Loader,"parent",children:[new("bad.jar",malformed)]));Assert.Contains(Select(p,root).Issues,i=>i.Code.Contains("unknown"));
        var first=Forge(p.Loader,null,module:"shared.identity");var second=Forge(p.Loader,null,module:"shared.identity",extraEntry:"other.txt");
        var conflict=await Item(ws,"module_conflict",Forge(p.Loader,"module_conflict",children:[new("one.jar",first,Artifact:"first"),new("two.jar",second,Artifact:"second")]));
        Assert.Contains(Select(p,conflict).Issues,i=>i.Code=="mods.nested.identity.unknown");
    }
}
