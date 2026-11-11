using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed record ContentFileAction(Guid Profile, ProfileContentKind Kind, ProfileContentEntry Entry,
    string Source, string Target, string? Backup, DateTime SourceChangedUtc);

public sealed partial class ProfileContent
{
    public ContentFileAction PlanCopy(Guid profile, ProfileContentKind kind, ProfileContentEntry entry, string directory)
    {
        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory) || IsWithin(paths.Root, directory)) throw new IOException("ContentCopyDestination");
        return Plan(profile, kind, entry, SafePath.Join(directory, entry.FileName), null);
    }

    public ContentFileAction PlanRemove(Guid profile, ProfileContentKind kind, ProfileContentEntry entry)
        => PlanRemove(profile, kind, entry, paths.Backups);

    public async Task<ContentFileAction> PlanRemoveAsync(Guid profile, ProfileContentKind kind, ProfileContentEntry entry,
        CancellationToken ct = default)
        => PlanRemove(profile, kind, entry, kind == ProfileContentKind.Worlds && backupManager is not null
            ? await backupManager.GetDirectoryAsync(ct) : paths.Backups);

    private ContentFileAction PlanRemove(Guid profile, ProfileContentKind kind, ProfileContentEntry entry, string backupDirectory)
    {
        var stamp = Guid.NewGuid().ToString("N")[..12];
        var target = SafePath.Join(paths.Root, $"trash/content/{profile:N}/{stamp}/{entry.FileName}");
        var backup = kind == ProfileContentKind.Worlds
            ? SafePath.Join(ReadableName.Free(SafePath.Join(backupDirectory, "worlds"),
                ReadableName.Stem(entry.FileName, "World", 100) + $" — {DateTime.UtcNow:yyyy-MM-dd}"), entry.FileName) : null;
        return Plan(profile, kind, entry, target, backup);
    }

    private ContentFileAction Plan(Guid profile, ProfileContentKind kind, ProfileContentEntry entry, string target, string? backup)
    {
        if (entry.FileName != Path.GetFileName(entry.FileName) || entry.FileName.StartsWith(".pteno-", StringComparison.OrdinalIgnoreCase))
            throw new IOException("ContentMissing");
        var source = SafePath.Join(Folder(profile, kind), entry.FileName);
        EnsureExists(entry, source);
        EnsureFree(target);
        return new(profile, kind, entry, source, target, backup, File.GetLastWriteTimeUtc(source));
    }

    public async Task CopyAsync(ContentFileAction plan, CancellationToken ct = default)
    {
        if (IsWithin(paths.Root, plan.Target)) throw new IOException("ContentCopyDestination");
        using var lease = ValidateAction(plan);
        await CopyItemAsync(plan.Entry, plan.Source, plan.Target, ct);
    }

    public async Task RemoveAsync(ContentFileAction plan, bool backup, CancellationToken ct = default)
    {
        var backupDirectory = backup && backupManager is not null ? await backupManager.GetDirectoryAsync(ct) : paths.Backups;
        if (!IsWithin(SafePath.Join(paths.Root, $"trash/content/{plan.Profile:N}"), plan.Target) ||
            backup && (plan.Backup is null || plan.Kind != ProfileContentKind.Worlds ||
                Path.GetFileName(plan.Backup) != plan.Entry.FileName || Path.GetDirectoryName(plan.Backup) is not { } container ||
                !string.Equals(Path.GetDirectoryName(container), SafePath.Join(backupDirectory, "worlds"), StringComparison.OrdinalIgnoreCase)))
            throw new IOException("ContentCopyDestination");
        using var lease = ValidateAction(plan);
        if (backup) await CopyItemAsync(plan.Entry, plan.Source, plan.Backup!, ct);
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(plan.Target)!);
        Move(plan.Entry, plan.Source, plan.Target);
    }

    private IDisposable ValidateAction(ContentFileAction plan)
    {
        var usage = new ProfileUsage(paths);
        var lease = usage.Acquire(plan.Profile);
        try
        {
            usage.CheckWorldLocks(plan.Profile);
            var source = SafePath.Join(Folder(plan.Profile, plan.Kind), plan.Entry.FileName);
            if (!string.Equals(source, plan.Source, StringComparison.OrdinalIgnoreCase) || File.GetLastWriteTimeUtc(source) != plan.SourceChangedUtc)
                throw new IOException("ContentChanged");
            EnsureExists(plan.Entry, source);
            EnsureFree(plan.Target);
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    internal static async Task CopyItemAsync(ProfileContentEntry item, string source, string target, CancellationToken ct)
    {
        EnsureFree(target);
        var parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        var pending = SafePath.Join(parent, ".pteno-copy-" + Guid.NewGuid().ToString("N") + ".part");
        var created = new List<(string Path, bool Directory)>();
        async Task CopyTree(string from, string to, bool directory)
        {
            ct.ThrowIfCancellationRequested();
            SafePath.RejectLinks(from);
            SafePath.RejectLinks(to);
            if (directory)
            {
                if (created.Count > 50000) throw new IOException("ContentCopyLimit");
                Directory.CreateDirectory(to);
                created.Add((to, true));
                foreach (var child in Directory.EnumerateFileSystemEntries(from))
                {
                    var name = Path.GetFileName(child);
                    if (name == "session.lock") continue;
                    await CopyTree(child, SafePath.Join(to, name), Directory.Exists(child));
                }
            }
            else
            {
                if (created.Count > 50000) throw new IOException("ContentCopyLimit");
                await using var input = File.OpenRead(from);
                await using var output = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                created.Add((to, false));
                await input.CopyToAsync(output, ct);
                await output.FlushAsync(ct);
                output.Flush(true);
            }
        }
        try
        {
            EnsureFree(pending);
            await CopyTree(source, pending, item.Directory);
            ct.ThrowIfCancellationRequested();
            Move(item, pending, target);
            created.Clear();
        }
        finally
        {
            foreach (var (path, directory) in created.AsEnumerable().Reverse())
            {
                SafePath.RejectLinks(path);
                if (directory) Directory.Delete(path); else File.Delete(path);
            }
        }
    }

    private static void Move(ProfileContentEntry item, string source, string target)
    {
        SafePath.RejectLinks(source);
        EnsureFree(target);
        if (item.Directory) Directory.Move(source, target); else File.Move(source, target, false);
    }

    private static void EnsureExists(ProfileContentEntry entry, string path)
    {
        SafePath.RejectLinks(path);
        if (!(entry.Directory ? Directory.Exists(path) : File.Exists(path))) throw new IOException("ContentMissing");
    }

    internal static void EnsureFree(string path)
    {
        SafePath.RejectLinks(path);
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("ContentCollision");
    }

    internal static bool IsWithin(string parent, string child)
    {
        var relative = Path.GetRelativePath(parent, child);
        return !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
