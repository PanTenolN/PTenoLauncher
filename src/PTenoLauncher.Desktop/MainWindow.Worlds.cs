using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Minecraft;

namespace PTenoLauncher.Desktop;

public sealed partial class MainWindow
{
    private async void ChooseBackupDirectory(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsIdle: true } vm) return;
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new()
            { Title = TextCatalog.Get("BackupChooseDirectory"), AllowMultiple = false });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            await vm.RunAsync(async ct =>
            {
                try { await vm.Services.Backups.SetDirectoryAsync(path, ct); }
                catch (IOException error) { throw new InvalidOperationException(TextCatalog.Get(error.Message), error); }
                vm.Status = TextCatalog.Format("BackupDirectorySaved", path);
            });
        }
        catch (Exception error) { vm.Status = TextCatalog.Get(error.Message); }
    }

    private async void ManageWorld(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsIdle: true, IsGameLaunchActive: false, WorkspaceWorlds: true, SelectedProfile: { } profile } vm ||
            vm.WorkspaceProfileId != profile.Id || sender is not Button { Tag: string command } ||
            !Enum.TryParse<WorldAction>(command, out var action)) return;
        var entry = vm.WorkspaceContentSelection;
        var key = "World" + action;
        var title = TextCatalog.Get(key);
        try
        {
            GameProfile? target = null;
            string? argument = null;
            switch (action)
            {
                case WorldAction.Copy:
                    if (entry is null) return;
                    var profiles = vm.Profiles.Where(p => p.Id != profile.Id && !p.Archived).ToArray();
                    if (profiles.Length == 0) { vm.Status = TextCatalog.Get("WorldNoDestination"); return; }
                    var choices = new ComboBox { ItemsSource = profiles, HorizontalAlignment = HorizontalAlignment.Stretch };
                    target = await ShowWorldInputAsync(title, TextCatalog.Get("WorldChooseProfile"), choices,
                        () => choices.SelectedItem as GameProfile);
                    if (target is null) return;
                    break;
                case WorldAction.Rename:
                    if (entry is null) return;
                    var name = new TextBox { Text = entry.FileName, MaxLength = 120 };
                    argument = await ShowWorldInputAsync(title, TextCatalog.Get("Name"), name, () => name.Text?.Trim());
                    if (argument is null) return;
                    break;
                case WorldAction.ImportZip:
                    var files = await StorageProvider.OpenFilePickerAsync(new()
                    { Title = title, FileTypeFilter = [new("ZIP") { Patterns = ["*.zip"] }] });
                    argument = files.FirstOrDefault()?.TryGetLocalPath();
                    if (argument is null) return;
                    break;
                case WorldAction.RestoreFolder:
                    var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = title, AllowMultiple = false });
                    argument = folders.FirstOrDefault()?.TryGetLocalPath();
                    if (argument is null) return;
                    break;
                case WorldAction.ExportZip:
                    if (entry is null) return;
                    argument = (await StorageProvider.SaveFilePickerAsync(new()
                    { Title = title, DefaultExtension = "zip", SuggestedFileName = entry.FileName + ".zip",
                        FileTypeChoices = [new("ZIP") { Patterns = ["*.zip"] }] }))?.TryGetLocalPath();
                    if (argument is null) return;
                    break;
                case WorldAction.BackupZip when entry is null:
                    return;
            }
            if (vm.WorkspaceProfileId != profile.Id || !vm.WorkspaceWorlds) return;
            WorldPlan? plan = null;
            await vm.RunAsync(async ct =>
            {
                try { plan = await vm.Services.Worlds.PreviewAsync(action, profile, entry?.FileName, target, argument, ct); }
                catch (Exception error) when (error is IOException or InvalidDataException)
                { throw new InvalidOperationException(TextCatalog.Get(error.Message), error); }
            });
            if (plan is null || vm.WorkspaceProfileId != profile.Id) return;
            var nameVersion = plan.Version.Name ?? TextCatalog.Get("WorldVersionUnknown");
            if (plan.Version.DataVersion is { } dataVersion) nameVersion += $" · DataVersion {dataVersion}";
            var sourceName = action is WorldAction.ImportZip or WorldAction.RestoreFolder
                ? TextCatalog.Get("WorldExternalSource") : profile.Name;
            var destination = target ?? profile;
            var detail = plan.WorldName + Environment.NewLine + TextCatalog.Format("WorldVersion", nameVersion);
            if (action is WorldAction.Copy or WorldAction.ImportZip or WorldAction.RestoreFolder)
                detail += Environment.NewLine + TextCatalog.Get(plan.Version.Name is null ? "WorldCompatibilityUnknown" :
                    plan.Version.Name == destination.MinecraftVersion ? "WorldVersionMatches" : "WorldVersionMismatch");
            var destinationText = action is WorldAction.ExportZip or WorldAction.BackupZip ? plan.Destination :
                destination.Name + " · " + destination.MinecraftVersion + Environment.NewLine + plan.Destination;
            var confirm = new ContentActionDialog(title, title, detail, sourceName + Environment.NewLine + plan.Source, destinationText);
            if ((await confirm.ShowDialog<bool?>(this)).HasValue) await vm.ApplyWorldAsync(plan);
        }
        catch (Exception error) { vm.Status = TextCatalog.Get(error.Message); }
    }

    private Task<T?> ShowWorldInputAsync<T>(string title, string prompt, Control control, Func<T?> read) where T : class
    {
        var window = new Window { Title = title, Width = 490, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var ok = new Button { Content = TextCatalog.Get("ContentDestination"), IsDefault = true };
        var cancel = new Button { Content = TextCatalog.Get("Cancel"), IsCancel = true };
        ok.Click += (_, _) => { if (read() is { } value) window.Close(value); };
        cancel.Click += (_, _) => window.Close();
        window.Content = new StackPanel
        {
            Margin = new Thickness(18), Spacing = 12,
            Children = { new TextBlock { Text = prompt }, control, new StackPanel { Orientation = Orientation.Horizontal,
                Spacing = 8, Children = { ok, cancel } } }
        };
        return window.ShowDialog<T?>(this);
    }
}
