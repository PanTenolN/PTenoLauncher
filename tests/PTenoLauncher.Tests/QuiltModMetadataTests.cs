using System.IO.Compression;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;
namespace PTenoLauncher.Tests;

public sealed class QuiltModMetadataTests
{
    private static void Jar(string path, string id = "example", string version = "1.0.0", string depends = "[]",
        string breaks = "[]", string provides = "[]", string environment = "*", string group = "test.mods",
        int schema = 1, string extra = "", byte[]? nested = null)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("quilt.mod.json").Open()))
            writer.Write($$$"""{"schema_version":{{{schema}}},"quilt_loader":{"id":"{{{id}}}","group":"{{{group}}}","version":"{{{version}}}","intermediate_mappings":"net.fabricmc:intermediary","metadata":{"name":"Quilt example"},"depends":{{{depends}}},"breaks":{{{breaks}}},"provides":{{{provides}}}{{{extra}}}},"minecraft":{"environment":"{{{environment}}}"}}""");
        if (nested is not null) { using var output = zip.CreateEntry("META-INF/jars/child.jar").Open(); output.Write(nested); }
    }
    private static InstallationReceipt Receipt(GameProfile p) => new(p.MinecraftVersion, 21, "org.quiltmc.loader.impl.launch.knot.KnotClient", DateTimeOffset.UtcNow, [], [], [], [], [], "test", ModLoader.Quilt, "0.30.1");
    private static async Task<GameProfile> Profile(TestWorkspace ws)
    {
        await ws.Store.InitializeAsync(); var p = ws.Profile(ModLoader.Quilt); await ws.Store.SaveProfileAsync(p);
        await AtomicFile.WriteJsonAsync(ws.Paths.Receipt(p.Id), Receipt(p)); return p;
    }
    private static Task<InstalledModReport> Check(TestWorkspace ws, GameProfile p) => new InstalledModReadiness(ws.Paths, new(ws.Paths, ws.Store)).CheckAsync(p, Receipt(p));
    private static async Task<ModCartItem> Item(TestWorkspace ws, string name, string depends = "[]", string breaks = "[]",
        string provides = "[]", string version = "1.0.0", string group = "test.mods", string environment = "*")
    {
        var path = Path.Combine(ws.Paths.Root, name + ".jar"); Jar(path, name, version, depends, breaks, provides, environment, group);
        return new(path, await new LocalModManager(ws.Paths, ws.Store).InspectAsync(path));
    }
    [Theory]
    [InlineData("1.7.2", "\"1.0.0\"", ConstraintMatch.Match)]
    [InlineData("2.0.0", "\"1.0.0\"", ConstraintMatch.Mismatch)]
    [InlineData("0.30.1", "\"^0.19\"", ConstraintMatch.Match)]
    [InlineData("1.0.0-beta.1", "\"^0.19\"", ConstraintMatch.Match)]
    [InlineData("1.1.0-beta.1", "\"~1.0.0\"", ConstraintMatch.Match)]
    [InlineData("1.0.1", "\"=1.0.0\"", ConstraintMatch.Mismatch)]
    [InlineData("1.21.1", "\"1.21.x\"", ConstraintMatch.Match)]
    [InlineData("1.22.0", "\"1.21.x\"", ConstraintMatch.Mismatch)]
    [InlineData("1.21.1", "{\"all\":[\">=1.21\",\"<1.22\"]}", ConstraintMatch.Match)]
    [InlineData("1.21.1", "{\"any\":[\"=1.20.1\",{\"all\":[\">=1.21\",\"<1.22\"]}]}", ConstraintMatch.Match)]
    [InlineData("1.21.1", "[\"=1.20.1\",\"=1.21.1\"]", ConstraintMatch.Match)]
    [InlineData("release-name", "\"release-name\"", ConstraintMatch.Match)]
    [InlineData("1.21.1", "\">=1.21 <1.22\"", ConstraintMatch.Unknown)]
    [InlineData("1.21.1", "{\"all\":[]}", ConstraintMatch.Unknown)]
    [InlineData("1.21.1", "{\"any\":[\"*\",false]}", ConstraintMatch.Unknown)]
    [InlineData("1.21.1", "{\"any\":[\"*\"],\"all\":[\"*\"]}", ConstraintMatch.Unknown)]
    [InlineData("1.21.1", "\"^2147483647\"", ConstraintMatch.Unknown)]
    public void QuiltUsesItsOwnVersionGrammar(string actual, string json, ConstraintMatch expected) => Assert.Equal(expected, QuiltVersionConstraints.Match(actual, json));

    [Fact] public async Task StaticInspectionReadsQuiltIdentityAliasesEnvironmentAndDependencies()
    {
        using var ws = new TestWorkspace(); var item = await Item(ws, "example", "[\"minecraft\",{\"id\":\"library\",\"optional\":true}]", provides:"[\"alias\",{\"id\":\"api.group:another_alias\",\"version\":\"2.0\"}]", environment:"dedicated_server");
        var mod = item.Inspection; Assert.Equal(ModLoader.Quilt, mod.Loader); Assert.Equal("example", mod.Id);
        Assert.Equal("Quilt example", mod.Name); Assert.Equal("server", mod.Environment); Assert.Equal("test.mods", mod.QuiltMetadata!.Group);
        Assert.Equal(["minecraft"], mod.RequiredDependencies); Assert.Empty(mod.MetadataWarnings!);
        Assert.Contains(mod.ProvidedMods!, a => a.Id == "another_alias" && a.Version == "2.0" && a.Group == "api.group");
        Assert.Single(Directory.GetFiles(ws.Paths.Root, "*.jar"));
    }
    [Fact] public async Task MissingDependencyBlocksBeforeCopyAndCompatibleCartInstallsWithIntegrityBaseline()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var mods = new LocalModManager(ws.Paths, ws.Store);
        var root = await Item(ws, "parent", "[{\"id\":\"library\",\"versions\":{\"all\":[\">=1.0\",\"<2\"]}}]");
        Assert.Contains((await mods.ReviewAsync(p, [root])).Details, b => b.Kind == ModBlockerKind.MissingDependency);
        await Assert.ThrowsAsync<InvalidOperationException>(() => mods.InstallAsync(p, [root], true));
        Assert.Empty(Directory.GetFiles(Path.Combine(ws.Paths.Game(p.Id), "mods")));
        var library = await Item(ws, "library", version:"1.5.0");
        var sentinel = Path.Combine(ws.Paths.Game(p.Id), "saves", "synthetic.txt"); await File.WriteAllTextAsync(sentinel, "world sentinel");
        await mods.InstallAsync(p, [root, library], true);
        var baseline = await File.ReadAllBytesAsync(new ModInventoryStore(ws.Paths).PathFor(p.Id));
        var result = await Check(ws, p); Assert.Empty(result.Issues); Assert.Equal(2, result.VerifiedFiles);
        Assert.Equal(baseline, await File.ReadAllBytesAsync(new ModInventoryStore(ws.Paths).PathFor(p.Id)));
        Assert.Equal("world sentinel", await File.ReadAllTextAsync(sentinel));
    }
    [Fact] public async Task MinecraftJavaAndPinnedQuiltLoaderConstraintsAreEnforced()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var item = await Item(ws, "example",
            "[{\"id\":\"minecraft\",\"versions\":\">=1.22\"},{\"id\":\"java\",\"versions\":\">=25\"},{\"id\":\"quilt_loader\",\"versions\":\">=0.31\"}]");
        var review = await new LocalModManager(ws.Paths, ws.Store).ReviewAsync(p, [item]);
        Assert.Equal(3, review.Blockers.Count); Assert.Contains(review.Blockers, b => b.Contains("quilt_loader"));
    }
    [Fact] public async Task OptionalDependencyMayBeAbsentButAnIncompatiblePresentVersionBlocks()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var mods = new LocalModManager(ws.Paths, ws.Store);
        var root = await Item(ws, "parent", "[{\"id\":\"library\",\"versions\":\">=2\",\"optional\":true}]");
        Assert.Empty((await mods.ReviewAsync(p, [root])).Blockers);
        var library = await Item(ws, "library");
        Assert.Contains((await mods.ReviewAsync(p, [root, library])).Blockers, b => b.Contains("library"));
    }
    [Fact] public async Task UnlessChecksVersionAndCanSuppressDependencyAndConflict()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var mods = new LocalModManager(ws.Paths, ws.Store);
        var root = await Item(ws, "parent", "[{\"id\":\"missing\",\"unless\":{\"id\":\"alternative\",\"versions\":\">=2\"}}]", "[{\"id\":\"minecraft\",\"unless\":\"alternative\"}]");
        Assert.Equal(2, (await mods.ReviewAsync(p, [root])).Blockers.Count);
        var alternative = await Item(ws, "alternative", version:"2.0.0");
        Assert.Empty((await mods.ReviewAsync(p, [root, alternative])).Blockers);
    }
    [Fact] public async Task QualifiedDependenciesRequireTheMatchingGroupAndProvidedAliasVersion()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var mods = new LocalModManager(ws.Paths, ws.Store);
        var root = await Item(ws, "parent", "[{\"id\":\"wanted.group:alias\",\"versions\":\">=2\"}]");
        var wrong = await Item(ws, "wrong", provides:"[\"alias\"]", group:"wrong.group");
        Assert.NotEmpty((await mods.ReviewAsync(p, [root, wrong])).Blockers);
        var right = await Item(ws, "right", provides:"[{\"id\":\"wanted.group:alias\",\"version\":\"2.3.0\"}]");
        Assert.Empty((await mods.ReviewAsync(p, [root, right])).Blockers);
    }
    [Fact] public async Task DependsAlternativesAreAnyAndBreaksAlternativesAreAll()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var mods = new LocalModManager(ws.Paths, ws.Store);
        var root = await Item(ws, "parent", "[[\"one\",\"two\"]]", "[[\"minecraft\",\"conflicting\"]]");
        var one = await Item(ws, "one"); Assert.Empty((await mods.ReviewAsync(p, [root, one])).Blockers);
        var conflicting = await Item(ws, "conflicting");
        Assert.Contains((await mods.ReviewAsync(p, [root, one, conflicting])).Details, b => b.Kind == ModBlockerKind.DependencyConflict);
    }
    [Fact] public async Task ServerOnlyModCannotSatisfyClientDependenciesAndFabricBridgeIsNotAssumed()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var mods = new LocalModManager(ws.Paths, ws.Store);
        var root = await Item(ws, "parent", "[\"library\"]"); var server = await Item(ws, "library", environment:"dedicated_server");
        Assert.NotEmpty((await mods.ReviewAsync(p, [root, server])).Blockers);
        var fabric = Path.Combine(ws.Paths.Game(p.Id), "mods", "fabric.jar");
        using (var zip = ZipFile.Open(fabric, ZipArchiveMode.Create))
        using (var output = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open())) output.Write("""{"schemaVersion":1,"id":"library","version":"1.0.0"}""");
        Assert.Contains((await Check(ws, p)).Issues, i => i.Code == "mods.loader.mismatch");
    }
    [Fact] public async Task UnsupportedSchemaAndDependencyExtensionKeepReadinessUnknown()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws);
        var file = Path.Combine(ws.Paths.Game(p.Id), "mods", "future.jar"); Jar(file, schema:99, depends:"[{\"id\":\"minecraft\",\"custom_constraint\":true}]");
        var mods = new LocalModManager(ws.Paths, ws.Store); var preview = await mods.PreviewBaselineAsync(p); await mods.ConfirmBaselineAsync(p, preview, true);
        var result = await Check(ws, p);
        Assert.Contains(result.Issues, i => i.Code == "mods.metadata.unknown"); Assert.Contains(result.Issues, i => i.Code == "mods.constraint.unknown");
        Assert.Equal(OfflineReadinessState.Unknown, new ReadinessReport(result.Issues, 0).State);
    }
    [Fact] public async Task ConditionalNestedModulesAreNotAssumedActiveAndNothingIsExtracted()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var child = Path.Combine(ws.Paths.Root, "child.jar"); Jar(child, "bundled");
        var file = Path.Combine(ws.Paths.Game(p.Id), "mods", "parent.jar");
        Jar(file, "parent", depends:"[\"bundled\"]", extra:",\"jars\":[\"META-INF/jars/child.jar\"]", nested:await File.ReadAllBytesAsync(child));
        var mods = new LocalModManager(ws.Paths, ws.Store); var info = await mods.InspectAsync(file);
        Assert.Empty(info.ProvidedMods!); Assert.Contains(info.MetadataWarnings!, w => w.Contains("JAR"));
        var result = await Check(ws, p); Assert.Contains(result.Issues, i => i.Code == "mods.metadata.unknown");
        Assert.Contains(result.Issues, i => i.Code == "mods.dependency.missing"); Assert.False(Directory.Exists(Path.Combine(ws.Paths.Cache, "META-INF")));
    }
    [Theory]
    [InlineData("../outside.jar")]
    [InlineData("/outside.jar")]
    [InlineData("META-INF/jars/missing.jar")]
    public async Task UnsafeOrMissingDeclaredNestedJarIsRejected(string name)
    {
        using var ws = new TestWorkspace(); var file = Path.Combine(ws.Paths.Root, "parent.jar");
        Jar(file, extra:",\"jars\":[" + JsonSerializer.Serialize(name) + "]");
        await Assert.ThrowsAsync<InvalidDataException>(() => new LocalModManager(ws.Paths, ws.Store).InspectAsync(file));
    }
    [Fact] public async Task ZipSlipInsideDeclaredNestedJarIsRejectedWithoutExtraction()
    {
        using var ws = new TestWorkspace(); using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true)) zip.CreateEntry("../outside.txt");
        var file = Path.Combine(ws.Paths.Root, "parent.jar"); Jar(file, extra:",\"jars\":[\"META-INF/jars/child.jar\"]", nested:memory.ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(() => new LocalModManager(ws.Paths, ws.Store).InspectAsync(file));
        Assert.False(File.Exists(Path.Combine(ws.Paths.Root, "outside.txt")));
    }
    [Fact] public async Task DuplicateMetadataFieldsAreRejectedInsteadOfChoosingOne()
    {
        using var ws = new TestWorkspace(); var file = Path.Combine(ws.Paths.Root, "duplicate.jar"); Jar(file, extra:",\"id\":\"another\"");
        await Assert.ThrowsAsync<InvalidDataException>(() => new LocalModManager(ws.Paths, ws.Store).InspectAsync(file));
    }
}
