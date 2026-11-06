using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;

namespace PTenoLauncher.Desktop;

public sealed partial class MainWindow
{
    private async void MarkWorking(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { CanMarkWorking: true } vm) await vm.MarkWorkingAsync();
    }

    private async void ReviewWorking(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsIdle: true, IsGameLaunchActive: false, WorkspaceProfileId: { } id } vm) return;
        while (vm.WorkspaceProfileId == id && await vm.PreviewWorkingAsync(id) is { } preview)
        {
            var dialog = new WorkingRestoreDialog(preview, vm.Offline);
            var action = await dialog.ShowDialog<WorkingRestoreDialog.Action?>(this);
            if (action is null || vm.WorkspaceProfileId != id) return;
            if (action.RelativePath is not { } relative)
            {
                await vm.ApplyWorkingAsync(preview);
                return;
            }
            string? file = null;
            if (!action.Download)
            {
                try
                {
                    var files = await StorageProvider.OpenFilePickerAsync(new()
                    {
                        Title = TextCatalog.Get("WorkingAddFile"),
                        FileTypeFilter = action.GameFile ? null : [new("JAR") { Patterns = ["*.jar", "*.jar.disabled"] }]
                    });
                    if (files.Count != 1 || files[0].TryGetLocalPath() is not { } selected) continue;
                    file = selected;
                }
                catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException)
                {
                    vm.Status = error.Message;
                    return;
                }
            }
            await vm.SupplyWorkingAsync(id, preview.Configuration.Id, relative, file, action.GameFile);
            if (!vm.LastOperationSucceeded) return;
        }
    }
}
