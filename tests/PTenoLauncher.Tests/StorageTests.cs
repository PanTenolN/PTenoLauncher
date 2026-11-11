using Microsoft.Data.Sqlite;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class TestWorkspace : IDisposable
{
    private readonly PTenoLauncher.Testing.OwnedTestRoot owned = new("fixture");
    public LauncherPaths Paths { get; }
    public SqliteLauncherStore Store { get; }
    public TestWorkspace() { Paths = new(owned.Root); Paths.Initialize(); Store = new(Paths); }
    public GameProfile Profile(ModLoader loader = ModLoader.Vanilla) => new(Guid.NewGuid(), "Мій профіль", "1.21.1", loader, 4096, null, DateTimeOffset.UtcNow);
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        owned.Dispose();
    }
}
public sealed class StorageTests
{
    [AvaloniaFact]
    public async Task ProfileNameReviewAppliesOnlyAConfirmedVerifiedChoice()
    {
        using var ws = new TestWorkspace();
        await ws.Store.InitializeAsync();
        var profile = ws.Profile(ModLoader.Fabric) with { Name = "T1" };
        await ws.Store.SaveProfileAsync(profile);
        var mods = SafePath.Join(ws.Paths.Game(profile.Id), "mods");
        var readable = SafePath.Join(mods, "Chosen.jar");
        var bytes = ModUpdateTests.Fixture.Jar("chosen", "1.0.0");
        var old = SafePath.Join(mods, Convert.ToHexStringLower(System.Security.Cryptography.SHA512.HashData(bytes)) + "-Chosen.jar");
        var unknown = SafePath.Join(mods, new string('f', 64) + "-Unknown.jar");
        await File.WriteAllBytesAsync(old, bytes);
        await File.WriteAllBytesAsync(unknown, ModUpdateTests.Fixture.Jar("other", "1.0.0"));
        using var services = new LauncherServices(ws.Paths);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.OpenProfileWorkspace(profile.Id);
        vm.ChooseWorkspaceSection(ProfileSection.Settings);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            window.FindControl<Button>("OrganizeProfileName")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var attempt = 0; attempt < 300 && window.OwnedWindows.Count == 0; attempt++) await Task.Delay(10);
            var dialog = Assert.Single(window.OwnedWindows);
            var list = dialog.GetVisualDescendants().OfType<ListBox>().Single(control => control.Name == "NameReviewRows");
            var confirm = dialog.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "ApplyProfileName");
            Assert.Equal(2, list.ItemCount);
            var verified = StringComparer.OrdinalIgnoreCase.Compare(old, unknown) < 0 ? 0 : 1;
            list.SelectedIndex = 1 - verified;
            Assert.False(confirm.IsEnabled);
            list.SelectedIndex = verified;
            Assert.True(confirm.IsEnabled);
            Assert.True(File.Exists(old));
            Assert.False(File.Exists(readable));
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var attempt = 0; attempt < 300 && (!File.Exists(readable) || vm.IsBusy || window.OwnedWindows.Count == 0); attempt++) await Task.Delay(10);
            Assert.False(File.Exists(old));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(readable));
            Assert.True(File.Exists(unknown));
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
            window.Close();
        }
    }

    [Fact]
    public async Task StagingCreatesItsParentAndFailedProfilePlacementRollsBackTheDatabase()
    {
        using var ws = new TestWorkspace();
        var staged = new LauncherPaths(SafePath.Join(ws.Paths.Root, "isolated-staging"));
        var profile = ws.Profile() with { Name = "T1" };
        Assert.False(Directory.Exists(Path.Combine(staged.Root, "instances")));
        staged.CreateProfile(profile);
        Assert.Equal(Path.Combine(staged.Root, "instances", "T1"), new LauncherPaths(staged.Root).Profile(profile.Id));

        await ws.Store.InitializeAsync();
        using (ws.Paths.AcquireProfileNames())
            await Assert.ThrowsAsync<IOException>(() => ws.Store.SaveProfileAsync(profile));
        Assert.Empty(await ws.Store.GetProfilesAsync());
        Assert.Empty(Directory.EnumerateFileSystemEntries(SafePath.Join(ws.Paths.Root, "instances")));
        Assert.Empty(Directory.EnumerateDirectories(ws.Paths.Root, ".pteno-create-*"));
        await ws.Store.SaveProfileAsync(profile);
        Assert.Equal(profile, Assert.Single(await ws.Store.GetProfilesAsync()));
    }

    [Fact]
    public async Task TechnicalModNameCanBePreviewedRenamedReopenedAndUndone()
    {
        using var ws = new TestWorkspace();
        await ws.Store.InitializeAsync();
        var profile = ws.Profile(ModLoader.Fabric) with { Name = "T1" };
        await ws.Store.SaveProfileAsync(profile);
        var game = ws.Paths.Game(profile.Id);
        var jar = SafePath.Join(ws.Paths.Root, "source.jar");
        SecurityAndModsTests.CreateJar(jar, "demo");
        var bytes = await File.ReadAllBytesAsync(jar);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        const string readable = "mods/jei-1.21.1-fabric-19.64.0.455.jar";
        var old = "mods/" + hash + "-jei-1.21.1-fabric-19.64.0.455.jar";
        await File.WriteAllBytesAsync(SafePath.Join(game, old), bytes);
        var baseline = new ModInventoryStore(ws.Paths);
        await baseline.RecordCommittedAsync(profile.Id, [new(old, hash, bytes.Length, "local")]);
        var pinFile = SafePath.Join(ws.Paths.Profile(profile.Id), "mod-pins.json");
        var sha512 = Convert.ToHexStringLower(System.Security.Cryptography.SHA512.HashData(bytes));
        await AtomicFile.WriteJsonAsync(pinFile, new ModPinSet(profile.Id, [new(new(old, hash, sha512, bytes.Length), null)]));
        var renamer = new ProfileFileRenamer(ws.Paths, ws.Store, new(ws.Paths, ws.Store), new(ws.Paths));
        using (new ProfileUsage(ws.Paths).Acquire(profile.Id))
            await Assert.ThrowsAsync<InvalidOperationException>(() => renamer.PreviewAsync(profile.Id, old));
        var plan = await renamer.PreviewAsync(profile.Id, old);
        Assert.Equal(readable, plan.To);
        Assert.True(File.Exists(plan.Source));
        Assert.False(File.Exists(plan.Target));
        renamer.FaultAfterMove = () => throw new IOException("fixture failure");
        await Assert.ThrowsAsync<IOException>(() => renamer.ApplyAsync(plan));
        Assert.True(File.Exists(plan.Source));
        Assert.Equal(old, Assert.Single((await baseline.ReadAsync(profile.Id))!.Files).RelativePath);
        Assert.Equal(old, Assert.Single((await AtomicFile.ReadJsonAsync<ModPinSet>(pinFile))!.Pins).File.Path);
        FileStream? held = null;
        renamer.FaultAfterMove = () =>
        {
            held = new FileStream(plan.Target, FileMode.Open, FileAccess.Read, FileShare.None);
            throw new IOException("interrupted fixture");
        };
        try { await Assert.ThrowsAsync<IOException>(() => renamer.ApplyAsync(plan)); }
        finally { held?.Dispose(); }
        Assert.True(File.Exists(ProfileFileRenamer.PendingPath(ws.Paths, profile.Id)));
        await renamer.RecoverAsync(profile.Id);
        Assert.True(File.Exists(plan.Source));
        Assert.False(File.Exists(plan.Target));
        renamer.FaultAfterMove = null;
        await renamer.ApplyAsync(plan);
        var reopened = new LauncherPaths(ws.Paths.Root);
        Assert.Equal("T1", Path.GetFileName(reopened.Profile(profile.Id)));
        Assert.False(File.Exists(plan.Source));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(SafePath.Join(reopened.Game(profile.Id), readable)));
        Assert.Equal(readable, Assert.Single((await new ModInventoryStore(reopened).ReadAsync(profile.Id))!.Files).RelativePath);
        Assert.Equal(readable, Assert.Single((await AtomicFile.ReadJsonAsync<ModPinSet>(pinFile))!.Pins).File.Path);
        Console.WriteLine($"Physical rename paths: {plan.Source} → {plan.Target}");
        await renamer.ApplyAsync(await renamer.PreviewAsync(profile.Id, null));
        Assert.Equal(old, Assert.Single((await baseline.ReadAsync(profile.Id))!.Files).RelativePath);
        Assert.True(File.Exists(plan.Source));
        Assert.False(File.Exists(plan.Target));
    }

    [Fact]
    public async Task LocalJarNamesPreserveUnverifiedPrefixesAndUseNumberedCollisions()
    {
        using var ws = new TestWorkspace();
        await ws.Store.InitializeAsync();
        var profile = ws.Profile(ModLoader.Fabric) with { Name = "T1" };
        await ws.Store.SaveProfileAsync(profile);
        var manager = new LocalModManager(ws.Paths, ws.Store);
        var sources = new[] { "first/Same.jar", "second/Same.jar", new string('a', 64) + "-Chosen.jar" };
        var cart = new List<ModCartItem>();
        for (var index = 0; index < sources.Length; index++)
        {
            var source = SafePath.Join(ws.Paths.Root, sources[index]);
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            SecurityAndModsTests.CreateJar(source, "demo" + index);
            cart.Add(new(source, await manager.InspectAsync(source)));
        }
        await manager.InstallAsync(profile, cart, true);
        var game = ws.Paths.Game(profile.Id);
        var names = Directory.GetFiles(SafePath.Join(game, "mods")).Select(Path.GetFileName).ToArray();
        Assert.Contains("Same.jar", names);
        Assert.Contains("Same-2.jar", names);
        Assert.Contains(sources[2], names);
        Assert.All(cart, item => Assert.True(File.Exists(item.SourcePath)));
        var renamer = new ProfileFileRenamer(ws.Paths, ws.Store, manager, new(ws.Paths));
        var skipped = Assert.Single(await renamer.ReviewAsync(profile.Id));
        Assert.Null(skipped.Plan);
        Assert.Equal("ProfileNameUnverified", skipped.Reason);
        Console.WriteLine("Physical local collision: " + SafePath.Join(game, "mods/Same-2.jar"));
    }

    [Fact]
    public async Task ResourcePackRenamePreservesActiveSelectionAndOtherOptions()
    {
        using var ws = new TestWorkspace();
        await ws.Store.InitializeAsync();
        var profile = ws.Profile(); await ws.Store.SaveProfileAsync(profile);
        var game = ws.Paths.Game(profile.Id);
        var original = SafePath.Join(game, "resourcepacks/source.zip");
        ProfileContentTests.WriteZip(original, "pack.mcmeta", """{"pack":{"pack_format":34,"description":"Bare Bones"}}""");
        var digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA512.HashData(await File.ReadAllBytesAsync(original)));
        var name = digest + "-Bare Bones.zip";
        File.Move(original, SafePath.Join(game, "resourcepacks/" + name));
        var options = SafePath.Join(game, "options.txt");
        await File.WriteAllTextAsync(options, "renderDistance:8\r\nresourcePacks:[\"vanilla\",\"file/" + name + "\"]\r\n" +
            "incompatibleResourcePacks:[\"file/" + name + "\"]\r\n");
        var renamer = new ProfileFileRenamer(ws.Paths, ws.Store, new(ws.Paths, ws.Store), new(ws.Paths));
        var plan = Assert.Single(await renamer.ReviewAsync(profile.Id)).Plan!;
        await renamer.ApplyAsync(plan);
        Assert.Equal("resourcepacks/Bare Bones.zip", plan.To);
        Assert.Equal("renderDistance:8\r\nresourcePacks:[\"vanilla\",\"file/Bare Bones.zip\"]\r\n" +
            "incompatibleResourcePacks:[\"file/Bare Bones.zip\"]\r\n", await File.ReadAllTextAsync(options));
    }

    [Fact]
    public async Task ReadableProfileFolderIsStableAndLegacyMoveCanBeReversed()
    {
        using var ws = new TestWorkspace();
        await ws.Store.InitializeAsync();
        var modern = ws.Profile() with { Name = "Clear Sky" };
        await ws.Store.SaveProfileAsync(modern);
        var named = ws.Paths.Profile(modern.Id);
        Assert.Equal("Clear Sky", Path.GetFileName(named));
        var sameName = ws.Profile() with { Name = "clear sky" };
        await ws.Store.SaveProfileAsync(sameName);
        Assert.Equal("clear sky-2", Path.GetFileName(ws.Paths.Profile(sameName.Id)));
        await ws.Store.SaveProfileAsync(modern with { Name = "Renamed" });
        Assert.Equal(named, new LauncherPaths(ws.Paths.Root).Profile(modern.Id));

        var legacy = ws.Profile() with { Name = "Legacy Pack" };
        var oldPath = SafePath.Join(ws.Paths.Root, $"instances/{legacy.Id:N}");
        Directory.CreateDirectory(oldPath);
        await ws.Store.SaveProfileAsync(legacy);
        Assert.Equal(oldPath, ws.Paths.Profile(legacy.Id));
        var usage = new ProfileUsage(ws.Paths);
        var mover = new ProfileFolderMover(ws.Paths, ws.Store, usage);
        var plan = await mover.PreviewAsync(legacy.Id);
        var marker = SafePath.Join(ws.Paths.Game(legacy.Id), "saves/world.dat");
        await File.WriteAllTextAsync(marker, "fixture world");
        await Assert.ThrowsAsync<IOException>(() => mover.MoveAsync(plan));
        Assert.True(File.Exists(marker));
        plan = await mover.PreviewAsync(legacy.Id);
        await mover.MoveAsync(plan);
        Assert.False(Directory.Exists(oldPath));
        Assert.Equal(plan.Target, ws.Paths.Profile(legacy.Id));
        Assert.Equal("Legacy Pack", Path.GetFileName(plan.Target));
        Assert.Equal(plan.Target, new LauncherPaths(ws.Paths.Root).Profile(legacy.Id));
        Assert.Equal("fixture world", await File.ReadAllTextAsync(SafePath.Join(ws.Paths.Game(legacy.Id), "saves/world.dat")));
        await mover.MoveAsync(await mover.PreviewAsync(legacy.Id));
        Assert.Equal(oldPath, ws.Paths.Profile(legacy.Id));
        Assert.True(File.Exists(marker));
    }

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
