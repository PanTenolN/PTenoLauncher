using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Desktop.Dialogs;

public sealed partial class LocalServerWindow : Window
{
    private readonly MainViewModel vm;
    private readonly LocalServerCard card;
    private bool editing;

    internal LocalServerWindow(MainViewModel vm, LocalServerCard card)
    {
        this.vm = vm;
        this.card = card;
        InitializeComponent();
        Title = card.Server.Name;
        DataContext = card;
        ServerCommandInput.AddHandler(KeyDownEvent, OnCommandKey, RoutingStrategies.Tunnel);
        Closing += (_, e) => e.Cancel = editing;
    }

    private async void OnCommandKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await vm.SendServerCommandAsync(card);
    }

    private void ReturnToLauncher(object? sender, RoutedEventArgs e)
    {
        var owner = Owner as Window;
        Close();
        owner?.Activate();
    }

    private void ShowMinecraft(object? sender, RoutedEventArgs e)
    {
        vm.Page = LauncherPage.Home;
        ReturnToLauncher(sender, e);
    }

    private void ShowOverview(object? sender, RoutedEventArgs e) => ShowPanel(Overview);
    private void ShowMods(object? sender, RoutedEventArgs e) => ShowPanel(ModsPanel);
    private void ShowPlugins(object? sender, RoutedEventArgs e) => ShowPanel(PluginsPanel);
    private void ShowFolders(object? sender, RoutedEventArgs e) => ShowPanel(FoldersPanel);

    private void ShowPanel(Control selected)
    {
        foreach (var panel in new[] { Overview, ModsPanel, PluginsPanel, FoldersPanel })
            panel.IsVisible = panel == selected;
    }

    private async void CopyAddress(object? sender, RoutedEventArgs e)
    {
        try { if (Clipboard is { } clipboard) await clipboard.SetTextAsync(card.Server.Address); }
        catch (Exception error) { card.Status = TextCatalog.Get(error.Message); }
    }

    private async void Start(object? sender, RoutedEventArgs e)
    {
        if (!editing) await vm.StartServerAsync(card);
    }
    private async void Stop(object? sender, RoutedEventArgs e) => await vm.StopServerAsync(card);
    private void CancelPreparation(object? sender, RoutedEventArgs e) => card.Preparation?.Cancel();
    private async void Send(object? sender, RoutedEventArgs e) => await vm.SendServerCommandAsync(card);
    private void FollowConsole(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box) box.CaretIndex = box.Text?.Length ?? 0;
    }

    private async void OpenSettings(object? sender, RoutedEventArgs e) => await EditAsync(async () =>
    {
        if (!card.CanModify) return;
        var manager = vm.ServerManager;
        var settings = await manager.ReadSettingsAsync(card.Server);
        await new LocalServerSettingsDialog(manager, settings).ShowDialog(this);
        await vm.RefreshManagedServersAsync(card.Server.Id);
        Title = card.Server.Name;
    });

    private async void OpenPlayers(object? sender, RoutedEventArgs e) => await EditAsync(async () =>
    {
        if (!card.CanManagePlayers) return;
        var manager = vm.ServerManager;
        var players = await manager.ReadPlayerAccessAsync(card.Server, card.Session);
        await new LocalServerPlayersDialog(manager, card, players).ShowDialog(this);
    });

    private void OpenServerFolder(object? sender, RoutedEventArgs e) => OpenFolder(null);
    private void OpenModsFolder(object? sender, RoutedEventArgs e) => OpenFolder("mods");
    private void OpenPluginsFolder(object? sender, RoutedEventArgs e) => OpenFolder("plugins");

    private void OpenFolder(string? relative)
    {
        try
        {
            var folder = vm.ServerManager.DirectoryFor(card.Server.Id);
            if (relative is not null) folder = SafePath.Join(folder, relative);
            if (!Directory.Exists(folder)) { card.Status = "ServerFolderMissing"; return; }
            SafePath.RejectLinks(folder);
            using var explorer = Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception error) { card.Status = TextCatalog.Get(error.Message); }
    }

    private async void AddPlugin(object? sender, RoutedEventArgs e) => await EditAsync(async () =>
    {
        if (!card.CanModify || !card.Server.HasPlugins) return;
        var title = TextCatalog.Get("ServerAddPlugin");
        var files = await StorageProvider.OpenFilePickerAsync(new()
            { Title = title, FileTypeFilter = [new("JAR") { Patterns = ["*.jar"] }] });
        if (files.Count != 1 || files[0].TryGetLocalPath() is not { } file) return;
        var manager = vm.ServerManager;
        var plan = await manager.PreviewPluginAsync(card.Server, file);
        var dialog = new ContentActionDialog(title, title, TextCatalog.Get("ServerPluginConfirm"),
            plan.Source + Environment.NewLine + "SHA-256 " + plan.Sha256, plan.Target);
        if (!(await dialog.ShowDialog<bool?>(this)).HasValue) return;
        await manager.AddPluginAsync(plan);
        card.Status = "ServerPluginAdded";
    });

    private async void AddLocalMod(object? sender, RoutedEventArgs e) => await EditAsync(async () =>
    {
        if (!card.CanModify || !card.Server.CanInstallMods) return;
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = TextCatalog.Get("ServerAddLocalMod"),
            FileTypeFilter = [new("JAR") { Patterns = ["*.jar"] }]
        });
        if (files.Count == 1 && files[0].TryGetLocalPath() is { } path)
            await ConfirmModsAsync(await vm.ServerManager.PreviewLocalModAsync(card.Server, path));
    });

    private async void AddModrinthMod(object? sender, RoutedEventArgs e) => await EditAsync(async () =>
    {
        if (!card.CanModify || !card.Server.CanInstallMods) return;
        var catalog = vm.Services.Modrinth;
        var loader = card.Server.Core == ServerCore.Forge ? PTenoLauncher.Core.ModLoader.Forge : PTenoLauncher.Core.ModLoader.Fabric;
        var choice = await new ServerModrinthDialog(catalog, card.Server.Version, loader).ShowDialog<PTenoLauncher.Core.CatalogModVersion?>(this);
        if (choice is not null)
            await ConfirmModsAsync(await vm.ServerManager.PreviewModrinthModAsync(card.Server, catalog, choice.ProjectId, choice.Id));
    });

    private async Task ConfirmModsAsync(ServerModInstallPlan plan)
    {
        if (plan.Files.Count == 0)
        {
            card.Status = plan.Excluded.Count == 0 ? "ServerModAlreadyInstalled" : string.Join(Environment.NewLine, plan.Excluded.Select(mod =>
                mod.Title + " · " + TextCatalog.Get(mod.Disposition == ServerModDisposition.Client ? "ServerModClient" : "ServerModReview")));
            return;
        }
        var title = TextCatalog.Get("ServerModInstall");
        var files = string.Join(Environment.NewLine, plan.Files.Select(file => file.Title + " → " + Path.GetFileName(file.Target) +
            (file.RequiresReview ? " · " + TextCatalog.Get("ServerModReview") : "")));
        var dialog = new ContentActionDialog(title, title, TextCatalog.Get("ServerModConfirm"), files, Path.GetDirectoryName(plan.Files[0].Target)!);
        if (plan.Files.Any(file => file.RequiresReview))
            dialog.RequireOptionalAction(TextCatalog.Get("ServerForgeCopyReviewed"));
        var review = await dialog.ShowDialog<bool?>(this);
        if (!review.HasValue) return;
        await vm.ServerManager.ApplyServerModsAsync(plan, reviewedForgeMods: review.Value);
        card.Status = "ServerModsAdded";
    }

    private async void Delete(object? sender, RoutedEventArgs e)
    {
        var deleted = false;
        await EditAsync(async () =>
        {
            if (!card.CanModify) return;
            var manager = vm.ServerManager;
            var plan = await manager.PreviewDeletionAsync(card.Server);
            var title = TextCatalog.Get("ServerDelete");
            var summary = TextCatalog.Get("ServerDeleteSummary");
            if (plan.HasWorld) summary += Environment.NewLine + string.Join(", ", plan.Worlds);
            var dialog = new ContentActionDialog(title, title, card.Server.Name, summary, plan.Folder);
            if (plan.HasWorld) dialog.OfferWorldBackup();
            if (await dialog.ShowDialog<bool?>(this) is not { } backup) return;
            var path = await manager.DeleteAsync(plan, backup);
            await vm.RefreshManagedServersAsync(card.Server.Id);
            vm.ShowServerDeleted(path);
            deleted = true;
        });
        if (deleted) ReturnToLauncher(sender, e);
    }

    private async Task EditAsync(Func<Task> action)
    {
        if (editing) return;
        editing = true;
        try { await action(); }
        catch (Exception error)
        {
            vm.Services.Log.Error("server.manage", error);
            var parts = error.Message.Split(Environment.NewLine, 2);
            card.Status = TextCatalog.Get(parts[0]) + (parts.Length == 2 ? Environment.NewLine + parts[1] : "");
        }
        finally { editing = false; }
    }
}
