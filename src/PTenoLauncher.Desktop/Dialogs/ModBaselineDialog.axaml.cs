using Avalonia.Controls;
using Avalonia.Interactivity;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Mods;
namespace PTenoLauncher.Desktop.Dialogs;
public sealed partial class ModBaselineDialog : Window
{
    private readonly MainViewModel? vm;
    private ModBaselinePreview? preview;
    private bool loading;
    public ModBaselineDialog() { InitializeComponent(); Opened += async (_, _) => await RefreshAsync(); }
    public ModBaselineDialog(MainViewModel vm) : this() { this.vm = vm; }
    public async Task RefreshAsync()
    {
        if (vm is null || vm.IsBusy || loading) return;
        loading = true; ConfirmButton.IsEnabled = false; Acceptance.IsChecked = false;
        try
        {
            preview = await vm.GetModBaselineAsync();
            ProfileLabel.Text = vm.ActiveName;
            FileList.Text = string.Join(Environment.NewLine + Environment.NewLine,
                preview.Files.Select(f => f.RelativePath + " · " + f.Size + " " + TextCatalog.Get("Bytes") + Environment.NewLine + "SHA256 " + f.Sha256)
                .Concat(preview.Removed.Select(f => TextCatalog.Get("ModBaselineRemoved") + ": " + f)));
            ErrorLabel.Text = TextCatalog.Get("Files") + ": " + preview.Files.Count + " · " + (preview.Files.Sum(f => f.Size) / 1048576.0).ToString("N2") + " MB";
        }
        catch (Exception ex) { preview = null; ErrorLabel.Text = ex.Message; }
        finally { loading = false; UpdateConfirmation(); }
    }
    private void UpdateConfirmation() { if (ConfirmButton is not null) ConfirmButton.IsEnabled = !loading && preview is not null && Acceptance.IsChecked == true; }
    private void AcceptanceChanged(object? sender, RoutedEventArgs e) => UpdateConfirmation();
    private async void Confirm(object? sender, RoutedEventArgs e)
    {
        if (vm is null || preview is null || Acceptance.IsChecked != true) return;
        loading = true; ConfirmButton.IsEnabled = false;
        await vm.ConfirmModBaselineAsync(preview);
        loading = false;
        if (vm.LastOperationSucceeded) Close(true);
        else { ErrorLabel.Text = vm.Status; preview = null; Acceptance.IsChecked = false; }
    }
    private void Cancel(object? sender, RoutedEventArgs e) => Close(false);
}
