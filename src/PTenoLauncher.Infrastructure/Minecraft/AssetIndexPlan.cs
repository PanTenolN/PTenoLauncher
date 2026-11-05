using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

/// <summary>Static, bounded interpretation of Mojang's modern and historical asset indexes.</summary>
public sealed record AssetIndexPlan(bool Virtual, bool MapToResources, IReadOnlyList<InstalledFile> Entries)
{
    public const int MaximumEntries = 30000;
    public static void ValidateEntries(string root, IReadOnlyList<InstalledFile> entries)
    {
        if (entries is null || entries.Count > MaximumEntries) throw new InvalidDataException("Asset index is too large.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hashes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var file in entries)
        {
            if (file is null || file.RelativePath is null || file.RelativePath.Length > 200 || file.RelativePath.Contains('\\') ||
                file.RelativePath.Any(c => c is '<' or '>' or '"' or '|' or '?' or '*'))
                throw new InvalidDataException("Unsafe asset name.");
            _ = SafePath.Join(root, file.RelativePath);
            if (!names.Add(file.RelativePath) || file.Algorithm != "SHA1" || file.Hash is null || file.Hash.Length != 40 || !file.Hash.All(Uri.IsHexDigit) ||
                file.Size is < 0 or > 256L * 1024 * 1024 || (total += file.Size) > 4L * 1024 * 1024 * 1024)
                throw new InvalidDataException("Invalid or conflicting asset descriptor.");
            if (hashes.TryGetValue(file.Hash, out var size) && size != file.Size) throw new InvalidDataException("Conflicting asset hash sizes.");
            hashes[file.Hash] = file.Size;
            for (var slash = file.RelativePath.IndexOf('/'); slash >= 0; slash = file.RelativePath.IndexOf('/', slash + 1))
                directories.Add(file.RelativePath[..slash]);
        }
        if (names.Overlaps(directories)) throw new InvalidDataException("An asset is also used as a directory.");
    }
    public static string ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64 || id is "." or ".." || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            throw new InvalidDataException("Invalid asset index ID.");
        return id;
    }
    public static AssetIndexPlan Parse(byte[] bytes, string validationRoot)
    {
        if (bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("Asset index exceeds 16 MiB.");
        try
        {
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            var root = doc.RootElement;
            if (root.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != root.EnumerateObject().Count())
                throw new InvalidDataException("Duplicate asset index properties.");
            bool Flag(string name) => root.TryGetProperty(name, out var value) && value.GetBoolean();
            var entries = new List<InstalledFile>();
            foreach (var entry in root.GetProperty("objects").EnumerateObject())
            {
                if (entries.Count >= MaximumEntries) throw new InvalidDataException("Asset index is too large.");
                if (entry.Value.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != entry.Value.EnumerateObject().Count())
                    throw new InvalidDataException("Duplicate asset descriptor properties.");
                entries.Add(new(entry.Name, entry.Value.GetProperty("hash").GetString()!, "SHA1", entry.Value.GetProperty("size").GetInt64()));
            }
            ValidateEntries(validationRoot, entries);
            return new(Flag("virtual"), Flag("map_to_resources"), entries);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { throw new InvalidDataException("Invalid asset index metadata.", e); }
    }
}
