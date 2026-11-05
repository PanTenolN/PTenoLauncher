using Avalonia.Controls;
using Avalonia.Interactivity;
namespace PTenoLauncher.Desktop.Dialogs;
public sealed partial class LocalDependenciesDialog : Window
{
    public LocalDependenciesDialog() => InitializeComponent();
    private void Find(object? sender, RoutedEventArgs e) => Close(true);
    private void Manual(object? sender, RoutedEventArgs e) => Close(false);
    private void Cancel(object? sender, RoutedEventArgs e) => Close(null);
}
