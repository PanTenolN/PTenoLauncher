using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Diagnostics;

public static partial class LiveProcessRunner
{
    public const string TranscriptLimitNotice = "Console transcript reached its 10 MiB limit. Further process output is visible live only.";
    [GeneratedRegex(@"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07\x1B]*(?:\x07|\x1B\\)|[ -/]*[@-Z\\-_])|[\x00-\x08\x0B-\x1F\x7F]")]
    private static partial Regex TerminalControls();

    public static async Task<int> RunAsync(ProcessStartInfo info, string logFile, Func<Process, Task>? started = null,
        IProgress<string>? output = null, IReadOnlyList<string>? secrets = null, CancellationToken ct = default,
        bool formatMinecraftLog = false)
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
        using var progressStop = new CancellationTokenSource();
        long bytes = 0, lastReport = 0;
        const long transcriptLimit = 10 * 1024 * 1024;
        var limitReached = false;
        var pending = new List<string>();
        bool drainExpired = false;
        var pumpFailure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task ReportPendingAsync()
        {
            if (pending.Count == 0) return;
            await writer.FlushAsync();
            output!.Report(string.Join('\n', pending));
            pending.Clear();
            lastReport = Environment.TickCount64;
        }
        async Task FlushLiveAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            try
            {
                while (await timer.WaitForNextTickAsync(progressStop.Token))
                {
                    await gate.WaitAsync(progressStop.Token);
                    try { await ReportPendingAsync(); }
                    finally { gate.Release(); }
                }
            }
            catch (OperationCanceledException) when (progressStop.IsCancellationRequested) { }
            catch (Exception ex) { pumpFailure.TrySetResult(ex); throw; }
        }
        var live = output is null ? Task.CompletedTask : FlushLiveAsync();
        string Redact(string raw)
        {
            var line = raw;
            foreach (var secret in secrets ?? [])
            {
                if (secret.Length < 8) continue;
                line = line.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
                if (formatMinecraftLog && System.Security.SecurityElement.Escape(secret) is { } escaped && escaped != secret)
                    line = line.Replace(escaped, "[REDACTED]", StringComparison.Ordinal);
            }
            return LauncherLog.Redact(line);
        }
        async Task RecordAsync(IEnumerable<string> lines, string channel)
        {
            using var incoming = lines.GetEnumerator();
            if (!incoming.MoveNext()) return;
            await gate.WaitAsync();
            try
            {
                var time = DateTimeOffset.UtcNow;
                do
                {
                    var text = $"{time:HH:mm:ss} [{channel}] {Redact(incoming.Current)}";
                    var size = Encoding.UTF8.GetByteCount(text) + Encoding.UTF8.GetByteCount(writer.NewLine);
                    if (!limitReached && bytes + size <= transcriptLimit)
                    {
                        await writer.WriteLineAsync(text);
                        bytes += size;
                    }
                    else if (!limitReached)
                    {
                        limitReached = true;
                        await writer.WriteLineAsync(TranscriptLimitNotice);
                        if (output is not null) pending.Add(TranscriptLimitNotice);
                    }
                    if (output is not null)
                    {
                        pending.Add(text);
                        if (pending.Count >= 64 || Environment.TickCount64 - lastReport >= 50) await ReportPendingAsync();
                    }
                }
                while (incoming.MoveNext());
            }
            finally { gate.Release(); }
        }
        async Task Pump(StreamReader reader, string channel)
        {
            var events = formatMinecraftLog ? new MinecraftLogEvents() : null;
            try
            {
                var lineLimit = formatMinecraftLog ? MinecraftLogEvents.EventLimit : 8192;
                await foreach (var raw in ReadBoundedLinesAsync(reader, lineLimit, readers.Token))
                    await RecordAsync(events?.Accept(TerminalControls().Replace(raw, "")) ?? [raw], channel);
            }
            catch (OperationCanceledException) when (readers.IsCancellationRequested) { drainExpired = true; }
            catch (Exception ex) { pumpFailure.TrySetResult(ex); throw; }
            try
            {
                if (events is not null) await RecordAsync(events.Finish(), channel);
            }
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
            await progressStop.CancelAsync();
            await live;
            await ReportPendingAsync();
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
                await progressStop.CancelAsync();
                // Observe both pumps before disposing their writer and process.
                try { await pumps; } catch { /* The original pump error propagates above. */ }
                try { await live; } catch { /* The original live output error propagates above. */ }
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
