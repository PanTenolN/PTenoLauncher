using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed class VanillaInstaller(LauncherPaths paths, VersionManager versions, VerifiedDownloader downloader, ILauncherStore store)
{
    public async Task<InstallationReceipt> InstallAsync(GameProfile profile, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        ProfileRules.Validate(profile);
        using var lease = new ProfileUsage(paths).Acquire(profile.Id); new ProfileUsage(paths).CheckWorldLocks(profile.Id);
        return await InstallUnderLeaseAsync(profile, progress, ct);
    }
    internal async Task<InstallationReceipt> InstallUnderLeaseAsync(GameProfile profile, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        ProfileRules.Validate(profile);
        if (profile.Loader != ModLoader.Vanilla) throw new NotSupportedException("Встановлення цього Mod Loader ще не реалізовано.");
        var catalog = await versions.GetCachedAsync(ct) ?? throw new InvalidOperationException("Оновіть каталог Minecraft.");
        var entry = catalog.Versions.SingleOrDefault(v => v.Id == profile.MinecraftVersion) ?? throw new InvalidOperationException("Версії немає у кеші каталогу.");
        using var manifest = await versions.GetVersionAsync(entry, ct);
        var root = manifest.RootElement;
        bool modern = root.TryGetProperty("arguments", out var arguments);
        if (!modern && !root.TryGetProperty("minecraftArguments", out _)) throw new NotSupportedException("Непідтримуваний формат arguments.");
        if (root.TryGetProperty("inheritsFrom", out _)) throw new NotSupportedException("Успадковані manifests потребують окремого адаптера.");
        var files = new Dictionary<string, InstalledFile>(StringComparer.OrdinalIgnoreCase);
        var classpath = new List<string>(); var natives = new List<string>();
        async Task<string> Fetch(JsonElement descriptor, string? explicitPath = null)
        {
            var hash = descriptor.GetProperty("sha1").GetString()!;
            if (hash.Length != 40 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Некоректний хеш артефакту.");
            var size = descriptor.GetProperty("size").GetInt64();
            var relative = explicitPath ?? $"objects/sha1/{hash[..2]}/{hash}.jar";
            await downloader.DownloadAsync(new Uri(descriptor.GetProperty("url").GetString()!), SafePath.Join(paths.Cache, relative), hash, size, progress: progress, ct: ct);
            files[relative] = new(relative, hash, "SHA1", size);
            return relative;
        }
        var clientJar = await Fetch(root.GetProperty("downloads").GetProperty("client"));
        foreach (var library in root.GetProperty("libraries").EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            if (!MinecraftRules.Allowed(library)) continue;
            if (!library.TryGetProperty("downloads", out var downloads)) throw new NotSupportedException("Бібліотека без download metadata не підтримується.");
            if (downloads.TryGetProperty("artifact", out var artifact)) classpath.Add(await Fetch(artifact));
            if (library.TryGetProperty("natives", out var nativeMap) && nativeMap.TryGetProperty("windows", out var windows))
            {
                var classifier = windows.GetString()!.Replace("$" + "{arch}", "64");
                natives.Add(await Fetch(downloads.GetProperty("classifiers").GetProperty(classifier)));
            }
        }
        classpath.Add(clientJar);
        var assetDescriptor = root.GetProperty("assetIndex");
        var assetId = assetDescriptor.GetProperty("id").GetString()!;
        var assetIndexPath = await Fetch(assetDescriptor, $"assets/indexes/{assetId}.json");
        using var assetIndex = JsonDocument.Parse(await File.ReadAllBytesAsync(SafePath.Join(paths.Cache, assetIndexPath), ct));
        if (assetIndex.RootElement.TryGetProperty("virtual", out var virtualFlag) && virtualFlag.GetBoolean() ||
            assetIndex.RootElement.TryGetProperty("map_to_resources", out var mapping) && mapping.GetBoolean())
            throw new NotSupportedException("Legacy virtual assets ще не підтримуються.");
        var assetFiles = new System.Collections.Concurrent.ConcurrentBag<InstalledFile>();
        var assets = assetIndex.RootElement.GetProperty("objects").EnumerateObject().ToArray()
            .GroupBy(a => a.Value.GetProperty("hash").GetString(), StringComparer.Ordinal).Select(g => g.First()).ToArray();
        await Parallel.ForEachAsync(assets, new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = ct }, async (asset, token) =>
        {
            var hash = asset.Value.GetProperty("hash").GetString()!;
            if (hash.Length != 40 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Некоректний хеш ресурсу.");
            var size = asset.Value.GetProperty("size").GetInt64();
            var relative = $"assets/objects/{hash[..2]}/{hash}";
            await downloader.DownloadAsync(new($"https://resources.download.minecraft.net/{hash[..2]}/{hash}"),
                SafePath.Join(paths.Cache, relative), hash, size, progress: progress, ct: token);
            assetFiles.Add(new(relative, hash, "SHA1", size));
        });
        foreach (var file in assetFiles) files[file.RelativePath] = file;
        var jvm = modern ? MinecraftRules.Arguments(arguments.GetProperty("jvm")).ToList() : new List<string> { "-Djava.library.path=$" + "{natives_directory}", "-cp", "$" + "{classpath}" };
        var gameArguments = modern ? MinecraftRules.Arguments(arguments.GetProperty("game")) : LegacyArguments.Split(root.GetProperty("minecraftArguments").GetString()!);
        if (root.TryGetProperty("logging", out var logging) && logging.TryGetProperty("client", out var loggingClient))
        {
            var configuration = await Fetch(loggingClient.GetProperty("file"), "logging/" + loggingClient.GetProperty("file").GetProperty("id").GetString());
            jvm.Add(loggingClient.GetProperty("argument").GetString()!.Replace("$" + "{path}", "$" + "{library_directory}/" + configuration));
        }
        var javaMajor = root.TryGetProperty("javaVersion", out var java) ? java.GetProperty("majorVersion").GetInt32() : 8;
        var receipt = new InstallationReceipt(profile.MinecraftVersion, javaMajor, root.GetProperty("mainClass").GetString()!,
            DateTimeOffset.UtcNow, files.Values.ToArray(), classpath, natives,
            jvm, gameArguments, assetId);
        paths.CreateProfile(profile);
        await AtomicFile.WriteJsonAsync(paths.Receipt(profile.Id), receipt, ct);
        await store.AddHistoryAsync(profile.Id, "minecraft.installed", profile.MinecraftVersion, ct);
        return receipt;
    }
}