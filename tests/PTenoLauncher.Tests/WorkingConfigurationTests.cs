using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class WorkingConfigurationTests
{
    private const string Active = "mods/unrelated-name.jar";
    private const string Disabled = "mods/b.jar.disabled";
    private const string Added = "mods/extra.jar";

    private static async Task<InstallationReceipt> InstallReceipt(ModUpdateTests.Fixture fixture, GameProfile profile, string loader)
    {
        var client = $"versions/{profile.MinecraftVersion}/{profile.MinecraftVersion}.jar";
        var fabric = $"libraries/fabric/net/fabricmc/fabric-loader/{loader}/fabric-loader-{loader}.jar";
        const string index = "assets/indexes/working.json";
        async Task<InstalledFile> Cache(string relative, string contents)
        {
            var path = SafePath.Join(fixture.Ws.Paths.Cache, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = System.Text.Encoding.UTF8.GetBytes(contents);
            await File.WriteAllBytesAsync(path, bytes);
            return new(relative, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "SHA256", bytes.Length);
        }
        var receipt = new InstallationReceipt(profile.MinecraftVersion, 21, "net.fabricmc.loader.impl.launch.knot.KnotClient",
            DateTimeOffset.UtcNow, [await Cache(client, client), await Cache(fabric, fabric), await Cache(index, """{"objects":{}}""")],
            [client, fabric], [], [], [], "working", ModLoader.Fabric, loader);
        await AtomicFile.WriteJsonAsync(fixture.Ws.Paths.Receipt(profile.Id), receipt);
        return receipt;
    }

    private static async Task<WorkingConfiguration> Capture(ModUpdateTests.Fixture fixture, GameProfile profile, InstallationReceipt receipt)
    {
        using var lease = new ProfileUsage(fixture.Ws.Paths).Acquire(profile.Id);
        using var captured = await fixture.Manager.CaptureWorkingLaunchUnderLeaseAsync(profile, receipt, default);
        return captured.Configuration;
    }

    private static async Task<WorkingConfiguration> Save(ModUpdateTests.Fixture fixture)
    {
        var receipt = await InstallReceipt(fixture, fixture.Profile, "0.16.9");
        await fixture.Add(Disabled, fixture.Bytes["B1"], owned: false);
        var candidate = await Capture(fixture, fixture.Profile, receipt);
        await fixture.Manager.MarkWorkingAsync(fixture.Profile, candidate, confirmed: true);
        return (await fixture.Reopen().ReadWorkingAsync(fixture.Profile.Id))!;
    }

    [Fact]
    public async Task ExplicitRenameUpdatesSavedWorkingSnapshotWithoutEnablingAJar()
    {
        using var fixture = new ModUpdateTests.Fixture();
        await fixture.Start();
        var profile = fixture.Profile;
        var receipt = await InstallReceipt(fixture, profile, "0.16.9");
        var old = "mods/" + ModUpdateTests.Fixture.Sha512(fixture.Bytes["B1"]) + "-b.jar.disabled";
        await fixture.Add(old, fixture.Bytes["B1"], owned: false);
        await fixture.Manager.MarkWorkingAsync(profile, await Capture(fixture, profile, receipt), confirmed: true);
        var renamer = new ProfileFileRenamer(fixture.Ws.Paths, fixture.Ws.Store, fixture.Local, new(fixture.Ws.Paths));
        await renamer.ApplyAsync(await renamer.PreviewAsync(profile.Id, old));
        var mod = Assert.Single((await fixture.Manager.ReadWorkingAsync(profile.Id))!.Mods, mod => !mod.Enabled);
        Assert.Equal(Disabled, mod.File.Path);
        Assert.True(File.Exists(fixture.Physical(Disabled)));
        Assert.False(File.Exists(fixture.Physical("mods/b.jar")));
    }

    [Fact]
    public async Task WorkingRestoreReusesVerifiedReadableNameAndPreservesDisabledState()
    {
        using var fixture = new ModUpdateTests.Fixture();
        await fixture.Start();
        var receipt = await InstallReceipt(fixture, fixture.Profile, "0.16.9");
        var old = "mods/" + ModUpdateTests.Fixture.Sha512(fixture.Bytes["B1"]) + "-Mezz Config.jar.disabled";
        await fixture.Add(old, fixture.Bytes["B1"], owned: false);
        await fixture.Manager.MarkWorkingAsync(fixture.Profile, await Capture(fixture, fixture.Profile, receipt), confirmed: true);
        File.Delete(fixture.Physical(old));
        fixture.Network.Offline = true;
        var preview = await fixture.Manager.PreviewWorkingAsync(fixture.Profile);
        Assert.True(preview.CanApply, string.Join("\n", preview.Plan.Blockers));
        await fixture.Manager.ApplyAsync(fixture.Profile, preview.Plan, true);
        var actual = fixture.Physical("mods/Mezz Config.jar.disabled");
        Assert.Equal(fixture.Bytes["B1"], await File.ReadAllBytesAsync(actual));
        Assert.False(File.Exists(fixture.Physical(old)));
        Console.WriteLine($"Physical restored names: {fixture.Physical(old)} → {actual}");
    }

    [Fact]
    public async Task ExplicitMarkPersistsExactLaunchedFilesAndNeverReplacesWorkingStateOnAnotherLaunch()
    {
        using var fixture = new ModUpdateTests.Fixture();
        await fixture.Start();
        var receipt = await InstallReceipt(fixture, fixture.Profile, "0.16.9");
        var candidate = await Capture(fixture, fixture.Profile, receipt);
        Assert.Null(await fixture.Manager.ReadWorkingAsync(fixture.Profile.Id));
        await Assert.ThrowsAsync<ModUpdateException>(() => fixture.Manager.MarkWorkingAsync(fixture.Profile, candidate, confirmed: false));
        Assert.Null(await fixture.Manager.ReadWorkingAsync(fixture.Profile.Id));

        var saved = await Save(fixture);
        Assert.Equal(1, saved.Schema);
        Assert.NotNull(saved.SavedAt);
        Assert.Equal("1.21.1", saved.Receipt.Version);
        Assert.Equal("0.16.9", saved.Receipt.LoaderVersion);
        Assert.Equal(fixture.Profile.Id, saved.ProfileId);
        Assert.Collection(saved.Mods,
            mod => { Assert.Equal(Disabled, mod.File.Path); Assert.Equal("1.0.0", mod.Version); Assert.False(mod.Enabled); },
            mod => { Assert.Equal(Active, mod.File.Path); Assert.Equal("9.0.0", mod.Version); Assert.True(mod.Enabled); });
        Assert.Equal(ModUpdateTests.Fixture.Sha512(fixture.Bytes["A1"]), saved.Mods[1].File.Sha512);

        await File.WriteAllBytesAsync(fixture.Physical(Active), fixture.Bytes["A2"]);
        var anotherLaunch = await Capture(fixture, fixture.Profile, await InstallReceipt(fixture, fixture.Profile, "0.17.0"));
        Assert.Equal(saved.Id, (await fixture.Reopen().ReadWorkingAsync(fixture.Profile.Id))!.Id);
        await File.WriteAllBytesAsync(fixture.Physical(Active), fixture.Bytes["A1"]);
        var error = await Assert.ThrowsAsync<ModUpdateException>(() => fixture.Manager.MarkWorkingAsync(fixture.Profile, anotherLaunch, confirmed: true));
        Assert.Equal("UpdatesStale", error.Code);
        Assert.Equal(saved.Id, (await fixture.Reopen().ReadWorkingAsync(fixture.Profile.Id))!.Id);
    }

    [Fact]
    public async Task OfflineRestoreAppliesConfirmedTargetAndLeavesNonPlanContentUntouched()
    {
        using var fixture = new ModUpdateTests.Fixture();
        await fixture.Start();
        var saved = await Save(fixture);
        var current = fixture.Profile with { MinecraftVersion = "1.21.4", PreferredLoaderVersion = "0.17.0", MemoryMb = 6144 };
        await fixture.Ws.Store.SaveProfileAsync(current);
        await InstallReceipt(fixture, current, "0.17.0");
        await File.WriteAllBytesAsync(fixture.Physical(Active), fixture.Bytes["A2"]);
        File.Move(fixture.Physical(Disabled), fixture.Physical("mods/b.jar"));
        await fixture.Add(Added, ModUpdateTests.Fixture.Jar("extra", "3.0"), owned: false);
        var sentinels = new[] { fixture.Physical("mods/readme.txt"), fixture.Physical("saves/world/level.dat"),
            SafePath.Join(fixture.Ws.Paths.Profile(current.Id), "translations/draft.json"),
            SafePath.Join(fixture.Ws.Paths.Backups, "unrelated.zip") };
        foreach (var path in sentinels)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "preserve");
        }
        await fixture.Ws.Store.SetSettingAsync("test.account", "untouched");
        fixture.Network.Offline = true;
        fixture.Manager = fixture.Reopen();
        var preview = await fixture.Manager.PreviewWorkingAsync(current);
        Assert.True(preview.CanApply, string.Join("\n", preview.Plan.Blockers));
        Assert.Equal("1.0.0", preview.ReplacedMods.Single(mod => mod.File.Path == Active).Version);
        Assert.Equal(4, preview.Plan.Changes.Count);
        Assert.Empty(preview.MissingMods);
        Assert.Empty(preview.MissingGameFiles);
        await Assert.ThrowsAsync<ModUpdateException>(() => fixture.Manager.ApplyAsync(current, preview.Plan, confirmed: false));
        Assert.Equal("1.21.4", Assert.Single(await fixture.Ws.Store.GetProfilesAsync()).MinecraftVersion);
        Assert.True(File.Exists(fixture.Physical(Added)));
        await fixture.Manager.ApplyAsync(current, preview.Plan, confirmed: true);
        var restored = Assert.Single(await fixture.Ws.Store.GetProfilesAsync());
        Assert.Equal(current with { MinecraftVersion = "1.21.1", PreferredLoaderVersion = "0.16.9" }, restored);
        Assert.Equal(fixture.Bytes["A1"], await File.ReadAllBytesAsync(fixture.Physical(Active)));
        Assert.Equal(fixture.Bytes["B1"], await File.ReadAllBytesAsync(fixture.Physical(Disabled)));
        Assert.False(File.Exists(fixture.Physical("mods/b.jar")));
        Assert.False(File.Exists(fixture.Physical(Added)));
        foreach (var path in sentinels) Assert.Equal("preserve", await File.ReadAllTextAsync(path));
        Assert.Equal("untouched", await fixture.Ws.Store.GetSettingAsync("test.account"));
        Assert.Equal(0, fixture.Requests);
        Assert.Equal(saved.Id, (await fixture.Reopen().ReadWorkingAsync(current.Id))!.Id);
        Assert.False((await fixture.Manager.PreviewWorkingAsync(restored)).CanApply);
    }

    [Fact]
    public async Task MissingExactJarCanBeSuppliedLocallyOrByVerifiedDigestAndStaleConsentCannotApply()
    {
        using var fixture = new ModUpdateTests.Fixture();
        await fixture.Start();
        var saved = await Save(fixture);
        var original = saved.Mods.Single(mod => mod.File.Path == Active);
        var cached = SafePath.Join(fixture.Ws.Paths.Cache, $"working-mods/{original.File.Sha512}.jar");
        File.Delete(cached);
        await File.WriteAllBytesAsync(fixture.Physical(Active), fixture.Bytes["A2"]);
        fixture.Network.Offline = true;
        var missing = await fixture.Manager.PreviewWorkingAsync(fixture.Profile);
        Assert.False(missing.CanApply);
        Assert.Equal(original.File, Assert.Single(missing.MissingMods).File);
        await Assert.ThrowsAsync<ModUpdateException>(() =>
            fixture.Manager.SupplyWorkingFileAsync(fixture.Profile.Id, saved.Id, Active, fixture.Physical(Active)));
        var manual = fixture.Physical("manual.jar");
        await File.WriteAllBytesAsync(manual, fixture.Bytes["A1"]);
        await fixture.Manager.SupplyWorkingFileAsync(fixture.Profile.Id, saved.Id, Active, manual);
        var stale = await fixture.Manager.PreviewWorkingAsync(fixture.Profile);
        Assert.True(stale.CanApply);
        using (new ProfileUsage(fixture.Ws.Paths).Acquire(fixture.Profile.Id))
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Manager.ApplyAsync(fixture.Profile, stale.Plan, confirmed: true));
        await File.AppendAllTextAsync(cached, "corrupt");
        await Assert.ThrowsAsync<ModUpdateException>(() => fixture.Manager.ApplyAsync(fixture.Profile, stale.Plan, confirmed: true));
        Assert.Equal(fixture.Bytes["A2"], await File.ReadAllBytesAsync(fixture.Physical(Active)));
        Assert.Equal(0, fixture.Requests);

        fixture.Network.Offline = false;
        await fixture.Manager.SupplyWorkingFileAsync(fixture.Profile.Id, saved.Id, Active, localFile: null);
        Assert.Equal(1, fixture.Downloads);
        var exact = await fixture.Manager.PreviewWorkingAsync(fixture.Profile);
        Assert.True(exact.CanApply);
        await fixture.Add(Added, ModUpdateTests.Fixture.Jar("extra", "3.0"), owned: false);
        var error = await Assert.ThrowsAsync<ModUpdateException>(() => fixture.Manager.ApplyAsync(fixture.Profile, exact.Plan, confirmed: true));
        Assert.Equal("UpdatesStale", error.Code);
        Assert.True(File.Exists(fixture.Physical(Added)));
    }

    [Theory]
    [InlineData("new:mods/unrelated-name.jar")]
    [InlineData("working:profile")]
    public async Task InterruptedRestoreReopensPriorReceiptAndFilesOffline(string point)
    {
        using var fixture = new ModUpdateTests.Fixture();
        await fixture.Start();
        await Save(fixture);
        var current = fixture.Profile with { MinecraftVersion = "1.21.4", PreferredLoaderVersion = "0.17.0" };
        await fixture.Ws.Store.SaveProfileAsync(current);
        var receipt = await InstallReceipt(fixture, current, "0.17.0");
        await File.WriteAllBytesAsync(fixture.Physical(Active), fixture.Bytes["A2"]);
        fixture.Network.Offline = true;
        var preview = await fixture.Manager.PreviewWorkingAsync(current);
        Assert.True(preview.CanApply);
        fixture.Manager.Fault = step => { if (step == point) throw new OperationCanceledException("controlled interruption"); };
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Manager.ApplyAsync(current, preview.Plan, confirmed: true));
        var reopened = fixture.Reopen();
        Assert.True(reopened.HasPendingWorking(current.Id));
        await reopened.RecoverWorkingAsync(current.Id);
        Assert.False(reopened.HasPendingWorking(current.Id));
        Assert.Equal(current, Assert.Single(await fixture.Ws.Store.GetProfilesAsync()));
        Assert.Equal(receipt.Version, (await AtomicFile.ReadJsonAsync<InstallationReceipt>(fixture.Ws.Paths.Receipt(current.Id)))!.Version);
        Assert.Equal(fixture.Bytes["A2"], await File.ReadAllBytesAsync(fixture.Physical(Active)));
        Assert.Equal(fixture.Bytes["B1"], await File.ReadAllBytesAsync(fixture.Physical(Disabled)));
        Assert.True((await reopened.PreviewWorkingAsync(current)).CanApply);
        Assert.Equal(0, fixture.Requests);
    }

    [Fact]
    public async Task MissingGameBinaryIsShownBeforeConfirmationAndChangedBytesBlockAnApprovedPlan()
    {
        using var fixture = new ModUpdateTests.Fixture();
        await fixture.Start();
        var saved = await Save(fixture);
        await File.WriteAllBytesAsync(fixture.Physical(Active), fixture.Bytes["A2"]);
        var preview = await fixture.Manager.PreviewWorkingAsync(fixture.Profile);
        Assert.True(preview.CanApply);
        var client = saved.Receipt.Classpath[0];
        var cached = SafePath.Join(fixture.Ws.Paths.Cache, client);
        var original = await File.ReadAllBytesAsync(cached);
        await File.AppendAllTextAsync(cached, "changed");
        var error = await Assert.ThrowsAsync<ModUpdateException>(() => fixture.Manager.ApplyAsync(fixture.Profile, preview.Plan, confirmed: true));
        Assert.Equal("WorkingGameMissing", error.Code);
        var blocked = await fixture.Manager.PreviewWorkingAsync(fixture.Profile);
        Assert.False(blocked.CanApply);
        Assert.Equal(client, Assert.Single(blocked.MissingGameFiles));
        Assert.Equal(fixture.Bytes["A2"], await File.ReadAllBytesAsync(fixture.Physical(Active)));
        var supplied = fixture.Physical("exact-client.jar");
        await File.WriteAllBytesAsync(supplied, original);
        await Assert.ThrowsAsync<ModUpdateException>(() => fixture.Manager.SupplyWorkingGameFileAsync(fixture.Profile.Id, saved.Id, client, supplied));
        File.Delete(cached);
        await Assert.ThrowsAsync<ModUpdateException>(() => fixture.Manager.SupplyWorkingGameFileAsync(fixture.Profile.Id, saved.Id, client, fixture.Physical(Active)));
        await fixture.Manager.SupplyWorkingGameFileAsync(fixture.Profile.Id, saved.Id, client, supplied);
        Assert.True((await fixture.Manager.PreviewWorkingAsync(fixture.Profile)).CanApply);
    }

    [AvaloniaFact]
    public async Task ProfileShowsRestoreDiffAndRequiresTheDialogConfirmation()
    {
        using var fixture = new ModUpdateTests.Fixture();
        await fixture.Start();
        await Save(fixture);
        await File.WriteAllBytesAsync(fixture.Physical(Active), fixture.Bytes["A2"]);
        using var services = new LauncherServices(fixture.Ws.Paths);
        services.Network.Offline = true;
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            vm.OpenProfileWorkspace(fixture.Profile.Id);
            Assert.False(window.FindControl<Button>("MarkWorkingButton")!.IsEnabled);
            var review = window.FindControl<Button>("ReviewWorkingButton")!;
            async Task<WorkingRestoreDialog> OpenReview()
            {
                review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (var i = 0; i < 500 && !window.OwnedWindows.OfType<WorkingRestoreDialog>().Any(); i++) await Task.Delay(10);
                return Assert.Single(window.OwnedWindows.OfType<WorkingRestoreDialog>());
            }
            var dialog = await OpenReview();
            var diff = dialog.FindControl<SelectableTextBlock>("RestoreDetails")!.Text;
            Assert.Contains("0.16.9", diff);
            Assert.Contains("1.0.0", diff);
            Assert.Contains("9.0.0", diff);
            Assert.Contains("SHA-256", diff);
            dialog.FindControl<Button>("CancelRestore")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(fixture.Bytes["A2"], await File.ReadAllBytesAsync(fixture.Physical(Active)));

            dialog = await OpenReview();
            var confirm = dialog.FindControl<Button>("ConfirmRestore")!;
            Assert.True(confirm.IsEnabled);
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 500 && (vm.IsBusy || vm.Status != PTenoLauncher.Desktop.Localization.TextCatalog.Get("WorkingRestored")); i++)
                await Task.Delay(10);
            Assert.True(vm.LastOperationSucceeded, vm.Status);
            Assert.Equal(fixture.Bytes["A1"], await File.ReadAllBytesAsync(fixture.Physical(Active)));
        }
        finally { window.Close(); }
    }
}
