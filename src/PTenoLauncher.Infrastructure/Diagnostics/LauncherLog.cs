using System.Text.RegularExpressions;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Diagnostics;

/// <summary>Only explicit diagnostic events; no HTTP bodies, command lines or user files.</summary>
public sealed partial class LauncherLog(LauncherPaths paths)
{
    private readonly object gate = new();
    public static string Redact(string text) => SensitivePattern().Replace(text, "$1=[REDACTED]");
    [GeneratedRegex(@"(?i)\b(access_token|refresh_token|authorization|identityToken|password|client_secret)\b\s*[:=]?\s*(""[^""]*""|[^\s,}]+)")]
    private static partial Regex SensitivePattern();
    public void Write(string category, string message)
    {
        lock (gate)
        {
            var path = SafePath.Join(paths.Logs, $"launcher-{DateTime.UtcNow:yyyy-MM-dd}.log");
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} [{category}] {Redact(message).Replace('\r', ' ').Replace('\n', ' ')}{Environment.NewLine}");
            // Fixed retention; user profile/world data are never part of log cleanup.
            foreach (var file in Directory.EnumerateFiles(paths.Logs, "launcher-????-??-??.log").OrderByDescending(File.GetLastWriteTimeUtc).Skip(14))
            { SafePath.RejectLinks(file); File.Delete(file); }
        }
    }
    public void Error(string operation, Exception ex) => Write("error", $"{operation}: {ex.GetType().Name}" +
        (ex is Authentication.AuthenticationServiceException auth ? $"; {auth.SafeDiagnostic}" : ""));
}
