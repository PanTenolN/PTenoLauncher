using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Modpacks;

namespace PTenoLauncher.Desktop.Dialogs;

public sealed partial class PackCompareDialog : Window
{
    private string? left, right;
    public sealed record CompareRow(string Status, string Left, string Right);

    public PackCompareDialog()
    {
        InitializeComponent();
        Closing += (_, e) => { if (DataContext is MainViewModel { IsBusy: true }) e.Cancel = true; };
    }

    private async void Choose(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsIdle: true } vm || sender is not Button { Tag: string side }) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new()
            {
                Title = TextCatalog.Get(side == "left" ? "PackCompareLeft" : "PackCompareRight"),
                AllowMultiple = false, FileTypeFilter = [new("Modrinth .mrpack") { Patterns = ["*.mrpack"] }]
            });
            if (files.Count != 1 || files[0].TryGetLocalPath() is not { } file) return;
            if (side == "left") left = file; else right = file;
            LeftSummary.Text = Path.GetFileName(left);
            RightSummary.Text = Path.GetFileName(right);
            TargetSummary.Text = CompareStatus.Text = "";
            Differences.ItemsSource = null;
            if (left is null || right is null) return;

            var leftFile = left;
            var rightFile = right;
            await vm.RunAsync(async ct =>
            {
                var comparison = await new PackComparison(vm.Services.Paths).CompareAsync(leftFile, rightFile, ct);
                LeftSummary.Text = Target(comparison.Left);
                RightSummary.Text = Target(comparison.Right);
                TargetSummary.Text = TextCatalog.Get(comparison.SameTarget ? "PackCompareTargetSame" : "PackCompareTargetChanged");
                Differences.ItemsSource = comparison.Files.Select(row => new CompareRow(TextCatalog.Get(row.Kind), Format(row.Left), Format(row.Right))).ToArray();
            });
            if (!vm.LastOperationSucceeded) CompareStatus.Text = vm.Status;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException)
        { CompareStatus.Text = error.Message; }
    }

    private static string Target(ComparedPack pack) =>
        $"{pack.Name} · Minecraft {pack.Minecraft} · {pack.Loader} {pack.LoaderVersion}".TrimEnd();
    private static string Format(ComparedPackFile? file) => file is null ? "" :
        string.Join(Environment.NewLine, new[] { file.Path, TextCatalog.Get(file.Source), file.Version ?? file.VersionId, "SHA-512 " + file.Sha512 }
            .Where(value => !string.IsNullOrEmpty(value)));
    private void CloseWindow(object? sender, RoutedEventArgs e) => Close();
}
