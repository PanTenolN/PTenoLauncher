using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Mods;

/// <summary>Public read-only v2 API; metadata is cached, no account/API key/AI required.</summary>
public sealed class ModrinthClient(LauncherPaths paths, VerifiedDownloader downloads, NetworkPolicy network) : IModCatalog
{
    private sealed record MetadataCache(DateTimeOffset FetchedAt, string Sha256, byte[] Body);
    private readonly SemaphoreSlim requestGate = new(1, 1);
    private DateTimeOffset lastRequest;
    public bool UsedCache { get; private set; }
    public void BeginOperation() => UsedCache = false;
    private const int MaxMetadata = 8 * 1024 * 1024;
    private static string Identifier(string value)
    {
        if (value.Length is < 1 or > 128 || !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new InvalidDataException("Некоректний Modrinth ID або slug.");
        return Uri.EscapeDataString(value);
    }
    public static string LoaderName(ModLoader loader) => loader switch
    {
        ModLoader.Fabric => "fabric", ModLoader.Forge => "forge", ModLoader.NeoForge => "neoforge",
        ModLoader.Quilt => "quilt", _ => throw new InvalidOperationException("Для модів потрібен Mod Loader.")
    };
    private string CachePath(string route) => SafePath.Join(paths.Cache,
        "modrinth/v2/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(route))).ToLowerInvariant() + ".json");
    private Task CacheAsync(string route, byte[] bytes, CancellationToken ct) =>
        AtomicFile.WriteJsonAsync(CachePath(route), new MetadataCache(DateTimeOffset.UtcNow,
            Convert.ToHexString(SHA256.HashData(bytes)), bytes), ct);
    private async Task<JsonDocument> GetAsync(string route, CancellationToken ct)
    {
        await requestGate.WaitAsync(ct);
        try
        {
            MetadataCache? cached = null;
            try
            {
                cached = await AtomicFile.ReadJsonAsync<MetadataCache>(CachePath(route), ct);
                if (cached is not null && (cached.Body.Length > MaxMetadata ||
                    !Convert.ToHexString(SHA256.HashData(cached.Body)).Equals(cached.Sha256, StringComparison.Ordinal))) cached = null;
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException) { cached = null; }
            if (network.Offline || cached is not null && DateTimeOffset.UtcNow - cached.FetchedAt < TimeSpan.FromMinutes(15))
            {
                if (cached is null) throw new InvalidDataException("Немає кешованих метаданих Modrinth. Під'єднайте інтернет для цієї операції.");
                UsedCache = true; return JsonDocument.Parse(cached.Body);
            }
            byte[] body;
            try
            {
                // At most four requests per second; no unbounded retry on rate-limit errors.
                var delay = TimeSpan.FromMilliseconds(250) - (DateTimeOffset.UtcNow - lastRequest);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
                lastRequest = DateTimeOffset.UtcNow;
                try { body = await downloads.GetBytesAsync(new Uri("https://api.modrinth.com/v2/" + route), MaxMetadata, ct, DownloadSource.Modrinth); }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { body = "null"u8.ToArray(); }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && cached is not null && ex is HttpRequestException or TaskCanceledException)
            {
                UsedCache = true; return JsonDocument.Parse(cached.Body);
            }
            using (JsonDocument.Parse(body)) { } // Never commit malformed JSON.
            await CacheAsync(route, body, ct);
            return JsonDocument.Parse(body);
        }
        finally { requestGate.Release(); }
    }
    public async Task<IReadOnlyList<CatalogSearchHit>> SearchAsync(string query, string minecraft, ModLoader loader, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 200) throw new ArgumentException("Введіть запит до 200 символів.");
        var facets = JsonSerializer.Serialize(new[] { new[] { "project_type:mod" }, new[] { "versions:" + minecraft }, new[] { "categories:" + LoaderName(loader) } });
        using var json = await GetAsync("search?query=" + Uri.EscapeDataString(query.Trim()) + "&limit=20&facets=" + Uri.EscapeDataString(facets), ct);
        return json.RootElement.GetProperty("hits").EnumerateArray().Select(h =>
            new CatalogSearchHit(h.GetProperty("project_id").GetString()!, h.GetProperty("slug").GetString()!,
                h.GetProperty("title").GetString()!, h.GetProperty("description").GetString()!)).ToArray();
    }
    public async Task<CatalogProject> GetProjectAsync(string id, CancellationToken ct = default)
    {
        var route = "project/" + Identifier(id);
        using var doc = await GetAsync(route, ct);
        var r = doc.RootElement;
        if (r.ValueKind == JsonValueKind.Null) throw new InvalidDataException("Проєкт Modrinth не знайдено: " + id);
        if (r.GetProperty("project_type").GetString() != "mod") throw new InvalidDataException("Цей каталог підтримує лише моди.");
        var p = new CatalogProject(r.GetProperty("id").GetString()!, r.GetProperty("slug").GetString()!,
            r.GetProperty("title").GetString()!, !r.TryGetProperty("client_side", out var c) || c.GetString() != "unsupported");
        _ = Identifier(p.Id);
        if (id != p.Id) await CacheAsync("project/" + p.Id, Encoding.UTF8.GetBytes(r.GetRawText()), ct);
        return p;
    }
    public async Task<IReadOnlyList<CatalogModVersion>> GetVersionsAsync(string projectId, string minecraft, ModLoader loader, CancellationToken ct = default)
    {
        var route = "project/" + Identifier(projectId) + "/version?game_versions=" +
            Uri.EscapeDataString(JsonSerializer.Serialize(new[] { minecraft })) + "&loaders=" +
            Uri.EscapeDataString(JsonSerializer.Serialize(new[] { LoaderName(loader) }));
        using var doc = await GetAsync(route, ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Недоступний список версій Modrinth.");
        if (doc.RootElement.GetArrayLength() > 5000) throw new InvalidDataException("Завеликий список версій.");
        var versions = new List<CatalogModVersion>();
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            // Every selectable version has validated download metadata. Unsupported artifacts aren't candidates.
            try
            {
                var version = ParseVersion(r);
                if (version.ProjectId != projectId) throw new InvalidDataException("Версія належить іншому проєкту.");
                versions.Add(version);
                await CacheAsync("version/" + version.Id, Encoding.UTF8.GetBytes(r.GetRawText()), ct);
            }
            catch (InvalidDataException) { }
        }
        return versions;
    }
    public async Task<CatalogModVersion> GetVersionAsync(string id, CancellationToken ct = default)
    {
        using var doc = await GetAsync("version/" + Identifier(id), ct);
        if (doc.RootElement.ValueKind == JsonValueKind.Null) throw new InvalidDataException("Версія Modrinth недоступна: " + id);
        var version = ParseVersion(doc.RootElement);
        if (version.Id != id) throw new InvalidDataException("Невідповідний ID версії.");
        return version;
    }
    public async Task<CatalogModVersion?> FindBySha512Async(string hash, CancellationToken ct = default)
    {
        if (hash.Length != 128 || !hash.All(Uri.IsHexDigit)) throw new ArgumentException("Потрібен SHA512.");
        using var doc = await GetAsync("version_file/" + hash.ToLowerInvariant() + "?algorithm=sha512", ct);
        if (doc.RootElement.ValueKind == JsonValueKind.Null) return null;
        return ParseVersion(doc.RootElement, hash);
    }
    private CatalogModVersion ParseVersion(JsonElement r, string? matchingHash = null)
    {
        var id = r.GetProperty("id").GetString()!; var project = r.GetProperty("project_id").GetString()!;
        _ = Identifier(id); _ = Identifier(project);
        if (r.TryGetProperty("status", out var status) && status.GetString() is not ("listed" or "unlisted" or "archived"))
            throw new InvalidDataException("Версія не опублікована.");
        var files = r.GetProperty("files").EnumerateArray().ToArray();
        var candidates = files.Where(f => f.GetProperty("filename").GetString()!.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) &&
            (!f.TryGetProperty("file_type", out var type) || type.ValueKind == JsonValueKind.Null || type.GetString() == "unknown")).ToArray();
        if (matchingHash is not null) candidates = candidates.Where(f => f.GetProperty("hashes").TryGetProperty("sha512", out var h) && string.Equals(h.GetString(), matchingHash, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length == 0) throw new InvalidDataException("Немає перевіреного JAR цієї версії.");
        var file = candidates.FirstOrDefault(f => f.TryGetProperty("primary", out var p) && p.GetBoolean());
        if (file.ValueKind == JsonValueKind.Undefined) file = candidates[0];
        var name = file.GetProperty("filename").GetString()!;
        if (name.Contains('/') || name.Contains('\\')) throw new InvalidDataException("Некоректне ім'я JAR.");
        _ = SafePath.Join(paths.Cache, "modrinth/filename-check/" + name);
        var url = new Uri(file.GetProperty("url").GetString()!);
        TrustedSources.Validate(url, DownloadSource.Modrinth);
        if (url.Host != "cdn.modrinth.com") throw new InvalidDataException("JAR має надходити з офіційного CDN Modrinth.");
        var hash = file.GetProperty("hashes").GetProperty("sha512").GetString()!;
        var size = file.GetProperty("size").GetInt64();
        if (size is <= 0 or > 512L * 1024 * 1024 || hash.Length != 128 || !hash.All(Uri.IsHexDigit))
            throw new InvalidDataException("Некоректний SHA512 або розмір JAR.");
        var dependencies = r.GetProperty("dependencies").EnumerateArray().Select(d => new CatalogDependency(
            GetString(d, "project_id"), GetString(d, "version_id"), d.GetProperty("dependency_type").GetString()!, GetString(d, "file_name"))).ToArray();
        if (dependencies.Length > 256 || dependencies.Any(d => d.Kind is not ("required" or "optional" or "incompatible" or "embedded")))
            throw new InvalidDataException("Непідтримувані залежності.");
        return new(id, project, r.GetProperty("version_number").GetString()!, r.GetProperty("name").GetString()!,
            r.GetProperty("date_published").GetDateTimeOffset(), r.GetProperty("version_type").GetString()!,
            r.GetProperty("game_versions").EnumerateArray().Select(x => x.GetString()!).ToArray(),
            r.GetProperty("loaders").EnumerateArray().Select(x => x.GetString()!).ToArray(),
            dependencies, new(name, url, size, hash.ToLowerInvariant()));
    }
    private static string? GetString(JsonElement r, string name) => r.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
