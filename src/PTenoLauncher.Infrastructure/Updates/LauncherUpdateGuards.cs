using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Updates;

public sealed record UpdateProcessIdentity(int Pid, long StartTicks, string Executable)
{
    public static UpdateProcessIdentity Capture(Process process) => new(process.Id, process.StartTime.ToUniversalTime().Ticks,
        Path.GetFullPath(process.MainModule?.FileName ?? throw new LauncherUpdateException("UpdaterParent")));
    public static UpdateProcessIdentity Current { get { using var p = Process.GetCurrentProcess(); return Capture(p); } }
    internal Process? OpenExact(bool required)
    {
        if (Pid <= 0 || StartTicks <= 0 || string.IsNullOrEmpty(Executable)) throw new LauncherUpdateException("UpdaterParent");
        Process p;
        try { p = Process.GetProcessById(Pid); }
        catch (ArgumentException) { if (required) throw new LauncherUpdateException("UpdaterParent"); return null; }
        try
        {
            if (p.HasExited) { p.Dispose(); if (required) throw new LauncherUpdateException("UpdaterParent"); return null; }
            if (Capture(p) != this) throw new LauncherUpdateException("UpdaterParent");
            return p;
        }
        catch { p.Dispose(); throw; }
    }
    public async Task WaitForExitAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        using var process = OpenExact(required: true)!;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(timeout);
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new LauncherUpdateException("UpdaterParent"); }
    }
}
public static class LauncherUpdateGuards
{
    public static string MutexName(LauncherPaths paths) => @"Local\PTenoLauncher-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(paths.Root.ToUpperInvariant())))[..24];
    private static string LockPath(string root) => SafePath.Join(root, ".pteno-running.lock");
    public static IDisposable ApplicationReadLease(string root)
    {
        var file = LockPath(root);
        if (!File.Exists(file)) { using var create = new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite); }
        return new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }
    internal static IDisposable ApplicationWriteLease(string root)
    {
        try { return new FileStream(LockPath(root), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new LauncherUpdateException("UpdaterBusy"); }
    }
    internal static async Task<IDisposable> ProfileLeases(LauncherPaths paths, CancellationToken ct)
    {
        var leases = new List<IDisposable>();
        try
        {
            // The launcher data-root mutex must already be held: no application profile can be added during this scan.
            var store = new SqliteLauncherStore(paths); var profiles = await store.GetProfilesAsync(ct);
            if (profiles.Count > 2048) throw new LauncherUpdateException("UpdaterBusy");
            var usage = new ProfileUsage(paths);
            foreach (var p in profiles.OrderBy(p => p.Id)) { ct.ThrowIfCancellationRequested(); leases.Add(usage.Acquire(p.Id)); usage.CheckWorldLocks(p.Id); }
            var servers = SafePath.Join(paths.Root, "servers");
            if (Directory.Exists(servers))
            {
                var entries = Directory.EnumerateFileSystemEntries(servers).Take(2049).ToArray(); if (entries.Length > 2048) throw new LauncherUpdateException("UpdaterBusy");
                foreach (var entry in entries)
                {
                    SafePath.RejectLinks(entry);
                    if (Directory.Exists(entry) && File.Exists(SafePath.Join(entry, "active-server.json")))
                        throw new LauncherUpdateException("UpdaterBusy"); // No running server subsystem exists yet; unknown active records fail closed.
                }
            }
            return new LeaseSet(leases);
        }
        catch (Exception e)
        {
            foreach (var l in leases) l.Dispose();
            if (e is OperationCanceledException or LauncherUpdateException) throw;
            throw new LauncherUpdateException("UpdaterBusy", e);
        }
    }
    private sealed class LeaseSet(List<IDisposable> leases) : IDisposable { public void Dispose() { foreach (var l in leases) l.Dispose(); } }
}
