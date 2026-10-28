using Avalonia.Controls;
using Avalonia.Interactivity;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Desktop.Dialogs;
public sealed partial class DeleteProfileDialog : Window
{
    private readonly MainViewModel vm;
    private ProfileDeletionPlan? plan;
    private bool loading;
    public DeleteProfileDialog(MainViewModel vm) : this() { this.vm = vm; }
    public DeleteProfileDialog()
    {
        vm = null!; InitializeComponent();
        Opened += async (_, _) => await RefreshAsync();
    }
    private async Task RefreshAsync()
    {
        if (loading || vm is null) return; loading = true; DeleteButton.IsEnabled = false;
        try
        {
            plan = await vm.GetDeletionPlanAsync(IncludeBackups.IsChecked == true);
            ProfileLabel.Text = plan.ProfileName;
            SizeLabel.Text = $"{plan.Files.Count} files · {plan.TotalBytes / 1048576.0:N2} MB";
            FileList.ItemsSource = plan.Files; ErrorLabel.Text = "";
        }
        catch (Exception ex) { plan = null; ErrorLabel.Text = ex.Message; }
        finally { loading = false; UpdateConfirmation(); }
    }
    private async void OptionsChanged(object? sender, RoutedEventArgs e) { if (IsVisible) await RefreshAsync(); }
    private void ConfirmationChanged(object? sender, TextChangedEventArgs e) => UpdateConfirmation();
    private void UpdateConfirmation()
    {
        if (DeleteButton is not null) DeleteButton.IsEnabled = !loading && plan is not null && ConfirmationName.Text == plan.ProfileName;
    }
    private async void Confirm(object? sender, RoutedEventArgs e)
    {
        if (plan is null || ConfirmationName.Text != plan.ProfileName) return;
        DeleteButton.IsEnabled = false;
        await vm.DeleteApprovedAsync(plan, ConfirmationName.Text);
        if (vm.LastOperationSucceeded) Close(true);
        else { ErrorLabel.Text = vm.Status; await RefreshAsync(); }
    }
    private void Cancel(object? sender, RoutedEventArgs e) => Close(false);
}