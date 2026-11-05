using System.Text.RegularExpressions;

namespace PTenoLauncher.Infrastructure.Mods;

public sealed record ModIdentityConflict(string Id, ModCartItem First, ModCartItem Second)
{
    private static readonly Regex PathLike = new(@"[\\]|[a-z]:|(?:^|\s)/\S|://", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // These labels may be persisted. Use metadata and basenames only, never input paths or exception text.
    private static string Label(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) || PathLike.IsMatch(value) || value.Any(char.IsControl)
            ? fallback : value[..Math.Min(value.Length, 160)];

    private static string Describe(ModCartItem item)
    {
        var mod = item.Inspection;
        var file = Label(mod.FileName, "JAR");
        var name = Label(item.CatalogTitle, Label(mod.Name, file));
        var version = Label(mod.Version, "?");
        var reason = Label(item.Reason, "Вибраний файл");
        return $"{name} {version} ({file} · {mod.SourceStatus} · {reason})";
    }

    public string Summary => $"Несумісні моди: {Describe(First)} ↔ {Describe(Second)}";
    public string Technical => $"{Summary} [Mod ID: {Label(Id, "?")}]";
}

public sealed class ModIdentityConflictException(IReadOnlyList<ModIdentityConflict> conflicts)
    : InvalidOperationException(string.Join(Environment.NewLine, conflicts.Take(8).Select(c => c.Summary)))
{
    public string Detail => string.Join(Environment.NewLine, conflicts.Take(8).Select(c => c.Technical));
}
