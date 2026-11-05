using System.Globalization;
using System.Text.RegularExpressions;

namespace PTenoLauncher.Core;

/// <summary>Bounded Quilt 0.30.1 SemanticVersionImpl ordering, without Fabric predicate grammar.
/// Not a generic/raw-version or dependency-solver implementation.</summary>
public static class QuiltSemanticOrder
{
    private sealed record Value(int[] Core, string? Pre);
    private static Value? Parse(string text)
    {
        if (text.Length > 1024) return null;
        var m = Regex.Match(text, @"^(?<core>[0-9]+(?:\.[0-9]+)*)(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!m.Success) return null;
        var parts = m.Groups["core"].Value.Split('.'); if (parts.Length > 16) return null;
        var core = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++) if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out core[i])) return null;
        return new(core, m.Groups["pre"].Success ? m.Groups["pre"].Value : null);
    }
    public static int? Compare(string? left, string? right)
    {
        if (left is null || right is null || Parse(left) is not { } a || Parse(right) is not { } b) return null;
        for (int i = 0; i < Math.Max(a.Core.Length, b.Core.Length); i++)
        {
            int difference = (i < a.Core.Length ? a.Core[i] : 0).CompareTo(i < b.Core.Length ? b.Core[i] : 0);
            if (difference != 0) return difference;
        }
        if (a.Pre == b.Pre) return 0;
        if (a.Pre is null) return 1; if (b.Pre is null) return -1;
        var x = a.Pre.Split('.'); var y = b.Pre.Split('.');
        // Quilt's UNSIGNED_INTEGER is 0|[1-9][0-9]*. Leading-zero tokens are strings, not numeric aliases.
        static bool Numeric(string s) => s == "0" || s[0] is >= '1' and <= '9' && s.All(char.IsAsciiDigit);
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            bool xn = Numeric(x[i]), yn = Numeric(y[i]);
            int difference = xn != yn ? xn ? -1 : 1 : xn && x[i].Length != y[i].Length ? x[i].Length.CompareTo(y[i].Length) : string.CompareOrdinal(x[i], y[i]);
            if (difference != 0) return difference;
        }
        return x.Length.CompareTo(y.Length);
    }
}
