using System.IO.Compression;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.AI;

public static partial class TranslationPackMetadataResolver
{
    private sealed record LocalEvidence(int Schema, string Minecraft, int Format, string ClientSha256, long ClientSize, byte[] VersionJson, string JsonSha256);
    private static string LocalPath(LauncherPaths paths, GameProfile profile) => SafePath.Join(paths.Profile(profile.Id), "translations/client-pack-metadata.json");
    private static int ReadVersion(byte[] data, string expected)
    {
        using var doc = Modpacks.MrpackReader.Json(data); var j = doc.RootElement;
        if (j.ValueKind != JsonValueKind.Object || !j.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || id.GetString() != expected ||
            !j.TryGetProperty("pack_version", out var pack) || pack.ValueKind != JsonValueKind.Object || !pack.TryGetProperty("resource", out var resource) || resource.ValueKind != JsonValueKind.Number || !resource.TryGetInt32(out int format) || format is < 4 or > 10000) throw new AiServiceException("AiUxPackUnknown");
        return format;
    }
    public static async Task<TranslationPackMetadata> ImportLocalClientAsync(LauncherPaths paths, GameProfile profile, string clientJar, CancellationToken ct = default)
    {
        SafePath.RejectLinks(clientJar); var size = new FileInfo(clientJar).Length;
        if (!clientJar.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || size > 512L * 1024 * 1024) throw new AiServiceException("AiUxPackUnknown");
        var hash = await Modpacks.MrpackReader.HashFile(clientJar, ct); Modpacks.MrpackReader.CheckContainer(clientJar);
        byte[] bytes;
        using (var z = ZipFile.OpenRead(clientJar))
        {
            Security.SafeArchive.Validate(z, paths.Cache, maxEntries: 50000);
            var matching = z.Entries.Where(e => e.FullName == "version.json").ToArray(); if (matching.Length != 1) throw new AiServiceException("AiUxPackUnknown");
            bytes = await Modpacks.MrpackReader.EntryBytes(matching[0], 128 * 1024, ct);
        }
        var format = ReadVersion(bytes, profile.MinecraftVersion);
        if (await Modpacks.MrpackReader.HashFile(clientJar, ct) != hash) throw new AiServiceException("AiUxDraftChanged");
        using var lease = new ProfileUsage(paths).Acquire(profile.Id);
        var target = LocalPath(paths, profile);
        if (File.Exists(target)) _ = await ReadLocalEvidence(paths, profile, ct); // Refuse to overwrite malformed/unrecognized evidence.
        if (File.Exists(target))
        {
            var old = await AtomicFile.ReadBytesAsync(target, 256 * 1024, ct); var backup = target + "." + Modpacks.MrpackReader.Hash(old) + ".previous";
            if (File.Exists(backup)) { if (!(await AtomicFile.ReadBytesAsync(backup, 256 * 1024, ct)).AsSpan().SequenceEqual(old)) throw new AiServiceException("AiUxPackConflict"); }
            else await AtomicFile.WriteAsync(backup, old, ct);
        }
        await AtomicFile.WriteJsonAsync(target, new LocalEvidence(1, profile.MinecraftVersion, format, hash, size, bytes, Modpacks.MrpackReader.Hash(bytes)), ct);
        return new(format, "explicit-local-client/version.json " + profile.MinecraftVersion + " SHA256 " + hash);
    }
    private static async Task<TranslationPackMetadata?> ReadLocalEvidence(LauncherPaths paths, GameProfile profile, CancellationToken ct)
    {
        var file = LocalPath(paths, profile); if (!File.Exists(file)) return null;
        using var json = Modpacks.MrpackReader.Json(await AtomicFile.ReadBytesAsync(file, 256 * 1024, ct));
        var proof = json.Deserialize<LocalEvidence>(AtomicFile.JsonOptions) ?? throw new AiServiceException("AiUxPackUnknown");
        if (proof.Schema != 1 || proof.Minecraft != profile.MinecraftVersion || proof.VersionJson is null || proof.VersionJson.Length > 128 * 1024 || proof.ClientSize is <= 0 or > 512L * 1024 * 1024 ||
            proof.ClientSha256 is null || proof.ClientSha256.Length != 64 || !proof.ClientSha256.All(Uri.IsHexDigit) || Modpacks.MrpackReader.Hash(proof.VersionJson) != proof.JsonSha256 || ReadVersion(proof.VersionJson, proof.Minecraft) != proof.Format) throw new AiServiceException("AiUxPackUnknown");
        return new(proof.Format, "explicit-local-client/version.json " + proof.Minecraft + " SHA256 " + proof.ClientSha256);
    }
}
