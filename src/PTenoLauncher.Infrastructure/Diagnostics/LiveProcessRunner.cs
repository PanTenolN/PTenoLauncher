using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Diagnostics;

public static class LiveProcessRunner
{
    public static async Task<int> RunAsync(ProcessStartInfo info, string logFile, Func<Process, Task>? started = null,
        IProgress<string>? output = null, IReadOnlyList<string>? secrets = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        info.UseShellExecute = false; info.CreateNoWindow = true;
        info.RedirectStandardOutput = true; info.RedirectStandardError = true;
        SafePath.RejectLinks(logFile); Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
        await using var file = new FileStream(logFile, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 81920, true);
        await using var writer = new StreamWriter(file, Encoding.UTF8);
        using var process = Process.Start(info) ?? throw new IOException("Process failed to start.");
        using var gate = new SemaphoreSlim(1, 1);
        using var readers = new CancellationTokenSource();
        long bytes = 0, lastReport = 0;
        int omitted = 0;
        bool drainExpired = false;
        var pumpFailure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Pump(StreamReader reader, string channel)
        {
            try
            {
                await foreach (var raw in ReadBoundedLinesAsync(reader, ct: readers.Token))
                {
                    var line = raw;
                    foreach (var secret in secrets ?? [])
                        if (secret.Length >= 8) line = line.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
                    line = LauncherLog.Redact(line);
                    await gate.WaitAsync();
                    try
                    {
                        var text = $"{DateTimeOffset.UtcNow:HH:mm:ss} [{channel}] {line}";
                        var size = Encoding.UTF8.GetByteCount(text) + 2;
                        if (bytes + size <= 10 * 1024 * 1024) { await writer.WriteLineAsync(text); bytes += size; }
                        var now = Environment.TickCount64;
                        if (now - lastReport >= 50)
                        {
                            output?.Report(omitted > 0 ? $"[{omitted} console lines omitted from live view]\n{text}" : text);
                            omitted = 0; lastReport = now;
                        }
                        else omitted++;
                    }
                    finally { gate.Release(); }
                }
            }
            catch (OperationCanceledException) when (readers.IsCancellationRequested) { drainExpired = true; }
            catch (Exception ex) { pumpFailure.TrySetResult(ex); throw; }
        }
        var pumps = Task.WhenAll(Pump(process.StandardOutput, "out"), Pump(process.StandardError, "err"));
        void Terminate()
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
            catch (System.ComponentModel.Win32Exception) when (process.HasExited) { }
        }
        try
        {
            if (started is not null)
                try { await started(process); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    await gate.WaitAsync();
                    try { await writer.WriteLineAsync("Startup diagnostics: " + ex.GetType().Name); }
                    finally { gate.Release(); }
                    output?.Report("Startup state could not be saved; close the game before changing its profile.");
                }
            var exitTask = process.WaitForExitAsync(ct);
            await Task.WhenAny(exitTask, pumpFailure.Task);
            // A broken log sink must not leave an unsupervised process running.
            if (pumpFailure.Task.IsCompleted)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(await pumpFailure.Task).Throw();
            try { await exitTask; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Terminate();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            // A descendant may retain inherited output after the parent exits.
            readers.CancelAfter(TimeSpan.FromSeconds(5));
            await pumps;
            if (drainExpired) await writer.WriteLineAsync("Console drain timed out; inherited output was closed.");
            if (ct.IsCancellationRequested) await writer.WriteLineAsync("Process stopped by an explicit cancellation request.");
            var exit = $"Exit code: {process.ExitCode}";
            await writer.WriteLineAsync(exit); await writer.FlushAsync(); output?.Report(exit);
            ct.ThrowIfCancellationRequested();
            return process.ExitCode;
        }
        finally
        {
            try
            {
                Terminate();
                if (!process.HasExited) await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                await readers.CancelAsync();
                // Observe both pumps before disposing their writer and process.
                try { await pumps; } catch { /* The original pump error propagates above. */ }
            }
        }
    }

    public static async IAsyncEnumerable<string> ReadBoundedLinesAsync(TextReader reader, int maxLength = 8192,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);
        var buffer = new char[2048]; var line = new StringBuilder(); bool truncated = false; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
            for (int i = 0; i < count; i++)
            {
                var c = buffer[i];
                if (c == '\n') { yield return line.ToString() + (truncated ? " [truncated]" : ""); line.Clear(); truncated = false; }
                else if (c != '\r') { if (line.Length < maxLength) line.Append(c); else truncated = true; }
            }
        if (line.Length > 0 || truncated) yield return line.ToString() + (truncated ? " [truncated]" : "");
    }
}
