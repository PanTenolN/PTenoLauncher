using PTenoLauncher.Infrastructure.Security;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;
namespace PTenoLauncher.Tests;
public sealed class Uninstaller020Tests
{
    [Fact] public async Task CleanupRequiresConfirmationAndKeepsUnrelatedFiles()
    {
        using var ws = new TestWorkspace();
        var paths = new LauncherPaths(Path.Combine(ws.Paths.Root, "PTenoLauncher"));
        var store = new SqliteLauncherStore(paths); await store.InitializeAsync();
        var p = ws.Profile(); await store.SaveProfileAsync(p);
        var world = Path.Combine(paths.Game(p.Id), "saves", "synthetic"); await File.WriteAllTextAsync(world, "test world");
        var unrelated = Path.Combine(paths.Root, "my-unrelated-note.txt"); await File.WriteAllTextAsync(unrelated, "keep");
        var cleaner = new UserDataCleaner(paths);
        await Assert.ThrowsAsync<InvalidOperationException>(() => cleaner.CleanAsync("wrong"));
        Assert.True(File.Exists(world));
        await cleaner.CleanAsync("PTenoLauncher");
        Assert.False(Directory.Exists(paths.Game(p.Id))); Assert.False(File.Exists(paths.Database));
        Assert.Equal("keep", await File.ReadAllTextAsync(unrelated));
    }
    [Fact] public async Task CleanupRefusesUnmarkedDirectory()
    {
        using var ws = new TestWorkspace();
        var paths = new LauncherPaths(Path.Combine(ws.Paths.Root, "PTenoLauncher")); Directory.CreateDirectory(paths.Root);
        await Assert.ThrowsAsync<InvalidDataException>(() => new UserDataCleaner(paths).CleanAsync("PTenoLauncher"));
    }
    [Fact] public async Task LockedDatabasePreventsAnyDestructiveCleanup()
    {
        using var ws = new TestWorkspace();
        var paths = new LauncherPaths(Path.Combine(ws.Paths.Root, "PTenoLauncher"));
        var store = new SqliteLauncherStore(paths); await store.InitializeAsync();
        var profile = ws.Profile(); await store.SaveProfileAsync(profile);
        var world = Path.Combine(paths.Game(profile.Id), "saves", "synthetic-world");
        await File.WriteAllTextAsync(world, "preserve if database cleanup cannot proceed");
        using (var externalDatabaseReader = new FileStream(paths.Database, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            await Assert.ThrowsAsync<IOException>(() => new UserDataCleaner(paths).CleanAsync("PTenoLauncher"));
            Assert.True(File.Exists(world)); Assert.True(File.Exists(paths.Database));
        }
        await new UserDataCleaner(paths).CleanAsync("PTenoLauncher");
        Assert.False(File.Exists(world)); Assert.False(File.Exists(paths.Database));
    }
}