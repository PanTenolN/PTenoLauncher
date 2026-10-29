using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed record JavaRuntime(string Path, int Major, bool Is64Bit);
public sealed partial class JavaManager
{
    [GeneratedRegex(@"(?:java|openjdk) version ""(?<major>\d+)(?:\.(?<minor>\d+))?")]
    private static partial Regex VersionPattern();
    public async Task<JavaRuntime> InspectAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!Path.GetFileName(path).Equals("java.exe", StringComparison.OrdinalIgnoreCase) || !JavaRuntimeManager.IsX64Executable(path))
            throw new ArgumentException("Select a Windows x64 java.exe.");
        SafePath.RejectLinks(path);
        var psi = new ProcessStartInfo(Path.GetFullPath(path)) { WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        // Version discovery must not execute agents/options inherited from unrelated Java tools.
        foreach (var name in new[] { "JAVA_TOOL_OPTIONS", "JDK_JAVA_OPTIONS", "_JAVA_OPTIONS", "CLASSPATH" }) psi.Environment.Remove(name);
        psi.ArgumentList.Add("-version");
        using var process = Process.Start(psi) ?? throw new IOException("Java failed to start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        void Kill()
        {
            try { if (!process.HasExited) process.Kill(true); }
            catch (InvalidOperationException) when (process.HasExited) { }
            catch (System.ComponentModel.Win32Exception) when (process.HasExited) { }
        }
        async Task<string> Read(StreamReader reader)
        {
            var output = new StringBuilder(); var buffer = new char[2048]; int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), timeout.Token)) > 0)
            {
                if (output.Length + count > 65536) { Kill(); throw new InvalidDataException("Java version output exceeded 64 KiB."); }
                output.Append(buffer, 0, count);
            }
            return output.ToString();
        }
        var stderr = Read(process.StandardError); var stdout = Read(process.StandardOutput);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var text = string.Concat(await Task.WhenAll(stderr, stdout));
            var match = VersionPattern().Match(text);
            if (process.ExitCode != 0 || !match.Success) throw new InvalidDataException("Could not determine Java version.");
            if (!int.TryParse(match.Groups["major"].Value, out var major) ||
                (major == 1 && !int.TryParse(match.Groups["minor"].Value, out major)) || major is < 8 or > 100)
                throw new InvalidDataException("Unsupported Java version.");
            return new(Path.GetFullPath(path), major, true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new TimeoutException("Java version probe timed out."); }
        finally
        {
            try { Kill(); if (!process.HasExited) await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            finally
            {
                await timeout.CancelAsync();
                try { await Task.WhenAll(stderr, stdout); } catch { /* Preserve the original validation/cancellation failure. */ }
            }
        }
    }
    public IEnumerable<string> Discover()
    {
        var candidates = new List<string>();
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome)) candidates.Add(Path.Combine(javaHome, "bin", "java.exe"));
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (!string.IsNullOrWhiteSpace(dir)) candidates.Add(Path.Combine(dir.Trim('"'), "java.exe"));
        foreach (var vendor in new[] { "Java", "Eclipse Adoptium", "Microsoft", "Zulu" })
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), vendor);
            try
            {
                if (Directory.Exists(directory))
                    foreach (var sub in Directory.EnumerateDirectories(directory)) candidates.Add(Path.Combine(sub, "bin", "java.exe"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* One unavailable vendor directory must not break discovery. */ }
        }
        return candidates.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
