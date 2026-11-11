using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Storage;
using PTenoLauncher.Testing;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class ProfileOrganizationTests
{
    [AvaloniaFact]
    public async Task OrganizationEditorPersistsAndComposesGroupSearchAndLoaderFilters()
    {
        using var ws = new TestWorkspace();
        await ws.Store.InitializeAsync();
        var first = ws.Profile(ModLoader.Fabric) with { Name = "Fabric Alpha" };
        var second = ws.Profile() with { Name = "Vanilla Alpha" };
        await ws.Store.SaveProfileAsync(first);
        await ws.Store.SaveProfileAsync(second);
        using (var services = new LauncherServices(ws.Paths))
        {
            var vm = new MainViewModel(services);
            await vm.InitializeAsync();
            vm.OpenProfileWorkspace(first.Id);
            var window = new MainWindow { DataContext = vm };
            window.Show();
            try
            {
                window.FindControl<Button>("ProfileOrganizationButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var editor = Assert.Single(window.OwnedWindows);
                editor.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "ProfileGroupInput").Text = " Expedition ";
                editor.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "ProfileNotesInput").Text = "Local notes\nKeep the test world";
                editor.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "SaveProfileOrganization")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntilAsync(() => vm.OrganizationFor(first.Id).Group == "Expedition");
            }
            finally { window.Close(); }
        }

        using var restarted = new LauncherServices(ws.Paths);
        var reopened = new MainViewModel(restarted);
        await reopened.InitializeAsync();
        Assert.Equal(new ProfileOrganization("Expedition", "Local notes\nKeep the test world"), reopened.OrganizationFor(first.Id));
        Assert.Equal(new ProfileOrganization("", ""), reopened.OrganizationFor(second.Id));
        reopened.LibraryQuery = "Alpha";
        reopened.LibraryGroup = reopened.LibraryGroups.Single(group => group.Name == "Expedition");
        Assert.Equal(first.Id, Assert.Single(reopened.LibraryProfiles).Id);
        reopened.LibraryLoader = reopened.LibraryLoaders.Single(loader => loader.Loader == ModLoader.Vanilla);
        Assert.Empty(reopened.LibraryProfiles);
        reopened.LibraryLoader = reopened.LibraryLoaders.Single(loader => loader.IsAll);
        Assert.Equal(first.Id, Assert.Single(reopened.LibraryProfiles).Id);
        Assert.Equal("Alpha", reopened.LibraryQuery);
    }

    [Fact]
    public async Task ScreenshotsReadOnlyTheRequestedProfilesLocalPng()
    {
        using var ws = new TestWorkspace();
        await ws.Store.InitializeAsync();
        var first = ws.Profile() with { Name = "First" };
        var second = ws.Profile() with { Name = "Second" };
        await ws.Store.SaveProfileAsync(first);
        await ws.Store.SaveProfileAsync(second);
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAAAFklEQVR4nGN4mKdBEmIY1TCqYfhqAAAVwXcQiykkaAAAAABJRU5ErkJggg==");
        var folder = SafePath.Join(ws.Paths.Game(first.Id), "screenshots");
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "test.png"), png);
        var screenshots = new ProfileScreenshots(new LauncherPaths(ws.Paths.Root));
        Assert.Equal("test.png", Assert.Single(await screenshots.ListAsync(first.Id)));
        Assert.Equal(png, await screenshots.ReadAsync(first.Id, "test.png"));
        Assert.Empty(await screenshots.ListAsync(second.Id));
        await Assert.ThrowsAsync<InvalidDataException>(() => screenshots.ReadAsync(second.Id, "../test.png"));
        png[16] = 255;
        await File.WriteAllBytesAsync(Path.Combine(folder, "bad.png"), png);
        await Assert.ThrowsAsync<InvalidDataException>(() => screenshots.ReadAsync(first.Id, "bad.png"));
    }

    [Fact]
    public async Task StorageReportCountsSharedCacheOnceAndKeepsUnknownFiles()
    {
        using var ws = new TestWorkspace();
        using var external = new OwnedTestRoot("storage-report-backups");
        await ws.Store.InitializeAsync();
        var first = ws.Profile() with { Name = "First" };
        var second = ws.Profile() with { Name = "Second" };
        await ws.Store.SaveProfileAsync(first);
        await ws.Store.SaveProfileAsync(second);
        var shared = new InstalledFile("versions/demo/client.jar", new string('1', 40), "SHA1", 7);
        var index = new InstalledFile("assets/indexes/demo.json", new string('2', 40), "SHA1", 3);
        var receipt = new InstallationReceipt(first.MinecraftVersion, 21, "Main", DateTimeOffset.UtcNow,
            [shared, index], [shared.RelativePath], [], [], [], "demo");
        await AtomicFile.WriteJsonAsync(ws.Paths.Receipt(first.Id), receipt);
        await AtomicFile.WriteJsonAsync(ws.Paths.Receipt(second.Id), receipt);
        await AtomicFile.WriteAsync(SafePath.Join(ws.Paths.Cache, shared.RelativePath), new byte[7]);
        await AtomicFile.WriteAsync(SafePath.Join(ws.Paths.Cache, index.RelativePath), new byte[3]);
        var unknown = SafePath.Join(ws.Paths.Cache, "unknown.bin");
        await AtomicFile.WriteAsync(unknown, new byte[5]);
        await AtomicFile.WriteAsync(SafePath.Join(ws.Paths.Game(first.Id), "saves/test/level.dat"), new byte[13]);
        var backups = new BackupManager(ws.Paths, ws.Store);
        await AtomicFile.WriteAsync(SafePath.Join(ws.Paths.Backups, "old.zip"), new byte[11]);
        await backups.SetDirectoryAsync(external.Root);
        await AtomicFile.WriteAsync(SafePath.Join(external.Root, $"worlds/{first.Id:N}/copy.zip"), new byte[17]);
        await AtomicFile.WriteAsync(SafePath.Join(external.Root, "unknown.zip"), new byte[19]);

        var report = await new StorageUsageReport(ws.Paths, ws.Store, backups).ReadAsync();
        Assert.Empty(report.UnreadablePaths);
        Assert.Equal(15, report.CacheBytes);
        Assert.Equal(28, report.BackupBytes);
        Assert.Equal(13, report.Profiles.Single(size => size.Profile.Id == first.Id).WorldBytes);
        Assert.Equal(0, report.Profiles.Single(size => size.Profile.Id == second.Id).WorldBytes);
        var reference = Assert.Single(report.CacheReferences, file => file.RelativePath == shared.RelativePath);
        Assert.Equal(new[] { first.Id, second.Id }, reference.Profiles.Select(profile => profile.Id));
        Assert.Equal(report.Profiles.Sum(size => size.ProfileBytes) + 43, report.TotalBytes);
        Assert.True(File.Exists(unknown));
        Assert.True(File.Exists(SafePath.Join(external.Root, "unknown.zip")));
    }

    private static async Task WaitUntilAsync(Func<bool> ready)
    {
        for (var attempt = 0; attempt < 200 && !ready(); attempt++) await Task.Delay(10);
        Assert.True(ready());
    }
}
