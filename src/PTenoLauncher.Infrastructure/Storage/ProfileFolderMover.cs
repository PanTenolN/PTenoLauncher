using System.Security.Cryptography;
using System.Text;
using PTenoLauncher.Core;

namespace PTenoLauncher.Infrastructure.Storage;

public sealed record ProfileFolderMove(Guid ProfileId, string Name, string Source, string Target, string Fingerprint);

public sealed class ProfileFolderMover(LauncherPaths paths, ILauncherStore store, ProfileUsage usage)
{
    public async Task<ProfileFolderMove> PreviewAsync(Guid id, CancellationToken ct = default)
    {
        using var lease = usage.Acquire(id);
        return await PlanAsync(id, ct);
    }

    public async Task MoveAsync(ProfileFolderMove approved, CancellationToken ct = default)
    {
        using var lease = usage.Acquire(approved.ProfileId);
        var current = await PlanAsync(approved.ProfileId, ct);
        if (approved != current) throw new IOException("ContentChanged");
        ct.ThrowIfCancellationRequested();
        SafePath.RejectLinks(current.Source);
        SafePath.RejectLinks(current.Target);
        Directory.Move(current.Source, current.Target);
    }

    private async Task<ProfileFolderMove> PlanAsync(Guid id, CancellationToken ct)
    {
        usage.CheckWorldLocks(id);
        var profile = (await store.GetProfilesAsync(ct)).SingleOrDefault(profile => profile.Id == id)
            ?? throw new IOException("ContentMissing");
        var source = paths.Profile(id);
        if (!Directory.Exists(source)) throw new IOException("ContentMissing");
        var legacy = LauncherPaths.LegacyProfileRelative(id);
        var target = SafePath.Join(paths.Root, paths.ProfileRelative(id) == legacy ? LauncherPaths.NamedProfileRelative(profile) : legacy);
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("ContentCollision");

        // Generated translation receipts and journals can bind data to the current directory.
        var transactions = SafePath.Join(source, "transactions");
        if (Directory.Exists(SafePath.Join(source, "translations")) ||
            (await store.GetArtifactsAsync(id, ct)).Any(artifact => artifact.Kind == "translation-resourcepack") ||
            Directory.Exists(transactions) && Directory.EnumerateFiles(transactions, "*.json").Any())
            throw new IOException("ProfileFolderMoveDependent");
        if (!string.IsNullOrWhiteSpace(profile.JavaPath))
        {
            var java = Path.GetRelativePath(source, Path.GetFullPath(profile.JavaPath));
            if (!Path.IsPathRooted(java) && java != ".." && !java.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new IOException("ProfileFolderMoveDependent");
        }

        var entries = new List<string>();
        var pending = new Stack<string>();
        pending.Push(source);
        while (pending.TryPop(out var folder))
        {
            SafePath.RejectLinks(folder);
            foreach (var child in Directory.EnumerateFileSystemEntries(folder))
            {
                ct.ThrowIfCancellationRequested();
                if (entries.Count >= 50000) throw new IOException("ContentCopyLimit");
                SafePath.RejectLinks(child);
                var relative = Path.GetRelativePath(source, child);
                if (Directory.Exists(child))
                {
                    pending.Push(child);
                    entries.Add($"D|{relative}");
                }
                else
                {
                    var file = new FileInfo(child);
                    entries.Add($"F|{relative}|{file.Length}|{file.LastWriteTimeUtc.Ticks}");
                }
            }
        }
        return new(id, profile.Name, source, target,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', entries.Order(StringComparer.Ordinal))))));
    }
}
