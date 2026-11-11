using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Desktop;

public sealed partial class MainWindow
{
    private sealed record NameReviewRow(string Source, string? Target, string? Reason, ProfileFolderMove? Folder = null, ProfileFileRename? File = null);

    private async void ReviewProfileNames(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsIdle: true, WorkspaceProfileId: { } id } vm) return;
        if (vm.IsGameLaunchActive) { vm.Status = TextCatalog.Get("ProfileNameInUse"); return; }
        while (vm.WorkspaceProfileId == id && !vm.IsGameLaunchActive)
        {
            var rows = new List<NameReviewRow>();
            await vm.RunAsync(async ct =>
            {
                try
                {
                    var folder = await vm.Services.ProfileFolders.PreviewAsync(id, ct, restoreLegacy: false);
                    rows.Add(new(folder.Source, folder.Target, null, Folder: folder));
                }
                catch (IOException error) when (error.Message == "ProfileFolderNameAlready") { }
                catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException)
                { rows.Add(new(vm.WorkspaceProfilePath, null, TextCatalog.Get(error.Message))); }
                foreach (var file in await vm.Services.ProfileFiles.ReviewAsync(id, ct))
                    rows.Add(new(file.Source, file.Plan?.Target, file.Reason is null ? null : TextCatalog.Get(file.Reason), File: file.Plan));
            });
            if (!vm.LastOperationSucceeded || vm.WorkspaceProfileId != id) return;
            if (rows.Count == 0) { vm.Status = TextCatalog.Get("ProfileNameNone"); return; }
            if (await ShowNameReviewAsync(rows) is not { } choice || vm.WorkspaceProfileId != id || vm.IsGameLaunchActive) return;
            await vm.RunAsync(async ct =>
            {
                try
                {
                    if (choice.Folder is { } folder)
                    {
                        await vm.Services.ProfileFolders.MoveAsync(folder, ct);
                        vm.RelocateConsoleLog(folder);
                        vm.WorkspaceFolderMoved(id);
                    }
                    else if (choice.File is { } file)
                    {
                        await vm.Services.ProfileFiles.ApplyAsync(file, ct);
                        await vm.InvalidateRenamedFileAsync(id, file.From);
                    }
                    vm.Status = TextCatalog.Format("ProfileNameChanged", Path.GetFileName(choice.Target));
                }
                catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException)
                { throw new InvalidOperationException(TextCatalog.Get(error.Message), error); }
            });
            if (!vm.LastOperationSucceeded) return;
        }
    }

    private Task<NameReviewRow?> ShowNameReviewAsync(IReadOnlyList<NameReviewRow> rows)
    {
        var dialog = new Window { Title = TextCatalog.Get("ProfileNamesOrganize"), Width = 800, Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var list = new ListBox { Name = "NameReviewRows", ItemsSource = rows, ItemTemplate = new FuncDataTemplate<NameReviewRow>((row, _) =>
            new StackPanel { Margin = new Thickness(6), Spacing = 4, Children =
            {
                new TextBlock { Text = row?.Source, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = row?.Target is { } target ? "→ " + target : row?.Reason, TextWrapping = TextWrapping.Wrap }
            } }) };
        var confirm = new Button { Name = "ApplyProfileName", Content = TextCatalog.Get("ProfileNameApply"), IsEnabled = false };
        var close = new Button { Content = TextCatalog.Get("Cancel"), IsCancel = true };
        list.SelectionChanged += (_, _) => confirm.IsEnabled = list.SelectedItem is NameReviewRow { Target: not null };
        confirm.Click += (_, _) => { if (list.SelectedItem is NameReviewRow { Target: not null } row) dialog.Close(row); };
        close.Click += (_, _) => dialog.Close();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { confirm, close } };
        Grid.SetRow(actions, 1);
        dialog.Content = new Grid { Margin = new Thickness(18), RowDefinitions = new("*,Auto"), RowSpacing = 10, Children = { list, actions } };
        return dialog.ShowDialog<NameReviewRow?>(this);
    }
}
