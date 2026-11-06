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
    private PackComparisonResult? comparison;
    private PackCopyPlan? plan;
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
            await SetSourceAsync(side, file);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException)
        { CompareStatus.Text = error.Message; }
    }

    internal async Task SetSourceAsync(string side, string file)
    {
        if (DataContext is not MainViewModel { IsIdle: true } vm) return;
        if (side == "left") left = file; else right = file;
        comparison = null;
        LeftBasis.IsChecked = RightBasis.IsChecked = false;
        LeftBasis.IsEnabled = RightBasis.IsEnabled = false;
        ResetPlan();
        LeftSummary.Text = Path.GetFileName(left);
        RightSummary.Text = Path.GetFileName(right);
        if (left is null || right is null) return;
        await vm.RunAsync(async ct => comparison = await new PackComparison(vm.Services.Paths).CompareAsync(left, right, ct));
        if (!vm.LastOperationSucceeded) { CompareStatus.Text = vm.Status; return; }
        LeftBasis.IsEnabled = RightBasis.IsEnabled = true;
        LeftSummary.Text = Target(comparison!.Left);
        RightSummary.Text = Target(comparison.Right);
        ShowComparison();
    }

    private void BasisChanged(object? sender, RoutedEventArgs e)
    {
        if (PrepareCopyButton is null || Differences is null) return;
        ResetPlan();
        ShowComparison();
    }

    private void ResetPlan()
    {
        plan = null;
        CopyConsent.IsChecked = false;
        CopyConsent.IsVisible = false;
        CreateCopyButton.IsEnabled = false;
        PrepareCopyButton.IsEnabled = comparison is not null && (LeftBasis.IsChecked == true || RightBasis.IsChecked == true);
        TargetSummary.Text = CompareStatus.Text = "";
        Differences.ItemsSource = null;
    }

    private void ShowComparison()
    {
        if (comparison is not { } current) return;
        TargetSummary.Text = TextCatalog.Get(current.SameTarget ? "PackCompareTargetSame" : "PackCompareTargetChanged");
        Differences.ItemsSource = current.Files.Select(row => new CompareRow(TextCatalog.Get(row.Kind), Format(row.Left), Format(row.Right))).ToArray();
    }

    private async void PrepareCopy(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsIdle: true, IsGameLaunchActive: false } vm || left is null || right is null ||
            comparison is null || LeftBasis.IsChecked != true && RightBasis.IsChecked != true) return;
        var basis = LeftBasis.IsChecked == true ? PackBasis.Left : PackBasis.Right;
        ResetPlan();
        CompareStatus.Text = TextCatalog.Get("PackCopyChecking");
        await vm.RunAsync(async ct => plan = await vm.Services.Modpacks.PrepareCopyAsync(left, right, basis, ct));
        if (!vm.LastOperationSucceeded || plan is not { } prepared)
        {
            ShowComparison();
            CompareStatus.Text = vm.Status;
            return;
        }
        TargetSummary.Text = TextCatalog.Format("PackCopyTarget", prepared.ProfileName, prepared.Minecraft, prepared.Loader, prepared.LoaderVersion ?? "—") +
            Environment.NewLine + Path.GetFileName(prepared.BaseArchive) + " · SHA-256 " + prepared.BaseSha256 +
            Environment.NewLine + Path.GetFileName(prepared.OtherArchive) + " · SHA-256 " + prepared.OtherSha256;
        var findings = prepared.Blockers.Select(problem => new CompareRow(TextCatalog.Get("PackBlocked"),
            TextCatalog.Get(problem.Code) + " " + problem.Detail, ""))
            .Concat(prepared.Warnings.Select(warning => new CompareRow("", warning, "")));
        Differences.ItemsSource = findings.Concat(prepared.Rows.Select(row => new CompareRow(
            TextCatalog.Get(row.Decision) + (row.Verification is { } state ? Environment.NewLine + TextCatalog.Get(state) : ""),
            Format(row.Basis), Format(row.Other)))).ToArray();
        CopyConsent.IsVisible = prepared.CanCreate;
        CompareStatus.Text = TextCatalog.Get(prepared.CanCreate ? "PackPreviewReady" : "PackBlocked");
    }

    private void ConsentChanged(object? sender, RoutedEventArgs e)
    {
        if (CreateCopyButton is not null) CreateCopyButton.IsEnabled = plan is { CanCreate: true } && CopyConsent.IsChecked == true;
    }

    private async void CreateCopy(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsIdle: true, IsGameLaunchActive: false } vm ||
            plan is not { CanCreate: true } reviewed || CopyConsent.IsChecked != true) return;
        CopyConsent.IsChecked = false;
        CopyConsent.IsVisible = false;
        plan = null;
        CompareStatus.Text = TextCatalog.Get("PackCopyCreate");
        await vm.CreateReconciledCopyAsync(reviewed);
        CompareStatus.Text = vm.Status;
    }

    private static string Target(ComparedPack pack) =>
        $"{pack.Name} · Minecraft {pack.Minecraft} · {pack.Loader} {pack.LoaderVersion}".TrimEnd();
    private static string Format(ComparedPackFile? file) => file is null ? "" :
        string.Join(Environment.NewLine, new[] { file.Path, TextCatalog.Get(file.Source), file.Version ?? file.VersionId, "SHA-512 " + file.Sha512 }
            .Where(value => !string.IsNullOrEmpty(value)));
    private void CloseWindow(object? sender, RoutedEventArgs e) => Close();
}
