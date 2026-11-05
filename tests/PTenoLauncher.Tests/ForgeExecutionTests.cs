using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Authentication;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class ForgeExecutionTests
{
    private static string Sha(byte[] b) => Convert.ToHexString(SHA1.HashData(b));
    private static byte[] Jar(string main, string manifest = "")
    {
        using var m = new MemoryStream();
        using (var z = new ZipArchive(m, ZipArchiveMode.Create, true))
        {
            using (var s = new StreamWriter(z.CreateEntry("META-INF/MANIFEST.MF").Open())) s.Write("Manifest-Version: 1.0\r\nMain-Class: " + main + "\r\n" + manifest + "\r\n");
            using var c = z.CreateEntry(main.Replace('.', '/') + ".class").Open(); c.Write("fixture class, never executed"u8);
        }
        return m.ToArray();
    }
    private sealed class Host(byte[] output) : IForgeProcessorHost
    {
        public int Executed, Verified;
        public bool OmitOutput;
        public byte[]? ReplacementOutput;
        public Dictionary<string, byte[]> OutputsByCoordinate { get; } = new();
        public Action<ForgeProcessorStep, string>? Before;
        public Task VerifyRuntimeAsync(int major, CancellationToken ct) { Assert.Equal(21, major); Verified++; return Task.CompletedTask; }
        public async Task ExecuteAsync(ForgeProcessorStep step, string stage, string log, CancellationToken ct)
        {
            Executed++; Before?.Invoke(step, stage);
            if (!OmitOutput) foreach (var o in step.Outputs) await File.WriteAllBytesAsync(o.Path, OutputsByCoordinate.GetValueOrDefault(o.Coordinate) ?? ReplacementOutput ?? output, ct);
        }
    }
    private sealed class Scenario : IDisposable
    {
        public TestWorkspace Ws { get; } = new();
        public ForgeComponentTests.Fixture F { get; }
        public Host Runner { get; }
        public ForgeInstaller Installer { get; }
        public byte[] Output { get; } = Jar("fixture.Generated");
        public GameProfile Profile = null!;
        private Guid otherProfileId;
        public InstallationReceipt Basis = null!;
        public ForgePreparedComponents Prepared = null!;
        public Scenario(ModLoader loader = ModLoader.Forge, Action<JsonObject, JsonObject>? mutate = null, string extraManifest = "")
        {
            F = new(Ws, loader, (i, v) => {
                i["processors"]![0]!["args"] = new JsonArray("--input", "{MINECRAFT_JAR}", "--patch", "{BINPATCH}", "--output", "{PATCHED}", "--side", "{SIDE}", "--task", "EXTRACT_FILES");
                i["processors"]![0]!["outputs"]!["{PATCHED}"] = "'" + Sha(Output) + "'";
                v["libraries"]!.AsArray().RemoveAt(2); // remote library remains an input, not a runtime JAR
                mutate?.Invoke(i, v);
            }, processorJar: Jar("fixture.Processor", extraManifest), runtimeJar: Jar("cpw.mods.bootstraplauncher.BootstrapLauncher"));
            Runner = new(Output);
            var downloads = new VerifiedDownloader(F.Http, F.Network); var versions = new VersionManager(Ws.Paths, downloads);
            var vanilla = new VanillaInstaller(Ws.Paths, versions, downloads, Ws.Store);
            Installer = new(Ws.Paths, F.Preparer, vanilla, Ws.Store, Runner);
        }
        public async Task Init()
        {
            Profile = await F.Profile(Ws); Prepared = await F.Preparer.PrepareAsync(Profile, F.Selected);
            var entries = new List<InstalledFile>();
            foreach (var (path, bytes) in new[] { ("fixture/client.jar", Jar("net.minecraft.client.main.Main")), ("assets/indexes/test.json", "{\"objects\":{}}"u8.ToArray()) })
            { await AtomicFile.WriteAsync(SafePath.Join(Ws.Paths.Cache, path), bytes); entries.Add(new(path, Sha(bytes), "SHA1", bytes.Length)); }
            Basis = new("1.21.1", 21, "net.minecraft.client.main.Main", DateTimeOffset.UtcNow, entries,
                ["fixture/client.jar"], [], ["-cp", "${classpath}"], ["--username", "${auth_player_name}"], "test");
            await AtomicFile.WriteJsonAsync(Ws.Paths.Receipt(Profile.Id), Basis);
            foreach (var p in new[] { "saves/synthetic.txt", "mods/sentinel.txt", "config/sentinel.txt" }) await File.WriteAllTextAsync(SafePath.Join(Ws.Paths.Game(Profile.Id), p), "SYNTHETIC USER DATA");
            var other = Ws.Profile(); otherProfileId = other.Id; await Ws.Store.SaveProfileAsync(other);
            await File.WriteAllTextAsync(SafePath.Join(Ws.Paths.Game(otherProfileId), "saves/other-profile.txt"), "SYNTHETIC OTHER PROFILE");
        }
        public async Task<InstallationReceipt> Complete(CancellationToken ct = default)
        { using var lease = new ProfileUsage(Ws.Paths).Acquire(Profile.Id); return await Installer.CompleteUnderLeaseAsync(Profile, Prepared, Basis, ct); }
        public async Task CacheVanilla()
        {
            var client = await File.ReadAllBytesAsync(SafePath.Join(Ws.Paths.Cache, "fixture/client.jar"));
            var assets = await File.ReadAllBytesAsync(SafePath.Join(Ws.Paths.Cache, "assets/indexes/test.json"));
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new {
                id = "1.21.1", type = "release", mainClass = "net.minecraft.client.main.Main", javaVersion = new { majorVersion = 21 },
                downloads = new { client = new { sha1 = Sha(client), size = client.Length, url = "https://piston-data.mojang.com/fixture-client.jar" } },
                libraries = Array.Empty<object>(), assetIndex = new { id = "test", sha1 = Sha(assets), size = assets.Length, url = "https://piston-meta.mojang.com/fixture-assets.json" },
                arguments = new { jvm = new[] { "-cp", "${classpath}" }, game = new[] { "--username", "${auth_player_name}" } }
            });
            var sha = Sha(bytes);
            await AtomicFile.WriteAsync(SafePath.Join(Ws.Paths.Cache, "metadata/versions/" + sha + ".json"), bytes);
            await AtomicFile.WriteAsync(SafePath.Join(Ws.Paths.Cache, "objects/sha1/" + Sha(client)[..2] + "/" + Sha(client) + ".jar"), client);
            await AtomicFile.WriteJsonAsync(SafePath.Join(Ws.Paths.Cache, "metadata/catalog.json"), new VersionCatalog(DateTimeOffset.UtcNow, "1.21.1",
                [new("1.21.1", "release", new("https://piston-meta.mojang.com/fixture-version.json"), sha, DateTimeOffset.UtcNow)]));
        }
        public async Task AssertSentinels()
        {
            foreach (var p in new[] { "saves/synthetic.txt", "mods/sentinel.txt", "config/sentinel.txt" }) Assert.Equal("SYNTHETIC USER DATA", await File.ReadAllTextAsync(SafePath.Join(Ws.Paths.Game(Profile.Id), p)));
            Assert.Equal("SYNTHETIC OTHER PROFILE", await File.ReadAllTextAsync(SafePath.Join(Ws.Paths.Game(otherProfileId), "saves/other-profile.txt")));
        }
        public void Dispose() { F.Dispose(); Ws.Dispose(); }
    }
    [Theory] [InlineData(ModLoader.Forge)] [InlineData(ModLoader.NeoForge)]
    public async Task VerifiedCompositionCommitsOnlyAfterOutputsAndRetainsIndependentFmlAndAccountGate(ModLoader loader)
    {
        using var s = new Scenario(loader); await s.Init(); var old = await File.ReadAllBytesAsync(s.Ws.Paths.Receipt(s.Profile.Id));
        s.Runner.Before = (step, stage) => {
            Assert.Equal(old, File.ReadAllBytes(s.Ws.Paths.Receipt(s.Profile.Id)));
            Assert.Contains(SafePath.Join(stage, "minecraft.jar"), step.Arguments); Assert.Contains("client", step.Arguments);
            Assert.Contains(SafePath.Join(stage, "data/data/client.lzma"), step.Arguments);
        };
        var result = await s.Complete(); ReceiptValidator.Validate(s.Ws.Paths, result);
        Assert.Equal(loader, result.Loader); Assert.Equal(s.F.Fml, result.Forge!.FmlCoordinate); Assert.Single(result.Forge.GeneratedFiles);
        Assert.Equal(1, s.Runner.Executed); Assert.Equal(1, s.Runner.Verified); Assert.Equal(21, result.JavaMajor);
        var operation = (await AtomicFile.ReadJsonAsync<ForgeOperation>(SafePath.Join(s.Ws.Paths.Cache, "forge/installations/" + result.Forge.OperationId + "/operation.json")))!;
        Assert.Equal(s.Profile.Id, operation.ProfileId); Assert.Equal("Committed", operation.State);
        Assert.Contains("forge/installations/", string.Join(" ", result.JvmArguments));
        Assert.Single(Directory.GetFiles(SafePath.Join(s.Ws.Paths.Profile(s.Profile.Id), "installation-backups")));
        var report = await new OfflineReadiness(s.Ws.Paths, new(), new MicrosoftAuthentication(new(s.Ws.Paths), s.F.Http, s.F.Network)).CheckAsync(s.Profile);
        Assert.DoesNotContain(report.Issues, i => i.Code is "loader.unsupported" or "manifest.corrupt" or "file.invalid");
        Assert.Contains(report.Issues, i => i.Code == "account.missing"); Assert.False(report.Ready); await s.AssertSentinels();
    }
    [Fact] public async Task OfflineRepeatRepairsCorruptInstallationFromVerifiedGeneratedCacheWithoutJavaOrHttp()
    {
        using var s = new Scenario(); await s.Init(); var first = await s.Complete(); var before = s.F.Handler.Requests; s.F.Network.Offline = true;
        await File.WriteAllTextAsync(SafePath.Join(s.Ws.Paths.Cache, first.Forge!.GeneratedFiles[0].RelativePath), "corrupt");
        var repaired = await s.Complete(); Assert.NotEqual(first.Forge.OperationId, repaired.Forge!.OperationId);
        Assert.Equal(1, s.Runner.Executed); Assert.Equal(before, s.F.Handler.Requests);
        foreach (var f in repaired.Files) Assert.True(await VerifiedDownloader.VerifyAsync(SafePath.Join(s.Ws.Paths.Cache, f.RelativePath), f.Hash, f.Algorithm, f.Size));
        await s.AssertSentinels();
    }
    [Theory] [InlineData("missing")][InlineData("wrong")][InlineData("input")][InlineData("exit")][InlineData("cancel")]
    public async Task FailedOrInterruptedOperationPreservesPreviousReceiptAndUserData(string fault)
    {
        using var s = new Scenario(); await s.Init(); var old = await File.ReadAllBytesAsync(s.Ws.Paths.Receipt(s.Profile.Id)); using var stop = new CancellationTokenSource();
        s.Runner.Before = (step, stage) => {
            if (fault == "input") File.WriteAllText(step.Classpath[0], "tampered during execution");
            if (fault == "exit") throw new IOException("Controlled exit code 7");
            if (fault == "cancel") stop.Cancel();
        };
        s.Runner.OmitOutput = fault == "missing"; if (fault == "wrong") s.Runner.ReplacementOutput = "wrong output"u8.ToArray();
        await Assert.ThrowsAnyAsync<Exception>(() => s.Complete(stop.Token));
        Assert.Equal(old, await File.ReadAllBytesAsync(s.Ws.Paths.Receipt(s.Profile.Id))); await s.AssertSentinels();
        var journal = await AtomicFile.ReadJsonAsync<ForgeOperation>(SafePath.Join(s.Ws.Paths.Profile(s.Profile.Id), "forge-operation.json")); Assert.Equal("Interrupted", journal!.State);
        s.Runner.Before = null; s.Runner.OmitOutput = false; s.Runner.ReplacementOutput = null;
        var repeated = await s.Complete(); Assert.Equal(ModLoader.Forge, repeated.Loader); await s.AssertSentinels();
    }
    [Theory] [InlineData("unknown-variable")][InlineData("outside")][InlineData("output-input")][InlineData("missing-output-hash")][InlineData("no-outputs")][InlineData("network-task")][InlineData("forward-reference")][InlineData("literal-path")]
    public async Task UnsupportedMetadataFailsBeforeAnyProcessorRuns(string fault)
    {
        using var s = new Scenario(mutate: (i, _) => {
            var p = i["processors"]![0]!;
            if (fault == "unknown-variable") p["args"]![0] = "{UNKNOWN}";
            if (fault == "outside") p["args"]![0] = "{ROOT}/../../outside";
            if (fault == "output-input") { p["outputs"]!.AsObject().Clear(); p["outputs"]!["{MINECRAFT_JAR}"] = "'" + new string('0', 40) + "'"; }
            if (fault == "missing-output-hash") p["outputs"]!["{PATCHED}"] = "''";
            if (fault == "no-outputs") p["outputs"]!.AsObject().Clear();
            if (fault == "network-task") p["args"]![0] = "DOWNLOAD_MOJMAPS";
            if (fault == "forward-reference") { i["data"]!["FUTURE"] = new JsonObject { ["client"] = "[fixture:future:1]", ["server"] = "'unused'" }; p["args"]![0] = "{FUTURE}"; }
            if (fault == "literal-path") p["args"]![0] = "C:\\Windows\\something";
        });
        await s.Init(); var old = await File.ReadAllBytesAsync(s.Ws.Paths.Receipt(s.Profile.Id));
        await Assert.ThrowsAnyAsync<Exception>(() => s.Complete()); Assert.Equal(0, s.Runner.Executed); Assert.Equal(0, s.Runner.Verified);
        Assert.Equal(old, await File.ReadAllBytesAsync(s.Ws.Paths.Receipt(s.Profile.Id))); await s.AssertSentinels();
    }
    [Theory] [InlineData("Class-Path: unverified.jar\r\n")][InlineData("Main-Class: fixture.Other\r\n")]
    public async Task ImplicitOrAmbiguousProcessorManifestCannotExecute(string extra)
    { using var s = new Scenario(extraManifest: extra); await s.Init(); await Assert.ThrowsAsync<NotSupportedException>(() => s.Complete()); Assert.Equal(0, s.Runner.Executed); }
    [Fact] public async Task InputsChangedAfterPreparationAreRejectedWithoutReceiptReplacement()
    {
        using var s = new Scenario(); await s.Init(); var old = await File.ReadAllBytesAsync(s.Ws.Paths.Receipt(s.Profile.Id));
        var input = s.Prepared.Files.Last(); await File.WriteAllTextAsync(SafePath.Join(s.Ws.Paths.Cache, input.RelativePath), "changed");
        await Assert.ThrowsAsync<InvalidDataException>(() => s.Complete()); Assert.Equal(0, s.Runner.Executed); Assert.Equal(old, await File.ReadAllBytesAsync(s.Ws.Paths.Receipt(s.Profile.Id)));
    }
    [Fact] public async Task ReadyJournalRecoversCommitAndRefusesAnUnrelatedNewerReceipt()
    {
        using var s = new Scenario(); await s.Init(); var old = await File.ReadAllBytesAsync(s.Ws.Paths.Receipt(s.Profile.Id)); var candidate = await s.Complete();
        var journalPath = SafePath.Join(s.Ws.Paths.Profile(s.Profile.Id), "forge-operation.json");
        var j = (await AtomicFile.ReadJsonAsync<ForgeOperation>(journalPath))!;
        await AtomicFile.WriteJsonAsync(journalPath, j with { State = "Ready" }); await AtomicFile.WriteAsync(s.Ws.Paths.Receipt(s.Profile.Id), old);
        var recovered = await s.Installer.RecoverAsync(s.Profile, s.F.Selected, default); Assert.Equal(candidate.Forge!.OperationId, recovered!.Forge!.OperationId);
        await AtomicFile.WriteJsonAsync(journalPath, j with { State = "Ready" });
        await AtomicFile.WriteJsonAsync(s.Ws.Paths.Receipt(s.Profile.Id), s.Basis with { InstalledAt = DateTimeOffset.UtcNow.AddMinutes(1) });
        var newer = await File.ReadAllBytesAsync(s.Ws.Paths.Receipt(s.Profile.Id));
        await Assert.ThrowsAsync<InvalidDataException>(() => s.Installer.RecoverAsync(s.Profile, s.F.Selected, default));
        Assert.Equal(newer, await File.ReadAllBytesAsync(s.Ws.Paths.Receipt(s.Profile.Id))); await s.AssertSentinels();
    }
    [Fact] public async Task RawPreparationIsNeverAcceptedAsAForgeInstallationReceipt()
    {
        using var s = new Scenario(); await s.Init();
        Assert.Throws<InvalidDataException>(() => ReceiptValidator.Validate(s.Ws.Paths, s.Basis with { Loader = ModLoader.Forge, LoaderVersion = s.F.Selected, MainClass = "cpw.mods.bootstraplauncher.BootstrapLauncher" }));
        Assert.False(s.Prepared.Plan.GameInstallationComplete);
    }
    [Fact] public async Task OrderedProcessorsUsePriorOutputsAndRecoverAfterPartialCompletion()
    {
        var intermediate = "synthetic mappings"u8.ToArray();
        using var s = new Scenario(mutate: (i, _) => {
            i["data"]!["MAPPINGS"] = new JsonObject { ["client"] = "[fixture:mappings:1@txt]", ["server"] = "'unused'" };
            var first = i["processors"]![0]!.DeepClone();
            first["args"] = new JsonArray("--output", "{MAPPINGS}", "--task", "MCP_DATA"); first["outputs"] = new JsonObject { ["{MAPPINGS}"] = "'" + Sha(intermediate) + "'" };
            i["processors"]!.AsArray().Insert(0, first);
            i["processors"]![1]!["args"]!.AsArray().Add("--mappings"); i["processors"]![1]!["args"]!.AsArray().Add("{MAPPINGS}");
        });
        await s.Init(); s.Runner.OutputsByCoordinate.Add("fixture:mappings:1@txt", intermediate);
        s.Runner.Before = (step, stage) => {
            if (step.Outputs[0].Coordinate == "net.minecraft:client:1.21.1:patched")
            { Assert.Equal(intermediate, File.ReadAllBytes(SafePath.Join(stage, "libraries/fixture/mappings/1/mappings-1.txt"))); throw new IOException("Controlled second-stage failure"); }
        };
        await Assert.ThrowsAsync<IOException>(() => s.Complete()); Assert.Equal(2, s.Runner.Executed);
        var requests = s.F.Handler.Requests; s.F.Network.Offline = true; s.Runner.Before = null;
        var result = await s.Complete(); Assert.Equal(3, s.Runner.Executed); Assert.Equal(2, result.Forge!.GeneratedFiles.Count);
        Assert.Equal(requests, s.F.Handler.Requests); await s.AssertSentinels();
    }
    [Fact] public async Task CorruptReadyCandidateIsMarkedInterruptedAndCanBeRebuilt()
    {
        using var s = new Scenario(); await s.Init(); var candidate = await s.Complete();
        var path = SafePath.Join(s.Ws.Paths.Profile(s.Profile.Id), "forge-operation.json"); var j = (await AtomicFile.ReadJsonAsync<ForgeOperation>(path))!;
        await AtomicFile.WriteJsonAsync(path, j with { State = "Ready" });
        await File.WriteAllTextAsync(SafePath.Join(s.Ws.Paths.Cache, candidate.Forge!.GeneratedFiles[0].RelativePath), "changed");
        Assert.Null(await s.Installer.RecoverAsync(s.Profile, s.F.Selected, default));
        Assert.Equal("Interrupted", (await AtomicFile.ReadJsonAsync<ForgeOperation>(path))!.State);
        Assert.Equal(ModLoader.Forge, (await s.Complete()).Loader);
    }
    [Fact] public async Task RecoveryDoesNotRaceAStillRunningTrackedProcess()
    {
        using var s = new Scenario(); await s.Init(); var candidate = await s.Complete();
        using var current = Process.GetCurrentProcess();
        var file = SafePath.Join(s.Ws.Paths.Cache, "forge/installations/" + candidate.Forge!.OperationId + "/active-processor.json");
        await AtomicFile.WriteJsonAsync(file, new ActiveGameRecord(current.Id, current.StartTime.ToUniversalTime().Ticks, current.MainModule!.FileName));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Installer.RecoverAsync(s.Profile, s.F.Selected, default));
        Assert.False(current.HasExited); await s.AssertSentinels();
    }
    [Theory] [InlineData(false)][InlineData(true)]
    public async Task PublicInstallerComposesVanillaWithoutPublishingItBeforeLoaderCommit(bool fail)
    {
        using var s = new Scenario(); await s.Init(); await s.CacheVanilla(); s.F.Network.Offline = true;
        var old = await File.ReadAllBytesAsync(s.Ws.Paths.Receipt(s.Profile.Id)); var requests = s.F.Handler.Requests;
        s.Runner.Before = (_, _) => { Assert.Equal(old, File.ReadAllBytes(s.Ws.Paths.Receipt(s.Profile.Id))); if (fail) throw new IOException("Controlled processor failure"); };
        if (fail)
        {
            await Assert.ThrowsAsync<IOException>(() => s.Installer.InstallAsync(s.Profile, s.F.Selected));
            Assert.Equal(old, await File.ReadAllBytesAsync(s.Ws.Paths.Receipt(s.Profile.Id)));
        }
        else Assert.Equal(ModLoader.Forge, (await s.Installer.InstallAsync(s.Profile, s.F.Selected)).Loader);
        Assert.Equal(requests, s.F.Handler.Requests); await s.AssertSentinels();
    }
    [AvaloniaFact] public async Task ActualUiCanRepairSupportedCachedInstallationAndRetainsLicensedReadinessGate()
    {
        using var s = new Scenario(); await s.Init(); await s.CacheVanilla(); await s.Complete();
        using var services = new LauncherServices(s.Ws.Paths); var vm = new MainViewModel(services); await vm.InitializeAsync(); vm.Offline = true;
        vm.SelectedProfile = vm.Profiles.Single(p => p.Id == s.Profile.Id); var window = new MainWindow { DataContext = vm }; window.Show();
        try
        {
            Assert.False(vm.CanInstallGame); await vm.LoadForgeVersionsAsync(); Assert.True(vm.CanInstallGame);
            Assert.True(window.FindControl<Button>("InstallGameButton")!.IsEnabled);
            await vm.InstallCommand.ExecuteAsync(); Assert.True(vm.LastOperationSucceeded);
            var receipt = (await AtomicFile.ReadJsonAsync<InstallationReceipt>(s.Ws.Paths.Receipt(s.Profile.Id)))!;
            Assert.Equal(ModLoader.Forge, receipt.Loader); ReceiptValidator.Validate(s.Ws.Paths, receipt);
            var readiness = await services.Readiness.CheckAsync(s.Profile);
            Assert.False(readiness.Ready); Assert.Contains(readiness.Issues, i => i.Code == "account.missing"); await s.AssertSentinels();
        }
        finally { window.Close(); }
    }
    private static ProcessStartInfo PowerShell(string command)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe"));
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-Command", command }) info.ArgumentList.Add(a); return info;
    }
    [Fact] public async Task ControlledProcessCapturesExitAndBoundedLogWithoutRunningAForgeJar()
    {
        using var ws = new TestWorkspace(); var log = SafePath.Join(ws.Paths.Root, "processor.log");
        await ForgeProcessorHost.RunCommandAsync(PowerShell("Write-Output 'controlled processor orchestration'; exit 0"), log, TimeSpan.FromSeconds(5), default);
        Assert.Contains("Exit code: 0", await File.ReadAllTextAsync(log));
        var error = await Assert.ThrowsAsync<IOException>(() => ForgeProcessorHost.RunCommandAsync(PowerShell("exit 7"), SafePath.Join(ws.Paths.Root, "failed.log"), TimeSpan.FromSeconds(5), default)); Assert.Contains("7", error.Message);
    }
    [Theory] [InlineData(false)][InlineData(true)]
    public async Task ControlledProcessTimeoutAndCancellationTerminateOwnedProcess(bool cancel)
    {
        using var ws = new TestWorkspace(); using var stop = new CancellationTokenSource(); if (cancel) stop.CancelAfter(500);
        var task = ForgeProcessorHost.RunCommandAsync(PowerShell("Start-Sleep -Seconds 30"), SafePath.Join(ws.Paths.Root, "timeout.log"), TimeSpan.FromMilliseconds(800), stop.Token);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task); else await Assert.ThrowsAsync<TimeoutException>(() => task);
    }
}
