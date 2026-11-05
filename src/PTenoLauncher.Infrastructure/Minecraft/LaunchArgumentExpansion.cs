using System.Text.RegularExpressions;

namespace PTenoLauncher.Infrastructure.Minecraft;

/// <summary>Metadata expansion only. Does not authenticate, authorize, or start a game.</summary>
public static partial class LaunchArgumentExpansion
{
    [GeneratedRegex(@"\$\{([^}]+)\}", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Placeholder();
    public static string Expand(string argument, IReadOnlyDictionary<string, string> values)
    {
        if (argument is null || argument.Length > 65536 || argument.Contains('\0')) throw new InvalidDataException("Invalid launch argument.");
        // One pass: a value containing ${...} remains literal and is never recursively expanded.
        return Placeholder().Replace(argument, m => values.TryGetValue(m.Groups[1].Value, out var value)
            ? value : throw new NotSupportedException("Непідтримуваний параметр запуску: " + m.Groups[1].Value));
    }
}
