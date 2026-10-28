using System.Text.Json;
using System.Text.RegularExpressions;
namespace PTenoLauncher.Core;

public sealed record QuiltDependency(string? Id, string VersionsJson, bool Optional = false,
    QuiltDependency? Unless = null, IReadOnlyList<QuiltDependency>? Children = null, bool All = false,
    bool Supported = true);
public sealed record QuiltModMetadata(string Group, IReadOnlyList<QuiltDependency> Dependencies,
    IReadOnlyList<QuiltDependency> Conflicts);

/// <summary>Quilt schema-1 version expressions. Unsupported/ambiguous input stays Unknown.
/// Based on Quilt Loader 0.30.1 V1ModMetadataReader, not Fabric's predicate grammar.</summary>
public static class QuiltVersionConstraints
{
    public static ConstraintMatch Match(string? actual, string versionsJson)
    {
        if (versionsJson.Length > 16384) return ConstraintMatch.Unknown;
        try
        {
            using var doc = JsonDocument.Parse(versionsJson, new() { MaxDepth = 20 });
            int budget = 512;
            return Match(actual, doc.RootElement, 0, ref budget);
        }
        catch (JsonException) { return ConstraintMatch.Unknown; }
    }
    private static ConstraintMatch Match(string? actual, JsonElement value, int depth, ref int budget)
    {
        if (depth > 16 || --budget < 0) return ConstraintMatch.Unknown;
        if (value.ValueKind == JsonValueKind.String) return Single(actual, value.GetString()!);
        bool all = false; JsonElement values;
        if (value.ValueKind == JsonValueKind.Array) values = value; // Deprecated Quilt array is ANY.
        else if (value.ValueKind == JsonValueKind.Object)
        {
            var fields = value.EnumerateObject().ToArray();
            if (fields.Length != 1 || fields[0].Name is not ("any" or "all")) return ConstraintMatch.Unknown;
            all = fields[0].Name == "all"; values = fields[0].Value;
        }
        else return ConstraintMatch.Unknown;
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() is 0 or > 256) return ConstraintMatch.Unknown;
        var matches = new List<ConstraintMatch>();
        foreach (var child in values.EnumerateArray()) matches.Add(Match(actual, child, depth + 1, ref budget));
        // Malformed children never silently disappear from a partially supported expression.
        if (matches.Contains(ConstraintMatch.Unknown)) return ConstraintMatch.Unknown;
        return all ? matches.All(m => m == ConstraintMatch.Match) ? ConstraintMatch.Match : ConstraintMatch.Mismatch
            : VersionConstraints.Or(matches);
    }
    private static ConstraintMatch Single(string? actual, string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 1024 || text.Any(char.IsWhiteSpace) || text.Contains('|')) return ConstraintMatch.Unknown;
        if (text == "*") return ConstraintMatch.Match;
        if (actual is null) return ConstraintMatch.Unknown;
        var match = Regex.Match(text, @"^(>=|<=|>|<|=|~|\^)?(.+)$", RegexOptions.None, TimeSpan.FromSeconds(1));
        var op = match.Groups[1].Value; var target = match.Groups[2].Value;
        if (op == "=" && !target.Contains('*')) return FabricVersionConstraints.Match(actual, text);
        bool semantic = Regex.IsMatch(target, @"^\d+(?:\.\d+)*(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (op == "" && target.EndsWith(".x", StringComparison.Ordinal))
        {
            var parts = target[..^2].Split('.');
            if (parts.Length is 0 or > 16 || parts.Any(p => !int.TryParse(p, out int n) || n < 0)) return ConstraintMatch.Unknown;
            if (!int.TryParse(parts[^1], out int last) || last == int.MaxValue) return ConstraintMatch.Unknown;
            parts[^1] = (last + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Both(actual, ">=" + target[..^2], "<" + string.Join('.', parts));
        }
        if (!semantic)
            return op is "" or "=" && !text.Contains('*') && !text.Contains('[') && !text.Contains(']')
                ? actual == target ? ConstraintMatch.Match : ConstraintMatch.Mismatch : ConstraintMatch.Unknown;
        if (op is "" or "^" or "~")
        {
            var parts = target.Split('-', '+')[0].Split('.');
            if (parts.Length > 16 || parts.Any(p => !int.TryParse(p, out int n) || n < 0)) return ConstraintMatch.Unknown;
            int index = op == "~" ? 1 : 0;
            int component = index < parts.Length ? int.Parse(parts[index], System.Globalization.CultureInfo.InvariantCulture) : 0;
            if (component == int.MaxValue) return ConstraintMatch.Unknown;
            string ceiling = index == 0 ? (component + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : parts[0] + "." + (component + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            // Quilt's upper bound is a normal release, so its prereleases are below the ceiling.
            return Both(actual, ">=" + target, "<" + ceiling);
        }
        return FabricVersionConstraints.Match(actual, text);
    }
    private static ConstraintMatch Both(string actual, string lower, string upper)
    {
        var a = FabricVersionConstraints.Match(actual, lower); var b = FabricVersionConstraints.Match(actual, upper);
        return a == ConstraintMatch.Unknown || b == ConstraintMatch.Unknown ? ConstraintMatch.Unknown
            : a == ConstraintMatch.Match && b == ConstraintMatch.Match ? ConstraintMatch.Match : ConstraintMatch.Mismatch;
    }
}
