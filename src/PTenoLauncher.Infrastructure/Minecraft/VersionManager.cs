using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed class VersionManager(LauncherPaths paths, VerifiedDownloader downloads)
{
    public static readonly Uri ManifestUri = new("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
    private string CatalogFile => SafePath.Join(paths.Cache, "metadata/catalog.json");
    public Task<VersionCatalog?> GetCachedAsync(CancellationToken ct = default) => AtomicFile.ReadJsonAsync<VersionCatalog>(CatalogFile, ct);
    public async Task<VersionCatalog> RefreshAsync(CancellationToken ct = default)
    {
        var bytes = await downloads.GetBytesAsync(ManifestUri, 8 * 1024 * 1024, ct);
        using var json = JsonDocument.Parse(bytes); var root = json.RootElement;
        var versions = root.GetProperty("versions").EnumerateArray().Select(e =>
            new VersionEntry(e.GetProperty("id").GetString()!, e.GetProperty("type").GetString()!,
                new Uri(e.GetProperty("url").GetString()!), e.GetProperty("sha1").GetString()!, e.GetProperty("releaseTime").GetDateTimeOffset())).ToArray();
        if (versions.Length is 0 or > 10000) throw new InvalidDataException("Некоректний каталог версій.");
        foreach (var entry in versions)
        {
            NetworkPolicy.ValidateDownloadUri(entry.Url);
            if (entry.Sha1.Length != 40 || !entry.Sha1.All(Uri.IsHexDigit)) throw new InvalidDataException("Некоректний SHA-1 маніфесту.");
        }
        var catalog = new VersionCatalog(DateTimeOffset.UtcNow, root.GetProperty("latest").GetProperty("release").GetString()!, versions, root.GetProperty("latest").TryGetProperty("snapshot", out var snapshot) ? snapshot.GetString() : null);
        await AtomicFile.WriteJsonAsync(CatalogFile, catalog, ct);
        return catalog;
    }
    public async Task<JsonDocument> GetVersionAsync(VersionEntry entry, CancellationToken ct = default)
    {
        var path = SafePath.Join(paths.Cache, $"metadata/versions/{entry.Sha1}.json");
        byte[] bytes;
        if (File.Exists(path)) bytes = await File.ReadAllBytesAsync(path, ct);
        else bytes = await downloads.GetBytesAsync(entry.Url, 8 * 1024 * 1024, ct);
        if (!Convert.ToHexString(SHA1.HashData(bytes)).Equals(entry.Sha1, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Пошкоджений маніфест версії. Оновіть каталог або видаліть пошкоджений кеш метаданих.");
        var json = JsonDocument.Parse(bytes);
        if (json.RootElement.GetProperty("id").GetString() != entry.Id) { json.Dispose(); throw new InvalidDataException("Ідентифікатор версії не відповідає каталогу."); }
        await AtomicFile.WriteAsync(path, bytes, ct);
        return json;
    }
}