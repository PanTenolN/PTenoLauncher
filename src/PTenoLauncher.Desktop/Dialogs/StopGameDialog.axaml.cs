using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PTenoLauncher.Desktop.Dialogs;

public sealed partial class StopGameDialog : Window
{
    public StopGameDialog() => InitializeComponent();
    private void AcceptanceChanged(object? sender, RoutedEventArgs e) => ConfirmStopButton.IsEnabled = StopAcceptance.IsChecked == true;
    private void Cancel(object? sender, RoutedEventArgs e) => Close(false);
    private void Confirm(object? sender, RoutedEventArgs e)
    {
        if (StopAcceptance.IsChecked == true) Close(true);
    }
}
