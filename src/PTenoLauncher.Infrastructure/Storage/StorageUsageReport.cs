using System.Text.Json;
using System.IO.Compression;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Minecraft;

namespace PTenoLauncher.Infrastructure.Storage;

public sealed record ProfileUsageSize(GameProfile Profile, long ProfileBytes, long WorldBytes);
public sealed record CacheFileUsage(string RelativePath, long Bytes, IReadOnlyList<GameProfile> Profiles);
public sealed record StorageUsageSnapshot(IReadOnlyList<ProfileUsageSize> Profiles, long BackupBytes, long CacheBytes,
    IReadOnlyList<CacheFileUsage> CacheReferences, IReadOnlyList<string> UnreadablePaths)
{
    public long TotalBytes => Profiles.Sum(profile => profile.ProfileBytes) + BackupBytes + CacheBytes;
}

public sealed class StorageUsageReport(LauncherPaths paths, ILauncherStore store, BackupManager backups)
{
    public Task<StorageUsageSnapshot> ReadAsync(CancellationToken ct = default) => Task.Run(async () =>
    {
        var profiles = await store.GetProfilesAsync(ct);
        var problems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sizes = new List<ProfileUsageSize>();
        var cache = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var references = new Dictionary<string, (InstalledFile File, List<GameProfile> Profiles)>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var root = paths.Profile(profile.Id);
                var worlds = SafePath.Join(root, "game") + Path.DirectorySeparatorChar + "saves";
                long worldBytes = 0;
                var bytes = Measure(root, problems, ct, file =>
                {
                    if (file.FullName.StartsWith(worlds + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        worldBytes += file.Length;
                });
                sizes.Add(new(profile, bytes, worldBytes));

                var receiptPath = paths.Receipt(profile.Id);
                try
                {
                    var receipt = await AtomicFile.ReadJsonAsync<InstallationReceipt>(receiptPath, ct);
                    if (receipt is null) continue;
                    ReceiptValidator.Validate(paths, receipt);
                    foreach (var file in receipt.Files)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!references.TryGetValue(file.RelativePath, out var reference))
                            references.Add(file.RelativePath, (file, [profile]));
                        else if (file.Size == reference.File.Size && file.Algorithm == reference.File.Algorithm &&
                            file.Hash.Equals(reference.File.Hash, StringComparison.OrdinalIgnoreCase)) reference.Profiles.Add(profile);
                        else conflicts.Add(file.RelativePath);
                    }
                }
                catch (Exception error) when (error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or ArgumentException)
                { problems.Add(receiptPath); }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            { problems.Add(profile.Name + " (" + profile.Id + ")"); }
        }

        var cacheBytes = Measure(paths.Cache, problems, ct, file =>
            cache[Path.GetRelativePath(paths.Cache, file.FullName).Replace('\\', '/')] = file.Length);
        var backupBytes = Measure(paths.Backups, problems, ct);
        string? alternate = null;
        try { alternate = await backups.GetDirectoryAsync(ct); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { problems.Add("profiles.backup-directory"); }
        if (alternate is not null && !alternate.Equals(paths.Backups, StringComparison.OrdinalIgnoreCase))
        {
            // The custom folder may contain unrelated files. Count only the launcher's backup name layout.
            var profileIds = profiles.Select(profile => profile.Id.ToString("N")).ToHashSet(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var file in new DirectoryInfo(alternate).EnumerateFiles("*.zip"))
                {
                    ct.ThrowIfCancellationRequested();
                    var parts = Path.GetFileNameWithoutExtension(file.Name).Split('-');
                    if ((file.Attributes & FileAttributes.ReparsePoint) != 0) { problems.Add(file.FullName); continue; }
                    if (parts.Length == 4 && profileIds.Contains(parts[0]) && parts[1].Length == 8 && parts[2].Length == 6 &&
                        parts[1].All(char.IsAsciiDigit) && parts[2].All(char.IsAsciiDigit) && Guid.TryParseExact(parts[3], "N", out _) ||
                        IsProfileBackup(file, profileIds))
                    {
                        backupBytes += file.Length;
                    }
                }
                var worlds = SafePath.Join(alternate, "worlds");
                if (Directory.Exists(worlds))
                {
                    foreach (var file in new DirectoryInfo(worlds).EnumerateFiles("*.zip"))
                    {
                        ct.ThrowIfCancellationRequested();
                        if ((file.Attributes & FileAttributes.ReparsePoint) == 0) backupBytes += file.Length;
                        else problems.Add(file.FullName);
                    }
                    foreach (var directory in new DirectoryInfo(worlds).EnumerateDirectories())
                    {
                        if (Guid.TryParseExact(directory.Name, "N", out _)) continue;
                        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) { problems.Add(directory.FullName); continue; }
                        foreach (var world in directory.EnumerateDirectories())
                        {
                            ct.ThrowIfCancellationRequested();
                            if ((world.Attributes & FileAttributes.ReparsePoint) != 0) { problems.Add(world.FullName); continue; }
                            if (File.Exists(SafePath.Join(world.FullName, "level.dat"))) backupBytes += Measure(world.FullName, problems, ct);
                        }
                    }
                }
                foreach (var id in profileIds)
                {
                    var worldBackups = Path.Combine(alternate, "worlds", id);
                    long worldArchiveBytes = 0;
                    Measure(worldBackups, problems, ct, file =>
                    {
                        if (file.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)) worldArchiveBytes += file.Length;
                    });
                    backupBytes += worldArchiveBytes;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { problems.Add(alternate); }
        }

        var linked = new List<CacheFileUsage>();
        foreach (var (relative, reference) in references)
        {
            ct.ThrowIfCancellationRequested();
            if (conflicts.Contains(relative)) problems.Add(Path.Combine(paths.Cache, relative));
            else if (cache.TryGetValue(relative, out var length) && length == reference.File.Size)
                linked.Add(new(relative, length, reference.Profiles));
        }
        return new StorageUsageSnapshot(sizes, backupBytes, cacheBytes,
            linked.OrderByDescending(file => file.Profiles.Count).ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            problems.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }, ct);

    private static long Measure(string root, HashSet<string> problems, CancellationToken ct, Action<FileInfo>? visit = null)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new(root));
        long bytes = 0;
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                SafePath.RejectLinks(directory.FullName);
                if (!directory.Exists) continue;
                foreach (var entry in directory.EnumerateFileSystemInfos())
                {
                    ct.ThrowIfCancellationRequested();
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) problems.Add(entry.FullName);
                    else if (entry is DirectoryInfo child) pending.Push(child);
                    else if (entry is FileInfo file)
                    {
                        bytes += file.Length;
                        visit?.Invoke(file);
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { problems.Add(directory.FullName); }
        }
        return bytes;
    }

    private static bool IsProfileBackup(FileInfo file, HashSet<string> profiles)
    {
        try
        {
            SafePath.RejectLinks(file.FullName);
            using var zip = ZipFile.OpenRead(file.FullName);
            if (zip.GetEntry("backup.json") is not { Length: > 0 and <= 1024 * 1024 } metadata) return false;
            using var stream = metadata.Open();
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("formatVersion", out var schema) && schema.TryGetInt32(out var version) && version == 1 &&
                document.RootElement.TryGetProperty("profile", out var profile) && profile.TryGetProperty("id", out var id) &&
                id.TryGetGuid(out var guid) && profiles.Contains(guid.ToString("N"));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or InvalidOperationException) { return false; }
    }
}
