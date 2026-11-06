using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Modpacks;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class PackReconciliationTests
{
    [Fact]
    public async Task IdenticalPacksCopyOneVerifiedJarAndLeaveUnknownJarManual()
    {
        using var f = new MrpackFixture { Payload = MrpackFixture.Jar() };
        await f.Workspace.Store.InitializeAsync();
        var index = MrpackFixture.Index([Link(f, "demo.jar", "Mod00001", "Ver00001", f.Payload)], "fabric-loader");
        var first = f.Pack(index, ("overrides/mods/local.jar", MrpackFixture.Jar("local")));
        var second = f.Pack(index, ("overrides/mods/local.jar", MrpackFixture.Jar("local")));
        var plan = await f.Importer.PrepareCopyAsync(first, second, PackBasis.Left);
        Assert.True(plan.CanCreate, string.Join(", ", plan.Blockers));
        Assert.Contains(plan.Rows, row => row.Decision == "PackCompareSame" && row.Verification == "PackCopyChecked");
        Assert.Contains(plan.Rows, row => row.Decision == "PackCopyManual" && row.Basis?.Path == "mods/local.jar");
        Assert.Equal(1, f.Requests);
        var result = await f.Importer.CreateCopyAsync(plan, true);
        Assert.Equal("mods/demo.jar", Assert.Single(result.Receipt.Files).RelativePath);
        Assert.Equal(f.Payload, await File.ReadAllBytesAsync(SafePath.Join(f.Workspace.Paths.Game(result.Profile.Id), "mods/demo.jar")));
        Assert.Equal(1, f.Requests);
    }

    [Fact]
    public async Task BasisVersionWinsAndOnlyPlanFilesReachAnIndependentProfile()
    {
        using var f = new MrpackFixture();
        await f.Workspace.Store.InitializeAsync();
        var old = MrpackFixture.Jar("example");
        var current = MrpackFixture.Jar("example");
        // Give the versions distinct byte digests without changing their declared mod identity.
        using (var bytes = new MemoryStream())
        {
            bytes.Write(current);
            bytes.Position = 0;
            using (var jar = new ZipArchive(bytes, ZipArchiveMode.Update, leaveOpen: true)) jar.CreateEntry("version-two");
            current = bytes.ToArray();
        }
        var resource = ResourceZip();
        var left = f.Pack(MrpackFixture.Index([Link(f, "old.jar", "Mod00001", "Ver00001", old)], "fabric-loader"),
            ("overrides/resourcepacks/addition.zip", resource), ("overrides/saves/world/level.dat", [7]));
        var right = f.Pack(MrpackFixture.Index([Link(f, "new.jar", "Mod00001", "Ver00002", current)], "fabric-loader"),
            ("overrides/config/setup.txt", Encoding.UTF8.GetBytes("launch command")));
        var original = await f.Importer.ImportAsync(await f.Importer.Reader.PreviewAsync(f.Pack()), true);
        var marker = SafePath.Join(f.Workspace.Paths.Game(original.Profile.Id), "saves/preserved/level.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        await File.WriteAllBytesAsync(marker, [9]);
        var leftHash = await File.ReadAllBytesAsync(left);
        var rightHash = await File.ReadAllBytesAsync(right);
        f.Response = (request, _) =>
        {
            Assert.EndsWith("/new.jar", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(current) });
        };

        var plan = await f.Importer.PrepareCopyAsync(left, right, PackBasis.Right);
        Assert.True(plan.CanCreate, string.Join(", ", plan.Blockers));
        Assert.Equal("0.16.10", plan.LoaderVersion);
        Assert.Contains(plan.Rows, r => r.Decision == "PackCompareVersion" && r.Basis?.VersionId == "Ver00002" && r.Other?.VersionId == "Ver00001");
        Assert.Contains(plan.Rows, r => r.Decision == "PackCopyAdd" && r.Other?.Path == "resourcepacks/addition.zip");
        Assert.Contains(plan.Rows, r => r.Decision == "PackCopyManual" && r.Basis?.Path == "config/setup.txt");
        var result = await f.Importer.CreateCopyAsync(plan, true);
        Assert.NotEqual(original.Profile.Id, result.Profile.Id);
        Assert.Equal(2, (await f.Workspace.Store.GetProfilesAsync()).Count);
        Assert.Equal(new[] { "mods/new.jar", "resourcepacks/addition.zip" }, result.Receipt.Files.Select(x => x.RelativePath));
        foreach (var row in plan.Rows.Where(r => r.Verification == "PackCopyChecked"))
        {
            var chosen = (row.Basis ?? row.Other)!;
            var actual = await File.ReadAllBytesAsync(SafePath.Join(f.Workspace.Paths.Game(result.Profile.Id), chosen.Path));
            Assert.Equal(chosen.Sha512, Convert.ToHexStringLower(SHA512.HashData(actual)));
        }
        Assert.Equal("0.16.10", await MrpackRequirements.RequestedAsync(f.Workspace.Paths, result.Profile));
        Assert.Equal(leftHash, await File.ReadAllBytesAsync(left));
        Assert.Equal(rightHash, await File.ReadAllBytesAsync(right));
        Assert.Equal(new byte[] { 9 }, await File.ReadAllBytesAsync(marker));
        Assert.False(File.Exists(f.Workspace.Paths.Receipt(result.Profile.Id)));
    }

    [Theory]
    [InlineData(false, "PackDownload")]
    [InlineData(true, "PackOfflineMissing")]
    public async Task UnavailableExactFileIsReportedBeforeConfirmation(bool offline, string reason)
    {
        using var f = new MrpackFixture { Payload = MrpackFixture.Jar() };
        await f.Workspace.Store.InitializeAsync();
        f.Network.Offline = offline;
        f.Response = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        var first = f.Pack(MrpackFixture.Index([Link(f, "demo.jar", "Mod00001", "Ver00001", f.Payload)], "fabric-loader"));
        var second = f.Pack(MrpackFixture.Index(loader: "fabric-loader"));
        var plan = await f.Importer.PrepareCopyAsync(first, second, PackBasis.Left);
        Assert.False(plan.CanCreate);
        Assert.Contains(plan.Blockers, blocker => blocker.Code == reason && blocker.Detail == "mods/demo.jar");
        Assert.Equal("PackCopyDeclared", Assert.Single(plan.Rows).Verification);
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.CreateCopyAsync(plan, true));
        Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
        Assert.Equal(offline ? 0 : 1, f.Requests);
    }

    [Fact]
    public async Task LossOfVerifiedCacheAfterReviewRequiresAnotherPreview()
    {
        using var f = new MrpackFixture { Payload = MrpackFixture.Jar() };
        await f.Workspace.Store.InitializeAsync();
        var first = f.Pack(MrpackFixture.Index([Link(f, "demo.jar", "Mod00001", "Ver00001", f.Payload)], "fabric-loader"));
        var second = f.Pack(MrpackFixture.Index(loader: "fabric-loader"));
        var plan = await f.Importer.PrepareCopyAsync(first, second, PackBasis.Left);
        Assert.True(plan.CanCreate);
        var digest = Assert.Single(plan.Rows).Basis!.Sha512;
        await File.WriteAllBytesAsync(SafePath.Join(f.Workspace.Paths.Cache, "modpacks/files/" + digest + ".bin"), [1, 2, 3]);
        Assert.Equal("PackCopyVerifyAgain", (await Assert.ThrowsAsync<MrpackException>(() => f.Importer.CreateCopyAsync(plan, true))).Code);
        Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
        Assert.Equal(1, f.Requests);
    }

    [Fact]
    public async Task ConflictingHashClaimsBlockBeforeDownloading()
    {
        using var f = new MrpackFixture { Payload = MrpackFixture.Jar() };
        await f.Workspace.Store.InitializeAsync();
        var first = f.Pack(MrpackFixture.Index([Link(f, "demo.jar", "Mod00001", "Ver00001", f.Payload)], "fabric-loader"));
        var second = f.Pack(MrpackFixture.Index([Link(f, "demo.jar", "Mod00001", "Ver00001", [1])], "fabric-loader"));
        var plan = await f.Importer.PrepareCopyAsync(first, second, PackBasis.Left);
        Assert.False(plan.CanCreate);
        Assert.Contains(plan.Blockers, b => b.Code == "PackCopyConflict");
        Assert.Equal(0, f.Requests);
    }

    [Fact]
    public async Task DeclinedOrStalePlanNeverPublishesAndRepreviewRequiresFreshConsent()
    {
        using var f = new MrpackFixture();
        await f.Workspace.Store.InitializeAsync();
        var first = f.Pack();
        var second = f.Pack();
        var plan = await f.Importer.PrepareCopyAsync(first, second, PackBasis.Left);
        Assert.Equal("PackConsent", (await Assert.ThrowsAsync<MrpackException>(() => f.Importer.CreateCopyAsync(plan, false))).Code);
        using (var zip = ZipFile.Open(second, ZipArchiveMode.Update)) zip.CreateEntry("overrides/saves/changed/level.dat");
        Assert.Equal("PackChanged", (await Assert.ThrowsAsync<MrpackException>(() => f.Importer.CreateCopyAsync(plan, true))).Code);
        Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
        Assert.Empty(await f.Importer.PendingAsync());
        var refreshed = await f.Importer.PrepareCopyAsync(first, second, PackBasis.Left);
        Assert.NotEqual(plan.OtherSha256, refreshed.OtherSha256);
        Assert.Equal("PackConsent", (await Assert.ThrowsAsync<MrpackException>(() => f.Importer.CreateCopyAsync(refreshed, false))).Code);
    }

    [Fact]
    public async Task CancellationDuringStagingDiscardsTheOwnedCopy()
    {
        using var f = new MrpackFixture();
        await f.Workspace.Store.InitializeAsync();
        var plan = await f.Importer.PrepareCopyAsync(
            f.Pack(null, ("overrides/resourcepacks/test.zip", ResourceZip())), f.Pack(), PackBasis.Left);
        using var cancel = new CancellationTokenSource();
        f.Importer.Fault = stage => { if (stage == "preparing") cancel.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Importer.CreateCopyAsync(plan, true, ct: cancel.Token));
        Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
        Assert.Empty(await f.Importer.PendingAsync());
        var operation = Assert.Single(Directory.GetDirectories(SafePath.Join(f.Workspace.Paths.Root, "modpack-imports")));
        Assert.False(Directory.Exists(SafePath.Join(operation, "staging")));
    }

    [Fact]
    public async Task DifferentTargetsKeepOnlyTheExplicitBasisAndReadyCopyCanRecover()
    {
        using var f = new MrpackFixture();
        await f.Workspace.Store.InitializeAsync();
        var first = f.Pack(MrpackFixture.Index(loader: "fabric-loader", version: "0.16.10"),
            ("overrides/resourcepacks/left.zip", ResourceZip("Left")));
        var second = f.Pack(MrpackFixture.Index(loader: "fabric-loader", version: "0.16.11"),
            ("overrides/resourcepacks/right.zip", ResourceZip("Right")));
        var plan = await f.Importer.PrepareCopyAsync(first, second, PackBasis.Right);
        Assert.True(plan.CanCreate);
        Assert.Equal("0.16.11", plan.LoaderVersion);
        Assert.Contains(plan.Rows, row => row.Decision == "PackCopyOtherTarget" && row.Other?.Path == "resourcepacks/left.zip");
        f.Importer.Fault = stage => { if (stage == "ready") throw new IOException("Synthetic interruption"); };
        await Assert.ThrowsAsync<IOException>(() => f.Importer.CreateCopyAsync(plan, true));
        Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
        Assert.Single(await f.Importer.PendingAsync());
        f.Importer.Fault = null;
        await f.Importer.RecoverAsync(true);
        var recovered = Assert.Single(await f.Workspace.Store.GetProfilesAsync());
        Assert.Equal("resourcepacks/right.zip", Assert.Single((await MrpackRequirements.ReadAsync(f.Workspace.Paths, recovered))!.Files).RelativePath);
    }

    [AvaloniaFact]
    public async Task DialogRequiresAnExplicitBasisAndFreshConsentBeforeCreating()
    {
        using var f = new MrpackFixture();
        using var services = new LauncherServices(f.Workspace.Paths);
        await services.Store.InitializeAsync();
        await services.Store.SetSettingAsync("ui.language", "en");
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.Offline = true;
        var dialog = new PackCompareDialog { DataContext = vm };
        dialog.Show();
        try
        {
            await dialog.SetSourceAsync("left", f.Pack());
            await dialog.SetSourceAsync("right", f.Pack(null, ("overrides/resourcepacks/test.zip", ResourceZip())));
            var prepare = dialog.FindControl<Button>("PrepareCopyButton")!;
            var create = dialog.FindControl<Button>("CreateCopyButton")!;
            var consent = dialog.FindControl<CheckBox>("CopyConsent")!;
            Assert.False(prepare.IsEnabled);
            Assert.False(create.IsEnabled);
            dialog.FindControl<RadioButton>("RightBasis")!.IsChecked = true;
            Assert.True(prepare.IsEnabled);
            prepare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => consent.IsVisible);
            Assert.False(create.IsEnabled);
            consent.IsChecked = true;
            Assert.True(create.IsEnabled);
            dialog.FindControl<RadioButton>("LeftBasis")!.IsChecked = true;
            Assert.False(consent.IsVisible);
            Assert.False(create.IsEnabled);
            prepare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => consent.IsVisible);
            consent.IsChecked = true;
            create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => !vm.IsBusy && vm.Profiles.Count == 1);
            Assert.True(vm.LastOperationSucceeded, vm.Status);
            Assert.False(consent.IsVisible);
        }
        finally { dialog.Close(); TextCatalog.Apply("uk"); }
    }

    private static Dictionary<string, object?> Link(MrpackFixture f, string name, string project, string version, byte[] data) =>
        f.File("mods/" + name, server: "unsupported", url: $"https://cdn.modrinth.com/data/{project}/versions/{version}/{name}", bytes: data);

    private static byte[] ResourceZip(string name = "Synthetic resource pack")
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        using (var stream = zip.CreateEntry("pack.mcmeta").Open())
            System.Text.Json.JsonSerializer.Serialize(stream, new { pack = new { pack_format = 34, description = name } });
        return bytes.ToArray();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }
}
