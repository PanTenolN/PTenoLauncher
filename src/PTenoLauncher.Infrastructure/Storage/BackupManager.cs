using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Security;

namespace PTenoLauncher.Infrastructure.Storage;

public sealed record BackupManifest(int FormatVersion, GameProfile Profile, IReadOnlyList<InstalledFile> Files);

public sealed class BackupManager(LauncherPaths paths, ILauncherStore store)
{
    private const string DirectorySetting = "profiles.backup-directory";

    public async Task<string> GetDirectoryAsync(CancellationToken ct = default) =>
        ResolveDirectory(await store.GetSettingAsync(DirectorySetting, ct));

    public Task SetDirectoryAsync(string? directory, CancellationToken ct = default) =>
        store.SetSettingAsync(DirectorySetting, directory is null ? "" : ResolveDirectory(directory), ct);

    private string ResolveDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory)) directory = paths.Backups;
        if (!Path.IsPathFullyQualified(directory)) throw new IOException("BackupDirectoryInvalid");
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        SafePath.RejectLinks(directory);
        var relative = Path.GetRelativePath(paths.Root, directory);
        if (!directory.Equals(paths.Backups, StringComparison.OrdinalIgnoreCase) && !Path.IsPathRooted(relative) &&
            relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("BackupDirectoryInvalid");
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("BackupDirectoryMissing");
        return directory;
    }

    public async Task<string> CreateAsync(GameProfile profile, CancellationToken ct = default, string label = "Backup")
    {
        var usage = new ProfileUsage(paths);
        using var lease = usage.Acquire(profile.Id);
        usage.CheckWorldLocks(profile.Id);
        var source = paths.Game(profile.Id);
        var directory = await GetDirectoryAsync(ct);
        var stem = ReadableName.Stem(profile.Name, "Profile", 64) + " — " + ReadableName.Stem(label, "Backup", 40) + $" — {DateTime.UtcNow:yyyy-MM-dd}";
        var partial = SafePath.Join(directory, ".pteno-backup-" + Guid.NewGuid().ToString("N") + ".part");
        var ownsPartial = false;
        var locks = new List<FileStream>();
        try
        {
            // Refuse active worlds rather than copying a running world's changing files.
            foreach (var session in EnumerateSafeFiles(Path.Combine(source, "saves")).Where(f => Path.GetFileName(f) == "session.lock"))
            {
                SafePath.RejectLinks(session);
                locks.Add(new(session, FileMode.Open, FileAccess.Read, FileShare.None));
            }
            var entries = new List<InstalledFile>();
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                ownsPartial = true;
                foreach (var file in EnumerateSafeFiles(source))
                {
                    ct.ThrowIfCancellationRequested();
                    if (Path.GetFileName(file) == "session.lock") continue;
                    var relative = Path.GetRelativePath(source, file).Replace('\\', '/');
                    var entry = archive.CreateEntry("game/" + relative, CompressionLevel.Fastest);
                    await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                    await using var destination = entry.Open();
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[81920]; int n; long size = 0;
                    while ((n = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        hash.AppendData(buffer, 0, n); size += n;
                        await destination.WriteAsync(buffer.AsMemory(0, n), ct);
                    }
                    entries.Add(new(relative, Convert.ToHexString(hash.GetHashAndReset()), "SHA256", size));
                }
                var metadata = archive.CreateEntry("backup.json");
                await using var json = metadata.Open();
                await JsonSerializer.SerializeAsync(json, new BackupManifest(1, profile, entries), AtomicFile.JsonOptions, ct);
            }
            await VerifyAsync(partial, ct);
            ct.ThrowIfCancellationRequested();
            string target;
            for (var attempt = 0; ; attempt++)
            {
                target = ReadableName.Free(directory, stem, ".zip");
                try { File.Move(partial, target, false); break; }
                catch (IOException) when (attempt < 7 && (File.Exists(target) || Directory.Exists(target))) { }
            }
            ownsPartial = false;
            if (directory.Equals(paths.Backups, StringComparison.OrdinalIgnoreCase))
                await store.RegisterArtifactAsync(new(profile.Id, Path.GetRelativePath(paths.Root, target).Replace('\\', '/'), "backup"), ct);
            await store.AddHistoryAsync(profile.Id, "backup.created", Path.GetFileName(target), ct);
            return target;
        }
        finally
        {
            foreach (var handle in locks) handle.Dispose();
            if (ownsPartial) { SafePath.RejectLinks(partial); File.Delete(partial); }
        }
    }
    private static IEnumerable<string> EnumerateSafeFiles(string directory)
    {
        SafePath.RejectLinks(directory);
        foreach (var item in Directory.EnumerateFileSystemEntries(directory))
        {
            SafePath.RejectLinks(item);
            if (Directory.Exists(item)) foreach (var file in EnumerateSafeFiles(item)) yield return file;
            else yield return item;
        }
    }
    public async Task<BackupManifest> VerifyAsync(string file, CancellationToken ct = default)
    {
        SafePath.RejectLinks(file);
        using var archive = ZipFile.OpenRead(file);
        SafeArchive.Validate(archive, paths.Root, 4L * 1024 * 1024 * 1024);
        var metadata = archive.GetEntry("backup.json") ?? throw new InvalidDataException("Немає backup.json.");
        if (metadata.Length > 16 * 1024 * 1024) throw new InvalidDataException("Маніфест backup завеликий.");
        await using var input = metadata.Open();
        var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(input, AtomicFile.JsonOptions, ct) ?? throw new InvalidDataException("Некоректний backup.");
        if (manifest.FormatVersion != 1) throw new InvalidDataException("Непідтримуваний формат backup.");
        ProfileRules.Validate(manifest.Profile);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "backup.json" };
        foreach (var item in manifest.Files)
        {
            _ = SafePath.Join(paths.Root, item.RelativePath);
            if (item.Algorithm != "SHA256" || !expected.Add("game/" + item.RelativePath)) throw new InvalidDataException("Некоректний запис backup.");
            var entry = archive.GetEntry("game/" + item.RelativePath) ?? throw new InvalidDataException("Backup неповний.");
            if (entry.Length != item.Size) throw new InvalidDataException("Розмір backup не відповідає маніфесту.");
            await using var stream = entry.Open();
            if (!Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).Equals(item.Hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Пошкоджено backup.");
        }
        if (archive.Entries.Any(e => !expected.Contains(e.FullName))) throw new InvalidDataException("Неочікувані файли у backup.");
        return manifest;
    }
    public async Task<GameProfile> RestoreAsNewAsync(string file, CancellationToken ct = default)
    {
        var manifest = await VerifyAsync(file, ct);
        var profile = manifest.Profile with { Id = Guid.NewGuid(), Name = manifest.Profile.Name[..Math.Min(65, manifest.Profile.Name.Length)] + " (відновлено)", CreatedAt = DateTimeOffset.UtcNow, Archived = false };
        var stage = SafePath.Join(paths.Root, "restore-" + Guid.NewGuid().ToString("N"));
        try
        {
            await SafeArchive.ExtractAsync(file, stage, ct, maxExpanded: 4L * 1024 * 1024 * 1024);
            foreach (var item in manifest.Files)
                if (!await PTenoLauncher.Infrastructure.Network.VerifiedDownloader.VerifyAsync(SafePath.Join(stage, "game/" + item.RelativePath), item.Hash, item.Algorithm, item.Size, ct))
                    throw new InvalidDataException("Розпакований backup не пройшов перевірку.");
            var stagedProfile = SafePath.Join(stage, ".pteno-profile");
            Directory.CreateDirectory(stagedProfile);
            if (Directory.Exists(Path.Combine(stage, "game"))) Directory.Move(Path.Combine(stage, "game"), Path.Combine(stagedProfile, "game"));
            paths.PlaceProfileDirectory(profile, stagedProfile);
            await store.SaveProfileAsync(profile, ct);
            await store.AddHistoryAsync(profile.Id, "backup.restored", Path.GetFileName(file), ct);
            // No installation receipt is restored; shared game files must be installed/verified again.
            return profile;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
}
