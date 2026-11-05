using System.IO.Compression;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.AI;

public sealed record TranslationPackMetadata(int Format, string Evidence);
public static partial class TranslationPackMetadataResolver
{
    // Exact IDs only, checked against these official release notes. No prefix/range inference.
    private static readonly IReadOnlyDictionary<string, TranslationPackMetadata> Known = new Dictionary<string, TranslationPackMetadata>(StringComparer.Ordinal)
    {
        ["1.20"] = new(15, "https://www.minecraft.net/en-us/article/trails-tales-update-out-today-java"),
        ["1.21"] = new(34, "https://www.minecraft.net/en-us/article/minecraft-java-edition-1-21")
        , ["1.19.4"] = new(13, "https://www.minecraft.net/en-us/article/minecraft-java-edition-1-19-4")
        , ["1.20.2"] = new(18, "https://www.minecraft.net/en-us/article/minecraft-java-edition-1-20-2")
        , ["1.20.4"] = new(22, "https://www.minecraft.net/en-us/article/minecraft-java-edition-1-20-5#resource-pack-versions-23-through-32")
        , ["1.20.5"] = new(32, "https://www.minecraft.net/en-us/article/minecraft-java-edition-1-20-5")
    };
    public static async Task<TranslationPackMetadata> ResolveAsync(LauncherPaths paths, GameProfile profile, CancellationToken ct = default)
    {
        if (Known.TryGetValue(profile.MinecraftVersion, out var known)) return known;
        var local = await ReadLocalEvidence(paths, profile, ct); if (local is not null) return local;
        var receipt = await AtomicFile.ReadJsonAsync<InstallationReceipt>(paths.Receipt(profile.Id), ct);
        if (receipt is { Classpath: not null, Files: not null } && receipt.Version == profile.MinecraftVersion)
        {
            foreach (var relative in receipt.Classpath.Reverse().Take(128))
            {
                var f = receipt.Files.SingleOrDefault(f => f.RelativePath == relative);
                if (f is null || !relative.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || f.Size > 512L * 1024 * 1024) continue;
                var file = SafePath.Join(paths.Cache, relative);
                if (!await VerifiedDownloader.VerifyAsync(file, f.Hash, f.Algorithm, f.Size, ct)) continue;
                using var zip = ZipFile.OpenRead(file);
                var entries = zip.Entries.Where(e => e.FullName == "version.json").Take(2).ToArray(); if (entries.Length != 1) continue;
                using var doc = Modpacks.MrpackReader.Json(await Modpacks.MrpackReader.EntryBytes(entries[0], 128 * 1024, ct)); var json = doc.RootElement;
                if (json.ValueKind == System.Text.Json.JsonValueKind.Object && json.TryGetProperty("id", out var id) && id.ValueKind == System.Text.Json.JsonValueKind.String && id.GetString() == profile.MinecraftVersion && json.TryGetProperty("pack_version", out var pack) && pack.ValueKind == System.Text.Json.JsonValueKind.Object && pack.TryGetProperty("resource", out var resource) && resource.ValueKind == System.Text.Json.JsonValueKind.Number && resource.TryGetInt32(out var format) && format is >= 4 and <= 10000)
                    return new(format, "verified-client/version.json " + profile.MinecraftVersion + " " + f.Hash);
            }
        }
        throw new AiServiceException("AiUxPackUnknown");
    }
}
