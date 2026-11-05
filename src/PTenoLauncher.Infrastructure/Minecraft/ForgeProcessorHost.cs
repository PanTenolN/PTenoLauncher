using System.Diagnostics;
using PTenoLauncher.Infrastructure.Diagnostics;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

// Internal test seam: production construction always selects the managed Java host below.
internal interface IForgeProcessorHost
{
    Task VerifyRuntimeAsync(int major, CancellationToken ct);
    Task ExecuteAsync(ForgeProcessorStep step, string stage, string log, CancellationToken ct);
}

internal sealed class ForgeProcessorHost(LauncherPaths paths, JavaRuntimeManager runtimes, JavaManager java) : IForgeProcessorHost
{
    private JavaRuntime? runtime;
    public async Task VerifyRuntimeAsync(int major, CancellationToken ct)
    {
        runtime = await runtimes.EnsureManagedAsync(major, ct: ct);
        if (runtime.Major != major || !runtime.Is64Bit || !await JavaRuntimeManager.VerifyManagedPathAsync(paths, runtime.Path, ct))
            throw new InvalidDataException("Processor Java runtime failed verification.");
        var inspected = await java.InspectAsync(runtime.Path, ct);
        if (inspected.Major != major || !inspected.Is64Bit) throw new InvalidDataException("Processor Java version mismatch.");
    }
    public async Task ExecuteAsync(ForgeProcessorStep step, string stage, string log, CancellationToken ct)
    {
        if (runtime is null || !await JavaRuntimeManager.VerifyManagedPathAsync(paths, runtime.Path, ct))
            throw new InvalidDataException("Processor Java runtime changed or is missing.");
        var info = new ProcessStartInfo(runtime.Path) { WorkingDirectory = stage };
        // Do not leak inherited provider credentials or inject inherited Java agents/options.
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        info.Environment.Clear(); info.Environment["SystemRoot"] = systemRoot;
        info.Environment["PATH"] = Path.GetDirectoryName(runtime.Path) + Path.PathSeparator + Path.Combine(systemRoot, "System32");
        var temporary = SafePath.Join(stage, "tmp"); Directory.CreateDirectory(temporary);
        info.Environment["TEMP"] = temporary; info.Environment["TMP"] = temporary;
        info.ArgumentList.Add("-Xmx1024M"); info.ArgumentList.Add("-Djava.io.tmpdir=" + temporary);
        info.ArgumentList.Add("-cp"); info.ArgumentList.Add(string.Join(Path.PathSeparator, step.Classpath));
        info.ArgumentList.Add(step.MainClass);
        foreach (var arg in step.Arguments) info.ArgumentList.Add(arg);
        var active = SafePath.Join(stage, "active-processor.json");
        try
        {
            await RunCommandAsync(info, log, TimeSpan.FromMinutes(5), ct, async process =>
            {
                try { await AtomicFile.WriteJsonAsync(active, new ActiveGameRecord(process.Id, process.StartTime.ToUniversalTime().Ticks, runtime.Path), ct); }
                catch
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    throw;
                }
            });
        }
        finally
        {
            SafePath.RejectLinks(active);
            var record = await AtomicFile.ReadJsonAsync<ActiveGameRecord>(active, CancellationToken.None);
            if (record is not null && !ActiveGameRecord.IsRunning(record)) File.Delete(active);
        }
    }
    internal static async Task RunCommandAsync(ProcessStartInfo info, string log, TimeSpan timeout, CancellationToken ct, Func<Process, Task>? started = null)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(timeout));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(timeout);
        int exit;
        Exception? startFailure = null;
        try { exit = await LiveProcessRunner.RunAsync(info, log, async process =>
        {
            if (started is null) return;
            try { await started(process); } catch (Exception e) { startFailure = e; throw; }
        }, ct: deadline.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("Forge client processor timed out."); }
        if (startFailure is not null) throw new IOException("Processor tracking could not be persisted.", startFailure);
        if (exit != 0) throw new IOException("Forge client processor failed with exit code " + exit + ". See its bounded local log.");
    }
}
