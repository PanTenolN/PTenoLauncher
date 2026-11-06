using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Desktop.Dialogs;

internal sealed class DataRootDialog : Window
{
    public DataRootDialog(string current)
    {
        Title = TextCatalog.Get("DataChooseRoot");
        Width = 700;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var choices = new ComboBox
        {
            ItemsSource = new[] { current, LauncherPaths.InstalledRoot, LauncherPaths.PortableRoot }
                .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Where(HasDatabase).ToArray(),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
        var details = new TextBlock { Text = TextCatalog.Get("DataRootHint"), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var confirm = new Button { Content = TextCatalog.Get("FirstRunContinue"), IsEnabled = false };
        choices.SelectionChanged += (_, _) => confirm.IsEnabled = choices.SelectedItem is string selected &&
            !selected.Equals(current, StringComparison.OrdinalIgnoreCase);
        confirm.Click += (_, _) => SelectRoot(choices.SelectedItem as string);
        var browse = new Button { Content = TextCatalog.Get("Browse") };
        browse.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = Title!, AllowMultiple = false });
            SelectRoot(folders.SingleOrDefault()?.TryGetLocalPath());
        };
        var cancel = new Button { Content = TextCatalog.Get("Cancel"), IsCancel = true };
        cancel.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 12,
            Children = { details, new SelectableTextBlock { Text = current, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, choices,
                new WrapPanel { ItemSpacing = 10, Children = { confirm, browse, cancel } } }
        };
        void SelectRoot(string? root)
        {
            if (root is null || root.Equals(current, StringComparison.OrdinalIgnoreCase)) return;
            if (HasDatabase(root)) Close(root);
            else details.Text = TextCatalog.Format("DataOpenFailed", root);
        }
    }

    private static bool HasDatabase(string root)
    {
        try { return File.Exists(SafePath.Join(root, "settings.db")); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
}
