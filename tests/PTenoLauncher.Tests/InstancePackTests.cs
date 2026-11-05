using System.IO.Compression;
using System.Text;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Modpacks;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;
namespace PTenoLauncher.Tests;

public sealed class InstancePackTests
{
    public static byte[] Metadata(string? uid = "net.fabricmc.fabric-loader", string version = "0.16.10") => JsonSerializer.SerializeToUtf8Bytes(new
    {
        formatVersion = 1,
        components = new[] { new { uid = "net.minecraft", version = "1.21.1" } }.Concat(uid is null ? [] : new[] { new { uid, version } })
    });
    public static string DirectorySource(MrpackFixture f, string game = "minecraft", string cfg = "[General]\nInstanceType=OneSix\nname=Local instance\n", byte[]? metadata = null)
    {
        var source = Path.Combine(f.Workspace.Paths.Root, "source-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "instance.cfg"), cfg); File.WriteAllBytes(Path.Combine(source, "mmc-pack.json"), metadata ?? Metadata());
        Put(source, game + "/config/example.txt", Encoding.UTF8.GetBytes("LOCAL CONTENT")); return source;
    }
    internal static void Put(string source, string path, byte[] bytes)
    { var file = SafePath.Join(source, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllBytes(file, bytes); }
    public static string Archive(MrpackFixture f, string root = "", string game = "minecraft", byte[]? metadata = null) => ZipPackTests.Zip(f,
        (root + "instance.cfg", Encoding.UTF8.GetBytes("InstanceType=OneSix\nname=Exported instance\n")), (root + "mmc-pack.json", metadata ?? Metadata()), (root + game + "/config/example.txt", [1, 2, 3]));

    [Theory][InlineData("minecraft")][InlineData(".minecraft")]
    public async Task DirectoryImportIsReadOnlyOfflineAndPublishesExactDataOnlyNewProfile(string game)
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); f.Network.Offline = true;
        var other = f.Workspace.Profile(); f.Workspace.Paths.CreateProfile(other); await f.Workspace.Store.SaveProfileAsync(other);
        Put(f.Workspace.Paths.Game(other.Id), "saves/world/data.txt", [9]);
        var source = DirectorySource(f, game); Put(source, game + "/mods/demo.jar", MrpackFixture.Jar());
        var excluded = new[] { "accounts.json", "settings.db", "instance.png", game + "/saves/world/level.dat", game + "/assets/x", game + "/logs/latest.log", game + "/config/tokens.json", game + "/config/run.cmd" };
        foreach (var p in excluded) Put(source, p, Encoding.UTF8.GetBytes("SYNTHETIC PRIVATE DATA"));
        var before = Directory.GetFiles(source, "*", SearchOption.AllDirectories).ToDictionary(p => p, p => File.ReadAllBytes(p));
        var preview = await f.Importer.InstanceReader.PreviewAsync(source, new("Independent profile", true));
        Assert.True(preview.Plan.CanImport, string.Join(",", preview.Plan.Blockers)); Assert.NotNull(preview.Review); Assert.Equal(2, preview.Content.Count);
        var result = await f.Importer.ImportInstanceAsync(preview, true); Assert.Equal(0, f.Requests); Assert.Equal(2, (await f.Workspace.Store.GetProfilesAsync()).Count);
        Assert.NotEqual(other.Id, result.Profile.Id); Assert.Equal("Independent profile", result.Profile.Name); Assert.Equal(4096, result.Profile.MemoryMb); Assert.Null(result.Profile.JavaPath);
        Assert.Equal(2, result.Receipt.Files.Count); Assert.All(result.Receipt.Files, file => Assert.True(file.RelativePath.StartsWith("config/") || file.RelativePath.StartsWith("mods/")));
        Assert.Equal("local-prism-multimc", (await MrpackRequirements.ReadAsync(f.Workspace.Paths, result.Profile))!.SourceKind);
        Assert.Equal("0.16.10", await MrpackRequirements.RequestedAsync(f.Workspace.Paths, result.Profile)); Assert.False(File.Exists(f.Workspace.Paths.Receipt(result.Profile.Id)));
        Assert.Contains(await MrpackRequirements.CheckAsync(f.Workspace.Paths, result.Profile, null, default), p => p.Code == "modpack.target.mismatch");
        var provenance = await File.ReadAllTextAsync(Path.Combine(f.Workspace.Paths.Profile(result.Profile.Id), "instance-import-target.json")); Assert.DoesNotContain("SYNTHETIC PRIVATE DATA", provenance); Assert.DoesNotContain(source.Replace("\\", "\\\\"), provenance);
        var pins = await AtomicFile.ReadJsonAsync<ModPinSet>(Path.Combine(f.Workspace.Paths.Profile(result.Profile.Id), "mod-pins.json")); Assert.Null(Assert.Single(pins!.Pins).Provider);
        Assert.False(File.Exists(Path.Combine(f.Workspace.Paths.Game(result.Profile.Id), "saves/world/level.dat")));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Path.Combine(f.Workspace.Paths.Game(other.Id), "saves/world/data.txt")));
        Assert.All(before, pair => Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key))); Assert.Empty(await f.Importer.PendingAsync());
    }
    [Theory][InlineData("")][InlineData("wrapper/")]
    public async Task SingleInstanceExportZipAllowsOneWrapperAndKeepsOriginal(string root)
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var zip = Archive(f, root, ".minecraft"); var before = File.ReadAllBytes(zip);
        var p = await f.Importer.InstanceReader.PreviewAsync(zip); Assert.True(p.Plan.CanImport); Assert.Equal(root + ".minecraft", p.Plan.ContentRoot);
        var result = await f.Importer.ImportInstanceAsync(p, true); Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(f.Workspace.Paths.Game(result.Profile.Id), "config/example.txt"))); Assert.Equal(before, File.ReadAllBytes(zip));
    }
    [Theory]
    [InlineData(null, ModLoader.Vanilla, "")]
    [InlineData("net.fabricmc.fabric-loader", ModLoader.Fabric, "0.16.10")]
    [InlineData("org.quiltmc.quilt-loader", ModLoader.Quilt, "0.28.1")]
    [InlineData("net.minecraftforge", ModLoader.Forge, "52.0.1")]
    [InlineData("net.neoforged", ModLoader.NeoForge, "21.1.172")]
    public async Task ExactComponentRequirementsSurvivePublicationAndRefuseOtherVersions(string? uid, ModLoader loader, string version)
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var source = DirectorySource(f, metadata: Metadata(uid, version));
        var p = await f.Importer.InstanceReader.PreviewAsync(source); Assert.True(p.Plan.CanImport); var result = await f.Importer.ImportInstanceAsync(p, true);
        Assert.Equal(loader, result.Profile.Loader); Assert.Equal(uid is null ? null : version, await MrpackRequirements.RequestedAsync(f.Workspace.Paths, result.Profile));
        if (uid is not null) Assert.Equal("PackTarget", (await Assert.ThrowsAsync<MrpackException>(() => MrpackRequirements.RequestedAsync(f.Workspace.Paths, result.Profile, "0.0.1"))).Code);
    }
    [Theory]
    [InlineData("{\"formatVersion\":2,\"components\":[]}")]
    [InlineData("{\"formatVersion\":1,\"components\":null}")]
    [InlineData("{\"formatVersion\":1,\"components\":[null]}")]
    [InlineData("{\"formatVersion\":1,\"components\":[{\"uid\":\"net.minecraft\",\"version\":\"1.21.1\"},{\"uid\":\"net.minecraft\",\"version\":\"1.20.1\"}]}")]
    [InlineData("{\"formatVersion\":1,\"formatVersion\":1,\"components\":[]}")]
    [InlineData("{broken")]
    public async Task MalformedMissingOrDuplicateComponentsAreRejected(string json)
    { using var f = new MrpackFixture(); await Assert.ThrowsAsync<MrpackException>(() => f.Importer.InstanceReader.PreviewAsync(DirectorySource(f, metadata: Encoding.UTF8.GetBytes(json)))) ; }
    [Theory]
    [InlineData("{\"formatVersion\":1,\"components\":[{\"uid\":\"net.minecraft\",\"cachedVersion\":\"1.21.1\"}]}")]
    [InlineData("{\"formatVersion\":1,\"components\":[{\"uid\":\"net.minecraft\",\"version\":\"1.21.1\",\"disabled\":true}]}")]
    [InlineData("{\"formatVersion\":1,\"components\":[{\"uid\":\"net.minecraft\",\"version\":\"1.21.1\"},{\"uid\":\"org.lwjgl3\",\"version\":\"3.3.3\",\"dependencyOnly\":true}]}")]
    [InlineData("{\"formatVersion\":1,\"components\":[{\"uid\":\"net.minecraft\",\"version\":\"1.21.1\",\"custom\":true}]}")]
    [InlineData("{\"formatVersion\":1,\"components\":[{\"uid\":\"net.minecraft\",\"version\":\"1.21.1\"},{\"uid\":\"net.fabricmc.fabric-loader\",\"version\":\"0.16.10\"},{\"uid\":\"org.quiltmc.quilt-loader\",\"version\":\"0.28.1\"}]}")]
    public async Task CachedOnlyDisabledUnknownAncillaryOverridesAndMultipleLoadersNeverBecomeReady(string json)
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var p = await f.Importer.InstanceReader.PreviewAsync(DirectorySource(f, metadata: Encoding.UTF8.GetBytes(json)));
        Assert.False(p.Plan.CanImport); await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ImportInstanceAsync(p, true)); Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
    }
    [Theory]
    [InlineData("PreLaunchCommand=echo test", "InstanceCustom")]
    [InlineData("PostExitCommand=echo test", "InstanceCustom")]
    [InlineData("WrapperCommand=wrapper", "InstanceCustom")]
    [InlineData("OverrideCommands=true", "InstanceCustom")]
    [InlineData("JvmArgs=-javaagent:unknown.jar", "InstanceCustom")]
    [InlineData("MinecraftPath=C:/somewhere", "InstanceExternal")]
    [InlineData("GameDir=../somewhere", "InstanceExternal")]
    public async Task CommandsAndExternalGamePathsBlockWithoutExecutingOrPersistingValues(string cfg, string code)
    {
        using var f = new MrpackFixture(); var p = await f.Importer.InstanceReader.PreviewAsync(DirectorySource(f, cfg: "InstanceType=OneSix\nname=Test\n" + cfg));
        Assert.Contains(p.Plan.Blockers, b => b.Code == code); Assert.DoesNotContain(p.Plan.Blockers, b => b.Detail.Contains("echo test") || b.Detail.Contains("C:/")); Assert.Equal(0, f.Requests);
    }
    [Theory][InlineData("patches/custom.json")][InlineData("jarmods/custom.jar")]
    public async Task LocalComponentPatchesRequireUnsupportedAdapter(string entry)
    { using var f = new MrpackFixture(); var dir = DirectorySource(f); Put(dir, entry, [1]); Assert.Contains((await f.Importer.InstanceReader.PreviewAsync(dir)).Plan.Blockers, b => b.Code == "InstanceCustom"); }
    [Fact] public async Task DisabledNonCoreComponentIsExplicitlyExcludedNotInstalled()
    {
        using var f = new MrpackFixture(); var json = "{\"formatVersion\":1,\"components\":[{\"uid\":\"net.minecraft\",\"version\":\"1.21.1\"},{\"uid\":\"custom.disabled\",\"version\":\"1.0\",\"disabled\":true}]}";
        var p = await f.Importer.InstanceReader.PreviewAsync(DirectorySource(f, metadata: Encoding.UTF8.GetBytes(json))); Assert.True(p.Plan.CanImport); Assert.Single(p.Components); Assert.Contains("InstanceDisabled: custom.disabled", p.Warnings);
    }
    [Theory][InlineData("name=Test\nname=Other")][InlineData("InstanceType=Legacy")][InlineData("[Other]\nname=x")]
    public async Task AmbiguousIniAndUnsupportedTypeFailClosed(string cfg)
    {
        using var f = new MrpackFixture(); var dir = DirectorySource(f, cfg: "InstanceType=OneSix\n" + cfg);
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.InstanceReader.PreviewAsync(dir));
    }
    [Fact] public async Task SupportedEscapedDisplayNameAndDefaultsDoNotCopyJavaOrRam()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var dir = DirectorySource(f, cfg: "InstanceType=OneSix\nname=Pack\\#1\nJavaPath=C:\\\\private\\\\java.exe\nMaxMemAlloc=16384\n");
        var p = await f.Importer.InstanceReader.PreviewAsync(dir); Assert.True(p.Plan.CanImport); Assert.Equal("Pack#1", p.Plan.Name);
        var result = await f.Importer.ImportInstanceAsync(p, true); Assert.Equal(4096, result.Profile.MemoryMb); Assert.Null(result.Profile.JavaPath);
    }
    [Theory][InlineData("Pack\\\\#1", "Pack\\#1")][InlineData("Pack #comment", "Pack")][InlineData("\"Pack, one\"", "Pack, one")]
    public async Task IniCommentsAndQuotesFollowConfirmedSourceOrder(string encoded, string expected)
    { using var f = new MrpackFixture(); var p = await f.Importer.InstanceReader.PreviewAsync(DirectorySource(f, cfg: "InstanceType=OneSix\nname=" + encoded + "\n")); Assert.Equal(expected, p.Plan.Name); }
    [Fact] public async Task DualGameRootsMultipleInstancesAndNestedWrapperAreNotChosenArbitrarily()
    {
        using var f = new MrpackFixture(); var dir = DirectorySource(f); Put(dir, ".minecraft/config/other", [2]);
        Assert.Contains((await f.Importer.InstanceReader.PreviewAsync(dir)).Plan.Blockers, b => b.Code == "InstanceLayout");
        var zip = Archive(f); using (var z = ZipFile.Open(zip, ZipArchiveMode.Update)) { using var s = z.CreateEntry("other/instance.cfg").Open(); s.WriteByte(1); }
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.InstanceReader.PreviewAsync(zip)); await Assert.ThrowsAsync<MrpackException>(() => f.Importer.InstanceReader.PreviewAsync(Archive(f, "one/two/")));
    }
    [Theory][InlineData("../bad")][InlineData("minecraft/config/CON")][InlineData("minecraft/config/a:ads")][InlineData("minecraft/config/e\u0301")]
    public async Task UnsafePathsAreRejectedBeforeReadingPayload(string path)
    {
        using var f = new MrpackFixture(); var zip = Archive(f); using (var z = ZipFile.Open(zip, ZipArchiveMode.Update)) { using var s = z.CreateEntry(path).Open(); s.WriteByte(1); }
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.InstanceReader.PreviewAsync(zip));
    }
    [Fact] public async Task CaseAliasesAndArchiveLinksAreRejected()
    {
        using var f = new MrpackFixture(); var zip = Archive(f); using (var z = ZipFile.Open(zip, ZipArchiveMode.Update)) z.CreateEntry("minecraft/config/EXAMPLE.txt");
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.InstanceReader.PreviewAsync(zip));
        zip = Archive(f); using (var z = ZipFile.Open(zip, ZipArchiveMode.Update)) z.GetEntry("minecraft/config/example.txt")!.ExternalAttributes = unchecked((int)0xA1FF0000);
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.InstanceReader.PreviewAsync(zip));
    }
    [Theory][InlineData("metadata")][InlineData("content")][InlineData("new-file")]
    public async Task SourceMutationInvalidatesConsentBeforeOperation(string kind)
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var dir = DirectorySource(f); var p = await f.Importer.InstanceReader.PreviewAsync(dir);
        if (kind == "metadata") File.AppendAllText(Path.Combine(dir, "instance.cfg"), "\nnotes=changed");
        else Put(dir, "minecraft/config/" + (kind == "content" ? "example.txt" : "new.txt"), Encoding.UTF8.GetBytes("NEWER CONTENT"));
        Assert.Equal("PackChanged", (await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ImportInstanceAsync(p, true))).Code); Assert.Empty(await f.Importer.PendingAsync()); Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
    }
    [Fact] public async Task ConfirmationIsBoundToExactSelectedSourceEvenWhenAnotherDirectoryHasIdenticalBytes()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var first = DirectorySource(f); var second = DirectorySource(f);
        var p = await f.Importer.InstanceReader.PreviewAsync(first); var q = await f.Importer.InstanceReader.PreviewAsync(second);
        Assert.Equal(p.Plan.ArchiveSha256, q.Plan.ArchiveSha256); Assert.NotEqual(p.Plan.Fingerprint, q.Plan.Fingerprint);
        var altered = p with { Plan = p.Plan with { Archive = second } };
        Assert.Equal("PackChanged", (await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ImportInstanceAsync(altered, true))).Code); Assert.Empty(await f.Importer.PendingAsync());
    }
    [Fact] public async Task SourceMutationDuringCopyCannotPublishMixedProfile()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var dir = DirectorySource(f); var p = await f.Importer.InstanceReader.PreviewAsync(dir);
        f.Importer.Fault = stage => { if (stage == "preparing") Put(dir, "minecraft/config/example.txt", Encoding.UTF8.GetBytes("OTHER CONTENT")); };
        Assert.Equal("PackChanged", (await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ImportInstanceAsync(p, true))).Code); Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
        f.Importer.Fault = null; await f.Importer.RecoverAsync(true); Assert.Empty(await f.Importer.PendingAsync());
    }
    [Theory][InlineData("preparing", false)][InlineData("ready", true)][InlineData("moved", true)][InlineData("before-database", true)][InlineData("database", true)]
    public async Task JournalRecoveryRequiresOnlyVerifiedSnapshotAndPreservesSqliteBoundary(string boundary, bool publishes)
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); f.Network.Offline = true; var source = DirectorySource(f); var p = await f.Importer.InstanceReader.PreviewAsync(source);
        f.Importer.Fault = stage => { if (stage == boundary) throw new IOException("Controlled interruption"); }; await Assert.ThrowsAsync<IOException>(() => f.Importer.ImportInstanceAsync(p, true));
        if (boundary != "database") Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
        File.Delete(Path.Combine(source, "instance.cfg")); f.Importer.Fault = null; await f.Importer.RecoverAsync(true); await f.Importer.RecoverAsync(true);
        Assert.Equal(publishes ? 1 : 0, (await f.Workspace.Store.GetProfilesAsync()).Count); Assert.Empty(await f.Importer.PendingAsync()); Assert.Equal(0, f.Requests);
    }
    [Theory][InlineData("ready")][InlineData("moved")]
    public async Task RecoveryRefusesForeignOrModifiedBytes(string boundary)
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var p = await f.Importer.InstanceReader.PreviewAsync(DirectorySource(f));
        f.Importer.Fault = stage => { if (stage == boundary) throw new IOException(); }; await Assert.ThrowsAsync<IOException>(() => f.Importer.ImportInstanceAsync(p, true)); f.Importer.Fault = null;
        var id = Assert.Single(await f.Importer.PendingAsync()); var dest = f.Workspace.Paths.Profile(id); Directory.CreateDirectory(dest); var sentinel = Path.Combine(dest, "foreign.txt"); File.WriteAllText(sentinel, "NEWER");
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.RecoverAsync(true)); Assert.Equal("NEWER", File.ReadAllText(sentinel)); Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
    }
    [Fact] public async Task CancellationAfterStagingStartsLeavesRecoverableJournalAndRequiresFreshRetry()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var p = await f.Importer.InstanceReader.PreviewAsync(DirectorySource(f));
        Assert.Equal("PackConsent", (await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ImportInstanceAsync(p, false))).Code);
        using var cancel = new CancellationTokenSource(); f.Importer.Fault = s => { if (s == "preparing") cancel.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Importer.ImportInstanceAsync(p, true, ct: cancel.Token)); Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
        f.Importer.Fault = null; await f.Importer.RecoverAsync(true); await f.Importer.ImportInstanceAsync(p, true); Assert.Single(await f.Workspace.Store.GetProfilesAsync());
    }
    [Fact] public async Task ActualJarDependenciesAndNestedCandidatesUseExistingReviewOffline()
    {
        using var f = new MrpackFixture(); var source = DirectorySource(f); Put(source, "minecraft/mods/dependent.jar", MrpackFixture.Jar(dependency: "missing-required"));
        var p = await f.Importer.InstanceReader.PreviewAsync(source); Assert.NotNull(p.Review); Assert.Contains(p.Plan.Blockers, b => b.Code == "PackCompatibility"); Assert.Equal(0, f.Requests);
    }
    [Fact] public async Task QuiltNestedJarIsReviewedWithoutExtractingItIntoRootModsOrInventingProvider()
    {
        static byte[] Quilt(string id, byte[]? nested = null)
        {
            using var bytes = new MemoryStream();
            using (var z = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            {
                using (var output = z.CreateEntry("quilt.mod.json").Open()) JsonSerializer.Serialize(output, new { schema_version = 1,
                    quilt_loader = new { id, group = "test.mods", version = "1.0.0", intermediate_mappings = "net.fabricmc:intermediary", depends = nested is null ? Array.Empty<string>() : new[] { "bundled" }, jars = nested is null ? Array.Empty<string>() : new[] { "META-INF/jars/child.jar" } } });
                if (nested is not null) { using var output = z.CreateEntry("META-INF/jars/child.jar").Open(); output.Write(nested); }
            }
            return bytes.ToArray();
        }
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); f.Network.Offline = true;
        var source = DirectorySource(f, metadata: Metadata("org.quiltmc.quilt-loader", "0.30.1")); var jar = Quilt("parent", Quilt("bundled")); Put(source, "minecraft/mods/parent.jar", jar);
        var p = await f.Importer.InstanceReader.PreviewAsync(source); Assert.True(p.Plan.CanImport, string.Join(",", p.Plan.Blockers)); Assert.NotNull(p.Review);
        var imported = await f.Importer.ImportInstanceAsync(p, true); var rootMods = Directory.GetFiles(Path.Combine(f.Workspace.Paths.Game(imported.Profile.Id), "mods"));
        Assert.Equal(jar, File.ReadAllBytes(Assert.Single(rootMods))); Assert.Equal(0, f.Requests);
    }
    [Fact] public async Task SameSourceCanCreateIndependentProfilesButNeverOverwriteAnExistingOne()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var p = await f.Importer.InstanceReader.PreviewAsync(DirectorySource(f));
        var a = await f.Importer.ImportInstanceAsync(p, true); Put(f.Workspace.Paths.Game(a.Profile.Id), "config/user-change", [9]); var b = await f.Importer.ImportInstanceAsync(p, true);
        Assert.NotEqual(a.Profile.Id, b.Profile.Id); Assert.Equal(2, (await f.Workspace.Store.GetProfilesAsync()).Count); Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Path.Combine(f.Workspace.Paths.Game(a.Profile.Id), "config/user-change")));
    }
    [Fact] public async Task MetadataAndArchiveBudgetsRejectBeforePublication()
    {
        using var f = new MrpackFixture(); var dir = DirectorySource(f); File.WriteAllBytes(Path.Combine(dir, "mmc-pack.json"), new byte[256 * 1024 + 1]);
        Assert.Equal("PackBudget", (await Assert.ThrowsAsync<MrpackException>(() => f.Importer.InstanceReader.PreviewAsync(dir))).Code);
        var zip = Archive(f); using (var z = ZipFile.Open(zip, ZipArchiveMode.Update)) { using var s = z.CreateEntry("minecraft/config/bomb", CompressionLevel.SmallestSize).Open(); s.Write(new byte[2 * 1024 * 1024]); }
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.InstanceReader.PreviewAsync(zip));
    }
}
