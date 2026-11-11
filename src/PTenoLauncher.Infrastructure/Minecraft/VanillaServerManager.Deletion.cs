using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed record ServerDeletionPlan(LocalVanillaServer Server, string Folder, IReadOnlyList<string> Worlds)
{
    public bool HasWorld => Worlds.Count > 0;
}

public sealed partial class VanillaServerManager
{
    public async Task<ServerDeletionPlan> PreviewDeletionAsync(LocalVanillaServer server, CancellationToken ct = default)
    {
        var folder = DirectoryFor(server.Id);
        using var lease = Acquire(folder);
        await CheckDeletionAsync(server, folder, ct);
        return new(server, folder, WorldFolders(server, folder));
    }

    public async Task<string?> DeleteAsync(ServerDeletionPlan plan, bool backupWorld, CancellationToken ct = default)
    {
        var folder = DirectoryFor(plan.Server.Id);
        if (!string.Equals(folder, plan.Folder, StringComparison.OrdinalIgnoreCase)) throw new IOException("ContentChanged");
        var staged = SafePath.Join(Root, ".deleting-" + plan.Server.Id.ToString("N"));
        string? backup = null;
        using (var lease = Acquire(folder))
        {
            await CheckDeletionAsync(plan.Server, folder, ct);
            if (!WorldFolders(plan.Server, folder).SequenceEqual(plan.Worlds, StringComparer.Ordinal) ||
                Directory.Exists(staged) || File.Exists(staged))
                throw new IOException("ContentChanged");
            if (backupWorld && plan.HasWorld)
                backup = await CopyServerWorldsAsync(plan, ct);
            ct.ThrowIfCancellationRequested();
            RejectServerLinks(folder, ct);
            lease.ReleaseFolderHandle();
            Directory.Move(folder, staged);
        }
        try
        {
            RejectServerLinks(staged, CancellationToken.None);
            Directory.Delete(staged, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new IOException("ServerDeleteIncomplete" + Environment.NewLine + staged, error);
        }
        return backup;
    }

    private static string[] WorldFolders(LocalVanillaServer server, string folder)
    {
        string[] names = server.HasPlugins ? ["world", "world_nether", "world_the_end"] : ["world"];
        return names.Where(name => Directory.Exists(SafePath.Join(folder, name))).ToArray();
    }

    private async Task<string> CopyServerWorldsAsync(ServerDeletionPlan plan, CancellationToken ct)
    {
        var stem = ReadableName.Stem(plan.Server.Name, "Server", 64) + $" - world - {DateTime.UtcNow:yyyy-MM-dd}";
        var backup = ReadableName.Free(paths.Backups, stem);
        var staged = SafePath.Join(paths.Backups, ".pteno-server-world-" + Guid.NewGuid().ToString("N") + ".part");
        if (Directory.Exists(staged) || File.Exists(staged)) throw new IOException("ContentChanged");
        Directory.CreateDirectory(staged);
        try
        {
            foreach (var name in plan.Worlds)
            {
                var world = SafePath.Join(plan.Folder, name);
                var lockPath = SafePath.Join(world, "session.lock");
                using var worldLock = File.Exists(lockPath) ? new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.None) : null;
                await ProfileContent.CopyItemAsync(new(name, name, true), world, SafePath.Join(staged, name), ct);
            }
            ct.ThrowIfCancellationRequested();
            RejectServerLinks(staged, ct);
            Directory.Move(staged, backup);
            return backup;
        }
        finally
        {
            if (Directory.Exists(staged))
            {
                RejectServerLinks(staged, CancellationToken.None);
                Directory.Delete(staged, recursive: true);
            }
        }
    }

    private static async Task CheckDeletionAsync(LocalVanillaServer server, string folder, CancellationToken ct)
    {
        await CheckIdentityAsync(server, folder, ct);
        await CheckProcessAsync(folder, ct);
        RejectServerLinks(folder, ct);
    }
}
