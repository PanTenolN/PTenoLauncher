using System.Text.RegularExpressions;
namespace PTenoLauncher.Core;

public enum LauncherTheme { GraphiteDark, MidnightBlack, Light, System, Custom }
public sealed record CustomPalette(string Background = "#111315", string Sidebar = "#191B1E",
    string Surface = "#222529", string Accent = "#A3B3C7", string Text = "#E5E7EB", double Radius = 6, bool Animations = true);
public sealed record ThemePreferences(LauncherTheme Theme = LauncherTheme.GraphiteDark, CustomPalette? Custom = null);
public sealed record OwnedArtifact(Guid ProfileId, string RelativePath, string Kind);
public enum OfflineReadinessState { Ready, NeedsPreparation, Error, Unknown }
public static class PreferencesValidation
{
    public static void Validate(ThemePreferences settings)
    {
        if (!Enum.IsDefined(settings.Theme)) throw new ArgumentException("Unknown theme.");
        var p = settings.Custom ?? new();
        foreach (var color in new[] { p.Background, p.Sidebar, p.Surface, p.Accent, p.Text })
            if (color is null || !Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$")) throw new ArgumentException("Color must have #RRGGBB format.");
        if (!double.IsFinite(p.Radius) || p.Radius is < 0 or > 18) throw new ArgumentException("Corner radius must be 0–18.");
    }
}