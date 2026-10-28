namespace PTenoLauncher.Infrastructure.Storage;

public static class SafePath
{
    public static string Join(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') || relative.Contains('\\') || relative.Any(char.IsControl))
            throw new InvalidDataException("Небезпечний відносний шлях.");
        var segments = relative.Split('/');
        if (segments.Any(s => s is "" or "." or ".." || s.EndsWith('.') || s.EndsWith(' ') ||
            s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || IsDevice(s)))
            throw new InvalidDataException("Небезпечний компонент шляху.");
        var fullRoot = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(fullRoot, Path.Combine(segments)));
        if (!full.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Шлях виходить за межі сховища.");
        RejectLinks(full);
        return full;
    }
    private static bool IsDevice(string s)
    {
        var stem = s.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '0' and <= '9');
    }
    public static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Посилання та junction у керованих шляхах не підтримуються.");
    }
}