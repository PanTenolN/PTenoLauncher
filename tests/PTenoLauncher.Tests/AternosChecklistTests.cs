using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class AternosChecklistTests
{
    [AvaloniaFact]
    public async Task SavedServerNameAndAddressReopenAndCopyWithOneClick()
    {
        using var ws = new TestWorkspace();
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using (var services = new LauncherServices(ws.Paths))
        {
            await services.Store.InitializeAsync();
            var dialog = new AternosChecklistDialog(services, [], [], null);
            dialog.Show();
            try
            {
                Control<TextBox>(dialog, "AternosSavedName").Text = "  My test server  ";
                Control<TextBox>(dialog, "AternosStatusAddress").Text = " fixture.aternos.me:23456 ";
                var save = Control<Button>(dialog, "SaveAternosServer");
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                while (!save.IsEnabled) await Task.Delay(10, ct.Token);
                Assert.Equal(TextCatalog.Get("AternosSavedAddressDone"), Control<TextBlock>(dialog, "AternosStatusResult").Text);
            }
            finally { dialog.Close(); }
        }
        using var reopened = new LauncherServices(new LauncherPaths(ws.Paths.Root));
        await reopened.Store.InitializeAsync();
        reopened.Network.Offline = true;
        var second = new AternosChecklistDialog(reopened, [], [], null);
        second.Show();
        try
        {
            var copy = Control<Button>(second, "AternosCopyServerName");
            while (!copy.IsEnabled) await Task.Delay(10, ct.Token);
            Assert.Equal("My test server", Control<TextBox>(second, "AternosSavedName").Text);
            Assert.Equal("fixture.aternos.me:23456", Control<TextBox>(second, "AternosStatusAddress").Text);
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("My test server", await second.Clipboard!.TryGetTextAsync());
            Control<Button>(second, "AternosStatusCheck").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(TextCatalog.Get("AternosStatusOffline"), Control<TextBlock>(second, "AternosStatusResult").Text);
        }
        finally { second.Close(); }

        static T Control<T>(Window window, string name) where T : Control =>
            window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
    }

    [Fact]
    public async Task LocalSourcesStaySeparateAndProjectLinksRequireAnExactFileMatch()
    {
        using var ws = new TestWorkspace();
        await ws.Store.InitializeAsync();
        var profile = ws.Profile(ModLoader.Fabric) with { Name = "Aternos test", PreferredLoaderVersion = "0.20.1" };
        await ws.Store.SaveProfileAsync(profile);
        var game = ws.Paths.Game(profile.Id);
        var mod = Archive(game, "mods/Server.jar", "fabric.mod.json",
            """{"schemaVersion":1,"id":"servermod","name":"Server Example","version":"1.0","environment":"server","depends":{"fabricloader":">=0.20.1"}}""");
        Archive(game, "mods/Client.jar", "fabric.mod.json",
            """{"schemaVersion":1,"id":"clientmod","name":"Client Example","version":"1.1","environment":"client"}""");
        Archive(game, "mods/Unknown.jar", "README.txt", "Unidentified");
        Archive(game, "mods/Disabled.jar.disabled", "fabric.mod.json", """{"schemaVersion":1,"id":"disabled","version":"1.0"}""");
        Archive(game, "resourcepacks/Resource.zip", "pack.mcmeta", """{"pack":{"description":"Pack Example","pack_format":34}}""");
        Archive(game, "shaderpacks/Shader.zip", "shaders/fixture.fsh", "#version 120");
        var modBytes = await File.ReadAllBytesAsync(mod);
        var hash = Convert.ToHexStringLower(SHA512.HashData(modBytes));
        using var transport = new StubHandler(request =>
        {
            Assert.Equal("api.modrinth.com", request.RequestUri!.Host);
            Assert.Null(request.Headers.Authorization);
            var body = request.RequestUri.PathAndQuery switch
            {
                var route when route == $"/v2/version_file/{hash}?algorithm=sha512" => JsonSerializer.SerializeToUtf8Bytes(new
                {
                    id = "VersionId", project_id = "ProjectId", version_number = "1.0+modrinth", name = "Matching release",
                    date_published = DateTimeOffset.UtcNow, version_type = "release", game_versions = new[] { "1.21.1" },
                    loaders = new[] { "fabric" }, dependencies = Array.Empty<object>(),
                    files = new[] { new { filename = "Server.jar", url = "https://cdn.modrinth.com/data/ProjectId/Server.jar",
                        size = modBytes.Length, hashes = new { sha512 = hash }, primary = true } }
                }),
                "/v2/project/ProjectId" => """{"id":"ProjectId","slug":"server-example","project_type":"mod","status":"approved"}"""u8.ToArray(),
                _ => throw new InvalidOperationException("Unexpected catalog lookup")
            };
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        });
        using var services = new LauncherServices(ws.Paths, coreTransport: transport);
        var service = new AternosChecklistService(ws.Paths, services.Mods, services.Content, services.Modrinth, services.Servers);
        var checklist = await service.FromProfileAsync(profile);
        Assert.Equal("0.20.1", checklist.Build);
        Assert.Equal(0, transport.Requests);
        Assert.Equal(5, checklist.Items.Count);
        Assert.Equal(AternosItemKind.ClientMod, checklist.Items.Single(item => item.File == "Client.jar").Kind);
        var serverMod = checklist.Items.Single(item => item.File == "Server.jar");
        Assert.Equal(AternosItemKind.ServerMod, serverMod.Kind);
        Assert.Contains("fabricloader", serverMod.Dependencies);
        Assert.True(checklist.Items.Single(item => item.File == "Unknown.jar").NeedsReview);
        Assert.Contains(checklist.Items, item => item.Kind == AternosItemKind.ResourcePack);
        Assert.Contains(checklist.Items, item => item.Kind == AternosItemKind.Shader);

        var identified = await service.MatchProjectAsync(serverMod, checklist.Minecraft, checklist.Software);
        Assert.Equal("1.0+modrinth", identified.Version);
        Assert.Equal("https://modrinth.com/mod/server-example", identified.Project?.AbsoluteUri);
        Assert.Equal(2, transport.Requests);
        Assert.Null((await service.MatchProjectAsync(serverMod, "1.20.1", checklist.Software)).Project);

        var paper = new LocalVanillaServer(Guid.NewGuid(), "Paper fixture", "1.21.1", 25565)
            { Core = ServerCore.Paper, Build = "133" };
        var folder = services.Servers.DirectoryFor(paper.Id);
        await AtomicFile.WriteJsonAsync(SafePath.Join(folder, "server.json"), paper);
        Archive(folder, "plugins/Example.jar", "plugin.yml", "name: PluginExample\nversion: '1.2'\ndepend: [Library]\n");
        var plugins = await service.FromPluginServerAsync(paper);
        Assert.Equal("133", plugins.Build);
        var item = Assert.Single(plugins.Items);
        Assert.Equal(AternosItemKind.Plugin, item.Kind);
        Assert.Equal("1.2", item.Version);
        Assert.Equal("Library", Assert.Single(item.Dependencies));
    }

    [Theory]
    [InlineData(AternosFormat.Txt)]
    [InlineData(AternosFormat.Csv)]
    [InlineData(AternosFormat.Json)]
    public void ExportIncludesOnlySelectedSafeMetadata(AternosFormat format)
    {
        var first = new AternosItem(AternosItemKind.ServerMod, "Example.jar", "=PotentialFormula",
            "1.0", ["BaseMod", @"C:\Users\private\settings.json", "accessToken: privateToken"], false,
            new("https://modrinth.com/mod/example?token=privateToken"), @"C:\Users\private\Example.jar", Installed: true);
        var skip = new AternosItem(AternosItemKind.Plugin, "Excluded.jar", "Excluded", null, [], true, null, null, Included: false);
        var includedLink = new AternosItem(AternosItemKind.ClientMod, "Client.jar", "Client", "2.0", [], false,
            new("https://modrinth.com/mod/example-client"), null);
        var checklist = new AternosChecklist("Source", "1.21.1", "Fabric", "0.20.1", [first, skip, includedLink]);
        var text = Encoding.UTF8.GetString(AternosChecklistService.Export(checklist, format));
        Assert.Contains("BaseMod", text);
        Assert.Contains("Example.jar", text);
        Assert.Contains("unverified", text);
        Assert.Contains("https://modrinth.com/mod/example-client", text);
        Assert.DoesNotContain("Excluded", text);
        Assert.DoesNotContain("privateToken", text);
        Assert.DoesNotContain("Users", text);
        if (format == AternosFormat.Csv) Assert.Contains("'=PotentialFormula", text);
        if (format == AternosFormat.Txt) Assert.Contains("[x]", text);
        if (format == AternosFormat.Json)
        {
            using var json = JsonDocument.Parse(text);
            Assert.True(json.RootElement.GetProperty("items")[0].GetProperty("installed").GetBoolean());
        }
    }

    [AvaloniaFact]
    public async Task ChecklistSearchAndCategoryFilterCanBeCombined()
    {
        using var ws = new TestWorkspace();
        await ws.Store.InitializeAsync();
        var profile = ws.Profile(ModLoader.Fabric);
        await ws.Store.SaveProfileAsync(profile);
        var game = ws.Paths.Game(profile.Id);
        Archive(game, "mods/Server.jar", "fabric.mod.json", """{"schemaVersion":1,"id":"servermod","name":"Searchable","version":"1.0","environment":"server"}""");
        Archive(game, "shaderpacks/Shader.zip", "shaders/fixture.fsh", "#version 120");
        using var services = new LauncherServices(ws.Paths);
        var dialog = new AternosChecklistDialog(services, [profile], [], profile.Id);
        dialog.Show();
        try
        {
            var list = dialog.GetVisualDescendants().OfType<ListBox>().Single(control => control.Name == "AternosRows");
            for (var attempt = 0; attempt < 200 && list.Items.Count < 2; attempt++) await Task.Delay(10);
            Assert.Equal(2, list.Items.Count);
            var search = dialog.GetVisualDescendants().OfType<TextBox>().Single(control => control.Name == "AternosSearchField");
            var categories = dialog.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "AternosCategoryPicker");
            search.Text = "searchable";
            Assert.Single(list.Items);
            categories.SelectedIndex = (int)AternosItemKind.Shader + 1;
            Assert.Empty(list.Items);
            search.Text = "";
            Assert.Single(list.Items);
        }
        finally { dialog.Close(); }
    }

    [AvaloniaFact]
    public async Task FilteredProjectNamesCopyExactlyWithMouseAndKeyboardWithoutChangingMarks()
    {
        using var ws = new TestWorkspace();
        await ws.Store.InitializeAsync();
        var profile = ws.Profile(ModLoader.Fabric);
        await ws.Store.SaveProfileAsync(profile);
        var game = ws.Paths.Game(profile.Id);
        Archive(game, "mods/fabric-api-0.116.12+1.21.1.jar", "fabric.mod.json",
            """{"schemaVersion":1,"id":"fabric-api","name":"Fabric API","version":"0.116.12+1.21.1","environment":"*"}""");
        Archive(game, "resourcepacks/bare-bones-1.21.zip", "pack.mcmeta",
            """{"pack":{"description":"Bare Bones","pack_format":34}}""");
        Archive(game, "shaderpacks/Complementary.zip", "shaders/fixture.fsh", "#version 120");
        using var services = new LauncherServices(ws.Paths);
        var paper = new LocalVanillaServer(Guid.NewGuid(), "Paper fixture", "1.21.1", 25565)
            { Core = ServerCore.Paper, Build = "133" };
        var folder = services.Servers.DirectoryFor(paper.Id);
        await AtomicFile.WriteJsonAsync(SafePath.Join(folder, "server.json"), paper);
        Archive(folder, "plugins/world-edit-7.3.jar", "plugin.yml", "name: WorldEdit\nversion: '7.3'\n");

        var dialog = new AternosChecklistDialog(services, [profile], [paper], profile.Id);
        dialog.Show();
        try
        {
            var search = dialog.GetVisualDescendants().OfType<TextBox>().Single(control => control.Name == "AternosSearchField");
            var categories = dialog.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "AternosCategoryPicker");
            var status = dialog.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Name == "AternosChecklistStatus");
            var clipboard = Assert.IsAssignableFrom<IClipboard>(dialog.Clipboard);
            var name = await FilterToAsync(AternosItemKind.ServerMod, "0.116.12", "Fabric API");
            var row = (StackPanel)name.GetVisualParent()!;
            var marks = row.GetVisualDescendants().OfType<CheckBox>().ToArray();
            Assert.Equal(2, marks.Length);
            marks[0].IsChecked = false;
            marks[1].IsChecked = true;
            Assert.Equal(TextCatalog.Get("AternosCopyName"), ToolTip.GetTip(name));

            var center = name.TranslatePoint(new Point(name.Bounds.Width / 2, name.Bounds.Height / 2), dialog);
            Assert.NotNull(center);
            dialog.MouseDown(center.Value, MouseButton.Left, RawInputModifiers.None);
            dialog.MouseUp(center.Value, MouseButton.Left, RawInputModifiers.None);
            Assert.Equal("Fabric API", await clipboard.TryGetTextAsync());
            Assert.Equal(TextCatalog.Get("AternosNameCopied"), status.Text);
            Assert.False(marks[0].IsChecked);
            Assert.True(marks[1].IsChecked);

            await CopyWithKeyboardAsync(AternosItemKind.ResourcePack, "bones", "Bare Bones");
            await CopyWithKeyboardAsync(AternosItemKind.Shader, "complementary", "Complementary");
            name = await FilterToAsync(AternosItemKind.ServerMod, "Fabric", "Fabric API");
            marks = ((StackPanel)name.GetVisualParent()!).GetVisualDescendants().OfType<CheckBox>().ToArray();
            Assert.False(marks[0].IsChecked);
            Assert.True(marks[1].IsChecked);
            marks[0].IsChecked = true;
            var copyList = dialog.GetVisualDescendants().OfType<Button>().Single(button =>
                Equals(button.Content, TextCatalog.Get("AternosCopy")));
            copyList.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("fabric-api-0.116.12+1.21.1.jar", await clipboard.TryGetTextAsync());

            var source = dialog.GetVisualDescendants().OfType<ComboBox>().Single(control => control != categories);
            source.SelectedIndex = 1;
            await CopyWithKeyboardAsync(AternosItemKind.Plugin, "7.3", "WorldEdit");

            async Task<Button> FilterToAsync(AternosItemKind kind, string query, string title)
            {
                search.Text = query;
                categories.SelectedIndex = (int)kind + 1;
                for (var attempt = 0; attempt < 200; attempt++)
                {
                    await Task.Delay(10);
                    var button = dialog.GetVisualDescendants().OfType<Button>().SingleOrDefault(button =>
                        button.Name == "AternosCopyProjectName" && button.Content is TextBlock text && text.Text == title);
                    if (button is { Bounds.Width: > 0 }) return button;
                }
                throw new InvalidOperationException($"No rendered Aternos name button for {title}.");
            }

            async Task CopyWithKeyboardAsync(AternosItemKind kind, string query, string title)
            {
                var button = await FilterToAsync(kind, query, title);
                Assert.True(button.Focus());
                dialog.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
                dialog.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
                Assert.Equal(title, await clipboard.TryGetTextAsync());
                Assert.Equal(TextCatalog.Get("AternosNameCopied"), status.Text);
            }
        }
        finally { dialog.Close(); }
    }

    private static string Archive(string folder, string relative, string name, string payload)
    {
        var path = SafePath.Join(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(payload);
        return path;
    }
}
