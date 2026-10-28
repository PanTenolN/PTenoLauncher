using System.Numerics;
using System.Text.RegularExpressions;
namespace PTenoLauncher.Core;

/// <summary>Bounded numeric Maven ranges and unions. Qualifier ordering is deliberately Unknown.</summary>
public static class MavenVersionConstraints
{
    public static ConstraintMatch Match(string? actual, string expression)
    {
        if (expression.Length > 4096 || actual?.Length > 1024) return ConstraintMatch.Unknown;
        var text = expression.Trim();
        if (text is "" or "*") return ConstraintMatch.Match;
        // A bare numeric Maven version is a recommendation, not a hard equality restriction.
        if (Parse(text) is not null) return ConstraintMatch.Match;
        if (Parse(actual) is not { } current) return ConstraintMatch.Unknown;
        int position = 0, intervals = 0; bool matched = false;
        BigInteger[]? previousUpper = null; bool previousInclusive = false;
        while (position < text.Length)
        {
            if (++intervals > 64 || text[position] is not ('[' or '(')) return ConstraintMatch.Unknown;
            char start = text[position++]; int closing = text.IndexOfAny([']', ')'], position);
            if (closing < 0) return ConstraintMatch.Unknown;
            char end = text[closing]; var bounds = text[position..closing].Split(',');
            BigInteger[]? lower, upper;
            if (bounds.Length == 1)
            {
                if (start != '[' || end != ']' || Parse(bounds[0].Trim()) is not { } exact) return ConstraintMatch.Unknown;
                lower = upper = exact;
            }
            else if (bounds.Length == 2)
            {
                string lo = bounds[0].Trim(), hi = bounds[1].Trim();
                lower = lo.Length == 0 ? null : Parse(lo); upper = hi.Length == 0 ? null : Parse(hi);
                if (lo.Length > 0 && lower is null || hi.Length > 0 && upper is null || lower is null && upper is null ||
                    lower is null && start == '[' || upper is null && end == ']') return ConstraintMatch.Unknown;
                if (lower is not null && upper is not null && (Compare(lower, upper) > 0 || Compare(lower, upper) == 0 && (start != '[' || end != ']')))
                    return ConstraintMatch.Unknown;
            }
            else return ConstraintMatch.Unknown;
            if (intervals > 1 && (previousUpper is null || lower is null || Compare(previousUpper, lower) > 0 ||
                Compare(previousUpper, lower) == 0 && previousInclusive && start == '[')) return ConstraintMatch.Unknown;
            bool loMatch = lower is null || Compare(current, lower) > 0 || start == '[' && Compare(current, lower) == 0;
            bool hiMatch = upper is null || Compare(current, upper) < 0 || end == ']' && Compare(current, upper) == 0;
            matched |= loMatch && hiMatch;
            previousUpper = upper; previousInclusive = end == ']'; position = closing + 1;
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
            if (position == text.Length) break;
            if (text[position++] != ',') return ConstraintMatch.Unknown;
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
            if (position == text.Length) return ConstraintMatch.Unknown;
        }
        return intervals == 0 ? ConstraintMatch.Unknown : matched ? ConstraintMatch.Match : ConstraintMatch.Mismatch;
    }
    private static BigInteger[]? Parse(string? value)
    {
        if (value is null || value.Length > 1024 || !Regex.IsMatch(value, @"^[0-9]+(?:\.[0-9]+){0,15}$", RegexOptions.None, TimeSpan.FromSeconds(1))) return null;
        return value.Split('.').Select(s => BigInteger.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    }
    private static int Compare(BigInteger[] left, BigInteger[] right)
    {
        for (int i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            int order = (i < left.Length ? left[i] : BigInteger.Zero).CompareTo(i < right.Length ? right[i] : BigInteger.Zero);
            if (order != 0) return order;
        }
        return 0;
    }
}
