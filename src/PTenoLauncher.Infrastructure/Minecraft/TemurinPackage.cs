using System.Text.Json;

namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed record TemurinPackage(string Release, string Name, Uri DownloadUri, string Sha256, long Size)
{
    public static TemurinPackage Parse(ReadOnlyMemory<byte> bytes, int major)
    {
        if (major is < 8 or > 100 || bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("Invalid Java metadata bounds.");
        try
        {
            using var json = JsonDocument.Parse(bytes);
            if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() > 100)
                throw new InvalidDataException("Invalid Java asset list.");
            foreach (var asset in json.RootElement.EnumerateArray())
            {
                var binary = asset.GetProperty("binary");
                if (asset.GetProperty("vendor").GetString() != "eclipse" || asset.GetProperty("version").GetProperty("major").GetInt32() != major ||
                    binary.GetProperty("architecture").GetString() != "x64" || binary.GetProperty("os").GetString() != "windows" ||
                    binary.GetProperty("image_type").GetString() != "jre" || binary.GetProperty("jvm_impl").GetString() != "hotspot" ||
                    binary.GetProperty("project").GetString() != "jdk") continue;
                var package = binary.GetProperty("package");
                var name = package.GetProperty("name").GetString();
                var release = asset.GetProperty("release_name").GetString();
                var hash = package.GetProperty("checksum").GetString();
                if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '+') ||
                    string.IsNullOrWhiteSpace(release) || release.Length > 128 || release.Any(char.IsControl) ||
                    hash is null || hash.Length != 64 || !hash.All(Uri.IsHexDigit) ||
                    !Uri.TryCreate(package.GetProperty("link").GetString(), UriKind.Absolute, out var uri) ||
                    uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                    uri.Host != "github.com" || !uri.AbsolutePath.StartsWith($"/adoptium/temurin{major}-binaries/releases/download/", StringComparison.Ordinal) ||
                    !uri.AbsolutePath.EndsWith("/" + name, StringComparison.Ordinal))
                    throw new InvalidDataException("Invalid Temurin package identity or download origin.");
                var size = package.GetProperty("size").GetInt64();
                if (size is <= 0 or > 512L * 1024 * 1024) throw new InvalidDataException("Invalid Java archive size.");
                return new(release, name, uri, hash.ToLowerInvariant(), size);
            }
            throw new NotSupportedException($"No matching Temurin Java {major} Windows x64 JRE ZIP.");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("Malformed Temurin metadata.", ex); }
    }
}
