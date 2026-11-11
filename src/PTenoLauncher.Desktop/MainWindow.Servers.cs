using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Minecraft;

namespace PTenoLauncher.Desktop;

public sealed partial class MainWindow
{
    private async void OpenAternosChecklist(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { CanEditServer: true } vm)
            await new AternosChecklistDialog(vm.Services, vm.Profiles.ToArray(), vm.LocalServers, vm.SelectedProfile?.Id).ShowDialog(this);
    }

    private async void RefreshServerVersions(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { CanEditServer: true, Offline: false } vm) await vm.ReloadServerChoicesAsync(refresh: true);
    }

    private async void CreateServer(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanCreateServer: true } vm) return;
        if (!vm.ServerFromProfile) { await vm.CreateServerAsync(); return; }
        if (await vm.PreviewServerProfileAsync() is not { } plan) return;
        var preview = new List<string>
        {
            plan.World is null ? TextCatalog.Get("ServerNewWorld") : plan.World + " · " +
                (plan.WorldVersion?.Name ?? TextCatalog.Get("ServerWorldUnknown")),
            ""
        };
        foreach (var disposition in Enum.GetValues<ServerModDisposition>())
        {
            preview.Add(TextCatalog.Get("ServerMod" + disposition));
            var files = plan.Mods.Where(mod => mod.Disposition == disposition).Select(mod =>
                string.Join(" · ", new[] { mod.File, mod.Version, mod.Reason,
                    mod.CopyAfterReview ? TextCatalog.Get("ServerForgeCopyCandidate") : null }.Where(value => !string.IsNullOrWhiteSpace(value)))).ToArray();
            preview.AddRange(files.Length == 0 ? ["—"] : files);
            preview.Add("");
        }
        preview.AddRange(plan.Blockers.Select(TextCatalog.Get));
        if (plan.ForgeCopyBlockers is { Count: > 0 } copyBlockers)
        {
            preview.Add(TextCatalog.Get("ServerForgeCopyBlocked"));
            preview.AddRange(copyBlockers.Select(TextCatalog.Get));
        }
        var title = TextCatalog.Get("ServerCreate");
        var dialog = new ContentActionDialog(title, title, plan.Profile.Name + " · " + plan.Profile.MinecraftVersion +
            " · " + plan.Core + " " + plan.Build, string.Join(Environment.NewLine, preview),
            vm.NewServerName + " · " + vm.NewServerMemory + " MiB · localhost:" + vm.NewServerPort);
        dialog.FindControl<Button>("ConfirmAction")!.IsEnabled = plan.Blockers.Count == 0;
        if (plan.ForgeCopyBlockers is { Count: 0 } && plan.Mods.Any(mod => mod.CopyAfterReview))
            dialog.OfferOptionalAction(TextCatalog.Get("ServerForgeCopyReviewed"));
        var choice = await dialog.ShowDialog<bool?>(this);
        if (choice.HasValue && vm.CanCreateServer) await vm.CreateServerAsync(plan, copyReviewedForgeMods: choice.Value);
    }

    private void ServerFromScratch(object? sender, RoutedEventArgs e)
    { if (DataContext is MainViewModel vm) vm.ServerFromProfile = false; }

    private void ServerFromProfile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.ServerFromProfile = true;
        vm.RefreshServerWorlds();
    }

    private async void ServerCoreChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count != 0 && e.AddedItems[0] is ServerCoreOption core &&
            DataContext is MainViewModel { CanEditServer: true, IsServers: true } vm)
        {
            vm.NewServerCore = core;
            await vm.ReloadServerChoicesAsync();
        }
    }

    private async void ServerVersionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count != 0 && e.AddedItems[0] is PTenoLauncher.Core.VersionEntry version &&
            DataContext is MainViewModel { CanEditServer: true, IsServers: true } vm)
        {
            vm.NewServerVersion = version;
            await vm.ReloadServerBuildsAsync();
        }
    }

    private void ServerProfileChanged(object? sender, SelectionChangedEventArgs e)
    { if (DataContext is MainViewModel vm) vm.RefreshServerWorlds(); }

    private void OpenServerEula(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        try { using var browser = Process.Start(new ProcessStartInfo(VanillaServerManager.EulaUrl) { UseShellExecute = true }); }
        catch (Exception error) { vm.Status = error.Message; }
    }

    private async void AcceptServerEula(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanEditServer: true, ShowServerEula: true } vm) return;
        var title = TextCatalog.Get("ServerAcceptEula");
        var dialog = new ContentActionDialog(title, title, TextCatalog.Get("ServerEulaConfirm"),
            VanillaServerManager.EulaUrl, TextCatalog.Get("ServerLocalVanilla"));
        if ((await dialog.ShowDialog<bool?>(this)).HasValue && vm.CanEditServer && vm.ShowServerEula)
            await vm.AcceptServerEulaAsync();
    }

    private readonly Dictionary<Guid, LocalServerWindow> serverWindows = [];

    private void OpenServerCard(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not Button { DataContext: LocalServerCard card }) return;
        if (serverWindows.TryGetValue(card.Server.Id, out var open))
        {
            open.Activate();
            return;
        }
        var window = new LocalServerWindow(vm, card);
        var id = card.Server.Id;
        serverWindows.Add(id, window);
        window.Closed += (_, _) => serverWindows.Remove(id);
        window.Show(this);
    }
}
