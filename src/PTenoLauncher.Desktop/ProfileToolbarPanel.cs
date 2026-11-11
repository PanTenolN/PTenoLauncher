using Avalonia;
using Avalonia.Controls;

namespace PTenoLauncher.Desktop;

public sealed class ProfileToolbarPanel : Panel
{
    private const double GroupSpacing = 12;
    private const double RowSpacing = 8;
    private bool stacked;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count != 2) return base.MeasureOverride(availableSize);
        var actions = Children[0];
        var options = Children[1];
        var naturalSize = new Size(double.PositiveInfinity, double.PositiveInfinity);
        actions.Measure(naturalSize);
        options.Measure(naturalSize);
        var combinedWidth = actions.DesiredSize.Width + GroupSpacing + options.DesiredSize.Width;
        stacked = combinedWidth > availableSize.Width;
        if (!stacked)
            return new Size(combinedWidth, Math.Max(actions.DesiredSize.Height, options.DesiredSize.Height));

        var rowSize = new Size(availableSize.Width, double.PositiveInfinity);
        actions.Measure(rowSize);
        options.Measure(rowSize);
        return new Size(Math.Max(actions.DesiredSize.Width, options.DesiredSize.Width),
            actions.DesiredSize.Height + RowSpacing + options.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count != 2) return base.ArrangeOverride(finalSize);
        var actions = Children[0];
        var options = Children[1];
        var actionSize = actions.DesiredSize;
        var optionSize = options.DesiredSize;
        if (stacked)
        {
            actions.Arrange(new Rect(new Point(0, 0), actionSize));
            options.Arrange(new Rect(new Point(0, actionSize.Height + RowSpacing), optionSize));
        }
        else
        {
            var rowHeight = Math.Max(actionSize.Height, optionSize.Height);
            actions.Arrange(new Rect(new Point(0, (rowHeight - actionSize.Height) / 2), actionSize));
            options.Arrange(new Rect(new Point(actionSize.Width + GroupSpacing, (rowHeight - optionSize.Height) / 2), optionSize));
        }
        return finalSize;
    }
}
