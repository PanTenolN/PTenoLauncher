using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Authentication;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;
namespace PTenoLauncher.Tests;
public sealed class InstalledModReadinessTests
{
    private static void Jar(string path, string id = "example", string version = "1.0.0", string depends = "{}", string breaks = "{}", string environment = "*", int schema = 1)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var output = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open());
        output.Write("{\"schemaVersion\":" + schema + ",\"id\":\"" + id + "\",\"version\":\"" + version + "\",\"environment\":\"" + environment + "\",\"depends\":" + depends + ",\"breaks\":" + breaks + "}");
    }
    private static InstallationReceipt Receipt(GameProfile p) => new(p.MinecraftVersion, 21, "net.fabricmc.loader.impl.launch.knot.KnotClient", DateTimeOffset.UtcNow, [], [], [], [], [], "test", p.Loader, "0.20.1");
    private static async Task<GameProfile> Profile(TestWorkspace ws)
    {
        await ws.Store.InitializeAsync(); var p = ws.Profile(ModLoader.Fabric); await ws.Store.SaveProfileAsync(p); return p;
    }
    private static async Task<string> Install(TestWorkspace ws, GameProfile p, string id = "example", string depends = "{}", string breaks = "{}")
    {
        var input = Path.Combine(ws.Paths.Root, id + ".jar"); Jar(input, id, depends: depends, breaks: breaks);
        var mods = new LocalModManager(ws.Paths, ws.Store);
        await mods.InstallAsync(p, [new(input, await mods.InspectAsync(input))], true);
        return Directory.GetFiles(Path.Combine(ws.Paths.Game(p.Id), "mods"), "*" + id + ".jar").Single();
    }
    private static Task<InstalledModReport> Check(TestWorkspace ws, GameProfile p) =>
        new InstalledModReadiness(ws.Paths, new(ws.Paths, ws.Store)).CheckAsync(p, Receipt(p));

    [Fact] public async Task ConfirmedInstallHasDurableHashesAndReadOnlyOfflineCheck()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var installed = await Install(ws, p);
        var inventory = new ModInventoryStore(ws.Paths); var before = await File.ReadAllBytesAsync(inventory.PathFor(p.Id));
        var result = await Check(ws, p); Assert.Empty(result.Issues); Assert.Equal(1, result.VerifiedFiles);
        var expected = Assert.Single((await inventory.ReadAsync(p.Id))!.Files);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(installed))), expected.Sha256, ignoreCase: true);
        Assert.Equal(before, await File.ReadAllBytesAsync(inventory.PathFor(p.Id)));
    }
    [Fact] public async Task ChangedJarCannotSilentlyReplaceItsBaseline()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var installed = await Install(ws, p);
        var path = new ModInventoryStore(ws.Paths).PathFor(p.Id); var before = await File.ReadAllBytesAsync(path);
        File.Delete(installed); Jar(installed, version: "2.0.0");
        Assert.Contains((await Check(ws, p)).Issues, i => i.Code == "mods.file.corrupt");
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }
    [Fact] public async Task MissingInstalledJarAndDependentAreBothReported()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var dep = await Install(ws, p, "library");
        await Install(ws, p, "parent", "{\"library\":\">=1.0\"}"); File.Delete(dep);
        var report = await Check(ws, p);
        Assert.Contains(report.Issues, i => i.Code == "mods.file.missing");
        Assert.Contains(report.Issues, i => i.Code == "mods.dependency.missing" && i.Message.Contains("library"));
    }
    [Fact] public async Task ExistingDuplicatesAndVersionMismatchesAreNotIgnored()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws);
        Jar(Path.Combine(ws.Paths.Game(p.Id), "mods", "first.jar"), depends: "{\"minecraft\":\">=1.22\"}");
        Jar(Path.Combine(ws.Paths.Game(p.Id), "mods", "second.jar"));
        var report = await Check(ws, p);
        Assert.Contains(report.Issues, i => i.Code == "mods.duplicate");
        Assert.Contains(report.Issues, i => i.Code == "mods.dependency.version");
        Assert.Contains(report.Issues, i => i.Code == "mods.integrity.unknown");
        Assert.Equal(0, report.VerifiedFiles);
    }
    [Fact] public async Task ExistingBreaksAreBlockingAndUnknownRangesStayUnknown()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws);
        Jar(Path.Combine(ws.Paths.Game(p.Id), "mods", "conflict.jar"), breaks: "{\"minecraft\":\"1.21.1\"}");
        Jar(Path.Combine(ws.Paths.Game(p.Id), "mods", "range.jar"), "range", depends: "{\"minecraft\":\">=unsupported-expression\"}");
        var report = await Check(ws, p);
        Assert.Contains(report.Issues, i => i.Code == "mods.conflict");
        Assert.Contains(report.Issues, i => i.Code == "mods.constraint.unknown");
    }
    [Fact] public async Task ServerOnlyModDoesNotSatisfyClientDependency()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws);
        Jar(Path.Combine(ws.Paths.Game(p.Id), "mods", "server.jar"), "serverlib", environment: "server");
        Jar(Path.Combine(ws.Paths.Game(p.Id), "mods", "client.jar"), "clientmod", depends: "{\"serverlib\":\"*\"}");
        Assert.Contains((await Check(ws, p)).Issues, i => i.Code == "mods.dependency.missing" && i.Message.Contains("serverlib"));
    }
    [Fact] public async Task EmptyModsAreReadyAndVanillaIgnoresUnusedJarDirectory()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); Assert.Empty((await Check(ws, p)).Issues);
        await File.WriteAllTextAsync(Path.Combine(ws.Paths.Game(p.Id), "mods", "broken.jar"), "not a jar");
        var vanilla = await Check(ws, p with { Loader = ModLoader.Vanilla });
        Assert.Empty(vanilla.Issues); Assert.Equal(0, vanilla.VerifiedFiles);
        Assert.Contains((await Check(ws, p)).Issues, i => i.Code == "mods.file.corrupt");
    }
    [Fact] public async Task PendingTransactionIsReportedWithoutRecoverySideEffects()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws);
        var path = SafePath.Join(ws.Paths.Profile(p.Id), "transactions/mods-unfinished.json");
        await AtomicFile.WriteAsync(path, "{}"u8.ToArray());
        Assert.Contains((await Check(ws, p)).Issues, i => i.Code == "mods.pending"); Assert.True(File.Exists(path));
    }
    [Fact] public async Task CorruptInventoryAndUnsafePathsCannotBecomeReadyOrBeRewritten()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var inventory = new ModInventoryStore(ws.Paths);
        await AtomicFile.WriteJsonAsync(inventory.PathFor(p.Id), new InstalledModInventory(p.Id, [new("mods/../../outside.jar", new string('a',64), 1)]));
        var before = await File.ReadAllBytesAsync(inventory.PathFor(p.Id));
        Assert.Contains((await Check(ws, p)).Issues, i => i.Code == "mods.inventory.corrupt");
        await Assert.ThrowsAsync<InvalidDataException>(() => new LocalModManager(ws.Paths,ws.Store).PreviewBaselineAsync(p));
        Assert.Equal(before, await File.ReadAllBytesAsync(inventory.PathFor(p.Id)));
    }
    [Fact] public async Task UnsupportedSchemaNeverClaimsFullCompatibility()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws);
        Jar(Path.Combine(ws.Paths.Game(p.Id), "mods", "future.jar"), schema: 99);
        Assert.Contains((await Check(ws, p)).Issues, i => i.Code == "mods.metadata.unknown");
    }
    [Fact] public async Task ManualBaselineRequiresConfirmationAndPreservesWorldsAndJars()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws);
        var file = Path.Combine(ws.Paths.Game(p.Id), "mods", "manual.jar"); Jar(file);
        var world = Path.Combine(ws.Paths.Game(p.Id), "saves", "synthetic.txt"); await File.WriteAllTextAsync(world, "synthetic world sentinel");
        var before = await File.ReadAllBytesAsync(file); var mods = new LocalModManager(ws.Paths,ws.Store);
        var preview = await mods.PreviewBaselineAsync(p);
        Assert.False(File.Exists(new ModInventoryStore(ws.Paths).PathFor(p.Id)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => mods.ConfirmBaselineAsync(p,preview,false));
        await mods.ConfirmBaselineAsync(p,preview,true);
        Assert.Empty((await Check(ws,p)).Issues); Assert.Equal(before, await File.ReadAllBytesAsync(file));
        Assert.Equal("synthetic world sentinel", await File.ReadAllTextAsync(world));
    }
    [Fact] public async Task ChangedFilesAndStaleOrForeignPreviewsRequireNewConfirmation()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var file = await Install(ws,p);
        var mods = new LocalModManager(ws.Paths,ws.Store); var preview = await mods.PreviewBaselineAsync(p);
        var path = new ModInventoryStore(ws.Paths).PathFor(p.Id); var before = await File.ReadAllBytesAsync(path);
        await Assert.ThrowsAsync<InvalidOperationException>(() => mods.ConfirmBaselineAsync(p,preview with {ProfileId=Guid.NewGuid()},true));
        File.Delete(file); Jar(file, version:"2.0.0");
        await Assert.ThrowsAsync<InvalidOperationException>(() => mods.ConfirmBaselineAsync(p,preview,true));
        Assert.Equal(before,await File.ReadAllBytesAsync(path));
    }
    [Fact] public async Task ExplicitReconciliationRemovesOnlyMissingBaselineEntries()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var file = await Install(ws,p); File.Delete(file);
        var mods = new LocalModManager(ws.Paths,ws.Store); var preview = await mods.PreviewBaselineAsync(p);
        Assert.Single(preview.Removed); Assert.Empty(preview.Files);
        await mods.ConfirmBaselineAsync(p,preview,true); Assert.Empty((await Check(ws,p)).Issues);
        Assert.Empty((await new ModInventoryStore(ws.Paths).ReadAsync(p.Id))!.Files);
    }
    [Fact] public async Task ActiveProfileLeasePreventsManualConfirmation()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var mods = new LocalModManager(ws.Paths,ws.Store);
        var preview = await mods.PreviewBaselineAsync(p); using var active = new ProfileUsage(ws.Paths).Acquire(p.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => mods.ConfirmBaselineAsync(p,preview,true));
    }
    [Fact] public async Task CommittedJournalRecoveryPersistsBaselineBeforeRemovingJournal()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var relative = "mods/recovered.jar";
        var target = SafePath.Join(ws.Paths.Game(p.Id),relative); Jar(target);
        var bytes = await File.ReadAllBytesAsync(target); var id = Guid.NewGuid();
        var path = SafePath.Join(ws.Paths.Profile(p.Id),"transactions/mods-"+id.ToString("N")+".json");
        await AtomicFile.WriteJsonAsync(path,new ModInstallJournal(id,p.Id,"Committed",[new(relative+".pending-"+id.ToString("N"),relative,Convert.ToHexString(SHA256.HashData(bytes)),bytes.Length)]));
        await new LocalModManager(ws.Paths,ws.Store).RecoverAsync(p);
        Assert.False(File.Exists(path)); Assert.Equal(bytes,await File.ReadAllBytesAsync(target));
        Assert.Empty((await Check(ws,p)).Issues); Assert.Single((await new ModInventoryStore(ws.Paths).ReadAsync(p.Id))!.Files);
    }
    [Fact] public async Task FullReadinessIncludesModFailuresWithoutNetworkOrAuthBypass()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); await Install(ws,p);
        using var handler = new StubHandler(_ => throw new Exception("Network prohibited")); using var http = new HttpClient(handler);
        var auth = new MicrosoftAuthentication(new(ws.Paths),http,new(){Offline=true});
        var result = await new OfflineReadiness(ws.Paths,new(),auth).CheckAsync(p);
        Assert.Equal(1,result.VerifiedMods); Assert.False(result.Ready); Assert.Contains(result.Issues,i=>i.Code=="account.missing");
        Assert.Equal(0,handler.Requests);
    }
    [Fact] public async Task RecoveryPreflightsAllTargetsBeforeDeletingAnyOfThem()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var transaction = Guid.NewGuid();
        var first = "mods/first.jar"; var second = "mods/second.jar";
        await File.WriteAllTextAsync(SafePath.Join(ws.Paths.Game(p.Id), first), "original");
        await File.WriteAllTextAsync(SafePath.Join(ws.Paths.Game(p.Id), second), "changed by user");
        var hash = Convert.ToHexString(SHA256.HashData("original"u8));
        var entries = new[]{first,second}.Select(f => new ModJournalEntry(f+".pending-"+transaction.ToString("N"),f,hash,8)).ToArray();
        var journal = SafePath.Join(ws.Paths.Profile(p.Id),"transactions/mods-"+transaction.ToString("N")+".json");
        await AtomicFile.WriteJsonAsync(journal,new ModInstallJournal(transaction,p.Id,"Prepared",entries));
        await Assert.ThrowsAsync<InvalidDataException>(()=>new LocalModManager(ws.Paths,ws.Store).RecoverAsync(p));
        Assert.Equal("original",await File.ReadAllTextAsync(SafePath.Join(ws.Paths.Game(p.Id),first)));
        Assert.Equal("changed by user",await File.ReadAllTextAsync(SafePath.Join(ws.Paths.Game(p.Id),second)));
        Assert.True(File.Exists(journal));
    }
    [Fact] public async Task RequiredMinecraftJavaAndLoaderVersionsAreAllChecked()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws);
        Jar(Path.Combine(ws.Paths.Game(p.Id),"mods","requirements.jar"), depends:"{\"minecraft\":\">=1.21 <1.22\",\"java\":\">=25\",\"fabricloader\":\">=0.21\"}");
        var issues = (await Check(ws,p)).Issues;
        Assert.Contains(issues,i=>i.Code=="mods.dependency.version" && i.Message.Contains("java"));
        Assert.Contains(issues,i=>i.Code=="mods.dependency.version" && i.Message.Contains("fabricloader"));
        Assert.DoesNotContain(issues,i=>i.Code=="mods.dependency.version" && i.Message.Contains("minecraft"));
    }
    [Fact] public async Task ForgeMetadataInFabricProfileIsReportedAsWrongLoader()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); var file = Path.Combine(ws.Paths.Game(p.Id),"mods","forge.jar");
        using(var zip=ZipFile.Open(file,ZipArchiveMode.Create))
        using(var text=new StreamWriter(zip.CreateEntry("META-INF/mods.toml").Open()))
            text.Write("modLoader=\"javafml\"\nloaderVersion=\"[52,)\"\nlicense=\"MIT\"\n[[mods]]\nmodId=\"forge_example\"\nversion=\"1.0\"\n");
        Assert.Contains((await Check(ws,p)).Issues,i=>i.Code=="mods.loader.mismatch");
    }
    [Theory]
    [InlineData("0.20.1","^0.16",ConstraintMatch.Match)]
    [InlineData("1.0.0","^0.16",ConstraintMatch.Mismatch)]
    [InlineData("0.15.0","^0.16",ConstraintMatch.Mismatch)]
    public void FabricCaretUsesOfficialMajorCeiling(string version,string expression,ConstraintMatch expected) =>
        Assert.Equal(expected,VersionConstraints.MatchFabric(version,expression));
    [Fact] public async Task CancellationIsNotConvertedIntoReadinessSuccess()
    {
        using var ws = new TestWorkspace(); var p = await Profile(ws); await Install(ws,p);
        using var ct = new CancellationTokenSource(); ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new InstalledModReadiness(ws.Paths,new(ws.Paths,ws.Store)).CheckAsync(p,Receipt(p),ct.Token));
    }
}
