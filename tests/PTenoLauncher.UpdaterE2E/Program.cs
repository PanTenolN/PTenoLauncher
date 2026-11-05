using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
using PTenoLauncher.Infrastructure.Updates;
using PTenoLauncher.Testing;

// Explicit opt-in only. Real builds/processes in a new isolated workspace, no production keys/network/game/accounts.
if(args.Length==2 && args[0]=="--lifecycle-probe")
{
    var probe=new OwnedTestRoot("runner-probe",64L*1024*1024);var mode=args[1];
    try
    {
        await File.WriteAllBytesAsync(Path.Combine(probe.Root,"synthetic-cache.bin"),new byte[32*1024*1024]);
        await File.WriteAllTextAsync(Path.Combine(probe.Root,"failure.log"),"Synthetic lifecycle outcome: "+mode);
        probe.CheckBudget();
        if(mode=="failure")throw new InvalidDataException("Controlled runner failure");
        if(mode=="cancel")throw new OperationCanceledException("Controlled runner cancellation");
    }
    catch(InvalidDataException){Environment.ExitCode=1;}
    catch(OperationCanceledException){Environment.ExitCode=2;}
    finally
    {
        try{probe.PreserveRunnerEvidence();}catch(Exception e){probe.Retain(e.Message);throw;}
        Console.WriteLine(JsonSerializer.Serialize(new{root=probe.Root,evidence=probe.Evidence,mode}));
        if(mode!="abandon")probe.Dispose(); // Explicit crash-recovery fixture; never normal runner behavior.
    }
    return Environment.ExitCode;
}
if (args.Length == 2 && args[0] == "--cleanup-isolated")
{
    var isolated = Path.GetFullPath(args[1]);
    if (!Path.GetFileName(isolated).StartsWith("updater-e2e-", StringComparison.Ordinal) || Directory.GetParent(isolated)?.Name != ".local") return 2;
    foreach (var file in Directory.EnumerateFiles(isolated, "helper-status.json", SearchOption.AllDirectories))
    {
        var status = await AtomicFile.ReadJsonAsync<LauncherHelperStatus>(file); if (status?.Child is not { } identity) continue;
        try
        {
            using var p = Process.GetProcessById(identity.Pid);
            if (UpdateProcessIdentity.Capture(p) != identity || !identity.Executable.StartsWith(isolated + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(Path.GetDirectoryName(identity.Executable)!, "isolated-e2e.flag"))) continue;
            await AtomicFile.WriteJsonAsync(Path.Combine(Path.GetDirectoryName(identity.Executable)!, "isolated-close-request.json"), identity);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20)); await p.WaitForExitAsync(deadline.Token);
            Console.WriteLine("Normally closed verified isolated-test GUI process " + identity.Pid + ", exit " + p.ExitCode);
        }
        catch (ArgumentException) { }
    }
    return 0;
}
if (args.Length != 1) { Console.Error.WriteLine("Pass the existing source root."); return 2; }
var source = Path.GetFullPath(args[0]);
if (!source.Equals(OwnedTestRoot.ProjectRoot,StringComparison.OrdinalIgnoreCase)) throw new IOException("Explicit source does not match this runner's project root.");
using var owned = new OwnedTestRoot("updater-e2e",16L * 1024 * 1024 * 1024);
var work = owned.Root;
using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var publicFile = SafePath.Join(work, "public.pem"); await File.WriteAllTextAsync(publicFile, signer.ExportSubjectPublicKeyInfoPem());
var trust = new LauncherUpdateTrust(new Dictionary<string, string> { ["isolated-test"] = signer.ExportSubjectPublicKeyInfoPem() });
var reports = new List<object>(); var dotnet = SafePath.Join(source, ".tools/dotnet/dotnet.exe");
var reportFile = SafePath.Join(work, "result.json");
try
{
    var a = await Build("a", 1001, false); var b = await Build("b", 1002, false); var bad = await Build("bad", 1003, true); var silent = await Build("silent", 1004, false, true);
    var beforeDll = Sha(await File.ReadAllBytesAsync(Path.Combine(a, "PTenoLauncher.dll"))); var afterDll = Sha(await File.ReadAllBytesAsync(Path.Combine(b, "PTenoLauncher.dll")));
    if (beforeDll == afterDll) throw new Exception("Builds must have different compiled DLLs.");
    await Exercise("update-health", a, b, new(1002, "isolated-b", "0.2.0", "preview"), false);
    await Exercise("failed-startup-rollback", a, bad, new(1003, "isolated-bad", "0.2.0", "preview"), true);
    await Exercise("live-health-timeout-recovery", a, silent, new(1004, "isolated-silent", "0.2.0", "preview"), true, true);
    await Save("PASSED"); Console.WriteLine(reportFile); return 0;
}
catch (Exception e) { reports.Add(new { failure = e.ToString() }); await Save("FAILED"); Console.WriteLine(e); Console.WriteLine(reportFile); return 1; }
finally
{
    try
    {
        owned.CheckBudget();
        // Only exact synthetic executable identities may receive cooperative closure requests.
        foreach (var file in Directory.EnumerateFiles(work,"helper-status.json",SearchOption.AllDirectories))
        {
            SafePath.RejectLinks(file);
            var status = await AtomicFile.ReadJsonAsync<LauncherHelperStatus>(file);
            if(status?.Child is not { } identity) continue;
            try { await CloseTestProcess(identity,Path.GetDirectoryName(identity.Executable)!,new LauncherPaths(Path.Combine(Path.GetDirectoryName(identity.Executable)!,"portable-data"))); }
            catch(ArgumentException) { /* Exact recorded test process is already gone. */ }
            catch(InvalidOperationException) { /* HasExited / identity mismatch: never act on a reused PID. */ }
        }
    }
    finally
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        // Evidence is archived before disposal; cleanup failures surface with a retained-root record.
        try { owned.PreserveRunnerEvidence(); }
        catch(Exception e) { owned.Retain("Evidence preservation/budget check failed: " + e.Message); throw; }
        owned.CheckBudget();
        Console.WriteLine("Compact E2E evidence: " + owned.Evidence);
    }
}

async Task<string> Build(string id, long sequence, bool fail, bool withhold = false)
{
    var buildSource = SafePath.Join(work, "source-" + id); Directory.CreateDirectory(buildSource);
    foreach (var name in new[] { "Directory.Build.props", "NuGet.Config", "global.json" }) File.Copy(SafePath.Join(source, name), SafePath.Join(buildSource, name));
    foreach (var p in Directory.EnumerateFiles(Path.Combine(source, "src"), "*", SearchOption.AllDirectories))
    {
        var rel = Path.GetRelativePath(source, p).Replace('\\', '/'); if (rel.Split('/').Any(x => x is "bin" or "obj")) continue;
        var dest = SafePath.Join(buildSource, rel); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(p, dest);
    }
    var output = SafePath.Join(work, "build-" + id); Directory.CreateDirectory(output);
    var properties = new[] { "-p:Configuration=UpdaterTest", "-p:PTenoUpdaterTestKeyFile=" + publicFile, "-p:PTenoBuildSequence=" + sequence, "-p:PTenoBuildId=isolated-" + id, "-p:PTenoBuildChannel=preview", "-p:PTenoTestFailStartup=" + (fail ? "true" : "false"), "-p:PTenoTestWithholdHealth=" + (withhold ? "true" : "false"), "-p:NuGetAudit=false", "-p:UseSharedCompilation=false" };
    foreach (var project in new[] { "PTenoLauncher.Desktop", "PTenoLauncher.UpdateHelper" })
    {
        var file = SafePath.Join(buildSource, "src/" + project + "/" + project + ".csproj");
        await Run(dotnet, new[] { "restore", file, "--configfile", SafePath.Join(buildSource, "NuGet.Config"), "--ignore-failed-sources", "--disable-parallel" }.Concat(properties).ToArray(), "restore-" + id + "-" + project);
        await Run(dotnet, new[] { "publish", file, "--no-restore", "--self-contained", "false", "-c", "UpdaterTest", "-o", project.EndsWith("UpdateHelper") ? Path.Combine(output, "updater") : output, "--disable-build-servers", "-m:1", "-p:DebugType=none" }.Concat(properties).ToArray(), "build-" + id + "-" + project);
    }
    var files = await Inventory(output);
    await AtomicFile.WriteJsonAsync(SafePath.Join(output, LauncherUpdater.InstallationManifest), new ManagedLauncherInstallation(new(sequence, "isolated-" + id, "0.2.0", "preview"), files));
    owned.CheckBudget();
    return output;
}
async Task Exercise(string name, string a, string next, LauncherBuild build, bool rollback, bool liveTimeout = false)
{
    var root = SafePath.Join(work, name); Directory.CreateDirectory(root); var app = SafePath.Join(root, "application"); Directory.CreateDirectory(app);
    foreach (var f in Directory.EnumerateFiles(a, "*", SearchOption.AllDirectories)) { var target = SafePath.Join(app, Path.GetRelativePath(a, f).Replace('\\', '/')); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(f, target); }
    await File.WriteAllTextAsync(SafePath.Join(app, "portable.flag"), "");
    await File.WriteAllTextAsync(SafePath.Join(app, "isolated-e2e.flag"), "test-only cooperative window close");
    var paths = new LauncherPaths(SafePath.Join(app, "portable-data")); paths.Initialize(); var store = new SqliteLauncherStore(paths); await store.InitializeAsync();
    await store.SetSettingAsync("ui.language", "en"); await store.SetSettingAsync("updates.auto", "false"); await store.SetSettingAsync("e2e.sentinel", "preserve");
    var profile = new GameProfile(Guid.NewGuid(), "Synthetic updater profile", "1.21.1", ModLoader.Vanilla, 2048, null, DateTimeOffset.UtcNow); await store.SaveProfileAsync(profile);
    var sentinel = SafePath.Join(paths.Game(profile.Id), "saves/not-a-real-world.txt"); await File.WriteAllTextAsync(sentinel, "SYNTHETIC - NOT A REAL WORLD");
    await File.WriteAllTextAsync(SafePath.Join(app, "unknown-user-file.txt"), "PRESERVE");
    var moreSentinels = new[] { SafePath.Join(paths.Game(profile.Id), "mods/synthetic-only.jar"), SafePath.Join(paths.Game(profile.Id), "config/user.txt"), SafePath.Join(paths.Profile(profile.Id), "translations/sentinel.txt"), SafePath.Join(paths.Backups, "synthetic-only.zip"), SafePath.Join(paths.Secrets, "synthetic-only.bin"), SafePath.Join(paths.Cache, "synthetic-only.bin") };
    foreach (var f in moreSentinels) { Directory.CreateDirectory(Path.GetDirectoryName(f)!); await File.WriteAllTextAsync(f, "SYNTHETIC - PRESERVE"); }
    var files = await Inventory(next); var archive = SafePath.Join(root, "payload.zip");
    using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) foreach (var f in files) zip.CreateEntryFromFile(SafePath.Join(next, f.Path), f.Path, CompressionLevel.Fastest);
    var bytes = await File.ReadAllBytesAsync(archive); var hash = Sha(bytes);
    var package = new LauncherUpdatePackage("Portable", new("https://github.com/PanTenolN/PTenoLauncher/releases/download/isolated-test/package.zip"), bytes.Length, hash, files);
    var release = new LauncherRelease(build, "Isolated real build update", [package]);
    var body = JsonSerializer.SerializeToUtf8Bytes(new LauncherUpdateIndex(1, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1), [release]), AtomicFile.JsonOptions);
    var signed = new SignedLauncherUpdates("isolated-test", body, signer.SignData(body, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    var cached = SafePath.Join(paths.Cache, "launcher-updates/" + hash + ".zip"); Directory.CreateDirectory(Path.GetDirectoryName(cached)!); File.Copy(archive, cached);
    var policy = new NetworkPolicy { Offline = true }; using var http = new HttpClient(new Deny());
    var updater = new LauncherUpdater(paths, new(http, policy), policy, trust, new(1001, "isolated-a", "0.2.0", "preview"));
    var t = await updater.PrepareAsync(new(updater.Build, release, package, signed), app, true);
    var info = new ProcessStartInfo(SafePath.Join(app, "PTenoLauncher.exe")) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = app };
    info.ArgumentList.Add("--data-root"); info.ArgumentList.Add(paths.Root); info.ArgumentList.Add("--isolated-update-operation"); info.ArgumentList.Add(t.Id.ToString("N"));
    if (liveTimeout) info.ArgumentList.Add("--isolated-short-health-timeout");
    using var parent = Process.Start(info) ?? throw new Exception("Cannot launch A.");
    owned.RegisterProcess(parent);
    var statusPath = SafePath.Join(updater.TransactionRoot(t.Id), "helper-status.json"); LauncherHelperStatus? status = null;
    var until = DateTimeOffset.UtcNow.AddSeconds(100); var parentWaitVerified = false; var liveTimeoutVerified = false;
    while (DateTimeOffset.UtcNow < until)
    {
        status = await AtomicFile.ReadJsonAsync<LauncherHelperStatus>(statusPath);
        if(status?.Child is { } recordedChild)owned.RegisterIdentity(recordedChild);
        if (status?.Phase == "AwaitingParent" && !parent.HasExited)
        {
            var prior = await AtomicFile.ReadJsonAsync<ManagedLauncherInstallation>(SafePath.Join(app, LauncherUpdater.InstallationManifest));
            if (prior?.Build.Sequence != 1001 || Sha(await File.ReadAllBytesAsync(SafePath.Join(app, "PTenoLauncher.dll"))) != Sha(await File.ReadAllBytesAsync(SafePath.Join(a, "PTenoLauncher.dll")))) throw new Exception("Files changed before exact parent exit.");
            parentWaitVerified = true;
        }
        if (status?.Phase == (rollback ? "RolledBack" : "Committed")) break;
        if (liveTimeout && status?.Phase == "RecoveryRequired" && status.Error == "UpdaterHealthTimeout" && status.Child is { } liveId)
        {
            using var live = Process.GetProcessById(liveId.Pid); if (live.HasExited || UpdateProcessIdentity.Capture(live) != liveId) throw new Exception("Timeout did not preserve the running build.");
            var current = await AtomicFile.ReadJsonAsync<ManagedLauncherInstallation>(SafePath.Join(app, LauncherUpdater.InstallationManifest));
            if (current?.Build != build || !File.Exists(SafePath.Join(updater.TransactionRoot(t.Id), "backup/PTenoLauncher.dll"))) throw new Exception("Timeout changed running files or lost backup.");
            // Observe it remaining alive after helper timeout before explicitly asking this exact test process to close.
            await Task.Delay(1200);
            using (OpenExactTestProcess(liveId)) { }
            foreach (var f in files)
                if (!await VerifiedDownloader.VerifyAsync(SafePath.Join(app, f.Path), f.Sha256, "SHA256", f.Size)) throw new Exception("Timeout changed running payload bytes.");
            foreach (var f in t.Before.Files)
                if (!await VerifiedDownloader.VerifyAsync(SafePath.Join(updater.TransactionRoot(t.Id), "backup/" + f.Path), f.Sha256, "SHA256", f.Size)) throw new Exception("Timeout backup is incomplete.");
            liveTimeoutVerified = true;
            await CloseTestProcess(liveId, app, paths);
            using var recovery = Process.Start(new ProcessStartInfo(SafePath.Join(updater.TransactionRoot(t.Id), "helper/" + LauncherUpdateRuntime.HelperName)) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = SafePath.Join(updater.TransactionRoot(t.Id), "helper") });
            if (recovery is null) throw new Exception("Cannot resume durable helper recovery.");
            using var recoveryLimit = new CancellationTokenSource(TimeSpan.FromSeconds(45)); await recovery.WaitForExitAsync(recoveryLimit.Token);
            if (recovery.ExitCode != 0) throw new Exception("Recovery helper exit " + recovery.ExitCode + ": " + await File.ReadAllTextAsync(SafePath.Join(updater.TransactionRoot(t.Id), "helper-failure.json")));
            liveTimeout = false;
        }
        else if (status?.Phase == "RecoveryRequired" && !liveTimeoutVerified) throw new Exception("Helper: " + status.Error);
        await Task.Delay(150);
    }
    if (status?.Phase != (rollback ? "RolledBack" : "Committed")) throw new Exception("No expected completion: " + status?.Phase);
    if (!parentWaitVerified) throw new Exception("Did not observe exact-parent wait.");
    if (!parent.HasExited) throw new Exception("Original A did not exit.");
    var now = await AtomicFile.ReadJsonAsync<ManagedLauncherInstallation>(SafePath.Join(app, LauncherUpdater.InstallationManifest));
    if (now?.Build.Sequence != (rollback ? 1001 : build.Sequence)) throw new Exception("Unexpected resulting build.");
    LauncherHealth? health = null;
    if (!rollback)
    {
        health = await AtomicFile.ReadJsonAsync<LauncherHealth>(SafePath.Join(updater.TransactionRoot(t.Id), "health.json"));
        if (health?.Build != build || !health.Initialized || !health.WindowReady || health.Process != status.Child) throw new Exception("Missing real health handshake.");
    }
    if (status.Child is not { } childId) throw new Exception("No exact child identity.");
    await CloseTestProcess(childId, app, paths);
    if (await File.ReadAllTextAsync(sentinel) != "SYNTHETIC - NOT A REAL WORLD" || await File.ReadAllTextAsync(SafePath.Join(app, "unknown-user-file.txt")) != "PRESERVE" || await store.GetSettingAsync("e2e.sentinel") != "preserve") throw new Exception("Data preservation failed.");
    foreach (var f in moreSentinels) if (await File.ReadAllTextAsync(f) != "SYNTHETIC - PRESERVE") throw new Exception("Sentinel changed: " + f);
    reports.Add(new { scenario = name, state = "PASSED", parent = parent.Id, child = childId, resultingBuild = now.Build, health, parentWaitVerified, liveTimeoutVerified, normalTestClosure = true, sentinelsPreserved = true, actualExecutableProcesses = true, networkRequests = 0, authenticationBypassed = false, gameplay = false });
    await Save("RUNNING");
    owned.CheckBudget();
}
async Task CloseTestProcess(UpdateProcessIdentity identity, string app, LauncherPaths paths)
{
    if (!Path.GetFullPath(app).StartsWith(work + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        !identity.Executable.Equals(SafePath.Join(app, "PTenoLauncher.exe"), StringComparison.OrdinalIgnoreCase) ||
        !File.Exists(SafePath.Join(app, "isolated-e2e.flag"))) throw new Exception("Not an isolated test process.");
    using var process = OpenExactTestProcess(identity);
    await AtomicFile.WriteJsonAsync(SafePath.Join(app, "isolated-close-request.json"), identity);
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20)); await process.WaitForExitAsync(limit.Token);
    var state = await File.ReadAllTextAsync(SafePath.Join(paths.Root, "isolated-close-state.json"));
    if (!state.Contains("\"normalClosure\": true", StringComparison.Ordinal) || process.ExitCode != 0) throw new Exception("No normal test closure confirmation.");
    File.Delete(SafePath.Join(app, "isolated-close-request.json"));
    await File.WriteAllTextAsync(SafePath.Join(paths.Root, "isolated-close-" + identity.Pid + ".json"), state);
}
static Process OpenExactTestProcess(UpdateProcessIdentity identity)
{
    var process = Process.GetProcessById(identity.Pid);
    try { if (process.HasExited || UpdateProcessIdentity.Capture(process) != identity) throw new Exception("Test process identity changed."); return process; }
    catch { process.Dispose(); throw; }
}
async Task<List<LauncherUpdateFile>> Inventory(string directory)
{
    var result = new List<LauncherUpdateFile>();
    foreach (var f in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
    {
        var rel = Path.GetRelativePath(directory, f).Replace('\\', '/'); if (rel == LauncherUpdater.InstallationManifest) continue;
        result.Add(new(rel, new FileInfo(f).Length, Sha(await File.ReadAllBytesAsync(f))));
    }
    return result;
}
async Task Run(string executable, string[] arguments, string name)
{
    var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = work, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var a in arguments) info.ArgumentList.Add(a);
    using var p = Process.Start(info)!; var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
    owned.RegisterProcess(p);
    using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(4)); await p.WaitForExitAsync(limit.Token);
    await File.WriteAllTextAsync(SafePath.Join(work, name + ".log"), await output + await error);
    if (p.ExitCode != 0) throw new Exception(name + " exit " + p.ExitCode);
}
Task Save(string state) => AtomicFile.WriteJsonAsync(reportFile, new { state, work, scenarios = reports, productionTrustConfigured = false, privateKeyWritten = false });
static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
sealed class Deny : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => throw new Exception("Unexpected E2E network request."); }
