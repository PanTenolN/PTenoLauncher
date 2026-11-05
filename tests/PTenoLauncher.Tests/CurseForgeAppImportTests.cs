using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Modpacks;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class CurseForgeAppImportTests
{
    private static string Source(MrpackFixture fixture, string? loader)
    {
        var source = Path.Combine(fixture.Workspace.Paths.Root, "curseforge-source");
        InstancePackTests.Put(source, "minecraftinstance.json", JsonSerializer.SerializeToUtf8Bytes(new
        {
            name = "CurseForge fixture", gameVersion = "1.21.1",
            baseModLoader = loader is null ? null : new { name = loader },
            profileImagePath = "do-not-open/foreign.png", launchCommand = "SYNTHETIC COMMAND"
        }));
        InstancePackTests.Put(source, "config/example.txt", [4, 5, 6]);
        InstancePackTests.Put(source, "config/run.cmd", [9]);
        InstancePackTests.Put(source, "saves/fixture/level.dat", [9]);
        return source;
    }

    [Theory]
    [InlineData(null, ModLoader.Vanilla, null)]
    [InlineData("forge-52.0.1", ModLoader.Forge, "1.21.1-52.0.1")]
    [InlineData("fabric-0.16.10-1.21.1", ModLoader.Fabric, "0.16.10")]
    public async Task SupportedLocalProfileUsesExactTargetAndExcludedContent(string? sourceLoader, ModLoader loader, string? version)
    {
        using var f = new MrpackFixture();
        await f.Workspace.Store.InitializeAsync();
        f.Network.Offline = true;
        var source = Source(f, sourceLoader);
        var metadata = Path.Combine(source, "minecraftinstance.json");
        var prior = File.ReadAllBytes(metadata);
        var preview = await f.Importer.CurseForgeAppReader.PreviewAsync(source, new());
        Assert.True(preview.Plan.CanImport, string.Join(", ", preview.Plan.Blockers));
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ImportCurseForgeAppAsync(preview, false));
        var result = await f.Importer.ImportCurseForgeAppAsync(preview, true);
        Assert.Equal(loader, result.Profile.Loader);
        Assert.Equal("1.21.1", result.Profile.MinecraftVersion);
        Assert.Equal(version, await MrpackRequirements.RequestedAsync(f.Workspace.Paths, result.Profile));
        Assert.Equal("local-curseforge-app", result.Receipt.SourceKind);
        Assert.Equal("config/example.txt", Assert.Single(result.Receipt.Files).RelativePath);
        Assert.Equal(prior, File.ReadAllBytes(metadata));
        Assert.Equal(0, f.Requests);
    }

    [Theory]
    [InlineData("neoforge-21.1.0")]
    [InlineData("fabric-0.16.10-1.20.4")]
    [InlineData("forge-latest")]
    public async Task UnknownOrMismatchedLoaderCannotBeImportedAsVanilla(string loader)
    {
        using var f = new MrpackFixture();
        await f.Workspace.Store.InitializeAsync();
        var preview = await f.Importer.CurseForgeAppReader.PreviewAsync(Source(f, loader), new());
        Assert.Contains(preview.Plan.Blockers, b => b.Code == "CurseForgeAppUnsupported");
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ImportCurseForgeAppAsync(preview, true));
        Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
    }

    [AvaloniaFact]
    public async Task LocalCurseForgePreviewUsesTheSharedConsentAndInstallButton()
    {
        using var f = new MrpackFixture();
        using var services = new LauncherServices(f.Workspace.Paths);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.Offline = true;
        var view = new ProfileImportView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        try
        {
            await vm.ChooseInstancePackAsync(Source(f, "forge-52.0.1"), curseForgeApp: true);
            Assert.True(vm.IsCurseForgeAppPack);
            Assert.True(view.FindControl<Button>("ChooseCurseForgeAppButton")!.IsVisible);
            var install = view.FindControl<Button>("ImportMrpackButton")!;
            Assert.False(install.IsEnabled);
            view.FindControl<CheckBox>("MrpackConsent")!.IsChecked = true;
            Assert.True(install.IsEnabled, string.Join(", ", vm.PackBlockers));
            install.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var attempt = 0; attempt < 400 && !vm.IsIdle; attempt++) await Task.Delay(10);
            Assert.True(vm.LastOperationSucceeded, vm.Status);
            Assert.Equal(ModLoader.Forge, Assert.Single(await services.Store.GetProfilesAsync()).Loader);
        }
        finally { window.Close(); }
    }
}
