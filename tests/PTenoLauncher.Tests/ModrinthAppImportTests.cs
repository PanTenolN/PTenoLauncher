using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Microsoft.Data.Sqlite;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Modpacks;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class ModrinthAppImportTests
{
    private const string Selected = "test-instance";

    private static string Source(MrpackFixture f, bool moved = false)
    {
        var root = Path.Combine(f.Workspace.Paths.Root, "modrinth-app-fixture");
        Directory.CreateDirectory(root);
        var db = Path.Combine(root, "app.db");
        Execute(db, """
            CREATE TABLE instances(id TEXT PRIMARY KEY,path TEXT,name TEXT,applied_content_set_id TEXT);
            CREATE TABLE instance_content_sets(id TEXT PRIMARY KEY,instance_id TEXT,game_version TEXT,loader TEXT,loader_version TEXT);
            CREATE TABLE credentials(secret TEXT);
            INSERT INTO credentials VALUES ('PRIVATE-TOKEN-FIXTURE');
            INSERT INTO instances VALUES ('test-instance','Local One','Local One','applied-set');
            INSERT INTO instances VALUES ('other-instance','Local Two','Local Two','other-set');
            INSERT INTO instance_content_sets VALUES ('applied-set','test-instance','1.21.1','fabric','0.16.10');
            INSERT INTO instance_content_sets VALUES ('unused-set','test-instance','1.20.4','quilt','0.28.1');
            INSERT INTO instance_content_sets VALUES ('other-set','other-instance','1.20.1','vanilla',NULL);
            """);
        var prefix = moved ? "moved/" : "profiles/";
        InstancePackTests.Put(root, prefix + "Local One/config/example.txt", [1, 2, 3]);
        InstancePackTests.Put(root, prefix + "Local One/mods/demo.jar", MrpackFixture.Jar());
        InstancePackTests.Put(root, prefix + "Local One/saves/world/level.dat", [9]);
        InstancePackTests.Put(root, prefix + "Local One/config/tokens.json", [9]);
        InstancePackTests.Put(root, prefix + "Local One/config/launch.cmd", [9]);
        InstancePackTests.Put(root, prefix + "Local Two/config/example.txt", [8]);
        return db;
    }

    private static void Execute(string file, string sql)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString());
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task ExplicitProfileImportsOnlyAllowedContentAndPreservesSourceAndExactTarget()
    {
        using var f = new MrpackFixture();
        await f.Workspace.Store.InitializeAsync();
        f.Network.Offline = true;
        var database = Source(f, moved: true);
        var prior = await File.ReadAllBytesAsync(database);
        var reader = f.Importer.ModrinthAppReader;
        var unselected = await reader.PreviewAsync(database, new());
        Assert.Equal(2, unselected.Choices.Count);
        Assert.Contains(unselected.Plan.Blockers, b => b.Code == "ModrinthAppChoose");
        Assert.Contains((await reader.PreviewAsync(database, new(Selected))).Plan.Blockers, b => b.Code == "ModrinthAppContent");
        var preview = await reader.PreviewAsync(database, new(Selected, "Independent", true, Path.Combine(Path.GetDirectoryName(database)!, "moved")));
        Assert.True(preview.Plan.CanImport, string.Join(", ", preview.Plan.Blockers));
        Assert.Equal(2, preview.Snapshot.Content.Count);
        Assert.Contains("saves", preview.Plan.Excluded);
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ImportModrinthAppAsync(preview, false));
        var result = await f.Importer.ImportModrinthAppAsync(preview, true);
        Assert.Equal("1.21.1", result.Profile.MinecraftVersion);
        Assert.Equal(ModLoader.Fabric, result.Profile.Loader);
        Assert.Equal("0.16.10", await MrpackRequirements.RequestedAsync(f.Workspace.Paths, result.Profile));
        Assert.Equal("local-modrinth-app", result.Receipt.SourceKind);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(f.Workspace.Paths.Game(result.Profile.Id), "config/example.txt")));
        Assert.Equal(2, result.Receipt.Files.Count);
        Assert.Equal(0, f.Requests);
        Assert.Equal(prior, await File.ReadAllBytesAsync(database));
        var provenance = File.ReadAllText(Path.Combine(f.Workspace.Paths.Profile(result.Profile.Id), "modrinth-app-target.json"));
        Assert.Contains(Selected, provenance);
        Assert.DoesNotContain("PRIVATE-TOKEN-FIXTURE", provenance);
        Assert.Empty(await f.Importer.PendingAsync());
    }

    [Fact]
    public async Task UnknownSchemaPendingWalAndChangedFilesCannotBeConfirmed()
    {
        using var f = new MrpackFixture();
        await f.Workspace.Store.InitializeAsync();
        var database = Source(f);
        var reader = f.Importer.ModrinthAppReader;
        var preview = await reader.PreviewAsync(database, new(Selected));
        Assert.True(preview.Plan.CanImport);
        InstancePackTests.Put(Path.GetDirectoryName(database)!, "profiles/Local One/config/example.txt", [2, 2, 2]);
        Assert.Equal("PackChanged", (await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ImportModrinthAppAsync(preview, true))).Code);
        await File.WriteAllBytesAsync(database + "-wal", [1]);
        Assert.Contains((await reader.PreviewAsync(database, new(Selected))).Plan.Blockers, b => b.Code == "ModrinthAppClose");
        var unknown = Path.Combine(f.Workspace.Paths.Root, "app.db");
        Execute(unknown, "CREATE TABLE unrelated(id TEXT)");
        Assert.Contains((await reader.PreviewAsync(unknown, new(Selected))).Plan.Blockers, b => b.Code == "ModrinthAppUnsupported");
        Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
    }

    [Fact]
    public async Task InterruptedPublicationUsesTheExistingRecoveryTransaction()
    {
        using var f = new MrpackFixture();
        await f.Workspace.Store.InitializeAsync();
        var preview = await f.Importer.ModrinthAppReader.PreviewAsync(Source(f), new(Selected));
        f.Importer.Fault = phase => { if (phase == "ready") throw new IOException("synthetic interruption"); };
        await Assert.ThrowsAsync<IOException>(() => f.Importer.ImportModrinthAppAsync(preview, true));
        Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
        Assert.Single(await f.Importer.PendingAsync());
        f.Importer.Fault = null;
        await f.Importer.RecoverAsync(true);
        var restored = Assert.Single(await f.Workspace.Store.GetProfilesAsync());
        Assert.Equal("0.16.10", await MrpackRequirements.RequestedAsync(f.Workspace.Paths, restored));
        Assert.Empty(await f.Importer.PendingAsync());
    }

    [AvaloniaFact]
    public async Task AppProfilePickerPreviewAndConsentDriveTheSharedImportView()
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
            await vm.ChooseModrinthAppAsync(Source(f));
            var picker = view.FindControl<ComboBox>("ModrinthAppProfile")!;
            var install = view.FindControl<Button>("ImportMrpackButton")!;
            Assert.True(picker.IsVisible);
            Assert.False(install.IsEnabled);
            picker.SelectedItem = vm.ModrinthAppChoices.Single(c => c.Id == Selected);
            view.FindControl<Button>("PreviewMrpackButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 400 && !vm.IsIdle; i++) await Task.Delay(10);
            Assert.False(install.IsEnabled);
            view.FindControl<CheckBox>("MrpackConsent")!.IsChecked = true;
            Assert.True(install.IsEnabled, string.Join(", ", vm.PackBlockers));
            install.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 400 && !vm.IsIdle; i++) await Task.Delay(10);
            Assert.True(vm.LastOperationSucceeded, vm.Status);
            Assert.Equal("Local One", Assert.Single(await services.Store.GetProfilesAsync()).Name);
        }
        finally { window.Close(); }
    }
}
