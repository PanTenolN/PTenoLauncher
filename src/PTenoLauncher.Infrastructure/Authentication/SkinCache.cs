using System.Buffers.Binary;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.Authentication;

public sealed class SkinCache(LauncherPaths paths, VerifiedDownloader downloader)
{
    public string? Cached(MinecraftAccount account)
    {
        if (!Guid.TryParseExact(account.Uuid, "N", out _)) return null;
        var file = SafePath.Join(paths.Cache, $"skins/{account.Uuid}.png");
        return File.Exists(file) ? file : null;
    }
    public async Task<string?> RefreshAsync(MinecraftAccount account, CancellationToken ct = default)
    {
        if (account.SkinUrl is null) return Cached(account);
        if (!Guid.TryParseExact(account.Uuid, "N", out _)) throw new InvalidDataException("Invalid account UUID.");
        var uri = new Uri(account.SkinUrl);
        TrustedSources.Validate(uri, DownloadSource.MinecraftSkin);
        var bytes = await downloader.GetBytesAsync(uri, 1024 * 1024, ct, DownloadSource.MinecraftSkin);
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 }) ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)) is 0 or > 1024 ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)) is 0 or > 1024)
            throw new InvalidDataException("Invalid/oversized PNG skin.");
        var file = SafePath.Join(paths.Cache, $"skins/{account.Uuid}.png");
        await AtomicFile.WriteAsync(file, bytes, ct); return file;
    }
}