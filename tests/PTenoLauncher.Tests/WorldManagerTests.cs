using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class WorldManagerTests
{
    [Fact]
    public async Task CopyAndRenameRequireFreeDestinationsAndRefuseAnActiveWorld()
    {
        using var ws = new TestWorkspace();
        await ws.Store.InitializeAsync();
        var source = ws.Profile() with { Name = "Origin" };
        var target = ws.Profile() with { Name = "Destination", MinecraftVersion = "1.20.1" };
        await ws.Store.SaveProfileAsync(source);
        await ws.Store.SaveProfileAsync(target);
        var content = new ProfileContent(ws.Paths);
        var manager = new WorldManager(ws.Paths, ws.Store, content, new(ws.Paths, ws.Store));
        var world = Path.Combine(content.Folder(source.Id, ProfileContentKind.Worlds), "Example");
        Directory.CreateDirectory(world);
        var level = LevelDat();
        await File.WriteAllBytesAsync(Path.Combine(world, "level.dat"), level);
        var copy = await manager.PreviewAsync(WorldAction.Copy, source, "Example", target);
        Assert.Equal(new WorldVersion("1.21.1", 3955), copy.Version);
        Assert.Equal(target, copy.DestinationProfile);
        using (File.Create(Path.Combine(world, "session.lock")))
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ApplyAsync(copy));
        await manager.ApplyAsync(copy);
        Assert.Equal(level, await File.ReadAllBytesAsync(Path.Combine(copy.Destination, "level.dat")));
        Assert.False(File.Exists(Path.Combine(copy.Destination, "session.lock")));
        Assert.Equal(level, await File.ReadAllBytesAsync(Path.Combine(world, "level.dat")));
        var conflict = await Assert.ThrowsAsync<IOException>(() => manager.ApplyAsync(copy));
        Assert.Equal("ContentCollision", conflict.Message);
        var rename = await manager.PreviewAsync(WorldAction.Rename, source, "Example", argument: "Renamed");
        await manager.ApplyAsync(rename);
        Assert.False(Directory.Exists(world));
        Assert.Equal(level, await File.ReadAllBytesAsync(Path.Combine(rename.Destination, "level.dat")));
    }

    [Fact]
    public async Task NewBackupFolderPreservesOldBackupsAndRestoresToAnIndependentProfile()
    {
        using var ws = new TestWorkspace();
        using var external = new TestWorkspace();
        await ws.Store.InitializeAsync();
        var source = ws.Profile() with { Name = "Origin" };
        var target = ws.Profile() with { Name = "Destination" };
        await ws.Store.SaveProfileAsync(source);
        await ws.Store.SaveProfileAsync(target);
        var backups = new BackupManager(ws.Paths, ws.Store);
        var content = new ProfileContent(ws.Paths, backups);
        var manager = new WorldManager(ws.Paths, ws.Store, content, backups);
        var world = Path.Combine(content.Folder(source.Id, ProfileContentKind.Worlds), "Example");
        Directory.CreateDirectory(world);
        await File.WriteAllTextAsync(Path.Combine(world, "level.dat"), "unknown older format");
        var old = await backups.CreateAsync(source);
        await backups.SetDirectoryAsync(external.Paths.Root);
        var newFull = await backups.CreateAsync(source);
        Assert.Equal(ws.Paths.Backups, Path.GetDirectoryName(old));
        Assert.Equal(external.Paths.Root, Path.GetDirectoryName(newFull));
        Assert.True(File.Exists(old));
        Assert.NotEqual(source.Id, (await backups.RestoreAsNewAsync(newFull)).Id);
        Assert.NotNull(await backups.VerifyAsync(old));

        var backup = await manager.PreviewAsync(WorldAction.BackupZip, source, "Example");
        Assert.Null(backup.Version.Name);
        await manager.ApplyAsync(backup);
        var restore = await manager.PreviewAsync(WorldAction.ImportZip, target, argument: backup.Destination);
        await manager.ApplyAsync(restore);
        Assert.Equal("Example", Path.GetFileName(restore.Destination));
        Assert.Equal("unknown older format", await File.ReadAllTextAsync(Path.Combine(restore.Destination, "level.dat")));
        Assert.True(Directory.Exists(world));
        var remove = await content.PlanRemoveAsync(source.Id, ProfileContentKind.Worlds, new("Example", "Example", true));
        Assert.StartsWith(external.Paths.Root, remove.Backup!);
        await content.RemoveAsync(remove, backup: true);
        var loose = await manager.PreviewAsync(WorldAction.RestoreFolder, source, argument: remove.Backup);
        await manager.ApplyAsync(loose);
        Assert.True(Directory.Exists(world));
        Assert.True(Directory.Exists(remove.Backup));
    }

    [Fact]
    public async Task UnsafeOrAmbiguousZipLeavesTheProfileUntouched()
    {
        using var ws = new TestWorkspace();
        using var external = new TestWorkspace();
        await ws.Store.InitializeAsync();
        var profile = ws.Profile();
        await ws.Store.SaveProfileAsync(profile);
        var content = new ProfileContent(ws.Paths);
        var manager = new WorldManager(ws.Paths, ws.Store, content, new(ws.Paths, ws.Store));
        var archive = Path.Combine(external.Paths.Root, "world.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            zip.CreateEntry("Example/level.dat");
            zip.CreateEntry("../escape.dat");
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.PreviewAsync(WorldAction.ImportZip, profile, argument: archive));
        Assert.Empty(Directory.EnumerateFileSystemEntries(content.Folder(profile.Id, ProfileContentKind.Worlds)));
    }

    private static byte[] LevelDat()
    {
        using var bytes = new MemoryStream();
        using (var gzip = new GZipStream(bytes, CompressionMode.Compress, leaveOpen: true))
        using (var writer = new BinaryWriter(gzip, Encoding.UTF8))
        {
            Tag(10, "");
            Tag(10, "Data");
            Tag(3, "DataVersion");
            writer.Write(BinaryPrimitives.ReverseEndianness(3955));
            Tag(10, "Version");
            Tag(8, "Name");
            String("1.21.1");
            writer.Write(new byte[3]);

            void Tag(byte kind, string name) { writer.Write(kind); String(name); }
            void String(string value)
            {
                var utf8 = Encoding.UTF8.GetBytes(value);
                writer.Write(BinaryPrimitives.ReverseEndianness((ushort)utf8.Length));
                writer.Write(utf8);
            }
        }
        return bytes.ToArray();
    }
}
