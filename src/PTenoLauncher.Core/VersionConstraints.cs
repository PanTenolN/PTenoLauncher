using System.Text.RegularExpressions;
namespace PTenoLauncher.Core;

public enum ConstraintMatch { Match, Mismatch, Unknown }
public sealed record ModRequirement(string Id, IReadOnlyList<string> Alternatives, string Kind = "required");
/// <summary>Deterministic common Fabric/Quilt and Maven ranges. Unsupported expressions stay Unknown.</summary>
public static class VersionConstraints
{
    /// <summary>Fabric schema-1 ranges, including ordered prereleases.</summary>
    public static ConstraintMatch MatchFabric(string? actual, string expression) => FabricVersionConstraints.Match(actual, expression);
    public static ConstraintMatch Match(string? actual, string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return ConstraintMatch.Unknown;
        if (expression == "*") return ConstraintMatch.Match;
        if (actual is null) return ConstraintMatch.Unknown;
        if (expression == actual) return ConstraintMatch.Match;
        var alternatives = expression.Split("||", StringSplitOptions.TrimEntries);
        if (alternatives.Length > 1) return Or(alternatives.Select(x => Match(actual, x)));
        if (!TryVersion(actual, out var version)) return ConstraintMatch.Unknown;
        var range = expression.Trim();
        if ((range.StartsWith('[') || range.StartsWith('(')) && (range.EndsWith(']') || range.EndsWith(')')))
        {
            var bounds = range[1..^1].Split(',');
            if (bounds.Length == 1 && range.StartsWith('[') && range.EndsWith(']')) return Match(actual, "=" + bounds[0]);
            if (bounds.Length != 2) return ConstraintMatch.Unknown;
            if (bounds[0].Length > 0 && !TryVersion(bounds[0].Trim(), out _)) return ConstraintMatch.Unknown;
            if (bounds[1].Length > 0 && !TryVersion(bounds[1].Trim(), out _)) return ConstraintMatch.Unknown;
            bool lower = bounds[0].Length == 0 || version!.CompareTo(Parse(bounds[0].Trim())) > 0 || range[0] == '[' && version!.CompareTo(Parse(bounds[0].Trim())) == 0;
            bool upper = bounds[1].Length == 0 || version!.CompareTo(Parse(bounds[1].Trim())) < 0 || range[^1] == ']' && version!.CompareTo(Parse(bounds[1].Trim())) == 0;
            return lower && upper ? ConstraintMatch.Match : ConstraintMatch.Mismatch;
        }
        range = Regex.Replace(range, @"([<>]=?|[=~^])\s+", "$1", RegexOptions.None, TimeSpan.FromSeconds(1));
        var terms = range.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length > 1) return And(terms.Select(x => Match(actual, x)));
        if (Regex.IsMatch(range, @"^\d+(?:\.\d+)*\.[xX*]$"))
        {
            var prefix = range[..^2].Split('.').Select(int.Parse).ToArray();
            var values = new[] { version!.Major, version.Minor, version.Build, version.Revision };
            if (prefix.Length > values.Length) return ConstraintMatch.Unknown;
            return prefix.Select((v,i) => v == values[i]).All(x => x) ? ConstraintMatch.Match : ConstraintMatch.Mismatch;
        }
        var match = Regex.Match(range, @"^(>=|<=|>|<|=|~|\^)?(.+)$", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!match.Success || !TryVersion(match.Groups[2].Value, out var target)) return ConstraintMatch.Unknown;
        var cmp = version!.CompareTo(target);
        bool result;
        switch (match.Groups[1].Value)
        {
            case ">=": result = cmp >= 0; break;
            case "<=": result = cmp <= 0; break;
            case ">": result = cmp > 0; break;
            case "<": result = cmp < 0; break;
            case "~": result = cmp >= 0 && version.CompareTo(match.Groups[2].Value.Split('.').Length == 1 ? new Version(target!.Major + 1, 0, 0, 0) : new Version(target!.Major, target.Minor + 1, 0, 0)) < 0; break;
            case "^":
                int count = match.Groups[2].Value.Split('.').Length;
                var ceiling = target!.Major > 0 || count == 1 ? new Version(target!.Major + 1, 0, 0, 0) :
                    target.Minor > 0 || count == 2 ? new Version(0, target.Minor + 1, 0, 0) : new Version(0, 0, target.Build + 1, 0);
                result = cmp >= 0 && version.CompareTo(ceiling) < 0; break;
            default: result = cmp == 0; break;
        }
        return result ? ConstraintMatch.Match : ConstraintMatch.Mismatch;
    }
    public static ConstraintMatch Or(IEnumerable<ConstraintMatch> values)
    {
        var items = values.ToArray();
        return items.Length == 0 ? ConstraintMatch.Unknown : items.Contains(ConstraintMatch.Match) ? ConstraintMatch.Match : items.Contains(ConstraintMatch.Unknown) ? ConstraintMatch.Unknown : ConstraintMatch.Mismatch;
    }
    private static ConstraintMatch And(IEnumerable<ConstraintMatch> values)
    {
        var items = values.ToArray();
        return items.Contains(ConstraintMatch.Mismatch) ? ConstraintMatch.Mismatch : items.Contains(ConstraintMatch.Unknown) ? ConstraintMatch.Unknown : ConstraintMatch.Match;
    }
    private static bool TryVersion(string text, out Version? value)
    {
        value = null;
        var core = text.Split('+')[0];
        if (!Regex.IsMatch(core, @"^\d+(?:\.\d+){0,3}$")) return false;
        var parts = core.Split('.');
        if (parts.Length == 1) core += ".0";
        if (!Version.TryParse(core, out var parsed)) return false;
        value = new(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision)); return true;
    }
    private static Version Parse(string text) { TryVersion(text, out var version); return version!; }
}