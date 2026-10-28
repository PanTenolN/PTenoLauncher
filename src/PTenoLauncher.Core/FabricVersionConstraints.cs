using System.Text.RegularExpressions;
namespace PTenoLauncher.Core;

/// <summary>Fabric version-1 predicates, including ordered prereleases and the empty-prerelease ceiling.
/// Unsupported syntax remains Unknown; this does not emulate Fabric's complete SAT dependency selection.</summary>
public static class FabricVersionConstraints
{
    private sealed record Semantic(int[] Core, string? Pre) : IComparable<Semantic>
    {
        public int Part(int index) => index < Core.Length ? Core[index] : 0;
        public int CompareTo(Semantic? other)
        {
            if (other is null) return 1;
            for (int i = 0; i < Math.Max(Core.Length, other.Core.Length); i++)
                if (Part(i).CompareTo(other.Part(i)) is var cmp && cmp != 0) return cmp;
            if (Pre == other.Pre) return 0;
            if (Pre is null) return 1;
            if (other.Pre is null) return -1;
            if (Pre.Length == 0) return -1;
            if (other.Pre.Length == 0) return 1;
            var left = Pre.Split('.'); var right = other.Pre.Split('.');
            for (int i = 0; i < Math.Min(left.Length, right.Length); i++)
            {
                bool a = left[i].All(char.IsAsciiDigit), b = right[i].All(char.IsAsciiDigit);
                int cmp = a && b ? CompareNumeric(left[i], right[i]) : a != b ? a ? -1 : 1 : string.CompareOrdinal(left[i], right[i]);
                if (cmp != 0) return cmp;
            }
            return left.Length.CompareTo(right.Length);
        }
        private static int CompareNumeric(string left, string right)
        {
            left = left.TrimStart('0'); right = right.TrimStart('0');
            return left.Length == right.Length ? string.CompareOrdinal(left, right) : left.Length.CompareTo(right.Length);
        }
    }
    public static ConstraintMatch Match(string? actual, string expression)
    {
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > 1024) return ConstraintMatch.Unknown;
        var text = expression.Trim();
        if (text == "*") return ConstraintMatch.Match;
        if (actual is null || actual.Length > 1024) return ConstraintMatch.Unknown;
        var alternatives = text.Split("||", StringSplitOptions.TrimEntries);
        if (alternatives.Length > 1) return VersionConstraints.Or(alternatives.Select(a => Match(actual, a)));
        text = Regex.Replace(text, @"([<>]=?|[=~^])\s+", "$1", RegexOptions.None, TimeSpan.FromSeconds(1));
        var terms = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length > 1)
        {
            var matches = terms.Select(t => Match(actual, t)).ToArray();
            return matches.Contains(ConstraintMatch.Mismatch) ? ConstraintMatch.Mismatch : matches.Contains(ConstraintMatch.Unknown) ? ConstraintMatch.Unknown : ConstraintMatch.Match;
        }
        var match = Regex.Match(text, @"^(>=|<=|>|<|=|~|\^)?(.+)$", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!match.Success) return ConstraintMatch.Unknown;
        var op = match.Groups[1].Value; var target = match.Groups[2].Value;
        var wildcard = target.Split('.');
        int firstWildcard = Array.FindIndex(wildcard, p => p is "x" or "X" or "*");
        if (firstWildcard >= 0)
        {
            if (op is not ("" or "=") || wildcard.Skip(firstWildcard).Any(p => p is not ("x" or "X" or "*"))) return ConstraintMatch.Unknown;
            if (!TryParse(actual, out var version)) return ConstraintMatch.Unknown;
            for (int i = 0; i < firstWildcard; i++)
            {
                if (!int.TryParse(wildcard[i], out int component) || component < 0) return ConstraintMatch.Unknown;
                if (version!.Part(i) != component) return ConstraintMatch.Mismatch;
            }
            return ConstraintMatch.Match;
        }
        if (!TryParse(actual, out var current) || !TryParse(target, out var reference))
            return op is "" or "=" ? actual == target ? ConstraintMatch.Match : ConstraintMatch.Mismatch : ConstraintMatch.Unknown;
        int comparison = current!.CompareTo(reference);
        bool result;
        switch (op)
        {
            case ">=": result = comparison >= 0; break;
            case "<=": result = comparison <= 0; break;
            case ">": result = comparison > 0; break;
            case "<": result = comparison < 0; break;
            case "^":
                if (reference!.Part(0) == int.MaxValue) return ConstraintMatch.Unknown;
                result = comparison >= 0 && current.CompareTo(new([reference.Part(0) + 1], "")) < 0; break;
            case "~":
                if (reference!.Part(1) == int.MaxValue) return ConstraintMatch.Unknown;
                result = comparison >= 0 && current.CompareTo(new([reference.Part(0), reference.Part(1) + 1], "")) < 0; break;
            default: result = comparison == 0; break;
        }
        return result ? ConstraintMatch.Match : ConstraintMatch.Mismatch;
    }
    private static bool TryParse(string text, out Semantic? version)
    {
        version = null;
        var match = Regex.Match(text, @"^(\d+(?:\.\d+)*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*|))?(?:\+[^\s]+)?$", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!match.Success) return false;
        var segments = match.Groups[1].Value.Split('.');
        if (segments.Length > 16) return false;
        var core = new int[segments.Length];
        for (int i = 0; i < segments.Length; i++)
            if (!int.TryParse(segments[i], out core[i])) return false;
        version = new(core, match.Groups[2].Success ? match.Groups[2].Value : null); return true;
    }
}
