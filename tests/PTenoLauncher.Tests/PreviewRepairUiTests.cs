using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Diagnostics;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class PreviewRepairUiTests
{
    [AvaloniaFact]
    public async Task StartupUsesTheSelectedDatabaseAndReportsCorruptionSeparately()
    {
        using var ws = new TestWorkspace();
        var installed = new LauncherPaths(Path.Combine(ws.Paths.Root, "installed"));
        using (var before = new LauncherServices(installed))
        {
            await before.Store.InitializeAsync();
            await before.Store.SaveProfileAsync(ws.Profile(ModLoader.Fabric) with { Name = "Original T1" });
        }
        var app = Path.Combine(ws.Paths.Root, "new-version");
        Directory.CreateDirectory(app);
        Assert.Equal(installed.Root, LauncherPaths.Resolve([], app, installed.Root).Root);
        await File.WriteAllTextAsync(Path.Combine(app, "portable.flag"), "");
        var portable = LauncherPaths.Resolve([], app, installed.Root);
        Assert.Equal(Path.Combine(app, "portable-data"), portable.Root);
        Assert.Throws<ArgumentException>(() => LauncherPaths.Resolve(["--data-root", "--skip-update-check"], app, installed.Root));
        using var updated = new LauncherServices(LauncherPaths.Resolve(["--data-root", installed.Root], app, installed.Root));
        var vm = new MainViewModel(updated);
        await vm.InitializeAsync();
        Assert.Equal("Original T1", Assert.Single(vm.Profiles).Name);
        Assert.False(Directory.Exists(portable.Root));

        using var broken = new LauncherServices(portable);
        await File.WriteAllTextAsync(portable.Database, "synthetic invalid SQLite");
        var failed = new MainViewModel(broken);
        await failed.InitializeAsync();
        var window = new MainWindow { DataContext = failed };
        window.Show();
        try
        {
            Assert.True(window.FindControl<TextBlock>("DataLoadError")!.IsVisible);
            Assert.False(window.FindControl<Button>("HeaderSignInButton")!.IsVisible);
            Assert.False(failed.NoProfile);
            Assert.Contains(portable.Root, failed.Status);
            Assert.Equal("synthetic invalid SQLite", await File.ReadAllTextAsync(portable.Database));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ControlledProcessDrivesTheFooterAndConsoleThroughTheSameLaunch()
    {
        using var ws = new TestWorkspace();
        using var services = new LauncherServices(ws.Paths);
        await services.Store.InitializeAsync();
        var profile = ws.Profile();
        await services.Store.SaveProfileAsync(profile);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            var permission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"));
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "exit 7" }) start.ArgumentList.Add(arg);
            var launch = vm.RunAsync(ct => vm.RunProfileConsoleAsync(profile.Id, async (output, started, _, token) =>
            {
                await permission.Task.WaitAsync(token);
                return await LiveProcessRunner.RunAsync(start, Path.Combine(ws.Paths.Root, "launch.log"), async _ =>
                {
                    started();
                    await Task.Delay(500, token);
                }, output, ct: token);
            }, ct));
            Assert.Equal(TextCatalog.Get("WorkspaceStarting"), vm.Status);
            Assert.True(window.FindControl<ProgressBar>("OperationProgress")!.IsVisible);
            permission.TrySetResult();
            await WaitForAsync(() => vm.GameProcessStarted);
            Assert.True(vm.IsGameLaunchActive);
            Assert.False(window.FindControl<ProgressBar>("OperationProgress")!.IsVisible);
            Assert.Equal(vm.ProfileConsoleStatus, window.FindControl<TextBlock>("StatusText")!.Text);
            await launch.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(vm.GameProcessStarted);
            Assert.False(vm.IsGameLaunchActive);
            Assert.Equal(TextCatalog.Format("GameExit", 7), vm.ProfileConsoleStatus);
            Assert.Equal(vm.ProfileConsoleStatus, vm.Status);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalCartCanVerifyAnAliasOrInstallOnlyOffline(bool manualOnly)
    {
        using var ws = new TestWorkspace();
        var rootJar = Jar("discord_test", "1.0.0", [], new() { ["fabric"] = ">=0.84" });
        var provider = Jar("fabric-api", "0.110.0", ["fabric"], []);
        var hash = Convert.ToHexStringLower(SHA512.HashData(provider));
        var version = new
        {
            id = "ProviderVersion", project_id = "Provider", version_number = "0.110.0", name = "Verified Fabric API",
            date_published = "2026-01-01T00:00:00Z", version_type = "release", game_versions = new[] { "1.21.1" },
            loaders = new[] { "fabric" }, dependencies = Array.Empty<object>(),
            files = new[] { new { filename = "provider.jar", size = provider.Length, url = "https://cdn.modrinth.com/data/provider.jar", hashes = new { sha512 = hash } } }
        };
        using var transport = new StubHandler(request =>
        {
            Assert.False(manualOnly);
            return request.RequestUri!.AbsolutePath switch
            {
                "/data/provider.jar" => new(HttpStatusCode.OK) { Content = new ByteArrayContent(provider) },
                "/v2/project/fabric-api" or "/v2/project/Provider" => Json(new { id = "Provider", slug = "fabric-api", title = "Fabric API", project_type = "mod", client_side = "required" }),
                "/v2/project/Provider/version" => Json(new[] { version }),
                "/v2/version/ProviderVersion" => Json(version),
                _ => Json(null)
            };
        });
        using var services = new LauncherServices(ws.Paths, coreTransport: transport);
        await services.Store.InitializeAsync();
        var profile = ws.Profile(ModLoader.Fabric);
        await services.Store.SaveProfileAsync(profile);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.Offline = manualOnly;
        var input = Path.Combine(ws.Paths.Root, "discord-test.jar");
        await File.WriteAllBytesAsync(input, rootJar);
        await vm.AddModsAsync([input]);
        vm.SelectLocalImportMode(manualOnly);
        var dialog = new ModCatalogDialog(vm, profile, review: true);
        dialog.Show();
        try
        {
            await WaitForAsync(() => vm.IsIdle && vm.LastOperationSucceeded);
            if (manualOnly) Assert.Contains("fabric", vm.CartDependencyFindings);
            else Assert.Equal("provider.jar", Assert.Single(vm.CartRequired).Version.File.Name);
            var install = dialog.FindControl<Button>("InstallCombinedCartButton")!;
            Assert.False(install.IsEnabled);
            dialog.FindControl<CheckBox>("UnknownModAcceptance")!.IsChecked = true;
            Assert.True(install.IsEnabled);
            install.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => vm.IsIdle && !dialog.IsVisible);
            Assert.Equal(manualOnly ? 1 : 2, Directory.GetFiles(Path.Combine(ws.Paths.Game(profile.Id), "mods"), "*.jar").Length);
            Assert.Equal(rootJar, await File.ReadAllBytesAsync(input));
            if (manualOnly) Assert.Equal(0, transport.Requests);
        }
        finally { if (dialog.IsVisible) dialog.Close(); }
    }

    private static HttpResponseMessage Json(object? value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    private static byte[] Jar(string id, string version, string[] provides, Dictionary<string, string> depends)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        using (var entry = zip.CreateEntry("fabric.mod.json").Open())
            JsonSerializer.Serialize(entry, new { schemaVersion = 1, id, version, name = id, provides, depends });
        return buffer.ToArray();
    }
    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int i = 0; i < 800 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }
}
