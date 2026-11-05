using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class ModUpdateTests
{
    internal sealed class Fixture : IDisposable
    {
        internal TestWorkspace Ws { get; } = new();
        internal GameProfile Profile { get; private set; } = null!;
        internal NetworkPolicy Network { get; } = new();
        internal Dictionary<string, byte[]> Bytes { get; } = new();
        internal ModUpdateManager Manager { get; set; } = null!;
        internal LocalModManager Local { get; private set; } = null!;
        internal bool RequiredB, CorruptDownload, FailDownload, Beta;
        internal string? ProviderFileName;
        internal int Requests, Downloads;
        private HttpClient http = null!;
        private ModrinthClient catalog = null!;
        private VerifiedDownloader downloader = null!;
        internal static byte[] Jar(string id, string version, object? depends = null)
        {
            using var m = new MemoryStream();
            using (var zip = new ZipArchive(m, ZipArchiveMode.Create, true))
            using (var w = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open()))
                w.Write(JsonSerializer.Serialize(new { schemaVersion = 1, id, version, name = id, environment = "*", depends = depends ?? new { } }));
            return m.ToArray();
        }
        internal string Physical(string relative) => SafePath.Join(Ws.Paths.Game(Profile.Id), relative);
        internal string PinPath => SafePath.Join(Ws.Paths.Profile(Profile.Id), "mod-pins.json");
        internal async Task Start(bool requiredB = false, ModLoader loader = ModLoader.Fabric)
        {
            RequiredB = requiredB; await Ws.Store.InitializeAsync(); Profile = Ws.Profile(loader); await Ws.Store.SaveProfileAsync(Profile);
            Bytes["A1"] = Jar("a", "9.0.0"); Bytes["A2"] = Jar("a", "1.0.0", requiredB ? new { b = ">=2.0.0" } : null);
            Bytes["B1"] = Jar("b", "1.0.0"); Bytes["B2"] = Jar("b", "2.0.0");
            if (loader == ModLoader.Quilt)
            {
                foreach (var id in Bytes.Keys.ToArray()) Bytes[id] = QuiltJar(id[..1].ToLowerInvariant(), id.EndsWith('1') ? "1.0.0" : "2.0.0");
                await AtomicFile.WriteJsonAsync(Ws.Paths.Receipt(Profile.Id), new InstallationReceipt(Profile.MinecraftVersion, 21, "org.quiltmc.loader.impl.launch.knot.KnotClient", DateTimeOffset.UtcNow, [], [], [], [], [], "fixture", loader, "0.30.1"));
            }
            http = new(new StubHandler(request =>
            {
                Requests++; var path = request.RequestUri!.AbsolutePath;
                if (request.RequestUri.Host == "cdn.modrinth.com")
                {
                    Downloads++; if (FailDownload) return new(HttpStatusCode.ServiceUnavailable);
                    var data = Bytes[Path.GetFileNameWithoutExtension(path)].ToArray(); if (CorruptDownload) data[^1] ^= 1;
                    return new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
                }
                foreach (var id in new[] { "A", "B" })
                {
                    if (path == "/v2/project/" + id) return Json(new { id, slug = id, title = id, project_type = "mod", client_side = "required" });
                    if (path == "/v2/project/" + id + "/version") return Json(new[] { Version(id + "1"), Version(id + "2") });
                }
                foreach (var (id, data) in Bytes)
                    if (path == "/v2/version/" + id || path == "/v2/version_file/" + Sha512(data)) return Json(Version(id));
                return new(HttpStatusCode.NotFound);
            }));
            downloader = new(http, Network); catalog = new(Ws.Paths, downloader, Network); Local = new(Ws.Paths, Ws.Store); Manager = Reopen();
            await Add("mods/unrelated-name.jar", Bytes["A1"]);
        }
        private object Version(string id) => new
        {
            id, project_id = id[..1], version_number = id == "A1" ? "9.0.0" : "1.0.0", name = id,
            date_published = id.EndsWith('1') ? "2025-01-01T00:00:00Z" : "2026-01-01T00:00:00Z", version_type = Beta && id == "A2" ? "beta" : "release", status = "listed",
            game_versions = new[] { "1.21.1" }, loaders = new[] { ModrinthClient.LoaderName(Profile.Loader) },
            dependencies = RequiredB && id == "A2" ? new[] { new { project_id = "B", version_id = "B2", dependency_type = "required" } } : [],
            files = new[] { new { filename = ProviderFileName ?? id + ".jar", url = "https://cdn.modrinth.com/data/" + id + ".jar", size = Bytes[id].Length, primary = true, hashes = new { sha512 = Sha512(Bytes[id]) } } }
        };
        internal static byte[] QuiltJar(string id, string version, bool child = false, string dependency = "")
        {
            if (id.Length == 1) id = "mod_" + id; // Quilt's schema requires at least two characters, unlike this Fabric fixture.
            using var m = new MemoryStream();
            using (var zip = new ZipArchive(m, ZipArchiveMode.Create, true))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("quilt.mod.json").Open()))
                    writer.Write("{\"schema_version\":1,\"quilt_loader\":{\"id\":\"" + id + "\",\"group\":\"test.mods\",\"version\":\"" + version + "\",\"intermediate_mappings\":\"net.fabricmc:intermediary\",\"jars\":" + (child ? "[\"META-INF/jars/child.jar\"]" : "[]") + ",\"depends\":" + (dependency.Length == 0 ? "[]" : "[\"" + dependency + "\"]") + "},\"minecraft\":{\"environment\":\"*\"}}");
                if (child) { using var s = zip.CreateEntry("META-INF/jars/child.jar").Open(); s.Write(QuiltJar("child", "1.0.0")); }
            }
            return m.ToArray();
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
        internal static string Sha512(byte[] b) => Convert.ToHexString(SHA512.HashData(b)).ToLowerInvariant();
        internal ModUpdateManager Reopen() => new(Ws.Paths, Ws.Store, catalog, downloader, Local);
        internal async Task Add(string relative, byte[] data, bool owned = true)
        {
            await File.WriteAllBytesAsync(Physical(relative), data);
            if (owned) await new ModInventoryStore(Ws.Paths).RecordCommittedAsync(Profile.Id, [new(relative, Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(), data.Length)]);
        }
        internal Task<ModUpdatePlan> Plan() => Manager.PreviewAsync(Profile, [new("mods/unrelated-name.jar", "A2")]);
        public void Dispose() { http?.Dispose(); Ws.Dispose(); }
    }
    [Fact] public async Task HashIdentityPublicationPolicyAndExplicitConfirmation()
    {
        using var f = new Fixture(); await f.Start(); var scan = await f.Manager.CheckAsync(f.Profile);
        var row = Assert.Single(scan.Rows); Assert.Equal("A1", row.Current!.Id); Assert.Equal("A2", row.Available!.Id);
        // Published A2 has a lower-looking display number; filenames and Mod IDs are not provider identity.
        var plan = await f.Plan(); Assert.True(plan.CanApply, string.Join("\n", plan.Blockers));
        await Assert.ThrowsAsync<ModUpdateException>(() => f.Manager.ApplyAsync(f.Profile, plan, false));
        Assert.Equal(f.Bytes["A1"], await File.ReadAllBytesAsync(f.Physical("mods/unrelated-name.jar")));
        await f.Manager.ApplyAsync(f.Profile, plan, true);
        Assert.Equal(f.Bytes["A2"], await File.ReadAllBytesAsync(f.Physical("mods/unrelated-name.jar")));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(f.Physical("mods/unrelated-name.jar"))!, "*.jar"));
    }
    [Fact] public async Task PinsSurviveRestartAndBlockDirectAndDependencyUpdates()
    {
        using var f = new Fixture(); await f.Start(requiredB: true); await f.Add("mods/b.jar", f.Bytes["B1"]);
        await f.Manager.SetPinAsync(f.Profile, "mods/b.jar", true, true);
        f.Manager = f.Reopen(); var pins = await AtomicFile.ReadJsonAsync<ModPinSet>(f.PinPath);
        Assert.Equal("B1", Assert.Single(pins!.Pins).Provider!.Id);
        var blocked = await f.Plan(); Assert.False(blocked.CanApply); Assert.NotEmpty(blocked.Blockers);
        await f.Manager.SetPinAsync(f.Profile, "mods/b.jar", false, true);
        var ready = await f.Plan(); Assert.True(ready.CanApply, string.Join("\n", ready.Blockers));
        Assert.Contains(ready.Changes, c => c.Kind == "DependencyReplace" && c.Version!.Id == "B2");
        await f.Manager.SetPinAsync(f.Profile, "mods/unrelated-name.jar", true, true);
        Assert.False((await f.Plan()).CanApply);
    }
    [Fact] public async Task LocalUnknownJarGetsExactLocalPinWithoutInventedProvider()
    {
        using var f = new Fixture(); await f.Start(); var local = Fixture.Jar("unknown", "1.0.0"); await f.Add("mods/A2.jar", local, false);
        var scan = await f.Manager.CheckAsync(f.Profile); Assert.Equal("UpdatesUnknown", scan.Rows.Single(x => x.File.Path == "mods/A2.jar").Note);
        await f.Manager.SetPinAsync(f.Profile, "mods/A2.jar", true, true);
        var pin = Assert.Single((await AtomicFile.ReadJsonAsync<ModPinSet>(f.PinPath))!.Pins);
        Assert.Null(pin.Provider); Assert.Equal(Fixture.Sha512(local), pin.File.Sha512);
        var plan = await f.Plan(); Assert.True(plan.CanApply, string.Join("\n", plan.Blockers));
        await f.Manager.ApplyAsync(f.Profile, plan, true); Assert.Equal(local, await File.ReadAllBytesAsync(f.Physical("mods/A2.jar")));
        await Assert.ThrowsAsync<ModUpdateException>(() => f.Manager.PreviewAsync(f.Profile, [new("mods/A2.jar", Remove: true)]));
    }
    [Fact] public async Task RequiredDependenciesAreAddedAndActualJarSetIsValidated()
    {
        using var f = new Fixture(); await f.Start(true); var plan = await f.Plan();
        Assert.True(plan.CanApply, string.Join("\n", plan.Blockers)); Assert.Contains(plan.Changes, c => c.Kind == "DependencyAdd" && c.Version!.Id == "B2");
        await f.Manager.ApplyAsync(f.Profile, plan, true);
        var report = await new InstalledModReadiness(f.Ws.Paths, f.Local).CheckAsync(f.Profile, null);
        Assert.Empty(report.Issues); Assert.Equal(2, report.VerifiedFiles);
    }
    [Fact] public async Task ProviderMetadataCannotOverrideActualJarDependencyConflict()
    {
        using var f = new Fixture(); await f.Start(); f.Bytes["A2"] = Fixture.Jar("a", "1.0.0", new { absent = ">=1.0.0" });
        var plan = await f.Plan(); Assert.False(plan.CanApply); Assert.Contains(plan.Blockers, b => b.Contains("absent"));
    }
    [Theory] [InlineData("add")] [InlineData("modify")] [InlineData("profile")] [InlineData("pin")] [InlineData("receipt")] [InlineData("plan")]
    public async Task ChangesAfterPreviewNeverSilentlyOverwriteNewerState(string kind)
    {
        using var f = new Fixture(); await f.Start(); var plan = await f.Plan();
        switch (kind)
        {
            case "add": await f.Add("mods/new-user.jar", Fixture.Jar("newuser", "1.0.0"), false); break;
            case "modify": await File.AppendAllTextAsync(f.Physical("mods/unrelated-name.jar"), "changed"); break;
            case "profile": await f.Ws.Store.SaveProfileAsync(f.Profile with { MemoryMb = 8192 }); break;
            case "pin": await f.Manager.SetPinAsync(f.Profile, "mods/unrelated-name.jar", true, true); break;
            case "receipt": await File.WriteAllTextAsync(f.Ws.Paths.Receipt(f.Profile.Id), "{}"); break;
            case "plan": plan = plan with { Changes = [] }; break;
        }
        var old = await File.ReadAllBytesAsync(f.Physical("mods/unrelated-name.jar"));
        await Assert.ThrowsAnyAsync<Exception>(() => f.Manager.ApplyAsync(f.Profile, plan, true));
        Assert.Equal(old, await File.ReadAllBytesAsync(f.Physical("mods/unrelated-name.jar")));
        Assert.Empty(await f.Manager.HistoryAsync(f.Profile));
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task FailedDownloadOrHashNeverMutatesProfile(bool corrupt)
    {
        using var f = new Fixture(); await f.Start(); f.CorruptDownload = corrupt; f.FailDownload = !corrupt;
        await Assert.ThrowsAnyAsync<Exception>(() => f.Plan());
        Assert.Equal(f.Bytes["A1"], await File.ReadAllBytesAsync(f.Physical("mods/unrelated-name.jar")));
        Assert.Empty(await f.Manager.HistoryAsync(f.Profile));
    }
    [Theory]
    [InlineData("prepared")] [InlineData("old:mods/unrelated-name.jar")] [InlineData("new:mods/unrelated-name.jar")]
    [InlineData("metadata:installed-mods.json")] [InlineData("metadata:mod-pins.json")] [InlineData("metadata:mod-update-epoch.json")]
    public async Task InterruptedTransactionBlocksOtherWritesThenRecoversExactly(string point)
    {
        using var f = new Fixture(); await f.Start(true); var plan = await f.Plan(); var inv = await File.ReadAllBytesAsync(new ModInventoryStore(f.Ws.Paths).PathFor(f.Profile.Id));
        f.Manager.Fault = where => { if (where == point) throw new OperationCanceledException("injected boundary"); };
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Manager.ApplyAsync(f.Profile, plan, true));
        var readiness = await new InstalledModReadiness(f.Ws.Paths, f.Local).CheckAsync(f.Profile, null); Assert.Contains(readiness.Issues, i => i.Code == "mods.pending");
        await Assert.ThrowsAsync<ModUpdateException>(() => f.Local.PreviewBaselineAsync(f.Profile));
        await Assert.ThrowsAsync<ModUpdateException>(() => f.Local.RecoverAsync(f.Profile));
        f.Manager = f.Reopen(); f.Network.Offline = true; var count = f.Requests;
        await f.Manager.RecoverAsync(f.Profile, true);
        Assert.Equal(count, f.Requests); Assert.Equal(f.Bytes["A1"], await File.ReadAllBytesAsync(f.Physical("mods/unrelated-name.jar")));
        Assert.Equal(inv, await File.ReadAllBytesAsync(new ModInventoryStore(f.Ws.Paths).PathFor(f.Profile.Id)));
        Assert.False(File.Exists(f.PinPath)); Assert.Single(Directory.GetFiles(Path.GetDirectoryName(f.Physical("mods/unrelated-name.jar"))!, "*.jar"));
        Assert.Equal("Recovered", Assert.Single(await f.Manager.HistoryAsync(f.Profile)).State);
    }
    [Fact] public async Task CommittedJournalRecoveryKeepsVerifiedNewState()
    {
        using var f = new Fixture(); await f.Start(); var plan = await f.Plan();
        f.Manager.Fault = where => { if (where == "committed") throw new IOException("interrupted"); };
        await Assert.ThrowsAsync<IOException>(() => f.Manager.ApplyAsync(f.Profile, plan, true));
        f.Manager = f.Reopen(); await f.Manager.RecoverAsync(f.Profile, true);
        Assert.Equal(f.Bytes["A2"], await File.ReadAllBytesAsync(f.Physical("mods/unrelated-name.jar")));
        Assert.Equal("Committed", Assert.Single(await f.Manager.HistoryAsync(f.Profile)).State);
    }
    [Fact] public async Task RecoveryRejectsChangedUserBytesWithoutDeletingThem()
    {
        using var f = new Fixture(); await f.Start(); var plan = await f.Plan();
        f.Manager.Fault = where => { if (where.StartsWith("new:")) throw new IOException(); };
        await Assert.ThrowsAsync<IOException>(() => f.Manager.ApplyAsync(f.Profile, plan, true));
        var user = Fixture.Jar("user", "1.0.0"); await File.WriteAllBytesAsync(f.Physical("mods/unrelated-name.jar"), user);
        f.Manager = f.Reopen(); await Assert.ThrowsAsync<ModUpdateException>(() => f.Manager.RecoverAsync(f.Profile, true));
        Assert.Equal(user, await File.ReadAllBytesAsync(f.Physical("mods/unrelated-name.jar")));
    }
    [Fact] public async Task OfflineCacheAndRollbackRestoreExactFilesInventoryAndPins()
    {
        using var f = new Fixture(); await f.Start(true); await f.Add("mods/local.jar", Fixture.Jar("local", "1.0.0"), false);
        await f.Manager.SetPinAsync(f.Profile, "mods/local.jar", true, true);
        var plan = await f.Plan(); Assert.True(plan.CanApply, string.Join("\n", plan.Blockers));
        var pins = await File.ReadAllBytesAsync(f.PinPath); var inv = await File.ReadAllBytesAsync(new ModInventoryStore(f.Ws.Paths).PathFor(f.Profile.Id));
        f.Network.Offline = true; var count = f.Requests;
        await f.Manager.ApplyAsync(f.Profile, plan, true);
        f.Manager = f.Reopen(); var rollback = await f.Manager.PreviewRollbackAsync(f.Profile, plan.Id);
        await Assert.ThrowsAsync<ModUpdateException>(() => f.Manager.ApplyAsync(f.Profile, rollback, false));
        await f.Manager.ApplyAsync(f.Profile, rollback, true);
        Assert.Equal(count, f.Requests); Assert.Equal(pins, await File.ReadAllBytesAsync(f.PinPath));
        Assert.Equal(inv, await File.ReadAllBytesAsync(new ModInventoryStore(f.Ws.Paths).PathFor(f.Profile.Id)));
        Assert.Equal(f.Bytes["A1"], await File.ReadAllBytesAsync(f.Physical("mods/unrelated-name.jar")));
        Assert.Equal(2, Directory.GetFiles(Path.GetDirectoryName(f.Physical("mods/local.jar"))!, "*.jar").Length);
        await Assert.ThrowsAsync<ModUpdateException>(() => f.Manager.PreviewRollbackAsync(f.Profile, plan.Id)); // Epoch prevents ABA.
    }
    [Theory] [InlineData("missing")] [InlineData("partial")] [InlineData("changed")]
    public async Task RollbackRequiresCompleteVerifiedBackup(string mode)
    {
        using var f = new Fixture(); await f.Start(); var plan = await f.Plan(); await f.Manager.ApplyAsync(f.Profile, plan, true);
        var h = Assert.Single(await f.Manager.HistoryAsync(f.Profile)); var backup = SafePath.Join(f.Ws.Paths.Root, h.Backup);
        if (mode == "missing") File.Delete(backup);
        if (mode == "partial") { using var s = File.OpenWrite(backup); s.SetLength(20); }
        if (mode == "changed") await File.AppendAllTextAsync(backup, "tampered");
        await Assert.ThrowsAsync<ModUpdateException>(() => f.Manager.PreviewRollbackAsync(f.Profile, plan.Id));
        Assert.Equal(f.Bytes["A2"], await File.ReadAllBytesAsync(f.Physical("mods/unrelated-name.jar")));
    }
    [Theory] [InlineData("user")] [InlineData("pin")] [InlineData("subsequent")]
    public async Task RollbackRefusesNewerState(string mode)
    {
        using var f = new Fixture(); await f.Start(); var plan = await f.Plan(); await f.Manager.ApplyAsync(f.Profile, plan, true);
        if (mode == "user") await File.AppendAllTextAsync(f.Physical("mods/unrelated-name.jar"), "edited");
        if (mode == "pin") await f.Manager.SetPinAsync(f.Profile, "mods/unrelated-name.jar", true, true);
        if (mode == "subsequent")
        {
            var remove = await f.Manager.PreviewAsync(f.Profile, [new("mods/unrelated-name.jar", Remove: true)]);
            await f.Manager.ApplyAsync(f.Profile, remove, true);
        }
        await Assert.ThrowsAnyAsync<Exception>(() => f.Manager.PreviewRollbackAsync(f.Profile, plan.Id));
    }
    [Fact] public async Task ColdOfflineNeverClaimsNoUpdatesAndWarmCacheReportsAge()
    {
        using var f = new Fixture(); await f.Start(); f.Network.Offline = true;
        var cold = await f.Manager.CheckAsync(f.Profile); Assert.Equal("UpdatesUnavailable", Assert.Single(cold.Rows).Note); Assert.NotEmpty(cold.Findings); Assert.Equal(0, f.Requests);
        f.Network.Offline = false; await f.Manager.CheckAsync(f.Profile); var count = f.Requests; f.Network.Offline = true;
        var warm = await f.Manager.CheckAsync(f.Profile); Assert.True(warm.Cached); Assert.NotNull(warm.MetadataAt); Assert.Equal("A2", Assert.Single(warm.Rows).Available!.Id); Assert.Equal(count, f.Requests);
    }
    [Fact] public async Task PrereleaseRequiresExplicitPolicyOptIn()
    {
        using var f = new Fixture(); await f.Start(); f.Beta = true;
        Assert.Null(Assert.Single((await f.Manager.CheckAsync(f.Profile)).Rows).Available);
        Assert.Equal("A2", Assert.Single((await f.Manager.CheckAsync(f.Profile, true)).Rows).Available!.Id);
        await Assert.ThrowsAsync<ModUpdateException>(() => f.Plan());
        Assert.True((await f.Manager.PreviewAsync(f.Profile, [new("mods/unrelated-name.jar", "A2")], true)).CanApply);
    }
    [Fact] public async Task OccupiedProfileAndCanceledPreparationNeverMutateFiles()
    {
        using var f = new Fixture(); await f.Start(); var plan = await f.Plan();
        using (new ProfileUsage(f.Ws.Paths).Acquire(f.Profile.Id))
            await Assert.ThrowsAnyAsync<Exception>(() => f.Manager.ApplyAsync(f.Profile, plan, true));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Manager.ApplyAsync(f.Profile, plan, true, cts.Token));
        Assert.Equal(f.Bytes["A1"], await File.ReadAllBytesAsync(f.Physical("mods/unrelated-name.jar")));
    }
    [Fact] public async Task WorldsConfigsTranslationsSettingsAndOtherProfilesAreUntouched()
    {
        using var f = new Fixture(); await f.Start(); var other = f.Profile with { Id = Guid.NewGuid(), Name = "Other" }; await f.Ws.Store.SaveProfileAsync(other);
        var sentinels = new[] { f.Physical("saves/synthetic-world.dat"), f.Physical("config/user.txt"), SafePath.Join(f.Ws.Paths.Profile(f.Profile.Id), "translations/manual.json"), SafePath.Join(f.Ws.Paths.Game(other.Id), "mods/other.jar") };
        foreach (var file in sentinels) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); await File.WriteAllTextAsync(file, "synthetic sentinel"); }
        await f.Ws.Store.SetSettingAsync("test.sentinel", "preserve"); var plan = await f.Plan();
        await f.Manager.ApplyAsync(f.Profile, plan, true); await f.Manager.ApplyAsync(f.Profile, await f.Manager.PreviewRollbackAsync(f.Profile, plan.Id), true);
        foreach (var file in sentinels) Assert.Equal("synthetic sentinel", await File.ReadAllTextAsync(file));
        Assert.Equal("preserve", await f.Ws.Store.GetSettingAsync("test.sentinel"));
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task ActualNestedQuiltCandidatesParticipateInResultingSetReview(bool valid)
    {
        using var f = new Fixture(); await f.Start(loader: ModLoader.Quilt);
        f.Bytes["A2"] = Fixture.QuiltJar("a", "2.0.0", child: true, dependency: valid ? "child" : "notbundled");
        var plan = await f.Plan(); Assert.Equal(valid, plan.CanApply);
        if (valid)
        {
            await f.Manager.ApplyAsync(f.Profile, plan, true);
            var receipt = await AtomicFile.ReadJsonAsync<InstallationReceipt>(f.Ws.Paths.Receipt(f.Profile.Id));
            var report = await new InstalledModReadiness(f.Ws.Paths, f.Local).CheckAsync(f.Profile, receipt);
            Assert.Empty(report.Issues); Assert.Equal(2, report.NativeSelection!.ActiveMods.Count);
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(f.Physical("mods/unrelated-name.jar"))!, "*.jar"));
        }
        else Assert.NotEmpty(plan.Blockers);
    }
    [Fact] public async Task SameProviderFilenameReplacementIsVerifiedWithoutWeakeningAddOnlyInventory()
    {
        using var f = new Fixture(); await f.Start(); f.ProviderFileName = "unrelated-name.jar";
        var plan = await f.Plan(); Assert.True(plan.CanApply, string.Join("\n", plan.Blockers));
        var next = plan.Changes.Single().After!;
        await Assert.ThrowsAsync<InvalidDataException>(() => new ModInventoryStore(f.Ws.Paths).RecordCommittedAsync(f.Profile.Id, [new(next.Path, next.Sha256, next.Size)]));
        await f.Manager.ApplyAsync(f.Profile, plan, true); Assert.Equal(f.Bytes["A2"], await File.ReadAllBytesAsync(f.Physical(next.Path)));
    }
    [Fact] public async Task ChangedOrMissingPinCanOnlyBeUnpinnedExplicitlyAndReadinessDetectsIt()
    {
        using var f = new Fixture(); await f.Start(); await f.Manager.SetPinAsync(f.Profile, "mods/unrelated-name.jar", true, true);
        await File.WriteAllBytesAsync(f.Physical("mods/unrelated-name.jar"), Fixture.Jar("user", "1.0.0"));
        var report = await new InstalledModReadiness(f.Ws.Paths, f.Local).CheckAsync(f.Profile, null); Assert.Contains(report.Issues, i => i.Code == "mods.pin.changed");
        await Assert.ThrowsAsync<ModUpdateException>(() => f.Manager.SetPinAsync(f.Profile, "mods/unrelated-name.jar", true, true));
        File.Delete(f.Physical("mods/unrelated-name.jar"));
        Assert.Equal("UpdatesPinConflict", Assert.Single((await f.Manager.CheckAsync(f.Profile)).Rows).Note);
        await Assert.ThrowsAsync<ModUpdateException>(() => f.Manager.SetPinAsync(f.Profile, "mods/unrelated-name.jar", false, false));
        await f.Manager.SetPinAsync(f.Profile, "mods/unrelated-name.jar", false, true);
        Assert.Empty((await AtomicFile.ReadJsonAsync<ModPinSet>(f.PinPath))!.Pins);
    }
    [Fact] public async Task UnselectedInstalledProjectStaysAtCurrentVersionAndRequiredRemovalIsBlocked()
    {
        using var f = new Fixture(); await f.Start(); await f.Add("mods/b.jar", f.Bytes["B1"]);
        var plan = await f.Plan(); Assert.True(plan.CanApply, string.Join("\n", plan.Blockers));
        Assert.DoesNotContain(plan.Changes, c => c.Version?.ProjectId == "B");
        await f.Manager.ApplyAsync(f.Profile, plan, true); Assert.Equal(f.Bytes["B1"], await File.ReadAllBytesAsync(f.Physical("mods/b.jar")));
        using var dependent = new Fixture(); await dependent.Start(true); await dependent.Add("mods/b.jar", dependent.Bytes["B1"]);
        var blocked = await dependent.Manager.PreviewAsync(dependent.Profile, [new("mods/unrelated-name.jar", "A2"), new("mods/b.jar", Remove: true)]);
        Assert.False(blocked.CanApply); Assert.NotEmpty(blocked.Blockers);
    }
    [Fact] public async Task RealCancellationTokenAfterDurablePrepareIsRecoverableOffline()
    {
        using var f = new Fixture(); await f.Start(); var plan = await f.Plan(); using var cts = new CancellationTokenSource();
        f.Manager.Fault = point => { if (point == "prepared") cts.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Manager.ApplyAsync(f.Profile, plan, true, cts.Token));
        f.Manager = f.Reopen(); f.Network.Offline = true; await f.Manager.RecoverAsync(f.Profile, true);
        Assert.Equal(f.Bytes["A1"], await File.ReadAllBytesAsync(f.Physical("mods/unrelated-name.jar")));
    }
    [Fact] public async Task CorruptCachedJarBeforeOfflineApplyDoesNotChangeProfile()
    {
        using var f = new Fixture(); await f.Start(); var plan = await f.Plan(); var v = plan.Changes.Single().Version!;
        var cache = SafePath.Join(f.Ws.Paths.Cache, "modrinth/files/" + v.File.Sha512 + "/" + v.File.Name); await File.AppendAllTextAsync(cache, "bad");
        f.Network.Offline = true; await Assert.ThrowsAnyAsync<Exception>(() => f.Manager.ApplyAsync(f.Profile, plan, true));
        Assert.Equal(f.Bytes["A1"], await File.ReadAllBytesAsync(f.Physical("mods/unrelated-name.jar")));
        Assert.Empty(await f.Manager.HistoryAsync(f.Profile));
    }
    [Fact] public async Task BackupIsReverifiedAfterRollbackPreviewAndUnsafePlanNeverWritesOutsideMods()
    {
        using var f = new Fixture(); await f.Start(); var plan = await f.Plan();
        var evil = plan with { Changes = [new("Update", plan.Changes[0].Before, plan.Changes[0].After! with { Path = "../escape.jar" }, plan.Changes[0].Version)] };
        await Assert.ThrowsAnyAsync<Exception>(() => f.Manager.ApplyAsync(f.Profile, evil, true));
        await f.Manager.ApplyAsync(f.Profile, plan, true); var rollback = await f.Manager.PreviewRollbackAsync(f.Profile, plan.Id);
        var backup = SafePath.Join(f.Ws.Paths.Root, Assert.Single(await f.Manager.HistoryAsync(f.Profile)).Backup); File.Delete(backup);
        await Assert.ThrowsAsync<ModUpdateException>(() => f.Manager.ApplyAsync(f.Profile, rollback, true));
        Assert.Equal(f.Bytes["A2"], await File.ReadAllBytesAsync(f.Physical("mods/unrelated-name.jar")));
        Assert.False(File.Exists(Path.Combine(f.Ws.Paths.Profile(f.Profile.Id), "escape.jar")));
    }
}
