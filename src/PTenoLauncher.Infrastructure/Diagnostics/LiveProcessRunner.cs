using System.Diagnostics;
using System.Text;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Diagnostics;
public static class LiveProcessRunner
{
    public static async Task<int> RunAsync(ProcessStartInfo info, string logFile, Func<Process, Task>? started = null,
        IProgress<string>? output = null, IReadOnlyList<string>? secrets = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        info.UseShellExecute = false; info.CreateNoWindow = true; info.RedirectStandardOutput = true; info.RedirectStandardError = true;
        SafePath.RejectLinks(logFile); Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
        await using var file = new FileStream(logFile, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 81920, true);
        await using var writer = new StreamWriter(file, Encoding.UTF8);
        using var process = Process.Start(info) ?? throw new IOException("Процес не запустився.");
        if (started is not null)
            try { await started(process); }
            catch (Exception ex) { await writer.WriteLineAsync("Startup diagnostics: " + ex.GetType().Name); output?.Report("Не вдалося записати повну діагностику запуску. Процес гри не завершується примусово."); }
        using var gate = new SemaphoreSlim(1, 1); long bytes = 0; long lastReport = 0; int omitted = 0;
        async Task Pump(StreamReader reader, string channel)
        {
            await foreach (var raw in ReadBoundedLinesAsync(reader))
            {
                var line = raw;
                foreach (var secret in secrets ?? []) if (secret.Length >= 8) line = line.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
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
        await Task.WhenAll(Pump(process.StandardOutput, "out"), Pump(process.StandardError, "err"), process.WaitForExitAsync(CancellationToken.None));
        var exit = $"Exit code: {process.ExitCode}"; await writer.WriteLineAsync(exit); await writer.FlushAsync();
        output?.Report(exit); return process.ExitCode;
    }
    public static async IAsyncEnumerable<string> ReadBoundedLinesAsync(TextReader reader, int maxLength = 8192)
    {
        var buffer = new char[2048]; var line = new StringBuilder(); bool truncated = false; int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
            for (int i = 0; i < count; i++)
            {
                var c = buffer[i];
                if (c == '\n') { yield return line.ToString() + (truncated ? " [truncated]" : ""); line.Clear(); truncated = false; }
                else if (c != '\r') { if (line.Length < maxLength) line.Append(c); else truncated = true; }
            }
        if (line.Length > 0 || truncated) yield return line.ToString() + (truncated ? " [truncated]" : "");
    }
}