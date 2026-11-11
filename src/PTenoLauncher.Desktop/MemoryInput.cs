using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;

namespace PTenoLauncher.Desktop;

internal static class MemoryInput
{
    internal static bool TryNormalize(string? text, out int memory)
    {
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            memory = 0;
            return false;
        }
        const int step = 256;
        memory = ((int)Math.Clamp(value, 512, 65536) + step / 2) / step * step;
        return true;
    }

    internal static void Attach(TextBox input)
    {
        input.LostFocus += (_, _) => Normalize();
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Normalize();
            e.Handled = true;
        };

        void Normalize()
        {
            if (TryNormalize(input.Text, out var memory))
                input.SetCurrentValue(TextBox.TextProperty, memory.ToString(CultureInfo.InvariantCulture));
        }
    }
}
