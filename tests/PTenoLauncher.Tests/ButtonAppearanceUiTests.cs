using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop.Themes;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class ButtonAppearanceUiTests
{
    [AvaloniaFact]
    public void ActiveNavigationAndDisabledButtonsStayDistinctAcrossThemes()
    {
        var appearance = new AppearanceManager(Application.Current!);
        var enabled = new Button { Content = "Open" };
        var navigation = new Button { Content = "Profiles", Classes = { "nav" } };
        var disabled = new Button { Content = "Unavailable", IsEnabled = false };
        var checkBox = new CheckBox { Content = "Show archived profiles" };
        var window = new Window { Content = new StackPanel { Children = { enabled, navigation, disabled, checkBox } } };
        window.Show();
        try
        {
            foreach (var theme in new[] { LauncherTheme.GraphiteDark, LauncherTheme.Light, LauncherTheme.MidnightBlack })
            {
                appearance.Apply(new() { Theme = theme });
                Color Background(Button button) => Assert.IsAssignableFrom<ISolidColorBrush>(
                    button.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter").Background).Color;
                var active = Background(enabled);
                Assert.Equal(active, Background(navigation));
                Assert.NotEqual(active, Background(disabled));
                Assert.Equal(new Thickness(1), enabled.BorderThickness);
                var square = checkBox.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "CheckboxSquare");
                var tick = checkBox.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Name == "CheckboxTick");
                var label = checkBox.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
                Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, square.VerticalAlignment);
                Assert.Equal(square.VerticalAlignment, label.VerticalAlignment);
                checkBox.IsChecked = false;
                Assert.False(tick.IsVisible);
                checkBox.IsChecked = true;
                Assert.True(tick.IsVisible);
                Assert.Equal(Color.Parse("#555555"), Assert.IsAssignableFrom<ISolidColorBrush>(square.Background).Color);
            }
        }
        finally { window.Close(); appearance.Apply(new()); }
    }
}
