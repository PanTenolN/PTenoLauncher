using Microsoft.Data.Sqlite;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.Security;
public sealed class UserDataCleaner(LauncherPaths paths)
{
    public const string MarkerName = "pteno-managed-data.marker";
    public const string MarkerText = "PTenoLauncher.ManagedData.v1";
    private static readonly string[] OwnedDirectories = ["instances","cache","runtimes","backups","secrets","logs","locks","trash","exports","translations"];
    public async Task CleanAsync(string confirmation, CancellationToken ct = default)
    {
        var leaf = Path.GetFileName(paths.Root.TrimEnd(Path.DirectorySeparatorChar));
        if (confirmation != "PTenoLauncher" || !(leaf.Equals("PTenoLauncher", StringComparison.OrdinalIgnoreCase) || leaf.Equals("portable-data", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Explicit application-name confirmation and an application data directory are required.");
        var marker = SafePath.Join(paths.Root, MarkerName);
        if (!File.Exists(marker) || await File.ReadAllTextAsync(marker, ct) != MarkerText) throw new InvalidDataException("Data root ownership could not be verified.");
        SafePath.RejectLinks(paths.Root);
        var targets = OwnedDirectories.Select(n => SafePath.Join(paths.Root, n)).ToArray();
        void Validate(string path)
        {
            SafePath.RejectLinks(path);
            if (Directory.Exists(path)) foreach (var item in Directory.EnumerateFileSystemEntries(path)) Validate(item);
        }
        foreach (var target in targets) Validate(target);
        if (File.Exists(paths.Database))
        {
            var store = new SqliteLauncherStore(paths); await store.InitializeAsync(ct);
            foreach (var profile in await store.GetProfilesAsync(ct))
            {
                using var lease = new ProfileUsage(paths).Acquire(profile.Id);
                new ProfileUsage(paths).CheckWorldLocks(profile.Id);
            }
        }
        ct.ThrowIfCancellationRequested(); SqliteConnection.ClearAllPools();
        using (var databaseLease = File.Exists(paths.Database)
            ? new FileStream(paths.Database, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete) : null)
        {
        foreach (var directory in targets) if (Directory.Exists(directory)) Directory.Delete(directory, true);
        foreach (var file in new[] { "settings.db", "settings.db-wal", "settings.db-shm", "settings.db-journal", MarkerName })
        {
            var target = SafePath.Join(paths.Root, file); if (File.Exists(target)) File.Delete(target);
        }
        }
        if (!Directory.EnumerateFileSystemEntries(paths.Root).Any()) Directory.Delete(paths.Root, false);
    }
}