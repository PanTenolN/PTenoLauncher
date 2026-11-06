using Avalonia.Controls;
using Avalonia.Interactivity;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Mods;

namespace PTenoLauncher.Desktop.Dialogs;

public sealed partial class WorkingRestoreDialog : Window
{
    internal sealed record Action(string? RelativePath, bool Download = false, bool GameFile = false);
    public sealed record MissingFile(string RelativePath, bool GameFile);
    private readonly bool offline;

    public WorkingRestoreDialog() => InitializeComponent();

    internal WorkingRestoreDialog(WorkingRestorePreview preview, bool offline) : this()
    {
        this.offline = offline;
        var snapshot = preview.Configuration;
        ProfileSummary.Text = preview.Plan.Before.Profile.Name + Environment.NewLine +
            TextCatalog.Format("WorkingSaved", snapshot.SavedAt?.ToLocalTime());
        var current = preview.Plan.Before.Profile;
        var prior = preview.CurrentReceipt;
        var lines = new List<string>
        {
            TextCatalog.Format("WorkingCurrent", Target(current.MinecraftVersion, current.Loader,
                current.PreferredLoaderVersion ?? (prior?.Loader == current.Loader ? prior?.LoaderVersion : null))),
            TextCatalog.Format("WorkingTarget", Target(snapshot.Receipt.Version, snapshot.Receipt.Loader, snapshot.Receipt.LoaderVersion)),
            ""
        };
        foreach (var change in preview.Plan.Changes)
        {
            if (change.Before is { } before)
                lines.Add(TextCatalog.Format("WorkingRemove", Describe(preview.ReplacedMods.FirstOrDefault(mod => mod.File == before), before)));
            if (change.After is { } after)
                lines.Add(TextCatalog.Format("WorkingRestore", Describe(snapshot.Mods.Single(mod => mod.File == after), after)));
        }
        if (!preview.CanApply && preview.Plan.Blockers.Count == 0) lines.Add(TextCatalog.Get("WorkingNoChanges"));
        foreach (var blocker in preview.Plan.Blockers.Distinct())
        {
            var parts = blocker.Split(": ", 2, StringSplitOptions.None);
            lines.Add(TextCatalog.Get(parts[0]) + (parts.Length == 2 ? " " + parts[1] : ""));
        }
        var gameFiles = snapshot.Receipt.Files.ToDictionary(file => file.RelativePath, StringComparer.Ordinal);
        lines.AddRange(preview.MissingGameFiles.Select(path => "  " + (gameFiles.TryGetValue(path, out var file)
            ? $"{path} · {file.Algorithm} {file.Hash}" : path)));
        lines.AddRange(preview.MissingMods.Select(mod => "  " + Describe(mod, mod.File)));
        RestoreDetails.Text = string.Join(Environment.NewLine, lines);
        var missing = preview.MissingMods.Select(mod => new MissingFile(mod.File.Path, false))
            .Concat(preview.MissingGameFiles.Where(gameFiles.ContainsKey).Select(path => new MissingFile(path, true))).ToArray();
        MissingSection.IsVisible = missing.Length > 0;
        MissingFiles.ItemsSource = missing;
        if (MissingSection.IsVisible) MissingFiles.SelectedIndex = 0;
        MissingSelectionChanged(null, null);
        ConfirmRestore.IsEnabled = preview.CanApply;
    }

    private static string Target(string version, ModLoader loader, string? loaderVersion) =>
        $"Minecraft {version} · {loader} {loaderVersion}".TrimEnd();

    private static string Describe(WorkingMod? mod, ModFileState file) =>
        $"{file.Path} · {mod?.Version ?? "?"} · {TextCatalog.Get((mod?.Enabled ?? !file.Path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) ? "WorkingEnabled" : "WorkingDisabled")}" +
        $"{Environment.NewLine}  SHA-256 {file.Sha256}";

    private void Apply(object? sender, RoutedEventArgs e) => Close(new Action(null));
    private void Cancel(object? sender, RoutedEventArgs e) => Close();
    private void Supply(object? sender, RoutedEventArgs e) => Pick(false);
    private void Fetch(object? sender, RoutedEventArgs e) => Pick(true);
    private void MissingSelectionChanged(object? sender, SelectionChangedEventArgs? e)
    {
        if (FetchButton is not null) FetchButton.IsEnabled = !offline && MissingFiles.SelectedItem is MissingFile { GameFile: false };
    }
    private void Pick(bool download)
    {
        if (MissingFiles.SelectedItem is MissingFile file && (!download || !offline && !file.GameFile))
            Close(new Action(file.RelativePath, download, file.GameFile));
    }
}
