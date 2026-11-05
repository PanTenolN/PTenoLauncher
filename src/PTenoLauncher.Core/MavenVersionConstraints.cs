using System.Numerics;
using System.Text.RegularExpressions;
namespace PTenoLauncher.Core;

/// <summary>Bounded Maven ranges/unions and single hyphenated standard qualifiers. Other shapes remain Unknown.</summary>
public static class MavenVersionConstraints
{
    public static ConstraintMatch Match(string? actual, string expression)
    {
        if (expression.Length > 4096 || actual?.Length > 1024) return ConstraintMatch.Unknown;
        var text = expression.Trim();
        if (text is "" or "*") return ConstraintMatch.Match;
        // A bare supported Maven version is a recommendation, not a hard equality restriction.
        if (Parse(text) is not null) return ConstraintMatch.Match;
        if (Parse(actual) is not { } current) return ConstraintMatch.Unknown;
        int position = 0, intervals = 0; bool matched = false;
        MavenVersion? previousUpper = null; bool previousInclusive = false;
        while (position < text.Length)
        {
            if (++intervals > 64 || text[position] is not ('[' or '(')) return ConstraintMatch.Unknown;
            char start = text[position++]; int closing = text.IndexOfAny([']', ')'], position);
            if (closing < 0) return ConstraintMatch.Unknown;
            char end = text[closing]; var bounds = text[position..closing].Split(',');
            MavenVersion? lower, upper;
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
    private sealed record MavenVersion(BigInteger[] Components, int Qualifier, BigInteger Number);
    private static MavenVersion? Parse(string? value)
    {
        if (value is null || value.Length > 1024) return null;
        // Deliberately not a universal ComparableVersion implementation. No chained, dot,
        // timestamp, vendor or numeric-hyphen suffixes are inferred from SemVer rules.
        var match = Regex.Match(value, @"^(?<base>[0-9]+(?:\.[0-9]+){0,15})(?:-(?<qualifier>[A-Za-z]+)(?<number>[0-9]*))?$",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success) return null;
        var qualifier = match.Groups["qualifier"].Value.ToLowerInvariant();
        var number = match.Groups["number"].Value;
        int rank = qualifier switch {
            "alpha" => 0, "a" when number.Length > 0 => 0,
            "beta" => 1, "b" when number.Length > 0 => 1,
            "milestone" => 2, "m" when number.Length > 0 => 2,
            "rc" or "cr" => 3, "snapshot" when number.Length == 0 => 4,
            "" or "ga" or "final" or "release" when number.Length == 0 => 5,
            "sp" => 6, _ => -1
        };
        if (rank < 0) return null;
        return new(match.Groups["base"].Value.Split('.').Select(s => BigInteger.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
            rank, number.Length == 0 ? BigInteger.Zero : BigInteger.Parse(number, System.Globalization.CultureInfo.InvariantCulture));
    }
    private static int Compare(MavenVersion left, MavenVersion right)
    {
        for (int i = 0; i < Math.Max(left.Components.Length, right.Components.Length); i++)
        {
            int order = (i < left.Components.Length ? left.Components[i] : BigInteger.Zero).CompareTo(i < right.Components.Length ? right.Components[i] : BigInteger.Zero);
            if (order != 0) return order;
        }
        int qualifier = left.Qualifier.CompareTo(right.Qualifier);
        return qualifier != 0 ? qualifier : left.Number.CompareTo(right.Number);
    }
}
