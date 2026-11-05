using System.Diagnostics;
using System.Text.Json;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Updates;

public sealed record LauncherHelperTicket(Guid Id, string ApplicationRoot, string DataRoot, UpdateProcessIdentity Parent, string Action, int HealthTimeoutSeconds = 45);
public sealed record LauncherHelperStatus(string Phase, string? Error = null, UpdateProcessIdentity? Child = null);
public sealed record LauncherHealth(Guid Id, string Token, LauncherBuild Build, UpdateProcessIdentity Process, bool Initialized, bool WindowReady, int DataSchema);
public sealed record LauncherUpdateBinding(Guid Id, string DataRoot);

/// <summary>The helper has no arbitrary path/ID entry point. Its installation and data root are derived from its verified location.</summary>
public static class LauncherUpdateRuntime
{
    public const string HelperName = "PTenoLauncher.UpdateHelper.exe";
    internal static string Marker(string root) => SafePath.Join(root, ".pteno-update.json");
    internal static string StatusFile(LauncherUpdater updater, Guid id) => SafePath.Join(updater.TransactionRoot(id), "helper-status.json");
    internal static LauncherUpdater ForSavedBuild(LauncherPaths paths, LauncherUpdateTrust trust, LauncherBuild build)
    {
        // Runtime coordination is strictly offline; preparation already verified/downloaded artifacts.
        var policy = new NetworkPolicy { Offline = true };
        return new(paths, new(new HttpClient(new DenyUpdatesHandler()), policy), policy, trust, build);
    }
    private sealed class DenyUpdatesHandler : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new InvalidOperationException("Helper network access is forbidden."); }
    internal static async Task<LauncherUpdateTransaction> ReadBound(LauncherPaths paths, LauncherUpdateTrust trust, Guid id, CancellationToken ct)
    {
        var file = SafePath.Join(paths.Root, "launcher-updates/operations/" + id.ToString("N") + "/transaction.json");
        var raw = await AtomicFile.ReadJsonAsync<LauncherUpdateTransaction>(file, ct) ?? throw new LauncherUpdateException("UpdaterInvalid");
        if (raw.Before?.Build is null) throw new LauncherUpdateException("UpdaterInvalid");
        return await ForSavedBuild(paths, trust, raw.Before.Build).ReadTransactionAsync(id, ct);
    }
    public static async Task<LauncherUpdateTransaction?> PendingAsync(LauncherPaths paths, string applicationRoot, LauncherUpdateTrust trust, CancellationToken ct = default)
    {
        var binding = await AtomicFile.ReadJsonAsync<LauncherUpdateBinding>(Marker(applicationRoot), ct); if (binding is null) return null;
        if (binding.Id == Guid.Empty || binding.DataRoot != paths.Root) throw new LauncherUpdateException("UpdaterRecovery");
        var t = await ReadBound(paths, trust, binding.Id, ct);
        if (t.ApplicationRoot != Path.GetFullPath(applicationRoot).TrimEnd(Path.DirectorySeparatorChar)) throw new LauncherUpdateException("UpdaterPath");
        if (t.State is "Committed" or "RolledBack")
        {
            var expected = t.State == "Committed" ? new ManagedLauncherInstallation(t.Offer.Release.Build, t.Offer.Package.Files) : t.Before;
            var observed = await AtomicFile.ReadJsonAsync<ManagedLauncherInstallation>(SafePath.Join(t.ApplicationRoot, LauncherUpdater.InstallationManifest), ct);
            if (JsonSerializer.Serialize(expected, AtomicFile.JsonOptions) != JsonSerializer.Serialize(observed, AtomicFile.JsonOptions)) throw new LauncherUpdateException("UpdaterIntegrity");
            foreach (var f in expected.Files)
                if (!await VerifiedDownloader.VerifyAsync(SafePath.Join(t.ApplicationRoot, f.Path), f.Sha256, "SHA256", f.Size, ct)) throw new LauncherUpdateException("UpdaterIntegrity");
            // A crash between durable terminal commit and marker removal must not strand future updates.
            if (await AtomicFile.ReadJsonAsync<LauncherUpdateBinding>(Marker(applicationRoot), ct) != binding) throw new LauncherUpdateException("UpdaterConflict");
            File.Delete(Marker(applicationRoot)); return null;
        }
        return t;
    }
    public static async Task StartHelperAsync(LauncherPaths paths, LauncherUpdateTrust trust, Guid id, string applicationRoot, bool rollback, CancellationToken ct = default, int healthTimeoutSeconds = 45)
    {
        if (healthTimeoutSeconds is < 5 or > 120) throw new LauncherUpdateException("UpdaterInvalid");
        var t = await ReadBound(paths, trust, id, ct);
        if (t.ApplicationRoot != Path.GetFullPath(applicationRoot).TrimEnd(Path.DirectorySeparatorChar)) throw new LauncherUpdateException("UpdaterPath");
        var updater = ForSavedBuild(paths, trust, t.Before.Build);
        var parent = UpdateProcessIdentity.Current;
        if (!parent.Executable.Equals(SafePath.Join(t.ApplicationRoot, "PTenoLauncher.exe"), StringComparison.OrdinalIgnoreCase)) throw new LauncherUpdateException("UpdaterParent");
        var files = t.Before.Files.Where(f => f.Path.StartsWith("updater/", StringComparison.Ordinal)).ToArray();
        if (!files.Any(f => f.Path == "updater/" + HelperName) || !files.Any(f => f.Path == "updater/PTenoLauncher.Infrastructure.dll")) throw new LauncherUpdateException("UpdaterBuild");
        foreach (var f in files)
        {
            var source = SafePath.Join(t.ApplicationRoot, f.Path);
            var helper = SafePath.Join(updater.TransactionRoot(id), "helper/" + f.Path[8..]);
            // Recovery reuses the old verified helper, even if the new application no longer contains those old bytes.
            if (File.Exists(helper))
            { if (!await VerifiedDownloader.VerifyAsync(helper, f.Sha256, "SHA256", f.Size, ct)) throw new LauncherUpdateException("UpdaterIntegrity"); }
            else
            {
                if (!await VerifiedDownloader.VerifyAsync(source, f.Sha256, "SHA256", f.Size, ct)) throw new LauncherUpdateException("UpdaterIntegrity");
                Directory.CreateDirectory(Path.GetDirectoryName(helper)!); File.Copy(source, helper, false);
                if (!await VerifiedDownloader.VerifyAsync(helper, f.Sha256, "SHA256", f.Size, ct)) throw new LauncherUpdateException("UpdaterIntegrity");
            }
        }
        var ticket = new LauncherHelperTicket(id, t.ApplicationRoot, paths.Root, parent, rollback ? "Rollback" : "Apply", healthTimeoutSeconds);
        await AtomicFile.WriteJsonAsync(SafePath.Join(updater.TransactionRoot(id), "helper-ticket.json"), ticket, ct);
        var ready = SafePath.Join(updater.TransactionRoot(id), "parent-verified.json"); if (File.Exists(ready)) File.Delete(ready);
        await AtomicFile.WriteJsonAsync(Marker(t.ApplicationRoot), new LauncherUpdateBinding(id, paths.Root), ct);
        var exe = SafePath.Join(updater.TransactionRoot(id), "helper/" + HelperName);
        using var child = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)! }) ?? throw new LauncherUpdateException("UpdaterHelper");
        var until = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < until)
        {
            ct.ThrowIfCancellationRequested();
            var observed = await AtomicFile.ReadJsonAsync<UpdateProcessIdentity>(ready, ct);
            if (observed == parent) return;
            if (child.HasExited) throw new LauncherUpdateException("UpdaterHelper");
            await Task.Delay(100, ct);
        }
        throw new LauncherUpdateException("UpdaterHelper"); // Never close parent unless the helper acknowledged its exact identity.
    }
    private static async Task VerifyHelper(LauncherUpdater updater, LauncherUpdateTransaction t, string helperRoot, CancellationToken ct)
    {
        if (t.Before.Build != LauncherBuild.Current) throw new LauncherUpdateException("UpdaterBuild");
        var expectedRoot = SafePath.Join(updater.TransactionRoot(t.Id), "helper");
        if (!helperRoot.Equals(expectedRoot, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(Environment.ProcessPath!).Equals(SafePath.Join(helperRoot, HelperName), StringComparison.OrdinalIgnoreCase)) throw new LauncherUpdateException("UpdaterPath");
        var files = t.Before.Files.Where(f => f.Path.StartsWith("updater/", StringComparison.Ordinal)).ToArray();
        var actual = Directory.EnumerateFiles(helperRoot, "*", SearchOption.AllDirectories).ToArray();
        if (files.Length == 0 || actual.Length != files.Length) throw new LauncherUpdateException("UpdaterIntegrity");
        foreach (var f in files)
            if (!await VerifiedDownloader.VerifyAsync(SafePath.Join(helperRoot, f.Path[8..]), f.Sha256, "SHA256", f.Size, ct)) throw new LauncherUpdateException("UpdaterIntegrity");
    }
    // Synchronous outer scope keeps Windows Mutex ownership on one thread across async orchestration.
    public static int RunHelper()
    {
        LauncherUpdater? updater = null; LauncherPaths? helperPaths = null; Guid id = Guid.Empty; string stage = "location";
        try
        {
            var helper = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
            if (Path.GetFileName(helper) != "helper") throw new LauncherUpdateException("UpdaterPath");
            var operation = Directory.GetParent(helper)!;
            if (!Guid.TryParseExact(operation.Name, "N", out id) || operation.Parent?.Name != "operations" || operation.Parent.Parent?.Name != "launcher-updates") throw new LauncherUpdateException("UpdaterPath");
            var paths = new LauncherPaths(operation.Parent.Parent.Parent!.FullName); helperPaths = paths;
            using var operationLease = new FileStream(SafePath.Join(operation.FullName, "helper.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var ticket = AtomicFile.ReadJsonAsync<LauncherHelperTicket>(SafePath.Join(operation.FullName, "helper-ticket.json")).GetAwaiter().GetResult() ?? throw new LauncherUpdateException("UpdaterInvalid");
            if (ticket.Id != id || ticket.DataRoot != paths.Root || ticket.Action is not ("Apply" or "Rollback") || ticket.HealthTimeoutSeconds is < 5 or > 120) throw new LauncherUpdateException("UpdaterInvalid");
            stage = "read-transaction";
            var t = ReadBound(paths, LauncherUpdateTrust.Production, id, default).GetAwaiter().GetResult();
            updater = ForSavedBuild(paths, LauncherUpdateTrust.Production, t.Before.Build);
            stage = "verify-helper";
            VerifyHelper(updater, t, helper, default).GetAwaiter().GetResult();
            stage = "verify-binding";
            var binding = PendingAsync(paths, ticket.ApplicationRoot, LauncherUpdateTrust.Production).GetAwaiter().GetResult();
            if (binding?.Id != id || ticket.ApplicationRoot != t.ApplicationRoot || !ticket.Parent.Executable.Equals(SafePath.Join(t.ApplicationRoot, "PTenoLauncher.exe"), StringComparison.OrdinalIgnoreCase)) throw new LauncherUpdateException("UpdaterParent");
            var verified = SafePath.Join(operation.FullName, "parent-verified.json");
            stage = "verify-parent";
            using var parent = ticket.Parent.OpenExact(required: false);
            if (parent is not null)
            {
                AtomicFile.WriteJsonAsync(verified, ticket.Parent).GetAwaiter().GetResult();
                AtomicFile.WriteJsonAsync(StatusFile(updater, id), new LauncherHelperStatus("AwaitingParent")).GetAwaiter().GetResult();
                ticket.Parent.WaitForExitAsync(TimeSpan.FromSeconds(45)).GetAwaiter().GetResult();
            }
            else if (AtomicFile.ReadJsonAsync<UpdateProcessIdentity>(verified).GetAwaiter().GetResult() != ticket.Parent) throw new LauncherUpdateException("UpdaterParent");
            // A restarted helper always restores an interrupted apply instead of guessing how far it got.
            var rollback = ticket.Action == "Rollback" || t.State is "Applying" or "AwaitingHealth";
            stage = "acquire-mutex";
            using var mutex = new Mutex(false, LauncherUpdateGuards.MutexName(paths));
            bool acquired; try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new LauncherUpdateException("UpdaterBusy");
            IDisposable? profiles = null;
            try
            {
                stage = "acquire-profiles";
                profiles = LauncherUpdateGuards.ProfileLeases(paths, default).GetAwaiter().GetResult();
                using (LauncherUpdateGuards.ApplicationWriteLease(t.ApplicationRoot))
                {
                    stage = rollback ? "rollback" : "apply";
                    AtomicFile.WriteJsonAsync(StatusFile(updater, id), new LauncherHelperStatus(rollback ? "RollingBack" : "Applying")).GetAwaiter().GetResult();
                    if (rollback) updater.RollbackAsync(id).GetAwaiter().GetResult();
                    else updater.ApplyPreparedAsync(id).GetAwaiter().GetResult();
                }
            }
            catch { profiles?.Dispose(); throw; }
            finally { mutex.ReleaseMutex(); }
            try
            {
                if (rollback)
                {
                    stage = "restart-restored";
                    File.Delete(Marker(t.ApplicationRoot));
                    using var restored = StartLauncher(t, false);
                    AtomicFile.WriteJsonAsync(StatusFile(updater, id), new LauncherHelperStatus("RolledBack", Child: UpdateProcessIdentity.Capture(restored))).GetAwaiter().GetResult(); return 0;
                }
                stage = "restart-new";
                using var next = StartLauncher(t, true);
                // A failed build can exit before MainModule is available. The retained process handle still supplies
                // its exact PID/start time; the executable path is the verified signed path we just started.
                var childIdentity = new UpdateProcessIdentity(next.Id, next.StartTime.ToUniversalTime().Ticks, Path.GetFullPath(next.StartInfo.FileName));
                AtomicFile.WriteJsonAsync(StatusFile(updater, id), new LauncherHelperStatus("AwaitingHealth", Child: childIdentity)).GetAwaiter().GetResult();
                stage = "wait-health";
                var success = WaitHealth(updater, t, childIdentity, next, TimeSpan.FromSeconds(ticket.HealthTimeoutSeconds)).GetAwaiter().GetResult();
                if (success)
                {
                    updater.ConfirmHealthAsync(id, t.HealthToken, t.Offer.Release.Build).GetAwaiter().GetResult();
                    File.Delete(Marker(t.ApplicationRoot));
                    AtomicFile.WriteJsonAsync(StatusFile(updater, id), new LauncherHelperStatus("Committed", Child: childIdentity)).GetAwaiter().GetResult(); return 0;
                }
                // Never roll back running files or terminate a process tree on a health timeout.
                if (!next.HasExited) throw new LauncherUpdateException("UpdaterHealthTimeout");
                profiles?.Dispose(); profiles = null;
                bool again; try { again = mutex.WaitOne(0); } catch (AbandonedMutexException) { again = true; }
                if (!again) throw new LauncherUpdateException("UpdaterBusy");
                try
                {
                    using var guard = LauncherUpdateGuards.ProfileLeases(paths, default).GetAwaiter().GetResult();
                    using var app = LauncherUpdateGuards.ApplicationWriteLease(t.ApplicationRoot);
                    updater.RollbackAsync(id).GetAwaiter().GetResult(); File.Delete(Marker(t.ApplicationRoot));
                    AtomicFile.WriteJsonAsync(StatusFile(updater, id), new LauncherHelperStatus("RolledBack", "UpdaterHealthFailed")).GetAwaiter().GetResult();
                }
                finally { mutex.ReleaseMutex(); }
                using var restoredAfterFailure = StartLauncher(t, false);
                AtomicFile.WriteJsonAsync(StatusFile(updater, id), new LauncherHelperStatus("RolledBack", "UpdaterHealthFailed", UpdateProcessIdentity.Capture(restoredAfterFailure))).GetAwaiter().GetResult(); return 1;
            }
            finally { profiles?.Dispose(); }
        }
        catch (Exception e)
        {
            // Durable bounded diagnostics survive detached processes with no usable console handles.
            try
            {
                if (helperPaths is not null) new PTenoLauncher.Infrastructure.Diagnostics.LauncherLog(helperPaths).Error("updater.helper." + stage, e);
                if (updater is not null) AtomicFile.WriteJsonAsync(SafePath.Join(updater.TransactionRoot(id), "helper-failure.json"), new {
                    stage, type = e.GetType().FullName, hresult = e.HResult,
                    nativeError = e is System.ComponentModel.Win32Exception native ? native.NativeErrorCode : (int?)null,
#if PTENO_UPDATE_TEST
                    testDetail = e.ToString()[..Math.Min(4096, e.ToString().Length)],
#endif
                    process = Environment.ProcessId, at = DateTimeOffset.UtcNow
                }).GetAwaiter().GetResult();
            }
            catch (Exception diagnostic) when (diagnostic is IOException or UnauthorizedAccessException) { /* Keep original recovery failure. */ }
            if (updater is not null)
            {
                var prior = AtomicFile.ReadJsonAsync<LauncherHelperStatus>(StatusFile(updater, id)).GetAwaiter().GetResult();
                AtomicFile.WriteJsonAsync(StatusFile(updater, id), new LauncherHelperStatus("RecoveryRequired", e is LauncherUpdateException u ? u.Code : "UpdaterHelper", prior?.Child)).GetAwaiter().GetResult();
            }
            Console.Error.WriteLine(e.GetType().Name + ": " + e.Message); return 1;
        }
    }
    private static Process StartLauncher(LauncherUpdateTransaction t, bool health)
    {
        var info = new ProcessStartInfo(SafePath.Join(t.ApplicationRoot, "PTenoLauncher.exe")) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Normal, WorkingDirectory = t.ApplicationRoot };
#if PTENO_UPDATE_TEST
        info.WindowStyle = ProcessWindowStyle.Hidden; // Unattended test builds only; real users receive the restarted interactive window.
#endif
        info.ArgumentList.Add("--data-root"); info.ArgumentList.Add(t.DataRoot);
        if (health) { info.ArgumentList.Add("--launcher-update-health"); info.ArgumentList.Add(t.Id.ToString("N")); }
        else info.ArgumentList.Add("--skip-update-check");
        return Process.Start(info) ?? throw new LauncherUpdateException("UpdaterHelper");
    }
    private static async Task<bool> WaitHealth(LauncherUpdater updater, LauncherUpdateTransaction t, UpdateProcessIdentity expected, Process child, TimeSpan timeout)
    {
        var file = SafePath.Join(updater.TransactionRoot(t.Id), "health.json"); var until = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < until && !child.HasExited)
        {
            var health = await AtomicFile.ReadJsonAsync<LauncherHealth>(file);
            if (health is not null && health.Id == t.Id && health.Token == t.HealthToken && health.Build == t.Offer.Release.Build && health.Process == expected && health.Initialized && health.WindowReady && health.DataSchema == t.Offer.Release.Build.DataSchema)
            { using var live = expected.OpenExact(required: true); return true; }
            await Task.Delay(100);
        }
        return false;
    }
    public static async Task WriteHealthAsync(LauncherPaths paths, string applicationRoot, Guid id, bool initialized, bool windowReady, CancellationToken ct = default)
    {
        var t = await PendingAsync(paths, applicationRoot, LauncherUpdateTrust.Production, ct) ?? throw new LauncherUpdateException("UpdaterRecovery");
        if (t.Id != id || t.State != "AwaitingHealth" || LauncherBuild.Current != t.Offer.Release.Build || !initialized || !windowReady) throw new LauncherUpdateException("UpdaterHealthFailed");
        var current = UpdateProcessIdentity.Current;
        if (!current.Executable.Equals(SafePath.Join(t.ApplicationRoot, "PTenoLauncher.exe"), StringComparison.OrdinalIgnoreCase)) throw new LauncherUpdateException("UpdaterParent");
        using var db = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = paths.Database, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly }.ToString());
        await db.OpenAsync(ct); using var command = db.CreateCommand(); command.CommandText = "PRAGMA user_version";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 2) throw new LauncherUpdateException("UpdaterBuild");
        foreach (var f in t.Offer.Package.Files)
            if (!await VerifiedDownloader.VerifyAsync(SafePath.Join(t.ApplicationRoot, f.Path), f.Sha256, "SHA256", f.Size, ct)) throw new LauncherUpdateException("UpdaterIntegrity");
        var updater = ForSavedBuild(paths, LauncherUpdateTrust.Production, t.Before.Build);
        await AtomicFile.WriteJsonAsync(SafePath.Join(updater.TransactionRoot(id), "health.json"), new LauncherHealth(id, t.HealthToken, LauncherBuild.Current, current, true, true, 2), ct);
    }
}
