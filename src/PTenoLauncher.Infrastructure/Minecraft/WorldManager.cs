using System.IO.Compression;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Modpacks;
using PTenoLauncher.Infrastructure.Security;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public enum WorldAction { Rename, Copy, ImportZip, RestoreFolder, ExportZip, BackupZip }

public sealed record WorldPlan(WorldAction Action, GameProfile Profile, string? WorldName, GameProfile? DestinationProfile,
    string Argument, string Source, string Destination, WorldVersion Version, long SourceSize, DateTime SourceModifiedUtc);

public sealed class WorldManager(LauncherPaths paths, ILauncherStore store, ProfileContent content, BackupManager backups)
{
    private const long ArchiveLimit = 1024L * 1024 * 1024;

    public async Task<WorldPlan> PreviewAsync(WorldAction action, GameProfile profile, string? name = null,
        GameProfile? destination = null, string? argument = null, CancellationToken ct = default)
    {
        var profiles = await store.GetProfilesAsync(ct);
        if (!profiles.Contains(profile) || destination is not null && !profiles.Contains(destination))
            throw new IOException("ContentChanged");
        var local = action is not (WorldAction.ImportZip or WorldAction.RestoreFolder);
        var saves = content.Folder(profile.Id, ProfileContentKind.Worlds);
        if (!Directory.Exists(saves)) throw new IOException("ContentMissing");
        var source = local ? SafePath.Join(saves, Leaf(name)) : Path.GetFullPath(argument ?? "");
        SafePath.RejectLinks(source);
        if (!local && ProfileContent.IsWithin(paths.Root, source) && !ProfileContent.IsWithin(paths.Backups, source))
            throw new IOException("ContentCopyDestination");
        var archiveSource = action == WorldAction.ImportZip;
        var identity = new FileInfo(archiveSource ? source : SafePath.Join(source, "level.dat"));
        if (!identity.Exists) throw new IOException("WorldArchiveInvalid");
        WorldVersion version;
        if (archiveSource)
        {
            if (identity.Length is <= 0 or > ArchiveLimit || !source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("WorldArchiveInvalid");
            using var zip = ZipFile.OpenRead(source);
            var prefix = CheckArchive(zip, saves);
            using var level = zip.GetEntry(prefix + "level.dat")!.Open();
            version = WorldVersionReader.Read(level);
            name = prefix.Length == 0 ? Path.GetFileNameWithoutExtension(source) : prefix.TrimEnd('/');
        }
        else
        {
            using var level = identity.OpenRead();
            version = WorldVersionReader.Read(level);
            if (!local) name = Path.GetFileName(source);
        }
        var target = action switch
        {
            WorldAction.Rename when destination is null => SafePath.Join(saves, Leaf(argument)),
            WorldAction.Copy when destination is not null && destination.Id != profile.Id =>
                SafePath.Join(content.Folder(destination.Id, ProfileContentKind.Worlds), Leaf(name)),
            WorldAction.ImportZip or WorldAction.RestoreFolder when destination is null => SafePath.Join(saves, Leaf(name)),
            WorldAction.ExportZip when destination is null => ExportDestination(argument),
            WorldAction.BackupZip when destination is null => BackupPath(await backups.GetDirectoryAsync(ct), profile, Leaf(name), argument),
            _ => throw new IOException("ContentCopyDestination")
        };
        ProfileContent.EnsureFree(target);
        return new(action, profile, name, destination, argument ?? (action == WorldAction.BackupZip ? Path.GetFileName(target) : ""),
            source, target, version, identity.Length, identity.LastWriteTimeUtc);
    }

    private static string BackupPath(string root, GameProfile profile, string world, string? argument)
    {
        var folder = SafePath.Join(root, "worlds");
        if (argument is not null) return SafePath.Join(folder, Leaf(argument));
        var stem = ReadableName.Stem(profile.Name, "Profile", 64) + " — " + ReadableName.Stem(world, "World", 64) + $" — {DateTime.UtcNow:yyyy-MM-dd}";
        return ReadableName.Free(folder, stem, ".zip");
    }

    public async Task ApplyAsync(WorldPlan plan, CancellationToken ct = default)
    {
        var usage = new ProfileUsage(paths);
        var ids = new[] { plan.Profile.Id, plan.DestinationProfile?.Id ?? plan.Profile.Id }.Distinct().Order().ToArray();
        using var first = usage.Acquire(ids[0]);
        using var second = ids.Length == 2 ? usage.Acquire(ids[1]) : null;
        foreach (var id in ids) usage.CheckWorldLocks(id);
        if (await PreviewAsync(plan.Action, plan.Profile, plan.WorldName, plan.DestinationProfile, plan.Argument, ct) != plan)
            throw new IOException("ContentChanged");
        var lockPath = plan.Action is WorldAction.ImportZip or WorldAction.Rename ? null : SafePath.Join(plan.Source, "session.lock");
        using var worldLock = lockPath is not null && File.Exists(lockPath)
            ? new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.None) : null;
        ct.ThrowIfCancellationRequested();
        switch (plan.Action)
        {
            case WorldAction.Rename:
                // Windows cannot move the directory while a handle inside it is held, even with FileShare.Delete.
                usage.CheckWorldLocks(plan.Profile.Id);
                Directory.Move(plan.Source, plan.Destination);
                break;
            case WorldAction.Copy:
            case WorldAction.RestoreFolder:
                await ProfileContent.CopyItemAsync(new(plan.WorldName!, plan.WorldName!, true), plan.Source, plan.Destination, ct);
                break;
            case WorldAction.ImportZip:
                await ImportZipAsync(plan.Source, plan.Destination, ct);
                break;
            case WorldAction.ExportZip:
            case WorldAction.BackupZip:
                await ExportZipAsync(plan.Source, plan.Destination, ct);
                break;
        }
    }

    private static string Leaf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name.StartsWith(".pteno-", StringComparison.OrdinalIgnoreCase))
            throw new IOException("WorldNameInvalid");
        return name;
    }

    private string ExportDestination(string? path)
    {
        var target = Path.GetFullPath(path ?? "");
        if (ProfileContent.IsWithin(paths.Root, target) || !target.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new IOException("ContentCopyDestination");
        return SafePath.Join(Path.GetDirectoryName(target)!, Leaf(Path.GetFileName(target)));
    }

    private static string CheckArchive(ZipArchive zip, string destination)
    {
        SafeArchive.Validate(zip, destination, ArchiveLimit);
        MrpackReader.PathsRule(zip.Entries.Select(entry => (entry.FullName.TrimEnd('/'), entry.FullName.EndsWith('/'))),
            allowExcludedNames: true);
        var levels = zip.Entries.Where(entry => entry.FullName.Split('/').Length <= 2 && entry.Name == "level.dat").ToArray();
        if (levels.Length != 1) throw new InvalidDataException("WorldArchiveInvalid");
        var prefix = levels[0].FullName[..^"level.dat".Length];
        if (prefix.Length > 0 && zip.Entries.Any(entry => !entry.FullName.StartsWith(prefix, StringComparison.Ordinal)))
            throw new InvalidDataException("WorldArchiveInvalid");
        return prefix;
    }

    private static async Task ImportZipAsync(string source, string destination, CancellationToken ct)
    {
        var parent = Path.GetDirectoryName(destination)!;
        var stage = SafePath.Join(parent, ".pteno-world-" + Guid.NewGuid().ToString("N"));
        using var zip = ZipFile.OpenRead(source);
        var prefix = CheckArchive(zip, stage);
        ProfileContent.EnsureFree(stage);
        Directory.CreateDirectory(stage);
        try
        {
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.FullName.EndsWith('/') || entry.Name.Equals("session.lock", StringComparison.OrdinalIgnoreCase)) continue;
                var target = SafePath.Join(stage, entry.FullName[prefix.Length..]);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await ProfileContent.CopyArchiveEntryAsync(entry, output, ct);
            }
            ct.ThrowIfCancellationRequested();
            ProfileContent.EnsureFree(destination);
            Directory.Move(stage, destination);
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                CheckTree(stage);
                Directory.Delete(stage, true);
            }
        }
    }

    private static async Task ExportZipAsync(string source, string destination, CancellationToken ct)
    {
        var parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        var partial = SafePath.Join(parent, ".pteno-world-" + Guid.NewGuid().ToString("N") + ".part");
        var ownsPartial = false;
        try
        {
            await using (var stream = new FileStream(partial, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true))
            {
                ownsPartial = true;
                using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                long total = 0;
                var count = 0;
                foreach (var file in Files(source))
                {
                    ct.ThrowIfCancellationRequested();
                    if (Path.GetFileName(file).Equals("session.lock", StringComparison.OrdinalIgnoreCase)) continue;
                    var relative = Path.GetRelativePath(Path.GetDirectoryName(source)!, file).Replace('\\', '/');
                    _ = SafePath.Join(source, relative);
                    await using var input = File.OpenRead(file);
                    if (++count > 50000 || input.Length > 512L * 1024 * 1024 || (total += input.Length) > ArchiveLimit)
                        throw new InvalidDataException("ContentCopyLimit");
                    await using var output = zip.CreateEntry(relative, CompressionLevel.Fastest).Open();
                    await input.CopyToAsync(output, ct);
                }
            }
            using (var zip = ZipFile.OpenRead(partial))
            {
                _ = CheckArchive(zip, source);
                foreach (var entry in zip.Entries)
                    await ProfileContent.CopyArchiveEntryAsync(entry, Stream.Null, ct);
            }
            ct.ThrowIfCancellationRequested();
            ProfileContent.EnsureFree(destination);
            File.Move(partial, destination, false);
            ownsPartial = false;
        }
        finally
        {
            if (ownsPartial) { SafePath.RejectLinks(partial); File.Delete(partial); }
        }
    }

    private static IEnumerable<string> Files(string directory)
    {
        SafePath.RejectLinks(directory);
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            SafePath.RejectLinks(path);
            if (Directory.Exists(path))
            {
                foreach (var child in Files(path)) yield return child;
            }
            else yield return path;
        }
    }

    private static void CheckTree(string stage)
    {
        foreach (var _ in Files(stage)) { }
    }
}
