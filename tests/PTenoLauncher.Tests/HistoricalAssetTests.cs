using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Authentication;
using PTenoLauncher.Infrastructure.Diagnostics;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class HistoricalAssetTests
{
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
    private sealed class Sink(Action<DownloadProgress> callback) : IProgress<DownloadProgress>
    { public void Report(DownloadProgress value) => callback(value); }
    private sealed class Fixture : IDisposable
    {
        public NetworkPolicy Network { get; } = new();
        public StubHandler Handler { get; }
        public VanillaInstaller Installer { get; }
        public HttpClient Http { get; }
        public byte[] Content { get; } = "synthetic sound, never executed"u8.ToArray();
        public bool Tamper { get; set; }
        public Fixture(TestWorkspace ws, bool virtualAssets, bool mapped, bool modern = false, string? invalidName = null)
        {
            var client = "synthetic client, never executed"u8.ToArray();
            var assets = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> {
                ["virtual"] = virtualAssets, ["map_to_resources"] = mapped,
                ["objects"] = new Dictionary<string, object> {
                    [invalidName ?? "sound/test.ogg"] = new { hash = Hash(Content), size = Content.Length },
                    ["lang/en_US.lang"] = new { hash = Hash(Content), size = Content.Length }
                }
            });
            var manifest = new Dictionary<string, object> {
                ["id"] = "1.21.1", ["type"] = modern ? "snapshot" : "release", ["mainClass"] = "net.minecraft.client.main.Main",
                ["downloads"] = new { client = new { sha1 = Hash(client), size = client.Length, url = "https://piston-data.mojang.com/client.jar" } },
                ["libraries"] = Array.Empty<object>(),
                ["assetIndex"] = new { id = "legacy", sha1 = Hash(assets), size = assets.Length, url = "https://piston-meta.mojang.com/assets.json" }
            };
            if (modern) manifest["arguments"] = new { jvm = new[] { "-cp", "${classpath}" }, game = new[] { "--assetsDir", "${assets_root}", "--assetIndex", "${assets_index_name}" } };
            else manifest["minecraftArguments"] = "--gameDir ${game_directory} --assetsDir ${game_assets} --versionType ${version_type}";
            var version = JsonSerializer.SerializeToUtf8Bytes(manifest);
            var catalog = JsonSerializer.SerializeToUtf8Bytes(new { latest = new { release = "1.21.1" }, versions = new[] {
                new { id = "1.21.1", type = "release", url = "https://piston-meta.mojang.com/test.json", sha1 = Hash(version), releaseTime = "2024-08-08T00:00:00Z" }
            }});
            Handler = new StubHandler(r => new(HttpStatusCode.OK) { Content = new ByteArrayContent(r.RequestUri!.AbsolutePath switch {
                "/mc/game/version_manifest_v2.json" => catalog, "/test.json" => version, "/client.jar" => client,
                "/assets.json" => assets, _ when r.RequestUri.Host == "resources.download.minecraft.net" => Tamper ? new byte[Content.Length] : Content,
                _ => throw new InvalidOperationException("Unexpected fixture URL")
            }) });
            Http = new(Handler); var downloader = new VerifiedDownloader(Http, Network); var versions = new VersionManager(ws.Paths, downloader);
            Installer = new(ws.Paths, versions, downloader, ws.Store);
        }
        public async Task Initialize(TestWorkspace ws, GameProfile profile)
        { await ws.Store.InitializeAsync(); await ws.Store.SaveProfileAsync(profile); await new VersionManager(ws.Paths, new(Http, Network)).RefreshAsync(); }
        public void Dispose() => Http.Dispose();
    }

    [Theory]
    [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)] [InlineData(false, false)]
    public async Task InstallsLayoutsDeduplicatesObjectsAndReusesOffline(bool virtualAssets, bool mapped)
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(); using var fixture = new Fixture(ws, virtualAssets, mapped);
        await fixture.Initialize(ws, p);
        var sentinel = SafePath.Join(ws.Paths.Game(p.Id), "saves/synthetic.txt"); await File.WriteAllTextAsync(sentinel, "not a real world");
        var receipt = await fixture.Installer.InstallAsync(p); ReceiptValidator.Validate(ws.Paths, receipt);
        Assert.Single(receipt.Files, f => f.RelativePath.StartsWith("assets/objects/", StringComparison.Ordinal));
        Assert.Equal(virtualAssets || mapped, receipt.LegacyAssets is not null);
        if (virtualAssets || mapped)
        {
            Assert.Equal(fixture.Content, await File.ReadAllBytesAsync(SafePath.Join(AssetManager.GameAssets(ws.Paths, p.Id, receipt), "sound/test.ogg")));
            Assert.Empty(await AssetManager.CheckAsync(ws.Paths, p.Id, receipt));
        }
        if (mapped) Assert.Equal(fixture.Content, await File.ReadAllBytesAsync(SafePath.Join(ws.Paths.Game(p.Id), "resources/sound/test.ogg")));
        var requests = fixture.Handler.Requests; fixture.Network.Offline = true;
        var again = await fixture.Installer.InstallAsync(p);
        Assert.Equal(requests, fixture.Handler.Requests);
        Assert.Equal(JsonSerializer.Serialize(receipt.LegacyAssets), JsonSerializer.Serialize(again.LegacyAssets));
        Assert.Equal("not a real world", await File.ReadAllTextAsync(sentinel));
    }
    [Theory] [InlineData(true, false)] [InlineData(false, true)]
    public async Task CorruptOwnedProjectionIsBackedUpAndRepairedOffline(bool virtualAssets, bool mapped)
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(); using var f = new Fixture(ws, virtualAssets, mapped); await f.Initialize(ws, p);
        var receipt = await f.Installer.InstallAsync(p); var requests = f.Handler.Requests; f.Network.Offline = true;
        var target = SafePath.Join(AssetManager.GameAssets(ws.Paths, p.Id, receipt), "sound/test.ogg");
        await File.WriteAllTextAsync(target, "changed tracked bytes");
        Assert.NotEmpty(await AssetManager.CheckAsync(ws.Paths, p.Id, receipt));
        await f.Installer.InstallAsync(p);
        Assert.Equal(f.Content, await File.ReadAllBytesAsync(target)); Assert.Equal(requests, f.Handler.Requests);
        Assert.Contains(Directory.EnumerateFiles(ws.Paths.Root, "test.ogg", SearchOption.AllDirectories), p => p.Contains(".backups", StringComparison.Ordinal) && File.ReadAllText(p) == "changed tracked bytes");
        Assert.Empty(await AssetManager.CheckAsync(ws.Paths, p.Id, receipt));
    }
    [Fact] public async Task UntrackedMappedUserFilePreventsCommitAndIsNeverOverwritten()
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(); using var f = new Fixture(ws, false, true); await f.Initialize(ws, p);
        var file = SafePath.Join(ws.Paths.Game(p.Id), "resources/sound/test.ogg"); Directory.CreateDirectory(Path.GetDirectoryName(file)!); await File.WriteAllTextAsync(file, "user data");
        await Assert.ThrowsAsync<IOException>(() => f.Installer.InstallAsync(p));
        Assert.Equal("user data", await File.ReadAllTextAsync(file)); Assert.False(File.Exists(ws.Paths.Receipt(p.Id)));
        Assert.False(File.Exists(SafePath.Join(ws.Paths.Profile(p.Id), "mapped-assets.json")));
    }
    [Fact] public async Task UntrackedFileAppearingAfterPreflightIsPreserved()
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(); using var f = new Fixture(ws, false, true); await f.Initialize(ws, p);
        var later = SafePath.Join(ws.Paths.Game(p.Id), "resources/lang/en_US.lang");
        await Assert.ThrowsAsync<IOException>(() => f.Installer.InstallAsync(p, new Sink(v =>
        {
            if (v.Name == "assets/sound/test.ogg") { Directory.CreateDirectory(Path.GetDirectoryName(later)!); File.WriteAllText(later, "late user file"); }
        })));
        Assert.Equal("late user file", await File.ReadAllTextAsync(later)); Assert.False(File.Exists(ws.Paths.Receipt(p.Id)));
    }
    [Fact] public async Task ChangedPendingCreationIsNotMistakenForAnOwnedRepairTarget()
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(); using var f = new Fixture(ws, false, true); await f.Initialize(ws, p);
        using var stop = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Installer.InstallAsync(p,
            new Sink(v => { if (v.Name.StartsWith("assets/", StringComparison.Ordinal)) stop.Cancel(); }), stop.Token));
        var path = SafePath.Join(ws.Paths.Game(p.Id), "resources/sound/test.ogg"); await File.WriteAllTextAsync(path, "changed before recovery");
        f.Network.Offline = true; var requests = f.Handler.Requests;
        await Assert.ThrowsAsync<IOException>(() => f.Installer.InstallAsync(p));
        Assert.Equal("changed before recovery", await File.ReadAllTextAsync(path)); Assert.Equal(requests, f.Handler.Requests);
    }
    [Theory] [InlineData(true, false)] [InlineData(false, true)]
    public async Task CancellationLeavesDurableIntentAndOfflineRetryRecovers(bool virtualAssets, bool mapped)
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(); using var f = new Fixture(ws, virtualAssets, mapped); await f.Initialize(ws, p);
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Installer.InstallAsync(p,
            new Sink(v => { if (v.Name.StartsWith("assets/", StringComparison.Ordinal)) cts.Cancel(); }), cts.Token));
        Assert.False(File.Exists(ws.Paths.Receipt(p.Id)));
        var requests = f.Handler.Requests; f.Network.Offline = true;
        var receipt = await f.Installer.InstallAsync(p);
        Assert.Empty(await AssetManager.CheckAsync(ws.Paths, p.Id, receipt)); Assert.Equal(requests, f.Handler.Requests);
        Assert.Empty(Directory.EnumerateFiles(ws.Paths.Root, "*.part", SearchOption.AllDirectories));
    }
    [Fact] public async Task ReadinessDetectsMappedDamageWithoutAnyAccountOrHttp()
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(); using var f = new Fixture(ws, false, true); await f.Initialize(ws, p);
        await f.Installer.InstallAsync(p); f.Network.Offline = true; var requests = f.Handler.Requests;
        File.Delete(SafePath.Join(ws.Paths.Game(p.Id), "resources/sound/test.ogg"));
        var auth = new MicrosoftAuthentication(new(ws.Paths), f.Http, f.Network);
        var report = await new OfflineReadiness(ws.Paths, new(), auth).CheckAsync(p);
        Assert.Contains(report.Issues, i => i.Code == "account.missing"); Assert.Contains(report.Issues, i => i.Code == "file.invalid");
        Assert.False(report.Ready); Assert.Equal(requests, f.Handler.Requests);
    }
    [Fact] public async Task UncachedOfflineAssetAndBadDownloadNeverCommitOrReplaceOldReceipt()
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(); using var f = new Fixture(ws, true, false); await f.Initialize(ws, p);
        var installed = await f.Installer.InstallAsync(p); var before = await File.ReadAllBytesAsync(ws.Paths.Receipt(p.Id));
        var asset = installed.Files.Single(f => f.RelativePath.StartsWith("assets/objects/", StringComparison.Ordinal));
        File.Delete(SafePath.Join(ws.Paths.Cache, asset.RelativePath)); f.Network.Offline = true; var requests = f.Handler.Requests;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Installer.InstallAsync(p)); Assert.Equal(requests, f.Handler.Requests);
        Assert.Equal(before, await File.ReadAllBytesAsync(ws.Paths.Receipt(p.Id)));
        f.Network.Offline = false; f.Tamper = true;
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Installer.InstallAsync(p));
        Assert.Equal(before, await File.ReadAllBytesAsync(ws.Paths.Receipt(p.Id)));
        Assert.Empty(Directory.EnumerateFiles(ws.Paths.Root, "*.part", SearchOption.AllDirectories));
        f.Tamper = false; await f.Installer.InstallAsync(p); Assert.Empty(await AssetManager.CheckAsync(ws.Paths, p.Id, installed));
    }
    [Fact] public async Task MalformedOwnershipJournalFailsClosedAndPreservesMappedUserData()
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(); using var f = new Fixture(ws, false, true); await f.Initialize(ws, p);
        var receipt = await f.Installer.InstallAsync(p); f.Network.Offline = true;
        var journal = SafePath.Join(ws.Paths.Profile(p.Id), "mapped-assets.json"); await File.WriteAllTextAsync(journal, "{}");
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Installer.InstallAsync(p));
        Assert.Equal(f.Content, await File.ReadAllBytesAsync(SafePath.Join(ws.Paths.Game(p.Id), "resources/sound/test.ogg")));
        Assert.Equal(receipt.Version, (await AtomicFile.ReadJsonAsync<InstallationReceipt>(ws.Paths.Receipt(p.Id)))!.Version);
    }
    // Requires a Windows environment that permits creating isolated junction fixtures.
    // This laptop returned Access is denied before preparation; never bypass that restriction.
    [Theory(Explicit = true)] [InlineData(false)] [InlineData(true)]
    public async Task JunctionInMappedResourcesIsRejectedWithoutWritingThroughIt(bool dangling)
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(); using var f = new Fixture(ws, false, true); await f.Initialize(ws, p);
        var outside = SafePath.Join(ws.Paths.Root, "synthetic-other-owner"); Directory.CreateDirectory(outside);
        var link = SafePath.Join(ws.Paths.Game(p.Id), "resources");
        var cmd = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe")) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "/d", "/c", "mklink", "/J", link, outside }) cmd.ArgumentList.Add(a);
        using (var process = Process.Start(cmd)!)
        {
            var stdout = await process.StandardOutput.ReadToEndAsync(); var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(); Assert.True(process.ExitCode == 0, "Junction fixture failed: " + stdout + stderr);
        }
        try
        {
            if (dangling) Directory.Delete(outside);
            await Assert.ThrowsAsync<IOException>(() => f.Installer.InstallAsync(p));
            if (dangling) Assert.False(Directory.Exists(outside)); else Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
            Assert.False(File.Exists(ws.Paths.Receipt(p.Id)));
        }
        finally { Directory.Delete(link); }
    }
    [Fact] public async Task FailedNewIndexPreservesPriorReceiptAndModernMetadata()
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(); using var f = new Fixture(ws, false, false, modern: true); await f.Initialize(ws, p);
        var receipt = await f.Installer.InstallAsync(p); Assert.Null(receipt.LegacyAssets); Assert.Equal("snapshot", receipt.VersionType);
        var before = await File.ReadAllBytesAsync(ws.Paths.Receipt(p.Id));
        using var bad = new Fixture(ws, true, false, invalidName: "../escape"); await bad.Initialize(ws, p);
        await Assert.ThrowsAsync<InvalidDataException>(() => bad.Installer.InstallAsync(p));
        Assert.Equal(before, await File.ReadAllBytesAsync(ws.Paths.Receipt(p.Id)));
    }
    [Fact] public async Task VirtualProjectionSharedButMappedResourcesAndDeletionRemainIsolated()
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(); var other = p with { Id = Guid.NewGuid() };
        using var f = new Fixture(ws, true, true); await f.Initialize(ws, p); await ws.Store.SaveProfileAsync(other);
        var receipt = await f.Installer.InstallAsync(p); f.Network.Offline = true;
        var second = await f.Installer.InstallAsync(other);
        Assert.Equal(AssetManager.GameAssets(ws.Paths, p.Id, receipt), AssetManager.GameAssets(ws.Paths, other.Id, second));
        Assert.NotEqual(ws.Paths.Game(p.Id), ws.Paths.Game(other.Id));
        var shared = AssetManager.GameAssets(ws.Paths, p.Id, receipt);
        // Only this isolated fixture instance is removed; no user world exists in this test workspace.
        Directory.Delete(ws.Paths.Profile(p.Id), true);
        Assert.True(Directory.Exists(shared)); Assert.Empty(await AssetManager.CheckAsync(ws.Paths, other.Id, second));
    }
    [Theory]
    [InlineData("../escape")] [InlineData("C:/escape")] [InlineData("/absolute")] [InlineData("a\\b")]
    [InlineData("a//b")] [InlineData("CON.txt")] [InlineData("a./b")] [InlineData("a/file:stream")]
    [InlineData("a?.ogg")] [InlineData("a\nfile")]
    public void UnsafeNamesAreRejectedBeforeProjection(string name)
    {
        using var ws = new TestWorkspace();
        Assert.Throws<InvalidDataException>(() => AssetIndexPlan.ValidateEntries(ws.Paths.Cache, [new(name, new string('a', 40), "SHA1", 1)]));
    }
    [Theory] [InlineData("Sound/test", "sound/Test")] [InlineData("sound", "sound/test")]
    public void CaseAndFileDirectoryCollisionsAreRejected(string first, string second)
    {
        using var ws = new TestWorkspace();
        Assert.Throws<InvalidDataException>(() => AssetIndexPlan.ValidateEntries(ws.Paths.Cache,
            [new(first, new string('a', 40), "SHA1", 1), new(second, new string('a', 40), "SHA1", 1)]));
    }
    [Theory] [InlineData("../index")] [InlineData("x/y")] [InlineData("")] [InlineData(".")]
    public void UnsafeIndexIdsAreRejected(string value) => Assert.Throws<InvalidDataException>(() => AssetIndexPlan.ValidateId(value));
    [Fact] public void DuplicateJsonPropertiesAndConflictingSizesAreRejected()
    {
        using var ws = new TestWorkspace(); var hash = new string('a', 40);
        var duplicate = "{\"objects\":{\"a\":{\"hash\":\"" + hash + "\",\"size\":1},\"a\":{\"hash\":\"" + hash + "\",\"size\":1}}}";
        Assert.Throws<InvalidDataException>(() => AssetIndexPlan.Parse(Encoding.UTF8.GetBytes(duplicate), ws.Paths.Cache));
        Assert.Throws<InvalidDataException>(() => AssetIndexPlan.ValidateEntries(ws.Paths.Cache, [new("a", hash, "SHA1", 1), new("b", hash, "SHA1", 2)]));
    }
    [Fact] public async Task ForgedLayoutCannotAddUnverifiedObjectsToAReceipt()
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(); using var f = new Fixture(ws, true, false); await f.Initialize(ws, p);
        var receipt = await f.Installer.InstallAsync(p);
        var changed = receipt with { LegacyAssets = receipt.LegacyAssets! with { Entries = [new("extra.ogg", new string('f', 40), "SHA1", 2)] } };
        Assert.Throws<InvalidDataException>(() => ReceiptValidator.Validate(ws.Paths, changed));
        Assert.Throws<InvalidDataException>(() => ReceiptValidator.Validate(ws.Paths, receipt with { LegacyAssets = receipt.LegacyAssets! with { IndexSha1 = new string('f', 40) } }));
    }
    [Theory] [InlineData(true, false)] [InlineData(false, true)]
    public async Task FreshInstallerRecoversFromInterruptedJournalAfterAllFilesWereCopied(bool virtualAssets, bool mapped)
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(); using var f = new Fixture(ws, virtualAssets, mapped); await f.Initialize(ws, p);
        var receipt = await f.Installer.InstallAsync(p); f.Network.Offline = true; var requests = f.Handler.Requests;
        var journal = virtualAssets ? SafePath.Join(ws.Paths.Cache, $"assets/projections/legacy-{receipt.LegacyAssets!.IndexSha1}.json") : SafePath.Join(ws.Paths.Profile(p.Id), "mapped-assets.json");
        var state = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(journal))!; state["inProgress"] = true;
        await AtomicFile.WriteAsync(journal, Encoding.UTF8.GetBytes(state.ToJsonString()));
        Assert.NotEmpty(await AssetManager.CheckAsync(ws.Paths, p.Id, receipt));
        // Simulates restart at the durable boundary after copy but before completion. Not a physical OS crash test.
        var downloads = new VerifiedDownloader(f.Http, f.Network);
        var fresh = new VanillaInstaller(ws.Paths, new(ws.Paths, downloads), downloads, new SqliteLauncherStore(ws.Paths));
        var recovered = await fresh.InstallAsync(p);
        Assert.Empty(await AssetManager.CheckAsync(ws.Paths, p.Id, recovered)); Assert.Equal(requests, f.Handler.Requests);
    }
    [Fact] public async Task RenamedLayoutEvenWithVerifiedObjectHashFailsIndexContentValidation()
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(); using var f = new Fixture(ws, true, false); await f.Initialize(ws, p);
        var receipt = await f.Installer.InstallAsync(p); var entries = receipt.LegacyAssets!.Entries.ToArray(); entries[0] = entries[0] with { RelativePath = "unexpected.ogg" };
        var changed = receipt with { LegacyAssets = receipt.LegacyAssets with { Entries = entries } };
        ReceiptValidator.Validate(ws.Paths, changed);
        await Assert.ThrowsAsync<InvalidDataException>(() => AssetManager.CheckAsync(ws.Paths, p.Id, changed));
    }
    [Fact] public async Task RealControlledProcessReceivesExpandedPathsAndLiteralArgumentsWithoutShell()
    {
        using var ws = new TestWorkspace(); var p = ws.Profile(); using var f = new Fixture(ws, true, false); await f.Initialize(ws, p);
        var receipt = await f.Installer.InstallAsync(p);
        var values = new Dictionary<string, string> { ["game_assets"] = AssetManager.GameAssets(ws.Paths, p.Id, receipt), ["game_directory"] = ws.Paths.Game(p.Id),
            ["version_type"] = receipt.VersionType, ["natives_directory"] = "C:/synthetic path/natives", ["classpath"] = string.Join(Path.PathSeparator, receipt.Classpath.Select(f => SafePath.Join(ws.Paths.Cache, f))) };
        var expanded = receipt.JvmArguments.Concat(receipt.GameArguments).Select(a => LaunchArgumentExpansion.Expand(a, values)).Concat(["two words", "", "${literal}", "quote\"value", "українська"]).ToArray();
        var probe = PTenoLauncher.Testing.OwnedTestRoot.BuildArtifact("PTenoLauncher.IntegrationSmoke", "PTenoLauncher.IntegrationSmoke", "tests/PTenoLauncher.IntegrationSmoke/bin/Release/net10.0/PTenoLauncher.IntegrationSmoke.dll");
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { } d ? Path.Combine(d, "dotnet.exe") : "dotnet");
        var start = new ProcessStartInfo(dotnet) { WorkingDirectory = ws.Paths.Root, UseShellExecute = false };
        start.ArgumentList.Add(probe); start.ArgumentList.Add("--argument-probe"); foreach (var a in expanded) start.ArgumentList.Add(a);
        var log = SafePath.Join(ws.Paths.Root, "argument-probe.log");
        Assert.Equal(0, await LiveProcessRunner.RunAsync(start, log));
        var output = (await File.ReadAllLinesAsync(log)).Single(l => l.Contains("[out] [", StringComparison.Ordinal));
        Assert.Equal(expanded, JsonSerializer.Deserialize<string[]>(output[(output.IndexOf("[out] ", StringComparison.Ordinal) + 6)..]));
        Assert.Throws<NotSupportedException>(() => LaunchArgumentExpansion.Expand("${unknown}", values));
        Assert.Equal(["--empty", "", "two words"], LegacyArguments.Split("--empty \"\" \"two words\""));
    }
}
