namespace PTenoLauncher.Infrastructure.Storage;

internal static class ReadableName
{
    public static string Stem(string value, string fallback, int maximum = 100)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = string.Concat(value.Select(c => char.IsControl(c) || invalid.Contains(c) || c is '/' or '\\' or ':' ? '-' : c))
            .Trim(' ', '.');
        if (name.Length > maximum)
            name = name[..(char.IsHighSurrogate(name[maximum - 1]) ? maximum - 1 : maximum)].TrimEnd(' ', '.');
        return name.Length == 0 || SafePath.IsDevice(name) || name.StartsWith(".pteno-", StringComparison.OrdinalIgnoreCase)
            ? fallback : name;
    }

    public static string Free(string directory, string stem, string extension = "", ISet<string>? reserved = null)
    {
        SafePath.RejectLinks(directory);
        var occupied = Directory.Exists(directory)
            ? Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index <= 2048; index++)
        {
            var name = stem + (index == 1 ? "" : "-" + index) + extension;
            if (occupied.Contains(name) || reserved is not null && !reserved.Add(name)) continue;
            return SafePath.Join(directory, name);
        }
        throw new IOException("ContentCollision");
    }

    internal static string? StripDigest(string stem, params string?[] digests)
    {
        var split = stem.IndexOfAny(['-', '_', ' ', '.']);
        var length = split < 0 ? stem.Length : split;
        if (!LooksTechnical(stem) ||
            !digests.Any(hash => hash is not null && hash.StartsWith(stem[..length], StringComparison.OrdinalIgnoreCase))) return null;
        return split < 0 ? "" : stem[(split + 1)..];
    }

    internal static bool LooksTechnical(string stem)
    {
        var split = stem.IndexOfAny(['-', '_', ' ', '.']);
        var prefix = split < 0 ? stem : stem[..split];
        return prefix.Length is >= 12 and <= 128 && prefix.All(Uri.IsHexDigit);
    }
}
