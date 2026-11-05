using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Desktop;

public sealed partial class MainWindow
{
    private async void MoveWorkspaceFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsIdle: true, IsGameLaunchActive: false, WorkspaceProfileId: { } id } vm) return;
        ProfileFolderMove? plan = null;
        await vm.RunAsync(async ct =>
        {
            try { plan = await vm.Services.ProfileFolders.PreviewAsync(id, ct); }
            catch (IOException error) { throw new InvalidOperationException(TextCatalog.Get(error.Message), error); }
        });
        if (plan is not { } approved || vm.WorkspaceProfileId != id) return;
        var confirmation = new ContentActionDialog(TextCatalog.Get("ProfileFolderMove"), TextCatalog.Get("ProfileFolderMoveConfirm"),
            approved.Name + Environment.NewLine + TextCatalog.Get("ProfileFolderMoveHint"), approved.Source, approved.Target);
        if (!(await confirmation.ShowDialog<bool?>(this)).HasValue || vm.WorkspaceProfileId != id || vm.IsGameLaunchActive) return;
        await vm.RunAsync(async ct =>
        {
            try { await vm.Services.ProfileFolders.MoveAsync(approved, ct); }
            catch (IOException error) { throw new InvalidOperationException(TextCatalog.Get(error.Message), error); }
            vm.RelocateConsoleLog(approved);
            vm.Status = TextCatalog.Format("ProfileFolderMoved", approved.Target);
        });
    }

    private async void OpenWorkspaceContentCatalog(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { IsIdle: true, Offline: false, SelectedProfile: { } profile } vm &&
            vm.WorkspaceProfileId == profile.Id && vm.WorkspaceCanImportPack && vm.WorkspaceContentKind is { } kind)
            await new ContentCatalogDialog(vm, profile, kind).ShowDialog(this);
    }

    private void OpenWorkspaceContent(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.WorkspaceProfileId is not { } id || vm.WorkspaceContentKind is not { } kind) return;
        try
        {
            var folder = vm.Services.Content.Folder(id, kind);
            if (Directory.Exists(folder)) Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            else vm.Status = TextCatalog.Get("WorkspaceNoFolder");
        }
        catch (Exception ex) { vm.Status = ex.Message; }
    }

    private async void ChangeWorkspaceContent(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsIdle: true, IsGameLaunchActive: false, WorkspaceContentSelection: { } item } vm ||
            vm.WorkspaceProfileId is not { } id || vm.WorkspaceContentKind is not { } kind) return;
        var remove = sender is Button { Tag: "remove" };
        try
        {
            var directory = "";
            if (!remove)
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = TextCatalog.Get("ContentCopy"), AllowMultiple = false });
                if (folders.Count != 1 || folders[0].TryGetLocalPath() is not { } selected) return;
                directory = selected;
            }
            if (vm.WorkspaceProfileId != id || vm.WorkspaceContentKind != kind) return;
            var plan = remove ? vm.Services.Content.PlanRemove(id, kind, item) : vm.Services.Content.PlanCopy(id, kind, item, directory);
            var backup = await new ContentActionDialog(plan, remove).ShowDialog<bool?>(this);
            if (backup.HasValue) await vm.ApplyWorkspaceContentAsync(plan, remove, backup.Value);
        }
        catch (Exception ex) { vm.Status = TextCatalog.Get(ex.Message); }
    }

    private async void ChooseWorkspacePack(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsIdle: true, IsGameLaunchActive: false, WorkspaceCanImportPack: true } vm ||
            vm.WorkspaceProfileId is not { } id || vm.WorkspaceContentKind is not { } kind) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new()
            {
                Title = TextCatalog.Get("ContentAddFile"), AllowMultiple = false,
                FileTypeFilter = [new("ZIP") { Patterns = ["*.zip"] }]
            });
            if (files.Count == 1 && files[0].TryGetLocalPath() is { } file) await vm.ImportWorkspacePackAsync(id, kind, file);
        }
        catch (Exception ex) { vm.Status = ex.Message; }
    }
}
