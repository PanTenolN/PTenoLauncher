using Avalonia.Controls;
using Avalonia.Interactivity;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Minecraft;

namespace PTenoLauncher.Desktop.Dialogs;

public sealed partial class ContentActionDialog : Window
{
    public ContentActionDialog() => InitializeComponent();
    internal ContentActionDialog(string title, string action, string item, string source, string destination) : this()
    {
        Title = title;
        ItemTitle.Text = item;
        ItemSource.Text = source;
        ItemDestination.Text = destination;
        ConfirmAction.Content = action;
    }
    internal ContentActionDialog(ContentFileAction plan, bool remove) : this(TextCatalog.Get(remove ? "ContentTrash" : "ContentCopy"),
        TextCatalog.Get(remove ? "ContentTrash" : "ContentCopyConfirm"), plan.Entry.Name + " · " + plan.Entry.FileName, plan.Source, plan.Target)
    {
        BackupWorld.IsVisible = BackupDestination.IsVisible = plan.Backup is not null;
        BackupDestination.Text = plan.Backup;
    }
    private void Confirm(object? sender, RoutedEventArgs e) => Close(BackupWorld.IsVisible && BackupWorld.IsChecked == true);
    private void Cancel(object? sender, RoutedEventArgs e) => Close(null);
}
