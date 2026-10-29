using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

internal static class ManagedJavaValidation
{
    public static bool OwnedFolder(string directory, ManagedJavaReceipt receipt)
    {
        if (receipt.ArchiveSha256 is null || receipt.ArchiveSha256.Length != 64 || !receipt.ArchiveSha256.All(Uri.IsHexDigit)) return false;
        var prefix = $"temurin-{receipt.Major}-{receipt.ArchiveSha256[..12]}";
        var name = Path.GetFileName(directory);
        return name.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            (name.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase) && name.Length == prefix.Length + 9 && name[(prefix.Length + 1)..].All(Uri.IsHexDigit));
    }

    public static async Task<bool> VerifyAsync(string root, ManagedJavaReceipt receipt, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            SafePath.RejectLinks(root);
            if (!Directory.Exists(root) || receipt.Major is < 8 or > 100 ||
                string.IsNullOrWhiteSpace(receipt.Release) || receipt.Release.Length > 128 || receipt.Release.Any(char.IsControl) ||
                receipt.ArchiveSha256 is null || receipt.ArchiveSha256.Length != 64 || !receipt.ArchiveSha256.All(Uri.IsHexDigit) ||
                string.IsNullOrWhiteSpace(receipt.JavaRelativePath) || receipt.JavaRelativePath.Length > 1024 ||
                receipt.Files is null || receipt.Files.Count is 0 or > 20000) return false;
            var java = SafePath.Join(root, receipt.JavaRelativePath);
            if (!Path.GetFileName(java).Equals("java.exe", StringComparison.OrdinalIgnoreCase)) return false;
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
            foreach (var entry in receipt.Files)
            {
                ct.ThrowIfCancellationRequested();
                if (entry is null || string.IsNullOrWhiteSpace(entry.RelativePath) || entry.RelativePath.Length > 1024 ||
                    entry.RelativePath.Equals("runtime.json", StringComparison.OrdinalIgnoreCase) || !files.Add(entry.RelativePath) ||
                    entry.Algorithm != "SHA256" || entry.Hash is null || entry.Hash.Length != 64 || !entry.Hash.All(Uri.IsHexDigit) ||
                    entry.Size is < 0 or > 512L * 1024 * 1024 || (total += entry.Size) > 4L * 1024 * 1024 * 1024) return false;
                if (!await VerifiedDownloader.VerifyAsync(SafePath.Join(root, entry.RelativePath), entry.Hash, entry.Algorithm, entry.Size, ct)) return false;
            }
            if (!files.Contains(receipt.JavaRelativePath) || !JavaRuntimeManager.IsX64Executable(java)) return false;
            // Reject untracked DLLs/files and directory links; never recurse through a reparse point.
            var pending = new Stack<string>(); pending.Push(root); int entries = 0, seen = 0;
            while (pending.TryPop(out var directory))
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    ct.ThrowIfCancellationRequested();
                    if (++entries > 40000) return false;
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
                    if ((attributes & FileAttributes.Directory) != 0) { pending.Push(path); continue; }
                    var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                    if (relative.Equals("runtime.json", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!files.Contains(relative)) return false;
                    seen++;
                }
            return seen == files.Count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or NotSupportedException)
        { return false; }
    }
}
