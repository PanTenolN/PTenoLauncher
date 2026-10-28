using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using PTenoLauncher.Core;

namespace PTenoLauncher.Desktop.Themes;
public sealed class AppearanceManager(Application app)
{
    private ThemePreferences current = new();
    public void Initialize()
    {
        app.PropertyChanged += (_, e) => { if (e.Property == Application.ActualThemeVariantProperty && current.Theme == LauncherTheme.System) ApplyPalette(); };
        Apply(new());
    }
    public void Apply(ThemePreferences preferences)
    {
        PreferencesValidation.Validate(preferences); current = preferences;
        app.RequestedThemeVariant = current.Theme == LauncherTheme.System ? ThemeVariant.Default :
            current.Theme == LauncherTheme.Light ? ThemeVariant.Light : ThemeVariant.Dark;
        ApplyPalette();
    }
    private void ApplyPalette()
    {
        bool light = current.Theme == LauncherTheme.Light || current.Theme == LauncherTheme.System && app.ActualThemeVariant == ThemeVariant.Light;
        var palette = current.Theme == LauncherTheme.Custom ? current.Custom ?? new() :
            light ? new("#F2F3F5", "#E7E9EC", "#FFFFFF", "#42566E", "#20252B") :
            current.Theme == LauncherTheme.MidnightBlack ? new("#08090A", "#101214", "#181B1E", "#ADB8C5", "#ECEFF3") : new CustomPalette();
        app.Resources["WindowBrush"] = new SolidColorBrush(Color.Parse(palette.Background));
        app.Resources["SidebarBrush"] = new SolidColorBrush(Color.Parse(palette.Sidebar));
        app.Resources["SurfaceBrush"] = new SolidColorBrush(Color.Parse(palette.Surface));
        app.Resources["HoverBrush"] = new SolidColorBrush(light ? Color.Parse("#E1E5EB") : Color.Parse("#2B2F34"));
        app.Resources["BorderBrush"] = new SolidColorBrush(light ? Color.Parse("#C6CCD4") : Color.Parse("#353A40"));
        app.Resources["TextBrush"] = new SolidColorBrush(Color.Parse(palette.Text));
        app.Resources["SecondaryBrush"] = new SolidColorBrush(light ? Color.Parse("#555F6C") : Color.Parse("#9CA3AF"));
        app.Resources["AccentBrush"] = new SolidColorBrush(Color.Parse(palette.Accent));
        var accent = Color.Parse(palette.Accent);
        app.Resources["AccentTextBrush"] = new SolidColorBrush((accent.R * .299 + accent.G * .587 + accent.B * .114) > 145 ? Color.Parse("#111315") : Colors.White);
        app.Resources["ControlRadius"] = new CornerRadius(palette.Radius);
        app.Resources["MotionDuration"] = palette.Animations ? TimeSpan.FromMilliseconds(120) : TimeSpan.Zero;
    }
}