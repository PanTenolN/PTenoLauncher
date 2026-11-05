using System.IO.Compression;
using System.Text;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Modpacks;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;
namespace PTenoLauncher.Tests;

public sealed class ZipPackTests
{
    public static string Zip(MrpackFixture f, params (string Path, byte[] Bytes)[] files)
    {
        var path = Path.Combine(f.Workspace.Paths.Root, Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, bytes) in files) { using var output = zip.CreateEntry(name, CompressionLevel.Fastest).Open(); output.Write(bytes); }
        return path;
    }
    public static ZipPackRequest Target(string? root = null) => new("ZIP fixture", "1.21.1", ModLoader.Fabric, "0.16.10", root);
    [Theory][InlineData("")][InlineData("wrapper/")][InlineData(".minecraft/")]
    public async Task SelfContainedLayoutsPublishNewOfflineProfileWithExactTargetAndProvenance(string wrapper)
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); f.Network.Offline = true;
        var other = new GameProfile(Guid.NewGuid(), "Other", "1.20.1", ModLoader.Vanilla, 4096, null, DateTimeOffset.UtcNow);
        f.Workspace.Paths.CreateProfile(other); await f.Workspace.Store.SaveProfileAsync(other); var sentinel = Path.Combine(f.Workspace.Paths.Game(other.Id), "saves", "world.txt"); Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!); await File.WriteAllTextAsync(sentinel, "preserve");
        var pack = Zip(f, (wrapper + "mods/a.jar", MrpackFixture.Jar()), (wrapper + "config/example.txt", [1, 2]), (wrapper + "resourcepacks/readme.txt", [3]), (wrapper + "shaderpacks/readme.txt", [4]));
        var preview = await f.Importer.ZipReader.PreviewAsync(pack, Target() with { PinMods = true }); Assert.True(preview.Plan.CanImport, string.Join(",", preview.Plan.Blockers)); Assert.Equal(wrapper.TrimEnd('/'), preview.Plan.ContentRoot); Assert.NotNull(preview.Review);
        var result = await f.Importer.ImportZipAsync(preview, true); Assert.Equal(2, (await f.Workspace.Store.GetProfilesAsync()).Count); Assert.Equal("preserve", await File.ReadAllTextAsync(sentinel)); Assert.Equal(0, f.Requests);
        var receipt = await MrpackRequirements.ReadAsync(f.Workspace.Paths, result.Profile); Assert.Equal("local-zip", receipt!.SourceKind); Assert.Equal(preview.Plan.ArchiveSha256, receipt.ArchiveSha256); Assert.Equal(4, receipt.Files.Count);
        Assert.Equal("0.16.10", await MrpackRequirements.RequestedAsync(f.Workspace.Paths, result.Profile)); Assert.False(File.Exists(f.Workspace.Paths.Receipt(result.Profile.Id)));
        var pins = await AtomicFile.ReadJsonAsync<ModPinSet>(Path.Combine(f.Workspace.Paths.Profile(result.Profile.Id), "mod-pins.json")); Assert.Null(Assert.Single(pins!.Pins).Provider);
        Assert.Empty(await f.Importer.PendingAsync());
    }
    [Fact] public async Task AmbiguousRootsRequireExplicitChoiceAndDoNotMergeOtherRoots()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var pack = Zip(f, ("a/config/x", [1]), ("b/config/y", [2]));
        var ambiguous = await f.Importer.ZipReader.PreviewAsync(pack, Target()); Assert.Contains(ambiguous.Plan.Blockers, p => p.Code == "ZipRoot"); Assert.Equal(2, ambiguous.Roots.Count);
        var selected = await f.Importer.ZipReader.PreviewAsync(pack, Target("b")); Assert.True(selected.Plan.CanImport); Assert.Equal("config/y", Assert.Single(selected.Plan.Files).Path); Assert.Contains("a/config/x", selected.Plan.Excluded);
        var result = await f.Importer.ImportZipAsync(selected, true); Assert.False(File.Exists(Path.Combine(f.Workspace.Paths.Game(result.Profile.Id), "config", "x")));
    }
    [Theory][InlineData("", "0.16.10")][InlineData("latest", "0.16.10")][InlineData("1.21.1", "")][InlineData("1.21.1", "latest")]
    public async Task RequirementsMustBeExplicitAndExact(string minecraft, string loader)
    { using var f = new MrpackFixture(); var p = await f.Importer.ZipReader.PreviewAsync(Zip(f, ("config/x", [1])), Target() with { Minecraft = minecraft, LoaderVersion = loader }); Assert.Contains(p.Plan.Blockers, b => b.Code == "ZipRequirements"); Assert.Empty(Directory.GetDirectories(Path.Combine(f.Workspace.Paths.Root, "instances"))); }
    [Theory][InlineData("manifest.json")][InlineData("instance.cfg")][InlineData("mmc-pack.json")][InlineData("minecraftinstance.json")][InlineData("pack.toml")]
    public async Task ForeignManifestsRequireSeparateAdaptersEvenWithLocalPayload(string manifest)
    { using var f = new MrpackFixture(); var p = await f.Importer.ZipReader.PreviewAsync(Zip(f, (manifest, Encoding.UTF8.GetBytes("{}")), ("mods/a.jar", MrpackFixture.Jar())), Target()); Assert.Contains(p.Plan.Blockers, b => b.Code == "ZipAdapter"); Assert.Null(p.Review); }
    [Fact] public async Task ManifestOnlyOrTooDeepLayoutsAreNotSelfContainedContent()
    {
        using var f = new MrpackFixture();
        foreach (var pack in new[] { Zip(f, ("manifest.json", [1])), Zip(f, ("one/two/config/x", [1])) })
        { var p = await f.Importer.ZipReader.PreviewAsync(pack, Target()); Assert.False(p.Plan.CanImport); Assert.Contains(p.Plan.Blockers, b => b.Code == "ZipContent"); }
    }
    [Fact] public async Task RootMrpackIndexRoutesToExistingAdapterWithoutGuessingZipTarget()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var pack = f.Pack(null, ("overrides/config/x", [1])); var renamed = Path.ChangeExtension(pack, ".zip"); File.Move(pack, renamed);
        var p = await f.Importer.ZipReader.PreviewAsync(renamed, new()); Assert.True(p.IsMrpack); Assert.True(p.Plan.CanImport); var result = await f.Importer.ImportZipAsync(p, true); Assert.Equal("local-mrpack", result.Receipt.SourceKind);
    }
    [Fact] public async Task WorldsAccountsRuntimeLaunchCommandsAndNestedUnselectedJarsAreExcluded()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync();
        var excluded = new[] { "saves/world/level.dat", "launcher_accounts.json", "settings.db", "java/bin/java.exe", "versions/client.jar", "launch.cmd", "config/accounts.json", "config/tokens.txt", "config/updater/state.json", "config/runtime/x.jar", "config/script.ps1", "mods/sub/a.jar", "config/cert.pem", "config/.env" };
        var pack = Zip(f, excluded.Select(p => (p, new byte[] { 9 })).Prepend(("config/kept.txt", new byte[] { 1 })).ToArray());
        var p = await f.Importer.ZipReader.PreviewAsync(pack, Target()); Assert.True(p.Plan.CanImport); Assert.Equal(excluded.Length, p.Plan.Excluded.Count); var result = await f.Importer.ImportZipAsync(p, true); Assert.Single(result.Receipt.Files); Assert.Equal(excluded.Order(), result.Receipt.Excluded.Order());
        Assert.False(File.Exists(Path.Combine(f.Workspace.Paths.Game(result.Profile.Id), "saves", "world", "level.dat")));
    }
    [Theory][InlineData("../outside")][InlineData("C:/x")][InlineData("config/a:ads")][InlineData("config/CON.txt")][InlineData("config/a./x")][InlineData("config\\x")][InlineData("config/e\u0301.txt")]
    public async Task UnsafePathsAreRejectedEvenWhenTheyWouldBeExcluded(string path)
    { using var f = new MrpackFixture(); await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ZipReader.PreviewAsync(Zip(f, (path, [1])), Target())); Assert.Equal(0, f.Requests); }
    [Theory][InlineData("config/A", "config/a")][InlineData("config/a", "config/a")][InlineData("config/a", "config/a/b")][InlineData("Config/a", "config/b")]
    public async Task DuplicateCaseAndFileDirectoryCollisionsFailBeforeExtraction(string a, string b)
    { using var f = new MrpackFixture(); await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ZipReader.PreviewAsync(Zip(f, (a, [1]), (b, [2])), Target())); }
    [Fact] public async Task ArchiveLinksBombAndEntryCountAreBounded()
    {
        using var f = new MrpackFixture(); var link = Zip(f, ("config/x", [1])); using (var z = ZipFile.Open(link, ZipArchiveMode.Update)) z.GetEntry("config/x")!.ExternalAttributes = unchecked((int)0xA1FF0000);
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ZipReader.PreviewAsync(link, Target()));
        var bomb = Zip(f); using (var z = ZipFile.Open(bomb, ZipArchiveMode.Update)) { using var s = z.CreateEntry("config/bomb", CompressionLevel.SmallestSize).Open(); s.Write(new byte[2 * 1024 * 1024]); }
        using (var z = ZipFile.OpenRead(bomb)) Assert.True(z.Entries[0].Length / z.Entries[0].CompressedLength > 1000);
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ZipReader.PreviewAsync(bomb, Target()));
        var many = Zip(f); using (var z = ZipFile.Open(many, ZipArchiveMode.Update)) for (int i = 0; i <= MrpackReader.MaxEntries; i++) z.CreateEntry("config/f" + i);
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ZipReader.PreviewAsync(many, Target()));
    }
    [Fact] public async Task ConsentAndStaleArchiveAreRejectedBeforeCreatingAnOperation()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var pack = Zip(f, ("config/x", [1])); var p = await f.Importer.ZipReader.PreviewAsync(pack, Target());
        Assert.Equal("PackConsent", (await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ImportZipAsync(p, false))).Code);
        using (var z = ZipFile.Open(pack, ZipArchiveMode.Update)) { using var s = z.CreateEntry("config/new").Open(); s.WriteByte(3); }
        Assert.Equal("PackChanged", (await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ImportZipAsync(p, true))).Code); Assert.Empty(await f.Importer.PendingAsync()); Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
    }
    [Theory][InlineData("preparing", false)][InlineData("ready", true)][InlineData("moved", true)][InlineData("before-database", true)][InlineData("database", true)]
    public async Task TransactionInterruptionRecoversOnlyVerifiedContentAndSqliteIsConsistent(string boundary, bool publishes)
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); f.Network.Offline = true; var pack = Zip(f, ("config/x", [1])); var p = await f.Importer.ZipReader.PreviewAsync(pack, Target());
        f.Importer.Fault = stage => { if (stage == boundary) throw new IOException("Controlled interruption"); };
        await Assert.ThrowsAsync<IOException>(() => f.Importer.ImportZipAsync(p, true));
        if (boundary != "database") Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
        else { var profile = Assert.Single(await f.Workspace.Store.GetProfilesAsync()); Assert.Contains(await MrpackRequirements.CheckAsync(f.Workspace.Paths, profile, null, default), x => x.Code == "modpack.pending"); }
        File.Delete(pack); f.Importer.Fault = null; await f.Importer.RecoverAsync(true); await f.Importer.RecoverAsync(true); Assert.Equal(publishes ? 1 : 0, (await f.Workspace.Store.GetProfilesAsync()).Count); Assert.Empty(await f.Importer.PendingAsync()); Assert.Equal(0, f.Requests);
        if (!publishes) { var retry = await f.Importer.ZipReader.PreviewAsync(Zip(f, ("config/retry", [2])), Target()); await f.Importer.ImportZipAsync(retry, true); Assert.Single(await f.Workspace.Store.GetProfilesAsync()); }
    }
    [Fact] public async Task CancellationAtReadyPreservesRecoverableBytesWithoutPublishing()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); using var cancel = new CancellationTokenSource(); var p = await f.Importer.ZipReader.PreviewAsync(Zip(f, ("config/x", [1])), Target()); f.Importer.Fault = s => { if (s == "ready") cancel.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Importer.ImportZipAsync(p, true, ct: cancel.Token)); Assert.Empty(await f.Workspace.Store.GetProfilesAsync()); f.Importer.Fault = null; await f.Importer.RecoverAsync(true); Assert.Single(await f.Workspace.Store.GetProfilesAsync());
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task RecoveryRejectsChangedAndForeignBytes(bool afterMove)
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var p = await f.Importer.ZipReader.PreviewAsync(Zip(f, ("config/x", [1])), Target()); f.Importer.Fault = s => { if (s == (afterMove ? "moved" : "ready")) throw new IOException(); }; await Assert.ThrowsAsync<IOException>(() => f.Importer.ImportZipAsync(p, true));
        var id = Assert.Single(await f.Importer.PendingAsync()); var game = afterMove ? f.Workspace.Paths.Game(id) : new LauncherPaths(Path.Combine(f.Importer.OperationRoot(id), "staging")).Game(id);
        await File.WriteAllTextAsync(Path.Combine(game, "config", "x"), "new user bytes"); f.Importer.Fault = null; await Assert.ThrowsAsync<MrpackException>(() => f.Importer.RecoverAsync(true)); Assert.Equal("new user bytes", await File.ReadAllTextAsync(Path.Combine(game, "config", "x"))); Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
    }
    [Fact] public async Task StaticDependenciesBlockMissingRequirementsAndUnknownStaysWarning()
    {
        using var f = new MrpackFixture(); var missing = await f.Importer.ZipReader.PreviewAsync(Zip(f, ("mods/a.jar", MrpackFixture.Jar(dependency: "missing"))), Target()); Assert.Contains(missing.Plan.Blockers, x => x.Code == "PackCompatibility");
        var server = await f.Importer.ZipReader.PreviewAsync(Zip(f, ("mods/a.jar", MrpackFixture.Jar(environment: "server"))), Target()); Assert.False(server.Plan.CanImport);
        var dependency = await f.Importer.ZipReader.PreviewAsync(Zip(f, ("mods/a.jar", MrpackFixture.Jar("a", "b")), ("mods/b.jar", MrpackFixture.Jar("b"))), Target()); Assert.True(dependency.Plan.CanImport); Assert.Empty(dependency.Review!.Blockers);
        using var bytes = new MemoryStream(); using (var z = new ZipArchive(bytes, ZipArchiveMode.Create, true)) z.CreateEntry("unidentified.txt");
        var unknown = await f.Importer.ZipReader.PreviewAsync(Zip(f, ("mods/unknown.jar", bytes.ToArray())), Target()); Assert.True(unknown.Plan.CanImport); Assert.NotEmpty(unknown.Review!.Warnings);
    }
    [Theory][InlineData(ModLoader.Vanilla, "")][InlineData(ModLoader.Forge, "52.0.1")][InlineData(ModLoader.NeoForge, "21.1.1")][InlineData(ModLoader.Quilt, "0.28.0")]
    public async Task ExplicitRequirementsPersistWithoutFakeGameReceipt(ModLoader loader, string version)
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var p = await f.Importer.ZipReader.PreviewAsync(Zip(f, ("config/x", [1])), Target() with { Loader = loader, LoaderVersion = version }); var result = await f.Importer.ImportZipAsync(p, true);
        var stored = await MrpackRequirements.ReadAsync(f.Workspace.Paths, result.Profile); Assert.Equal(loader, stored!.Profile.Loader); Assert.Equal(version == "" ? null : version, stored.LoaderVersion);
        await Assert.ThrowsAsync<MrpackException>(() => MrpackRequirements.RequestedAsync(f.Workspace.Paths, result.Profile, "latest")); Assert.False(File.Exists(f.Workspace.Paths.Receipt(result.Profile.Id)));
    }
    [Fact] public async Task FabricEmbeddedDependencyStaysInsideOriginalJarAndReceivesStaticReview()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            using (var s = zip.CreateEntry("fabric.mod.json").Open()) JsonSerializer.Serialize(s, new { schemaVersion = 1, id = "parent", version = "1.0.0", environment = "client", depends = new { child = ">=1.0.0" }, jars = new[] { new { file = "META-INF/jars/child.jar" } } });
            using (var s = zip.CreateEntry("META-INF/jars/child.jar").Open()) s.Write(MrpackFixture.Jar("child"));
        }
        var original = bytes.ToArray(); var p = await f.Importer.ZipReader.PreviewAsync(Zip(f, ("mods/parent.jar", original)), Target()); Assert.True(p.Plan.CanImport, string.Join(";", p.Plan.Blockers));
        var result = await f.Importer.ImportZipAsync(p, true); var mods = Path.Combine(f.Workspace.Paths.Game(result.Profile.Id), "mods"); Assert.Single(Directory.GetFiles(mods)); Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(mods, "parent.jar")));
    }
    [Fact] public async Task ExtraUserFileOrForeignProfileAtDestinationBlocksRecoveryWithoutOverwrite()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var p = await f.Importer.ZipReader.PreviewAsync(Zip(f, ("config/x", [1])), Target());
        f.Importer.Fault = s => { if (s == "ready") throw new IOException(); }; await Assert.ThrowsAsync<IOException>(() => f.Importer.ImportZipAsync(p, true)); var id = Assert.Single(await f.Importer.PendingAsync());
        var foreign = new GameProfile(id, "Foreign", "1.20.1", ModLoader.Vanilla, 4096, null, DateTimeOffset.UtcNow); f.Workspace.Paths.CreateProfile(foreign); await f.Workspace.Store.SaveProfileAsync(foreign);
        var sentinel = Path.Combine(f.Workspace.Paths.Game(id), "config", "foreign.txt"); await File.WriteAllTextAsync(sentinel, "foreign"); f.Importer.Fault = null;
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.RecoverAsync(true)); Assert.Equal(foreign, Assert.Single(await f.Workspace.Store.GetProfilesAsync())); Assert.Equal("foreign", await File.ReadAllTextAsync(sentinel));
    }
    [Fact] public async Task DamagedArchiveAndDamagedTargetMetadataFailClosed()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var broken = Zip(f, ("config/x", [1])); await File.WriteAllBytesAsync(broken, [80, 75, 3, 4]);
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ZipReader.PreviewAsync(broken, Target()));
        var valid = await f.Importer.ZipReader.PreviewAsync(Zip(f, ("config/x", [1])), Target()); var result = await f.Importer.ImportZipAsync(valid, true);
        await File.WriteAllTextAsync(Path.Combine(f.Workspace.Paths.Profile(result.Profile.Id), "zip-import-target.json"), "{}");
        await Assert.ThrowsAsync<MrpackException>(() => MrpackRequirements.ReadAsync(f.Workspace.Paths, result.Profile));
        Assert.Contains(await MrpackRequirements.CheckAsync(f.Workspace.Paths, result.Profile, null, default), i => i.Code == "modpack.metadata.unknown");
    }
}
