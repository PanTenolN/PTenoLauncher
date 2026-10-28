using Microsoft.Data.Sqlite;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class TestWorkspace : IDisposable
{
    public LauncherPaths Paths { get; } = new(Path.Combine(Path.GetTempPath(), "PTenoLauncher-tests", Guid.NewGuid().ToString("N")));
    public SqliteLauncherStore Store { get; }
    public TestWorkspace() { Paths.Initialize(); Store = new(Paths); }
    public GameProfile Profile(ModLoader loader = ModLoader.Vanilla) => new(Guid.NewGuid(), "Мій профіль", "1.21.1", loader, 4096, null, DateTimeOffset.UtcNow);
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(Paths.Root)) Directory.Delete(Paths.Root, true);
    }
}
public sealed class StorageTests
{
    [Fact] public async Task ProfilesAndSettingsSurviveRestartWithoutNetwork()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync();
        var p = ws.Profile(); await ws.Store.SaveProfileAsync(p);
        await ws.Store.SetSettingAsync("language", "uk");
        var reopened = new SqliteLauncherStore(ws.Paths); await reopened.InitializeAsync();
        Assert.Equal(p, Assert.Single(await reopened.GetProfilesAsync()));
        Assert.Equal("uk", await reopened.GetSettingAsync("language"));
        Assert.True(Directory.Exists(Path.Combine(ws.Paths.Game(p.Id), "saves")));
    }
    [Fact] public async Task RenameAndArchivePreserveWorldFiles()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync();
        var p = ws.Profile(); await ws.Store.SaveProfileAsync(p);
        var world = Path.Combine(ws.Paths.Game(p.Id), "saves", "world.dat");
        await File.WriteAllTextAsync(world, "precious data");
        await ws.Store.SaveProfileAsync(p with { Name = "Renamed", Archived = true });
        Assert.True(Assert.Single(await ws.Store.GetProfilesAsync()).Archived);
        Assert.Equal("precious data", await File.ReadAllTextAsync(world));
        Assert.Equal(2, (await ws.Store.GetHistoryAsync(p.Id)).Count);
    }
    [Fact] public async Task SecretsAreRejectedBySettingsStore()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => ws.Store.SetSettingAsync("access_token", "secret"));
        Assert.Null(await ws.Store.GetSettingAsync("access_token"));
    }
    [Fact] public async Task NewerDatabaseSchemaIsRejected()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync();
        using (var db = new SqliteConnection("Data Source=" + ws.Paths.Database))
        {
            db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA user_version=999"; cmd.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => ws.Store.InitializeAsync());
    }
    [Theory]
    [InlineData("../outside")][InlineData("/absolute")][InlineData("C:/windows")][InlineData("a/../../b")]
    [InlineData("a\\b")][InlineData("a//b")][InlineData("file:stream")][InlineData("CON.txt")]
    [InlineData("x/NUL")][InlineData("a. /b")][InlineData("a./b")][InlineData("./file")]
    public void UnsafePathsAreRejected(string relative)
    {
        using var ws = new TestWorkspace();
        Assert.Throws<InvalidDataException>(() => SafePath.Join(ws.Paths.Root, relative));
    }
    [Fact] public async Task CorruptJsonDoesNotBecomeDefaultSuccess()
    {
        using var ws = new TestWorkspace(); var file = Path.Combine(ws.Paths.Root, "test.json");
        await File.WriteAllTextAsync(file, "{broken");
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => AtomicFile.ReadJsonAsync<GameProfile>(file));
    }
    [Fact] public async Task CanceledAtomicWritePreservesExistingFile()
    {
        using var ws = new TestWorkspace(); var file = Path.Combine(ws.Paths.Root, "test.json");
        await File.WriteAllTextAsync(file, "original");
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AtomicFile.WriteAsync(file, new byte[100], cts.Token));
        Assert.Equal("original", await File.ReadAllTextAsync(file));
        Assert.Empty(Directory.EnumerateFiles(ws.Paths.Root, "*.tmp"));
    }
}