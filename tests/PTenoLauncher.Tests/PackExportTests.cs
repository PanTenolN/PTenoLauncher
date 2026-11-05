using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Modpacks;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class PackExportTests
{
    private const string Private = "Synthetic-personal-data-291815";
    private const string ArchiveName = "Friends pack";
    private const string LoaderVersion = "0.16.10";

    private sealed class Fixture : IDisposable
    {
        private readonly TestWorkspace ws = new();
        public LauncherPaths Paths { get; }
        public LauncherServices Services { get; }
        public GameProfile Profile { get; } = new(Guid.NewGuid(), Private, "1.21.1", ModLoader.Fabric, 4096, null, DateTimeOffset.UtcNow)
            { PreferredLoaderVersion = LoaderVersion };
        public byte[] Embedded { get; } = LicensedJar("embed");
        public byte[] Linked { get; } = MrpackFixture.Jar("linked");
        public byte[] NoLicense { get; } = MrpackFixture.Jar("no_license");
        public int Requests { get; private set; }
        public int Downloads { get; private set; }
        public string Archive => SafePath.Join(ws.Paths.Root, "friends.mrpack");
        public string Directory => ws.Paths.Root;
        public string Game => Paths.Game(Profile.Id);

        public Fixture()
        {
            Paths = new(SafePath.Join(ws.Paths.Root, "launcher"));
            Services = new(Paths, coreTransport: new StubHandler(Respond));
        }

        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            Requests++;
            var uri = request.RequestUri!;
            if (uri.Host == "cdn.modrinth.com")
            {
                Downloads++;
                Assert.Equal(Link("linked"), uri.AbsoluteUri);
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Linked) };
            }
            Assert.Equal("api.modrinth.com", uri.Host);
            foreach (var (id, data, license) in new[] { ("embed", Embedded, "CC0-1.0"), ("linked", Linked, "LicenseRef-All-Rights-Reserved"),
                ("nolicense", NoLicense, (string?)null) })
            {
                if (uri.AbsolutePath == "/v2/project/" + id)
                    return Json(new { id, slug = id, title = id, project_type = "mod", client_side = "required", license = new { id = license } });
                var hash = Convert.ToHexStringLower(SHA512.HashData(data));
                if (uri.AbsolutePath == "/v2/version_file/" + hash)
                    return Json(new
                    {
                        id = id + "v", project_id = id, name = id, version_number = "1.0.0", version_type = "release",
                        date_published = "2026-01-01T00:00:00Z", game_versions = new[] { "1.21.1" }, loaders = new[] { "fabric" },
                        dependencies = Array.Empty<object>(), files = new[]
                        { new { filename = id + ".jar", url = Link(id), size = data.Length, primary = true, hashes = new { sha512 = hash } } }
                    });
            }
            return new(HttpStatusCode.NotFound);
        }

        public async Task SeedAsync()
        {
            await Services.Store.InitializeAsync();
            await Services.Store.SaveProfileAsync(Profile);
            await Services.Store.SetSettingAsync("ui.language", "en");
            await File.WriteAllBytesAsync(SafePath.Join(Game, "mods/hash-prefix-embed.jar"), Embedded);
            await File.WriteAllBytesAsync(SafePath.Join(Game, "mods/hash-prefix-linked.jar"), Linked);
            await File.WriteAllBytesAsync(SafePath.Join(Game, "mods/no-license.jar"), NoLicense);
            await File.WriteAllBytesAsync(SafePath.Join(Game, "mods/local.jar"), MrpackFixture.Jar("local"));
            await File.WriteAllBytesAsync(SafePath.Join(Game, "mods/disabled.jar.disabled"), MrpackFixture.Jar("disabled"));
            await File.WriteAllTextAsync(SafePath.Join(Game, PackExporter.GraphicsPath),
                "{\"performance\":{\"use_entity_culling\":true,\"use_fog_occlusion\":\"" + Private + "\",\"account_token\":\"" + Private + "\"}}");
            foreach (var path in new[] { "saves/world.dat", "config/private.txt", "options.txt", "launcher_accounts.json",
                "logs/latest.log", "versions/1.21.1/client.jar", "resourcepacks/private.zip" })
            {
                var target = SafePath.Join(Game, path);
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllTextAsync(target, Private);
            }
        }

        public Task<PackExportPlan> Preview(bool shareGraphics = false) =>
            Services.PackExporter.PreviewAsync(Profile.Id, ArchiveName, shareGraphics);
        public static string Link(string id) => $"https://cdn.modrinth.com/data/{id}/versions/{id}v/{id}.jar";
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
        private static byte[] LicensedJar(string id)
        {
            using var bytes = new MemoryStream();
            bytes.Write(MrpackFixture.Jar(id));
            bytes.Position = 0;
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Update, leaveOpen: true))
            using (var writer = new StreamWriter(zip.CreateEntry("LICENSE").Open()))
                writer.WriteLine("SPDX-License-Identifier: CC0-1.0");
            return bytes.ToArray();
        }
        public void Dispose() { Services.Dispose(); ws.Dispose(); }
    }

    [Fact]
    public async Task ExportsPermittedBytesExactLinksAndManualItemsAndImportsAnIndependentProfile()
    {
        using var f = new Fixture();
        await f.SeedAsync();
        var plan = await f.Preview(shareGraphics: true);
        Assert.Equal("1.21.1", plan.Minecraft);
        Assert.Equal(LoaderVersion, plan.LoaderVersion);
        Assert.Equal(PackExportAction.Include, plan.Items.Single(i => i.Path == "mods/embed.jar").Action);
        Assert.Equal(PackExportAction.Link, plan.Items.Single(i => i.Path == "mods/linked.jar").Action);
        Assert.Equal("ExportUnknownSource", plan.Items.Single(i => i.Path == "mods/local.jar").Reason);
        Assert.Equal("ExportUnknownLicense", plan.Items.Single(i => i.Path == "mods/no-license.jar").Reason);
        Assert.Equal("ExportDisabled", plan.Items.Single(i => i.Path.EndsWith(".disabled", StringComparison.Ordinal)).Reason);
        await f.Services.PackExporter.ExportAsync(plan, f.Archive, true);
        Assert.Equal(0, f.Downloads);

        using (var zip = ZipFile.OpenRead(f.Archive))
        {
            Assert.Equal(new[] { "modrinth.index.json", "overrides/config/sodium-options.json", "overrides/mods/embed.jar" },
                zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
            var manifest = await ReadText(zip, "modrinth.index.json");
            Assert.DoesNotContain(Private, manifest);
            Assert.DoesNotContain(f.Profile.Id.ToString(), manifest);
            Assert.DoesNotContain(f.Paths.Root, manifest);
            Assert.DoesNotContain("disabled.jar", manifest);
            using var json = JsonDocument.Parse(manifest);
            var link = json.RootElement.GetProperty("files")[0];
            Assert.Equal(Fixture.Link("linked"), link.GetProperty("downloads")[0].GetString());
            Assert.Equal(Convert.ToHexStringLower(SHA512.HashData(f.Linked)), link.GetProperty("hashes").GetProperty("sha512").GetString());
            var metadata = json.RootElement.GetProperty("ptenoExport").GetProperty("files").EnumerateArray();
            var embedded = metadata.Single(e => e.GetProperty("path").GetString() == "mods/embed.jar");
            Assert.Equal("embedv", embedded.GetProperty("versionId").GetString());
            Assert.Equal(Convert.ToHexStringLower(SHA512.HashData(f.Embedded)), embedded.GetProperty("sha512").GetString());
            var graphics = await ReadText(zip, "overrides/config/sodium-options.json");
            Assert.DoesNotContain(Private, graphics);
            using var safeGraphics = JsonDocument.Parse(graphics);
            var option = Assert.Single(safeGraphics.RootElement.GetProperty("performance").EnumerateObject());
            Assert.Equal("use_entity_culling", option.Name);
            Assert.True(option.Value.GetBoolean());
        }

        var preview = await f.Services.Modpacks.ZipReader.PreviewAsync(f.Archive, new(ProfileName: "Friend", PinMods: true));
        Assert.True(preview.IsMrpack);
        Assert.Equal(new[] { "mods/local.jar", "mods/no-license.jar" }, preview.Plan.ManualFiles.Order(StringComparer.Ordinal));
        var result = await f.Services.Modpacks.ImportZipAsync(preview, true);
        Assert.NotEqual(f.Profile.Id, result.Profile.Id);
        Assert.Equal(f.Profile.MinecraftVersion, result.Profile.MinecraftVersion);
        Assert.Equal(f.Profile.Loader, result.Profile.Loader);
        Assert.Equal(LoaderVersion, result.Receipt.LoaderVersion);
        var game = f.Paths.Game(result.Profile.Id);
        Assert.Equal(f.Embedded, await File.ReadAllBytesAsync(SafePath.Join(game, "mods/embed.jar")));
        Assert.Equal(f.Linked, await File.ReadAllBytesAsync(SafePath.Join(game, "mods/linked.jar")));
        Assert.Equal(2, System.IO.Directory.GetFiles(SafePath.Join(game, "mods")).Length);
        Assert.Empty(System.IO.Directory.GetFiles(SafePath.Join(game, "saves")));
        Assert.False(File.Exists(f.Paths.Receipt(result.Profile.Id)));
        await File.WriteAllBytesAsync(SafePath.Join(game, "mods/embed.jar"), [1]);
        Assert.Equal(f.Embedded, await File.ReadAllBytesAsync(SafePath.Join(f.Game, "mods/hash-prefix-embed.jar")));
        Assert.Equal(1, f.Downloads);
    }

    [Fact]
    public async Task ConsentAndFreshSourceAreRequiredAndCancellationOrFailureNeverPublishPartialFiles()
    {
        using var f = new Fixture();
        await f.SeedAsync();
        var plan = await f.Preview();
        var exporter = f.Services.PackExporter;
        Assert.Equal("ExportConsent", (await Assert.ThrowsAsync<MrpackException>(() => exporter.ExportAsync(plan, f.Archive, false))).Code);
        var unknown = SafePath.Join(f.Game, "mods/local.jar");
        await File.WriteAllBytesAsync(unknown, MrpackFixture.Jar("changed_local"));
        Assert.Equal("ExportChanged", (await Assert.ThrowsAsync<MrpackException>(() => exporter.ExportAsync(plan, f.Archive, true))).Code);
        Assert.False(File.Exists(f.Archive));
        plan = await f.Preview();
        using var cancellation = new CancellationTokenSource();
        var didStart = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exporter.ExportAsync(plan, f.Archive, true, cancellation.Token,
            new CallbackProgress(p => { Assert.Equal("modrinth.index.json", p.Name); didStart = true; cancellation.Cancel(); })));
        Assert.True(didStart);
        Assert.False(File.Exists(f.Archive));
        Assert.Empty(System.IO.Directory.GetFiles(f.Directory, "*.part"));
        await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync(plan, f.Archive, true,
            progress: new CallbackProgress(_ => throw new IOException("Synthetic failure while writing"))));
        Assert.False(File.Exists(f.Archive));
        Assert.Empty(System.IO.Directory.GetFiles(f.Directory, "*.part"));

        var changed = false;
        var failure = await Assert.ThrowsAsync<MrpackException>(() => exporter.ExportAsync(plan, f.Archive, true,
            progress: new CallbackProgress(_ =>
            {
                if (changed) return;
                changed = true;
                File.WriteAllBytes(unknown, MrpackFixture.Jar("changed_during_write"));
            })));
        Assert.Equal("ExportChanged", failure.Code);
        Assert.False(File.Exists(f.Archive));
        Assert.Empty(System.IO.Directory.GetFiles(f.Directory, "*.part"));
    }

    [Fact]
    public async Task ExportReusesOfflineCatalogButRejectsTamperedEmbeddedBytesOnImport()
    {
        using var f = new Fixture();
        await f.SeedAsync();
        var plan = await f.Preview();
        var requests = f.Requests;
        f.Services.Network.Offline = true;
        await f.Services.PackExporter.ExportAsync(plan, f.Archive, true);
        Assert.Equal(requests, f.Requests);
        var preview = await f.Services.Modpacks.Reader.PreviewAsync(f.Archive);
        Assert.Contains(preview.Blockers, issue => issue.Code == "PackOfflineMissing" && issue.Detail == "mods/linked.jar");
        using (var zip = ZipFile.Open(f.Archive, ZipArchiveMode.Update))
        {
            zip.GetEntry("overrides/mods/embed.jar")!.Delete();
            using var output = zip.CreateEntry("overrides/mods/embed.jar").Open();
            var modified = f.Embedded.ToArray();
            modified[0] ^= 1;
            output.Write(modified);
        }
        Assert.Equal("PackIntegrity", (await Assert.ThrowsAsync<MrpackException>(() => f.Services.Modpacks.Reader.PreviewAsync(f.Archive))).Code);
    }

    [AvaloniaFact]
    public async Task WorkspaceActionPreviewsReconfirmsAndImportsUsingExistingControls()
    {
        using var f = new Fixture();
        await f.SeedAsync();
        var other = f.Profile with { Id = Guid.NewGuid(), Name = "Second profile" };
        await f.Services.Store.SaveProfileAsync(other);
        var vm = new MainViewModel(f.Services);
        await vm.InitializeAsync();
        vm.OpenProfileWorkspace(f.Profile.Id);
        var window = new MainWindow { DataContext = vm, Width = 980, Height = 680 };
        window.Show();
        try
        {
            Click(window.FindControl<Button>("ExportProfilePackButton")!);
            var dialog = Assert.Single(window.OwnedWindows.OfType<PackExportDialog>());
            var confirm = dialog.FindControl<Button>("ConfirmExport")!;
            Assert.False(confirm.IsEnabled);
            Click(dialog.FindControl<Button>("PreviewExport")!);
            await Until(() => confirm.IsEnabled);
            Assert.Contains("Fabric " + LoaderVersion, dialog.FindControl<TextBlock>("ExportTarget")!.Text);
            var rows = dialog.FindControl<ListBox>("ExportFiles")!.ItemsSource!.Cast<PackExportDialog.ExportRow>().ToArray();
            Assert.Contains(rows, r => r.Link == Fixture.Link("linked"));
            Assert.Contains(rows, r => r.File == "mods/local.jar" && r.Decision.Contains(TextCatalog.Get("ExportActionSkip")));
            Capture(dialog, "export-preview");
            await File.WriteAllBytesAsync(SafePath.Join(f.Game, "mods/new-local.jar"), MrpackFixture.Jar("another_local"));
            Assert.False(await dialog.SaveToAsync(f.Archive));
            Assert.False(File.Exists(f.Archive));
            Assert.Equal(TextCatalog.Get("ExportChanged"), dialog.FindControl<TextBlock>("ExportStatus")!.Text);
            Assert.True(confirm.IsEnabled);
            Assert.True(await dialog.SaveToAsync(f.Archive));
            dialog.Close();

            vm.Page = LauncherPage.Modpacks;
            await vm.ChooseZipPackAsync(f.Archive);
            Assert.Contains(vm.PackFiles, line => line.Contains("mods/new-local.jar"));
            var import = window.FindControl<ProfileImportView>("MainProfileImport")!;
            import.FindControl<CheckBox>("MrpackConsent")!.IsChecked = true;
            Click(import.FindControl<Button>("ImportMrpackButton")!);
            await Until(() => vm.IsIdle && vm.Profiles.Count == 3);
            Assert.NotEqual(f.Profile.Id, vm.SelectedProfile?.Id);
            Assert.NotEqual(other.Id, vm.SelectedProfile?.Id);
            Assert.Equal(LoaderVersion, await MrpackRequirements.RequestedAsync(f.Paths, vm.SelectedProfile!));
            Assert.Empty(System.IO.Directory.GetFiles(SafePath.Join(f.Paths.Game(other.Id), "mods")));
            Capture(window, "reimported-profile");
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.OfType<PackExportDialog>().ToArray()) dialog.Close();
            window.DataContext = null;
            window.Close();
            TextCatalog.Apply("uk");
        }
    }

    private static async Task<string> ReadText(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return await reader.ReadToEndAsync();
    }

    private sealed class CallbackProgress(Action<DownloadProgress> action) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => action(value);
    }

    private static void Click(Button button)
    {
        Assert.True(button.IsEnabled);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 800 && !done(); i++) await Task.Delay(10);
        Assert.True(done());
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("PTENO_PACK_EXPORT_SCREENSHOTS") is not { Length: > 0 } directory) return;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();
        using var image = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
        image.Render(window);
        image.Save(Path.Combine(directory, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
