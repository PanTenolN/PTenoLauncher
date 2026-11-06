using Avalonia.Headless.XUnit;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.ViewModels;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class WorkspaceTabCacheTests
{
    [AvaloniaFact]
    public async Task CachedTabsStayInstantAndExternalRenamesOnlyRefreshTheirProfile()
    {
        using var workspace = new TestWorkspace();
        using var services = new LauncherServices(workspace.Paths);
        await services.Store.InitializeAsync();
        var first = workspace.Profile(ModLoader.Fabric) with { Name = "Cache first" };
        var second = workspace.Profile(ModLoader.Fabric) with { Name = "Cache second" };
        await services.Store.SaveProfileAsync(first);
        await services.Store.SaveProfileAsync(second);
        var firstMod = Path.Combine(workspace.Paths.Game(first.Id), "mods", "first.jar");
        var secondMod = Path.Combine(workspace.Paths.Game(second.Id), "mods", "second.jar");
        SecurityAndModsTests.CreateJar(firstMod, "first");
        SecurityAndModsTests.CreateJar(secondMod, "second");
        var pack = Path.Combine(workspace.Paths.Game(first.Id), "resourcepacks", "first.zip");
        await using (var stream = File.Create(pack))
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create))
            await using (var metadata = zip.CreateEntry("pack.mcmeta").Open())
                await metadata.WriteAsync("""{"pack":{"pack_format":34,"description":"Test pack"}}"""u8.ToArray());

        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            vm.OpenProfileWorkspace(first.Id);
            vm.ChooseWorkspaceSection(ProfileSection.Mods);
            await vm.WaitForWorkspaceSnapshotAsync();
            var originalMod = Assert.Single(vm.WorkspaceModRows);
            vm.ChooseWorkspaceSection(ProfileSection.ResourcePacks);
            await vm.WaitForWorkspaceSnapshotAsync();
            var originalPack = Assert.Single(vm.WorkspaceContentRows);
            for (var i = 0; i < 5; i++)
            {
                vm.ChooseWorkspaceSection(ProfileSection.Mods);
                Assert.Same(originalMod, Assert.Single(vm.WorkspaceModRows));
                await vm.WaitForWorkspaceSnapshotAsync();
                Assert.Same(originalMod, Assert.Single(vm.WorkspaceModRows));
                vm.ChooseWorkspaceSection(ProfileSection.ResourcePacks);
                Assert.Same(originalPack, Assert.Single(vm.WorkspaceContentRows));
                await vm.WaitForWorkspaceSnapshotAsync();
                Assert.False(vm.IsBusy);
            }
            vm.ChooseWorkspaceSection(ProfileSection.Mods);
            File.Move(firstMod, Path.Combine(Path.GetDirectoryName(firstMod)!, "renamed.jar"));
            Assert.Same(originalMod, Assert.Single(vm.WorkspaceModRows));
            for (var attempt = 0; attempt < 200 && vm.WorkspaceModRows.All(row => row.File != "renamed.jar"); attempt++)
                await Task.Delay(25);
            Assert.Equal("renamed.jar", Assert.Single(vm.WorkspaceModRows).File);
            Assert.False(vm.IsBusy);

            vm.OpenProfileWorkspace(second.Id);
            vm.ChooseWorkspaceSection(ProfileSection.Mods);
            Assert.DoesNotContain(vm.WorkspaceModRows, row => row.File == "renamed.jar");
            await vm.WaitForWorkspaceSnapshotAsync();
            Assert.Equal("second.jar", Assert.Single(vm.WorkspaceModRows).File);
            vm.OpenProfileWorkspace(first.Id);
            vm.ChooseWorkspaceSection(ProfileSection.Mods);
            Assert.Equal("renamed.jar", Assert.Single(vm.WorkspaceModRows).File);
            vm.ChooseWorkspaceSection(ProfileSection.ResourcePacks);
            Assert.Same(originalPack, Assert.Single(vm.WorkspaceContentRows));
        }
        finally { window.Close(); }
    }
}
