using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed record ForgeLoaderVersion(ModLoader Loader, string Version, string? MinecraftVersion)
{
    public override string ToString() => Version;
}
public sealed record ForgeInstallerArtifact(string RelativePath, string Hash, string Algorithm, long Size);

/// <summary>Official Maven catalogs and checksum-pinned installers. Never executes JAR files.</summary>
public sealed class ForgeCatalog(LauncherPaths paths, VerifiedDownloader downloads, NetworkPolicy network)
{
    private sealed record CatalogCache(DateTimeOffset At, string Sha256, byte[] Body);
    public bool UsedCachedMetadata { get; private set; }
    public async Task<string?> GetRecommendedForgeVersionAsync(string minecraft, CancellationToken ct)
    {
        var cache = SafePath.Join(paths.Cache, "forge/catalog/promotions.json");
        try
        {
            byte[]? body;
            if (network.Offline)
                body = await AtomicFile.ReadJsonAsync<byte[]>(cache, ct);
            else
                body = await downloads.GetBytesAsync(new("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json"), 128 * 1024, ct, DownloadSource.Forge);
            if (body is null || body.Length > 128 * 1024) return null;
            using var json = System.Text.Json.JsonDocument.Parse(body);
            if (!json.RootElement.TryGetProperty("promos", out var promos) || promos.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (!network.Offline) await AtomicFile.WriteJsonAsync(cache, body, ct);
            return promos.TryGetProperty(minecraft + "-recommended", out var version) && version.ValueKind == System.Text.Json.JsonValueKind.String
                ? minecraft + "-" + version.GetString() : null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is IOException or HttpRequestException or System.Text.Json.JsonException or TaskCanceledException or InvalidOperationException) { return null; }
    }
    public async Task<IReadOnlyList<ForgeLoaderVersion>> GetVerifiedNeoForgeVersionsAsync(string minecraft,
        bool refresh = false, CancellationToken ct = default)
    {
        var catalog = await GetVersionsAsync(ModLoader.NeoForge, minecraft, refresh, ct);
        // A numeric series only narrows the probe set. Compatibility comes from the verified installer.
        var parts = minecraft.Split('.');
        if (parts.Length is < 2 or > 3 || parts[0] != "1" ||
            !parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit))) return [];
        var series = parts[1] + "." + (parts.Length == 3 ? parts[2] : "0") + ".";
        var verified = new List<ForgeLoaderVersion>();
        foreach (var candidate in catalog.Where(v => v.Version.StartsWith(series, StringComparison.Ordinal)).Take(4))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var installer = await GetInstallerAsync(ModLoader.NeoForge, candidate.Version, ct: ct);
                await ForgeInstallerMetadata.InspectAsync(SafePath.Join(paths.Cache, installer.RelativePath),
                    SafePath.Join(paths.Cache, "forge/validation"), ModLoader.NeoForge, candidate.Version, minecraft, ct);
                verified.Add(candidate with { MinecraftVersion = minecraft });
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is IOException or HttpRequestException or InvalidDataException or InvalidOperationException)
            { /* An unavailable or unsupported installer is not a compatible result. */ }
        }
        return verified;
    }
    public async Task<IReadOnlyList<ForgeLoaderVersion>> GetVersionsAsync(ModLoader loader, string? minecraft = null, bool refresh = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        UsedCachedMetadata = false; var uri = ForgeMaven.Metadata(loader);
        var file = SafePath.Join(paths.Cache, $"forge/catalog/{loader}.json");
        CatalogCache? cached = null;
        try { cached = await AtomicFile.ReadJsonAsync<CatalogCache>(file, ct); }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException) { }
        if (cached is not null && (cached.Body is null || cached.Body.Length > 8 * 1024 * 1024 || cached.Sha256 != Convert.ToHexString(SHA256.HashData(cached.Body)))) cached = null;
        byte[] body;
        if (network.Offline || !refresh && cached is not null && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromMinutes(15))
        {
            if (cached is null) throw new InvalidDataException("Official loader metadata is not cached. Prepare it online first.");
            body = cached.Body; UsedCachedMetadata = true;
        }
        else
        {
            try
            {
                body = await downloads.GetBytesAsync(uri, 8 * 1024 * 1024, ct, ForgeMaven.Source(loader));
                _ = Parse(loader, body); // Reject unsafe/invalid XML before replacing a working cache.
                await AtomicFile.WriteJsonAsync(file, new CatalogCache(DateTimeOffset.UtcNow, Convert.ToHexString(SHA256.HashData(body)), body), ct);
            }
            catch (Exception e) when (!ct.IsCancellationRequested && cached is not null && e is HttpRequestException or TaskCanceledException)
            { body = cached.Body; UsedCachedMetadata = true; }
        }
        var versions = Parse(loader, body);
        // NeoForge does not publish an MC binding in this index. The installer must confirm it, never FML/version-number guessing.
        return versions.Where(v => loader == ModLoader.NeoForge || minecraft is null || v.MinecraftVersion == minecraft).Reverse().ToArray();
    }
    public static IReadOnlyList<ForgeLoaderVersion> Parse(ModLoader loader, byte[] bytes)
    {
        _ = ForgeMaven.Source(loader);
        if (bytes.Length > 8 * 1024 * 1024) throw new InvalidDataException("Maven index exceeds its size bound.");
        try
        {
            using var input = new MemoryStream(bytes);
            using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 });
            var root = XDocument.Load(reader).Root ?? throw new InvalidDataException("Missing Maven metadata.");
            var group = loader == ModLoader.Forge ? "net.minecraftforge" : "net.neoforged";
            var artifact = loader == ModLoader.Forge ? "forge" : "neoforge";
            if (root.Name != "metadata" || root.Elements("groupId").Count() != 1 || root.Element("groupId")?.Value != group ||
                root.Elements("artifactId").Count() != 1 || root.Element("artifactId")?.Value != artifact || root.Elements("versioning").Count() != 1 ||
                root.Element("versioning")!.Elements("versions").Count() != 1) throw new InvalidDataException("Maven metadata identity mismatch.");
            var entries = root.Element("versioning")!.Element("versions")!.Elements("version").ToArray();
            if (entries.Length is 0 or > 20000) throw new InvalidDataException("Invalid Maven version count.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var result = new List<ForgeLoaderVersion>();
            foreach (var entry in entries)
            {
                var version = ForgeMaven.Segment(entry.Value);
                if (!names.Add(version)) throw new InvalidDataException("Duplicate Maven version.");
                string? mc = null;
                if (loader == ModLoader.Forge)
                {
                    var separator = version.IndexOf('-');
                    if (separator <= 0 || separator == version.Length - 1) throw new InvalidDataException("Forge version is missing its Minecraft binding.");
                    mc = version[..separator];
                }
                result.Add(new(loader, version, mc));
            }
            return result;
        }
        catch (XmlException e) { throw new InvalidDataException("Invalid or unsafe Maven XML.", e); }
    }
    public async Task<ForgeInstallerArtifact> GetInstallerAsync(ModLoader loader, string version, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var artifact = ForgeMaven.Installer(loader, version); var uri = new Uri(ForgeMaven.Repository(loader) + artifact.Path);
        ForgeMaven.ValidateArtifactUri(uri, artifact, loader);
        var catalog = await GetVersionsAsync(loader, ct: ct);
        if (!catalog.Any(v => v.Version == version)) throw new InvalidDataException("Selected loader is not present in the official cached catalog.");
        var descriptor = SafePath.Join(paths.Cache, $"forge/installers/{loader}/{version}.json");
        ForgeInstallerArtifact? prior = null;
        try { prior = await AtomicFile.ReadJsonAsync<ForgeInstallerArtifact>(descriptor, ct); }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException) { }
        if (prior is not null && ValidArtifact(prior))
            if (await VerifiedDownloader.VerifyAsync(SafePath.Join(paths.Cache, prior.RelativePath), prior.Hash, prior.Algorithm, prior.Size, ct)) return prior;
        network.RequireOnline();
        string? hash = null, algorithm = null;
        foreach (var candidate in new[] { ("SHA512", ".sha512", 128), ("SHA256", ".sha256", 64), ("SHA1", ".sha1", 40) })
        {
            try
            {
                var text = Encoding.ASCII.GetString(await downloads.GetBytesAsync(new(uri.AbsoluteUri + candidate.Item2), 2048, ct, ForgeMaven.Source(loader))).Trim();
                var fields = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length is < 1 or > 2 || fields[0].Length != candidate.Item3 || !fields[0].All(Uri.IsHexDigit) ||
                    fields.Length == 2 && fields[1].TrimStart('*') != System.IO.Path.GetFileName(artifact.Path)) throw new InvalidDataException("Invalid official installer checksum.");
                hash = fields[0].ToLowerInvariant(); algorithm = candidate.Item1; break;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { }
        }
        if (hash is null || algorithm is null) throw new InvalidDataException("No supported official installer checksum is available.");
        var bytes = await downloads.GetBytesAsync(uri, 128 * 1024 * 1024, ct, ForgeMaven.Source(loader));
        var actual = algorithm switch { "SHA512" => SHA512.HashData(bytes), "SHA256" => SHA256.HashData(bytes), _ => SHA1.HashData(bytes) };
        if (!Convert.ToHexString(actual).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Installer checksum mismatch.");
        var relative = $"forge/installers/{algorithm.ToLowerInvariant()}/{hash}.jar";
        await AtomicFile.WriteAsync(SafePath.Join(paths.Cache, relative), bytes, ct);
        var receipt = new ForgeInstallerArtifact(relative, hash, algorithm, bytes.LongLength);
        await AtomicFile.WriteJsonAsync(descriptor, receipt, ct);
        progress?.Report(new(System.IO.Path.GetFileName(artifact.Path), bytes.LongLength, bytes.LongLength));
        return receipt;
    }
    private static bool ValidArtifact(ForgeInstallerArtifact value) => value.Hash is not null && value.Algorithm is "SHA1" or "SHA256" or "SHA512" &&
        value.Hash.Length == (value.Algorithm == "SHA1" ? 40 : value.Algorithm == "SHA256" ? 64 : 128) && value.Hash.All(Uri.IsHexDigit) &&
        value.Size is > 0 and <= 128 * 1024 * 1024 && value.RelativePath == $"forge/installers/{value.Algorithm.ToLowerInvariant()}/{value.Hash}.jar";
}
