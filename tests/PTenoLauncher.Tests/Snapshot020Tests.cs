using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Storage;
using Avalonia.Headless.XUnit;
using Xunit;
namespace PTenoLauncher.Tests;
public sealed class Snapshot020Tests
{
    [AvaloniaFact] public async Task SnapshotsAndHistoricalFiltersUseCacheWithoutChangingEditedVersion()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync();
        var date = DateTimeOffset.UtcNow;
        var entries = new[] {
            new VersionEntry("1.21.1","release",new("https://piston-meta.mojang.com/release.json"),new string('a',40),date),
            new VersionEntry("test-snapshot","snapshot",new("https://piston-meta.mojang.com/snapshot.json"),new string('b',40),date.AddDays(1)),
            new VersionEntry("old-beta","old_beta",new("https://piston-meta.mojang.com/beta.json"),new string('c',40),date.AddYears(-10))
        };
        await AtomicFile.WriteJsonAsync(SafePath.Join(ws.Paths.Cache,"metadata/catalog.json"),new VersionCatalog(date,"1.21.1",entries,"test-snapshot"));
        using var services = new LauncherServices(ws.Paths); var vm = new MainViewModel(services);
        await vm.InitializeAsync(); vm.Offline = true; vm.VersionId = "1.20.4";
        Assert.Contains(vm.Versions,v=>v.Type=="snapshot");
        vm.ShowSnapshots=false; Assert.Single(vm.Versions);
        vm.ShowHistorical=true; Assert.Equal(2,vm.Versions.Count);
        vm.ShowSnapshots=true; Assert.Equal(3,vm.Versions.Count);
        Assert.Equal("1.20.4",vm.VersionId);
        await vm.RefreshCatalogIfStaleAsync(); Assert.Equal(3,vm.Versions.Count);
        await vm.SaveVersionVisibilityAsync(); Assert.Equal("True",await services.Store.GetSettingAsync("versions.snapshots"));
    }
}