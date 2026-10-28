using System.IO.Compression;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;
namespace PTenoLauncher.Tests;

public sealed class ForgeModMetadataTests
{
    private static string Metadata(string mods, string dependencies = "", string languageRange = "[52,)") =>
        "modLoader=\"javafml\"\nloaderVersion=\"" + languageRange + "\"\nlicense=\"MIT\"\n" + mods + "\n" + dependencies;
    private static string Mod(string id, string version = "1.0.0") => "[[mods]]\nmodId=\"" + id + "\"\nversion=\"" + version + "\"\ndisplayName='Mod # name'\n";
    private static string Dependency(string owner, string id, string range = "", string kind = "required", string order = "NONE", string side = "BOTH", bool neo = false) =>
        "[[dependencies." + owner + "]]\nmodId=\"" + id + "\"\n" + (neo ? "type=\"" + kind + "\"" : "mandatory=" + (kind == "required" ? "true" : "false")) +
        "\nversionRange=\"" + range + "\"\nordering=\"" + order + "\"\nside=\"" + side + "\"\n";
    private static async Task<ModCartItem> Item(TestWorkspace ws, string file, string content, ModLoader loader = ModLoader.Forge, string? manifest = null, bool jarjar = false)
    {
        var path = Path.Combine(ws.Paths.Root, file + ".jar");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry(loader == ModLoader.NeoForge ? "META-INF/neoforge.mods.toml" : "META-INF/mods.toml").Open())) writer.Write(content);
            if (manifest is not null) { using var text = new StreamWriter(zip.CreateEntry("META-INF/MANIFEST.MF").Open()); text.Write(manifest); }
            if (jarjar) { using var text = new StreamWriter(zip.CreateEntry("META-INF/jarjar/metadata.json").Open()); text.Write("{\"jars\":[]}"); }
        }
        return new(path, await new LocalModManager(ws.Paths, ws.Store).InspectAsync(path));
    }
    private static InstallationReceipt Receipt(GameProfile p) => new(p.MinecraftVersion, 21, "test.SyntheticMain", DateTimeOffset.UtcNow, [], [], [], [], [], "test", p.Loader, p.Loader == ModLoader.Forge ? "52.1.0" : "21.1.200");
    private static async Task<GameProfile> Profile(TestWorkspace ws, ModLoader loader = ModLoader.Forge)
    {
        await ws.Store.InitializeAsync(); var p = ws.Profile(loader); await ws.Store.SaveProfileAsync(p);
        await AtomicFile.WriteJsonAsync(ws.Paths.Receipt(p.Id), Receipt(p)); return p;
    }
    private static Task<InstalledModReport> Check(TestWorkspace ws, GameProfile p) => new InstalledModReadiness(ws.Paths, new(ws.Paths, ws.Store)).CheckAsync(p, Receipt(p));

    [Theory]
    [InlineData("1.5", "[1.0,2.0)", ConstraintMatch.Match)]
    [InlineData("2.0", "[1.0,2.0)", ConstraintMatch.Mismatch)]
    [InlineData("1.0", "(1.0,2.0]", ConstraintMatch.Mismatch)]
    [InlineData("1.0.0.0", "[1.0]", ConstraintMatch.Match)]
    [InlineData("1.0", "(,1.0]", ConstraintMatch.Match)]
    [InlineData("1.0", "(,1.0)", ConstraintMatch.Mismatch)]
    [InlineData("999999999999999999999", "[2,)", ConstraintMatch.Match)]
    [InlineData("1.5", "(,1.0],[1.5,)", ConstraintMatch.Match)]
    [InlineData("1.2", "(,1.0],[1.5,)", ConstraintMatch.Mismatch)]
    [InlineData("2.0", "1.0", ConstraintMatch.Match)]
    [InlineData("1.0-rc1", "[1.0,)", ConstraintMatch.Unknown)]
    [InlineData("1.0", "[1.0-beta,)", ConstraintMatch.Unknown)]
    [InlineData("1.0", "[2,1]", ConstraintMatch.Unknown)]
    [InlineData("1.0", "[1,1)", ConstraintMatch.Unknown)]
    [InlineData("1.0", "[1,2],[1.5,3]", ConstraintMatch.Unknown)]
    [InlineData("1.0", "[1,)", ConstraintMatch.Match)]
    [InlineData("1.0", ">=1", ConstraintMatch.Unknown)]
    [InlineData("1.0", "[1,]", ConstraintMatch.Unknown)]
    [InlineData("1.0", "[1],", ConstraintMatch.Unknown)]
    [InlineData("qualifier", "", ConstraintMatch.Match)]
    public void NumericMavenRangesDoNotUseFabricSemantics(string actual, string range, ConstraintMatch expected) => Assert.Equal(expected, MavenVersionConstraints.Match(actual, range));

    [Theory]
    [InlineData(ModLoader.Forge)]
    [InlineData(ModLoader.NeoForge)]
    public async Task MultiModJarKeepsEachIdentityVersionAndDependencyOwner(ModLoader loader)
    {
        using var ws = new TestWorkspace(); bool neo = loader == ModLoader.NeoForge;
        var file = await Item(ws, "multi", Metadata(Mod("first", "1.0") + Mod("second", "2.5"), Dependency("second", "library", "[3,)", neo:neo)), loader);
        var result = file.Inspection; Assert.Equal(loader, result.Loader); Assert.Equal("Mod # name", result.Name);
        Assert.Equal(2, result.ForgeMetadata!.Mods.Count); Assert.Contains(result.ProvidedMods!, p => p.Id == "second" && p.Version == "2.5");
        Assert.Empty(result.ForgeMetadata.Mods[0].Dependencies); Assert.Equal("library", Assert.Single(result.ForgeMetadata.Mods[1].Dependencies).Id);
    }
    [Fact] public async Task MissingDependencyBlocksBeforeMutationAndCompatibleFilesInstallWithReadOnlyBaseline()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var local = new LocalModManager(ws.Paths, ws.Store);
        var root = await Item(ws, "parent", Metadata(Mod("parent"), Dependency("parent", "library", "[1,2)")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => local.InstallAsync(p, [root], true));
        Assert.Empty(Directory.GetFiles(Path.Combine(ws.Paths.Game(p.Id), "mods")));
        var library = await Item(ws, "library", Metadata(Mod("library", "1.5")));
        var world = Path.Combine(ws.Paths.Game(p.Id), "saves", "synthetic.txt"); await File.WriteAllTextAsync(world, "synthetic world");
        await local.InstallAsync(p, [root, library], true);
        var inventory = new ModInventoryStore(ws.Paths).PathFor(p.Id); var original = await File.ReadAllBytesAsync(inventory);
        var report = await Check(ws, p); Assert.Empty(report.Issues); Assert.Equal(2, report.VerifiedFiles);
        Assert.Equal(original, await File.ReadAllBytesAsync(inventory)); Assert.Equal("synthetic world", await File.ReadAllTextAsync(world));
    }
    [Theory]
    [InlineData(ModLoader.Forge)]
    [InlineData(ModLoader.NeoForge)]
    public async Task OptionalAbsentIsAllowedButPresentWrongVersionBlocks(ModLoader loader)
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws, loader); var local = new LocalModManager(ws.Paths, ws.Store); bool neo = loader == ModLoader.NeoForge;
        var root = await Item(ws, "parent", Metadata(Mod("parent"), Dependency("parent", "library", "[2,)", "optional", neo:neo)), loader);
        Assert.Empty((await local.ReviewAsync(p, [root])).Blockers);
        var library = await Item(ws, "library", Metadata(Mod("library")), loader);
        Assert.Contains((await local.ReviewAsync(p, [root, library])).Blockers, b => b.Contains("library"));
    }
    [Fact] public async Task NeoForgeIncompatibleBlocksAndDiscouragedOnlyWarns()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws, ModLoader.NeoForge); var local = new LocalModManager(ws.Paths, ws.Store);
        var other = await Item(ws, "other", Metadata(Mod("other")), ModLoader.NeoForge);
        var bad = await Item(ws, "bad", Metadata(Mod("bad"), Dependency("bad", "other", "[1,)", "incompatible", neo:true)), ModLoader.NeoForge);
        Assert.Contains((await local.ReviewAsync(p, [bad, other])).Details, d => d.Kind == ModBlockerKind.DependencyConflict);
        var warning = await Item(ws, "warning", Metadata(Mod("warning"), Dependency("warning", "other", "[1,)", "discouraged", neo:true)), ModLoader.NeoForge);
        var review = await local.ReviewAsync(p, [warning, other]); Assert.Empty(review.Blockers); Assert.Contains(review.Warnings, w => w.Contains("other"));
    }
    [Fact] public async Task ServerSideDependencyIsNotRequiredForClient()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var root = await Item(ws, "parent", Metadata(Mod("parent"), Dependency("parent", "server_library", "[99,)", side:"SERVER")));
        Assert.Empty(root.Inspection.RequiredDependencies); Assert.Empty((await new LocalModManager(ws.Paths, ws.Store).ReviewAsync(p, [root])).Blockers);
    }
    [Fact] public async Task KnownRuntimeConstraintsAreDefiniteBlockers()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws);
        var root = await Item(ws, "parent", Metadata(Mod("parent") + "[mods.features]\njava_version=\"[25,)\"\n", Dependency("parent", "minecraft", "[1.22,)") + Dependency("parent", "forge", "[53,)")));
        var review = await new LocalModManager(ws.Paths, ws.Store).ReviewAsync(p, [root]);
        Assert.Equal(3, review.Details.Count); Assert.All(review.Details, d => Assert.Equal(ModBlockerKind.Definite, d.Kind));
    }
    [Fact] public async Task LanguageLoaderUsesForgeMajorButNeverNeoForgeVersionAsFml()
    {
        using var ws = new TestWorkspace(); var forge = await Profile(ws); var local = new LocalModManager(ws.Paths, ws.Store);
        var file = await Item(ws, "forge", Metadata(Mod("example"), languageRange:"[52,53)"));
        Assert.Empty((await local.ReviewAsync(forge, [file])).Blockers);
        var bad = await Item(ws, "bad_fml", Metadata(Mod("bad_fml"), languageRange:"[53,)"));
        Assert.Contains((await local.ReviewAsync(forge, [bad])).Details, d => d.Kind == ModBlockerKind.Definite);
        var neo = await Profile(ws, ModLoader.NeoForge); var neoFile = await Item(ws, "neo", Metadata(Mod("neo_example"), languageRange:"[4,5)"), ModLoader.NeoForge);
        await local.InstallAsync(neo, [neoFile], true);
        var report = await Check(ws, neo); Assert.Contains(report.Issues, i => i.Code == "mods.language.unknown");
        Assert.DoesNotContain(report.Issues, i => i.Code == "mods.runtime.version");
    }
    [Fact] public async Task OrderingCycleBlocksAndAcyclicOrderDoesNot()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var local = new LocalModManager(ws.Paths, ws.Store);
        var first = await Item(ws, "first", Metadata(Mod("first"), Dependency("first", "second", order:"BEFORE")));
        var second = await Item(ws, "second", Metadata(Mod("second"), Dependency("second", "first", order:"BEFORE")));
        Assert.Contains((await local.ReviewAsync(p, [first, second])).Blockers, b => b.Contains("BEFORE/AFTER"));
        var correct = await Item(ws, "correct", Metadata(Mod("second"), Dependency("second", "first", order:"AFTER")));
        Assert.Empty((await local.ReviewAsync(p, [first, correct])).Blockers);
    }
    [Fact] public async Task SecondaryModVersionAndDuplicateIdentityAreChecked()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var local = new LocalModManager(ws.Paths, ws.Store);
        var multi = await Item(ws, "multi", Metadata(Mod("first") + Mod("second", "2.5")));
        var good = await Item(ws, "good", Metadata(Mod("good"), Dependency("good", "second", "[2,3)")));
        Assert.Empty((await local.ReviewAsync(p, [multi, good])).Blockers);
        var duplicate = await Item(ws, "duplicate", Metadata(Mod("second", "2.5")));
        Assert.Contains((await local.ReviewAsync(p, [multi, duplicate])).Blockers, b => b.Contains("second"));
    }
    [Fact] public async Task ManifestVersionIsUnfoldedAndNamedEntryVersionIsNotUsed()
    {
        using var ws = new TestWorkspace(); var a = await Item(ws, "main", Metadata(Mod("example", "${file.jarVersion}")), manifest:"Manifest-Version: 1.0\r\nImplementation-Version: 1.\r\n 5.0\r\n\r\nName: other\r\nImplementation-Version: 99.0\r\n");
        Assert.Equal("1.5.0", a.Inspection.Version);
        var b = await Item(ws, "named", Metadata(Mod("another", "${file.jarVersion}")), manifest:"Manifest-Version: 1.0\n\nName: other\nImplementation-Version: 99\n");
        Assert.Null(b.Inspection.Version); Assert.NotEmpty(b.Inspection.MetadataWarnings!);
    }
    [Fact] public async Task SubstitutionPropertiesAreStaticAndUnknownPlaceholdersAreNotInvented()
    {
        using var ws = new TestWorkspace(); var a = await Item(ws, "props", Metadata(Mod("example", "${file.release}")) + "\n[properties]\nrelease='2.3'\n");
        Assert.Equal("2.3", a.Inspection.Version);
        var b = await Item(ws, "unknown", Metadata(Mod("another", "${unknown}")));
        Assert.Null(b.Inspection.Version); Assert.NotEmpty(b.Inspection.MetadataWarnings!);
    }
    [Fact] public async Task MultilineTomlDescriptionCannotInjectDependencyTables()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws);
        var file = await Item(ws, "description", Metadata(Mod("example") + "description='''\n[[dependencies.example]]\nmodId=\"invented\"\nmandatory=true\n'''\n"));
        Assert.Empty(file.Inspection.RequiredDependencies); Assert.Empty((await new LocalModManager(ws.Paths, ws.Store).ReviewAsync(p, [file])).Blockers);
    }
    [Theory]
    [InlineData("modId=\"example\"\nmodId=\"different\"\n")]
    [InlineData("modId=\"example\"\nversion=12\n")]
    [InlineData("modId=\"../escape\"\n")]
    [InlineData("modId=\"example\"\nversion=\"unterminated\n")]
    public async Task InvalidTomlAndWrongTypesAreRejected(string body)
    {
        using var ws = new TestWorkspace(); await Assert.ThrowsAsync<InvalidDataException>(() => Item(ws, "invalid", Metadata("[[mods]]\n" + body)));
    }
    [Fact] public async Task QualifiersAndJarJarNeverBecomeConfirmedReady()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var file = await Item(ws, "qualifier", Metadata(Mod("example"), Dependency("example", "minecraft", "[1.21.1-beta,)")), jarjar:true);
        await new LocalModManager(ws.Paths, ws.Store).InstallAsync(p, [file], true);
        var report = await Check(ws, p); Assert.Contains(report.Issues, i => i.Code == "mods.constraint.unknown");
        Assert.Contains(report.Issues, i => i.Code == "mods.metadata.unknown");
    }
    [Fact] public async Task TamperedForgeJarCannotKeepItsInstalledIntegrityStatus()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var item = await Item(ws, "example", Metadata(Mod("example")));
        var local = new LocalModManager(ws.Paths, ws.Store); await local.InstallAsync(p, [item], true);
        var installed = Directory.GetFiles(Path.Combine(ws.Paths.Game(p.Id), "mods")).Single();
        await File.AppendAllTextAsync(installed, "changed"); Assert.Contains((await Check(ws, p)).Issues, i => i.Code == "mods.file.corrupt");
    }
}
