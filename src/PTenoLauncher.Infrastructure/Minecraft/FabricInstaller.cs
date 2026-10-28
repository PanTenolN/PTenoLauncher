using System.IO.Compression;
using PTenoLauncher.Infrastructure.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed record FabricLoaderVersion(string Version, bool Stable, int MinimumJava)
{
    public override string ToString() => Version + (Stable ? " (stable)" : " (experimental)");
}

/// <summary>Official Fabric profile composed over a verified Mojang Vanilla receipt; never executes installers.</summary>
public sealed class FabricInstaller(LauncherPaths paths, VerifiedDownloader downloads, NetworkPolicy network,
    VanillaInstaller vanilla, ILauncherStore store)
{
    private sealed record Metadata(DateTimeOffset At, string Sha256, byte[] Body);
    public bool UsedCachedMetadata { get; private set; }
    private static string Segment(string value)
    {
        if (value.Length is < 1 or > 128 || value is "." or ".." || !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '+'))
            throw new InvalidDataException("Некоректна версія Fabric/Minecraft.");
        return Uri.EscapeDataString(value);
    }
    private async Task<byte[]> MetadataAsync(Uri uri, int maximum, CancellationToken ct)
    {
        TrustedSources.Validate(uri, DownloadSource.Fabric);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
        var file = SafePath.Join(paths.Cache, "fabric/metadata/" + key + ".json");
        Metadata? cached = null;
        try { cached = await AtomicFile.ReadJsonAsync<Metadata>(file, ct); }
        catch (JsonException) { }
        if (cached is not null && (cached.Body is null || cached.Body.Length > maximum || Convert.ToHexString(SHA256.HashData(cached.Body)) != cached.Sha256)) cached = null;
        if (network.Offline || cached is not null && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromMinutes(15))
        {
            if (cached is null) throw new InvalidDataException("Немає кешованих метаданих Fabric. Потрібна онлайн-підготовка.");
            UsedCachedMetadata = true; return cached.Body;
        }
        byte[] body;
        try { body = await downloads.GetBytesAsync(uri, maximum, ct, DownloadSource.Fabric); }
        catch (Exception ex) when (!ct.IsCancellationRequested && cached is not null && ex is HttpRequestException or TaskCanceledException)
        { UsedCachedMetadata = true; return cached.Body; }
        await AtomicFile.WriteJsonAsync(file, new Metadata(DateTimeOffset.UtcNow, Convert.ToHexString(SHA256.HashData(body)), body), ct);
        return body;
    }
    public async Task<IReadOnlyList<FabricLoaderVersion>> GetVersionsAsync(string minecraft, CancellationToken ct = default)
    {
        UsedCachedMetadata = false;
        using var doc = JsonDocument.Parse(await MetadataAsync(new("https://meta.fabricmc.net/v2/versions/loader/" + Segment(minecraft)), 8 * 1024 * 1024, ct));
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() > 4096)
            throw new InvalidDataException("Некоректний каталог Fabric.");
        var result = new List<FabricLoaderVersion>();
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            var loader = entry.GetProperty("loader"); var version = loader.GetProperty("version").GetString()!;
            _ = Segment(version);
            var java = entry.GetProperty("launcherMeta").TryGetProperty("min_java_version", out var j) ? j.GetInt32() : 8;
            if (java is < 8 or > 100) throw new InvalidDataException("Некоректна вимога Java.");
            result.Add(new(version, loader.GetProperty("stable").GetBoolean(), java));
        }
        return result.DistinctBy(v => v.Version).ToArray(); // Preserve the official newest-first order.
    }
    public async Task<InstallationReceipt> InstallAsync(GameProfile profile, string? loaderVersion = null,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        ProfileRules.Validate(profile);
        if (profile.Loader != ModLoader.Fabric) throw new InvalidOperationException("Потрібен профіль Fabric.");
        using var lease = new ProfileUsage(paths).Acquire(profile.Id); new ProfileUsage(paths).CheckWorldLocks(profile.Id);
        InstallationReceipt? previous = null;
        try { previous = await AtomicFile.ReadJsonAsync<InstallationReceipt>(paths.Receipt(profile.Id), ct); if (previous is not null) ReceiptValidator.Validate(paths, previous); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { previous = null; }
        loaderVersion ??= previous is { Loader: ModLoader.Fabric } ? previous.LoaderVersion : null;
        try
        {
            var basis = await vanilla.InstallUnderLeaseAsync(profile with { Loader = ModLoader.Vanilla }, progress, ct);
            return await PrepareUnderLeaseAsync(profile, basis, loaderVersion, progress, ct);
        }
        catch
        {
            if (previous is not null) await AtomicFile.WriteJsonAsync(paths.Receipt(profile.Id), previous, CancellationToken.None);
            throw;
        }
    }
    public async Task<InstallationReceipt> PrepareAsync(GameProfile profile, InstallationReceipt verifiedVanilla,
        string? loaderVersion = null, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        ProfileRules.Validate(profile);
        using var lease = new ProfileUsage(paths).Acquire(profile.Id); new ProfileUsage(paths).CheckWorldLocks(profile.Id);
        return await PrepareUnderLeaseAsync(profile, verifiedVanilla, loaderVersion, progress, ct);
    }
    private async Task<InstallationReceipt> PrepareUnderLeaseAsync(GameProfile profile, InstallationReceipt basis,
        string? version, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        if (profile.Loader != ModLoader.Fabric || basis.Loader != ModLoader.Vanilla || basis.Version != profile.MinecraftVersion)
            throw new InvalidDataException("Fabric потребує відповідної базової Vanilla-інсталяції.");
        ReceiptValidator.Validate(paths, basis);
        foreach (var file in basis.Files)
            if (!await VerifiedDownloader.VerifyAsync(SafePath.Join(paths.Cache, file.RelativePath), file.Hash, file.Algorithm, file.Size, ct))
                throw new InvalidDataException("Базові файли Minecraft пошкоджені. Спочатку відновіть інсталяцію.");
        var versions = await GetVersionsAsync(profile.MinecraftVersion, ct);
        var selected = version is null ? versions.FirstOrDefault(v => v.Stable) : versions.FirstOrDefault(v => v.Version == version);
        if (selected is null) throw new InvalidDataException("Сумісну версію Fabric не знайдено.");
        var endpoint = new Uri("https://meta.fabricmc.net/v2/versions/loader/" + Segment(profile.MinecraftVersion) + "/" + Segment(selected.Version) + "/profile/json");
        using var doc = JsonDocument.Parse(await MetadataAsync(endpoint, 2 * 1024 * 1024, ct));
        var root = doc.RootElement;
        var main = root.GetProperty("mainClass").GetString();
        if (root.GetProperty("inheritsFrom").GetString() != profile.MinecraftVersion ||
            main is not ("net.fabricmc.loader.impl.launch.knot.KnotClient" or "net.fabricmc.loader.launch.knot.KnotClient"))
            throw new InvalidDataException("Непідтримуваний успадкований профіль Fabric.");
        var libraries = root.GetProperty("libraries").EnumerateArray().ToArray();
        if (libraries.Length is 0 or > 64) throw new InvalidDataException("Некоректний список бібліотек Fabric.");
        var files = basis.Files.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
        var classpath = new List<string>();
        foreach (var library in libraries)
        {
            var relative = MavenPath(library.GetProperty("name").GetString()!);
            var origin = new Uri(library.GetProperty("url").GetString()!);
            TrustedSources.Validate(origin, DownloadSource.Fabric);
            if (origin.Host != "maven.fabricmc.net" || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0)
                throw new InvalidDataException("Бібліотека має надходити з офіційного Maven Fabric.");
            var url = new Uri(origin, relative);
            string algorithm = "SHA512"; string hash;
            if (library.TryGetProperty("sha512", out var sha)) hash = sha.GetString()!;
            else if (library.TryGetProperty("sha256", out sha)) { hash = sha.GetString()!; algorithm = "SHA256"; }
            else if (library.TryGetProperty("sha1", out sha)) { hash = sha.GetString()!; algorithm = "SHA1"; }
            else
                hash = Encoding.ASCII.GetString(await MetadataAsync(new(url.AbsoluteUri + ".sha512"), 1024, ct))
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (hash.Length != (algorithm == "SHA512" ? 128 : algorithm == "SHA256" ? 64 : 40) || !hash.All(Uri.IsHexDigit))
                throw new InvalidDataException("Некоректна контрольна сума Fabric.");
            var cacheRelative = "libraries/fabric/" + relative;
            if (classpath.Contains(cacheRelative, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Дублікат бібліотеки Fabric.");
            var target = SafePath.Join(paths.Cache, cacheRelative);
            long size;
            if (library.TryGetProperty("size", out var knownSize))
            {
                size = knownSize.GetInt64();
                if (size is <= 0 or > 32 * 1024 * 1024) throw new InvalidDataException("Завелика бібліотека Fabric.");
                await downloads.DownloadAsync(url, target, hash, size, algorithm, progress, ct, DownloadSource.Fabric);
            }
            else
            {
                size = File.Exists(target) ? new FileInfo(target).Length : 0;
                if (size <= 0 || size > 32 * 1024 * 1024 || !await VerifiedDownloader.VerifyAsync(target, hash, algorithm, size, ct))
                {
                    var bytes = await downloads.GetBytesAsync(url, 32 * 1024 * 1024, ct, DownloadSource.Fabric);
                    var actual = algorithm switch { "SHA512" => SHA512.HashData(bytes), "SHA256" => SHA256.HashData(bytes), _ => SHA1.HashData(bytes) };
                    if (!Convert.ToHexString(actual).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Бібліотека Fabric не пройшла перевірку хешу.");
                    size = bytes.Length; await AtomicFile.WriteAsync(target, bytes, ct);
                }
                progress?.Report(new("Fabric " + relative, size, size));
            }
            using (var archive = ZipFile.OpenRead(target))
            {
                SafeArchive.Validate(archive, paths.Cache, maxExpanded: 256L * 1024 * 1024, maxEntries: 20000);
                if (relative.Contains("/fabric-loader/") && archive.GetEntry(main!.Replace('.', '/') + ".class") is null)
                    throw new InvalidDataException("У JAR Fabric не знайдено головний клас.");
            }
            files.Add(cacheRelative, new(cacheRelative, hash, algorithm, size)); classpath.Add(cacheRelative);
        }
        static IReadOnlyList<string> Arguments(JsonElement r, string kind)
        {
            if (!r.TryGetProperty("arguments", out var arguments) || !arguments.TryGetProperty(kind, out var group)) return [];
            if (group.GetArrayLength() > 128) throw new InvalidDataException("Завеликий список аргументів Fabric.");
            return group.EnumerateArray().Select(a => a.ValueKind == JsonValueKind.String && a.GetString()!.Length <= 4096 &&
                !a.GetString()!.Contains('\0') ? a.GetString()! : throw new InvalidDataException("Непідтримуваний аргумент Fabric.")).ToArray();
        }
        var receipt = basis with {
            Loader = ModLoader.Fabric, LoaderVersion = selected.Version, JavaMajor = Math.Max(basis.JavaMajor, selected.MinimumJava),
            MainClass = main!, Files = files.Values.ToArray(), Classpath = classpath.Concat(basis.Classpath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            JvmArguments = basis.JvmArguments.Concat(Arguments(root, "jvm")).ToArray(),
            GameArguments = basis.GameArguments.Concat(Arguments(root, "game")).ToArray(), InstalledAt = DateTimeOffset.UtcNow
        };
        ReceiptValidator.Validate(paths, receipt);
        await AtomicFile.WriteJsonAsync(paths.Receipt(profile.Id), receipt, ct);
        await store.AddHistoryAsync(profile.Id, "fabric.installed", selected.Version, CancellationToken.None);
        return receipt;
    }
    internal static string MavenPath(string coordinate)
    {
        var p = coordinate.Split(':');
        if (p.Length is < 3 or > 4 || p.Any(s => s.Length is < 1 or > 128 || s is "." or ".." || !s.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '+')) ||
            p[0].Split('.').Any(s => s.Length == 0))
            throw new InvalidDataException("Некоректні Maven-координати.");
        return p[0].Replace('.', '/') + "/" + p[1] + "/" + p[2] + "/" + p[1] + "-" + p[2] + (p.Length == 4 ? "-" + p[3] : "") + ".jar";
    }
}
