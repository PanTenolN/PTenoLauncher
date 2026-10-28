using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed record JavaRuntime(string Path, int Major, bool Is64Bit);

public sealed partial class JavaManager
{
    [GeneratedRegex(@"(?:java|openjdk) version ""(?<major>\d+)(?:\.(?<minor>\d+))?")]
    private static partial Regex VersionPattern();
    public async Task<JavaRuntime> InspectAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path) || !Path.GetFileName(path).Equals("java.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Оберіть java.exe із довіреної Java x64.");
        var psi = new ProcessStartInfo(Path.GetFullPath(path)) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        psi.ArgumentList.Add("-version");
        using var process = Process.Start(psi) ?? throw new IOException("Java не запускається.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var text = await stderr + await stdout;
            var match = VersionPattern().Match(text);
            if (process.ExitCode != 0 || !match.Success) throw new InvalidDataException("Не вдалося визначити версію Java.");
            var major = int.Parse(match.Groups["major"].Value);
            if (major == 1) major = int.Parse(match.Groups["minor"].Value);
            return new(Path.GetFullPath(path), major, text.Contains("64-Bit", StringComparison.OrdinalIgnoreCase) || text.Contains("aarch64", StringComparison.OrdinalIgnoreCase));
        }
        finally { if (!process.HasExited) process.Kill(true); }
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
            if (Directory.Exists(directory))
                foreach (var sub in Directory.EnumerateDirectories(directory))
                    candidates.Add(Path.Combine(sub, "bin", "java.exe"));
        }
        return candidates.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }
}