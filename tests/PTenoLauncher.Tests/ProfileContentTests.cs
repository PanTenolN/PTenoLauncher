using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Storage;
using PTenoLauncher.Testing;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class ProfileContentTests
{
    [AvaloniaFact]
    public async Task ResourceCatalogInstallsTheSelectedZipInOnlyTheOpenedProfile()
    {
        using var ws = new TestWorkspace();
        var zip = SafePath.Join(ws.Paths.Root, "fixture.zip");
        WriteZip(zip, "pack.mcmeta", """{"pack":{"pack_format":34,"description":"Verified catalog pack"}}""");
        var bytes = await File.ReadAllBytesAsync(zip);
        var hash = Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant();
        const string download = "https://cdn.modrinth.com/data/Pack1234/versions/File1234/fixture.zip";
        var routes = new List<string>();
        using var transport = new StubHandler(request =>
        {
            var uri = request.RequestUri!;
            routes.Add(uri.AbsolutePath);
            return uri.AbsolutePath switch
            {
                "/v2/search" => Response("""{"hits":[{"project_id":"Pack1234","slug":"fixture-pack","title":"Fixture pack","description":"A test pack"}]}"""),
                "/v2/project/Pack1234" => Response("""{"id":"Pack1234","project_type":"resourcepack"}"""),
                "/v2/project/Pack1234/version" => Response(JsonSerializer.Serialize(new[]
                {
                    new { id = "File1234", project_id = "Pack1234", version_number = "4.2", name = "Fixture",
                        date_published = "2026-01-01T00:00:00Z", version_type = "release", game_versions = new[] { "1.21.1" },
                        loaders = new[] { "minecraft" }, dependencies = Array.Empty<object>(),
                        files = new[] { new { filename = "fixture.zip", url = download, hashes = new { sha512 = hash }, size = bytes.Length } } }
                })),
                "/data/Pack1234/versions/File1234/fixture.zip" => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) },
                _ => throw new InvalidOperationException("Unexpected test route: " + uri.AbsolutePath)
            };
        });
        using var services = new LauncherServices(ws.Paths, coreTransport: transport);
        await services.Store.InitializeAsync();
        var first = ws.Profile() with { Name = "First" };
        var second = ws.Profile() with { Name = "Second" };
        await services.Store.SaveProfileAsync(first);
        await services.Store.SaveProfileAsync(second);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.OpenProfileWorkspace(first.Id);
        vm.ChooseWorkspaceSection(ProfileSection.ResourcePacks);
        var dialog = new ContentCatalogDialog(vm, first, ProfileContentKind.ResourcePacks);
        dialog.Show();
        try
        {
            var source = dialog.FindControl<ComboBox>("ContentSource")!;
            source.SelectedIndex = 1;
            Assert.True(dialog.FindControl<TextBlock>("ContentSourceUnavailable")!.IsVisible);
            Assert.Empty(routes);
            source.SelectedIndex = 0;
            dialog.FindControl<TextBox>("ContentQuery")!.Text = "fixture";
            dialog.FindControl<Button>("ContentSearch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var results = dialog.FindControl<ListBox>("ContentResults")!;
            await WaitForAsync(() => results.ItemCount == 1 && vm.IsIdle);
            results.SelectedIndex = 0;
            var install = dialog.FindControl<Button>("ContentInstall")!;
            await WaitForAsync(() => install.IsEnabled && vm.IsIdle);
            Assert.Equal("4.2", Assert.IsType<CatalogModVersion>(dialog.FindControl<ComboBox>("ContentVersions")!.SelectedItem).Number);
            Assert.Equal("fixture.zip", dialog.FindControl<TextBlock>("ContentFile")!.Text);
            install.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => vm.IsIdle && vm.WorkspaceContentRows.Count == 1);
            Assert.True(vm.LastOperationSucceeded, vm.Status);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(SafePath.Join(services.Content.Folder(first.Id, ProfileContentKind.ResourcePacks), "fixture.zip")));
            Assert.Empty(Directory.GetFiles(services.Content.Folder(second.Id, ProfileContentKind.ResourcePacks)));
            Assert.Equal(new[] { "/v2/search", "/v2/project/Pack1234", "/v2/project/Pack1234/version", "/data/Pack1234/versions/File1234/fixture.zip" }, routes);
        }
        finally { if (dialog.IsVisible) dialog.Close(); }
    }

    private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 250 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task ShaderAndWorldActionsLeaveTheOtherProfileAndOriginalCopyUntouched()
    {
        using var ws = new TestWorkspace();
        using var destination = new OwnedTestRoot("world-copy");
        var first = ws.Profile();
        var second = ws.Profile();
        ws.Paths.CreateProfile(first);
        ws.Paths.CreateProfile(second);
        var content = new ProfileContent(ws.Paths);
        var source = SafePath.Join(ws.Paths.Root, "shader.zip");
        WriteZip(source, "shaders/final.fsh", "void main() {}");
        var bytes = await File.ReadAllBytesAsync(source);
        await Assert.ThrowsAsync<InvalidDataException>(() => content.ImportAsync(first.Id, ProfileContentKind.Shaders,
            source, expectedSha512: new string('0', 128)));
        var shader = await content.ImportAsync(first.Id, ProfileContentKind.Shaders, source);
        Assert.Equal("shader.zip", shader.FileName);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(source));
        await Assert.ThrowsAsync<IOException>(() => content.ImportAsync(first.Id, ProfileContentKind.Shaders, source));
        Assert.Empty(await content.ListAsync(second.Id, ProfileContentKind.Shaders));

        var world = SafePath.Join(content.Folder(first.Id, ProfileContentKind.Worlds), "Fixture World");
        Directory.CreateDirectory(world);
        await File.WriteAllTextAsync(SafePath.Join(world, "level.dat"), "synthetic world");
        var entry = Assert.Single(await content.ListAsync(first.Id, ProfileContentKind.Worlds));
        var copy = content.PlanCopy(first.Id, ProfileContentKind.Worlds, entry, destination.Root);
        using (var cancellation = new CancellationTokenSource())
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => content.CopyAsync(copy, cancellation.Token));
        }
        Assert.False(Directory.Exists(copy.Target));
        await content.CopyAsync(copy);
        Assert.Equal("synthetic world", await File.ReadAllTextAsync(SafePath.Join(copy.Target, "level.dat")));
        Assert.Throws<IOException>(() => content.PlanCopy(first.Id, ProfileContentKind.Worlds, entry, destination.Root));
        var removal = content.PlanRemove(first.Id, ProfileContentKind.Worlds, entry);
        using (new ProfileUsage(ws.Paths).Acquire(first.Id))
            await Assert.ThrowsAnyAsync<Exception>(() => content.RemoveAsync(removal, true));
        Assert.True(Directory.Exists(world));
        await content.RemoveAsync(removal, true);
        Assert.False(Directory.Exists(world));
        Assert.Equal("synthetic world", await File.ReadAllTextAsync(SafePath.Join(removal.Backup!, "level.dat")));
        Assert.Equal("synthetic world", await File.ReadAllTextAsync(SafePath.Join(removal.Target, "level.dat")));
        Assert.Empty(await content.ListAsync(second.Id, ProfileContentKind.Worlds));
    }

    [AvaloniaFact]
    public async Task ResourcePackImportsOfflineIntoOpenedProfileAndRefreshesItsList()
    {
        using var ws = new TestWorkspace();
        using var services = new LauncherServices(ws.Paths, coreTransport: new StubHandler(_ => throw new IOException("Offline")));
        await services.Store.InitializeAsync();
        var target = ws.Profile() with { Name = "Resource target" };
        var other = ws.Profile() with { Name = "Other profile" };
        await services.Store.SaveProfileAsync(target);
        await services.Store.SaveProfileAsync(other);
        await services.Store.SetSettingAsync("network.offline", "True");
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.OpenProfileWorkspace(target.Id);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            window.FindControl<Button>("ProfileResourcePacks")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; vm.IsBusy && i < 200; i++) await Task.Delay(10);
            Assert.True(vm.WorkspaceResourcePacks);
            Assert.True(window.FindControl<Button>("WorkspaceAddPack")!.IsVisible);
            var source = SafePath.Join(ws.Paths.Root, "my pack.zip");
            WriteZip(source, "pack.mcmeta", """{"pack":{"pack_format":34,"description":{"text":"Clear ","extra":[{"text":"Sky"}]}}}""");
            var original = await File.ReadAllBytesAsync(source);
            await vm.ImportWorkspacePackAsync(target.Id, ProfileContentKind.ResourcePacks, source);
            Assert.True(vm.LastOperationSucceeded, vm.Status);
            Assert.Equal("Clear Sky", Assert.Single(vm.WorkspaceContentRows).Name);
            Assert.Equal("my pack.zip", vm.WorkspaceContentSelection?.FileName);
            Assert.Equal(original, await File.ReadAllBytesAsync(source));
            Assert.Equal(original, await File.ReadAllBytesAsync(SafePath.Join(ws.Paths.Game(target.Id), "resourcepacks/my pack.zip")));
            Assert.Empty(Directory.GetFiles(services.Content.Folder(other.Id, ProfileContentKind.ResourcePacks)));
        }
        finally { window.DataContext = null; window.Close(); }
    }

    [Theory]
    [InlineData("wrapper/pack.mcmeta")]
    [InlineData("../pack.mcmeta")]
    public async Task InvalidZipLayoutDoesNotLeaveAnInstalledPack(string entry)
    {
        using var ws = new TestWorkspace();
        var profile = ws.Profile();
        ws.Paths.CreateProfile(profile);
        var source = SafePath.Join(ws.Paths.Root, "invalid.zip");
        WriteZip(source, entry, """{"pack":{"pack_format":34,"description":"Example"}}""");
        var content = new ProfileContent(ws.Paths);
        var original = await File.ReadAllBytesAsync(source);
        await Assert.ThrowsAnyAsync<Exception>(() => content.ImportAsync(profile.Id, ProfileContentKind.ResourcePacks, source));
        Assert.Empty(Directory.GetFiles(content.Folder(profile.Id, ProfileContentKind.ResourcePacks)));
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
    }

    internal static void WriteZip(string path, string entry, string value)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var output = new StreamWriter(zip.CreateEntry(entry).Open());
        output.Write(value);
    }
}
