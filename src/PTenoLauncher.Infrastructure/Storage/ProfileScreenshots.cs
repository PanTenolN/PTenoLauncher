using System.Buffers.Binary;

namespace PTenoLauncher.Infrastructure.Storage;

public sealed class ProfileScreenshots(LauncherPaths paths)
{
    private const int MaximumBytes = 24 * 1024 * 1024;

    private string DirectoryFor(Guid profile) => SafePath.Join(paths.Game(profile), "screenshots");

    public Task<IReadOnlyList<string>> ListAsync(Guid profile, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            var directory = DirectoryFor(profile);
            if (!Directory.Exists(directory)) return [];
            var names = new List<string>();
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*.png"))
            {
                ct.ThrowIfCancellationRequested();
                if ((file.Attributes & FileAttributes.ReparsePoint) == 0 && file.Length is >= 24 and <= MaximumBytes)
                    names.Add(file.Name);
            }
            return names.OrderDescending(StringComparer.OrdinalIgnoreCase).ToArray();
        }, ct);

    public async Task<byte[]> ReadAsync(Guid profile, string name, CancellationToken ct = default)
    {
        if (name != Path.GetFileName(name) || !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ScreenshotInvalid");
        var bytes = await AtomicFile.ReadBytesAsync(SafePath.Join(DirectoryFor(profile), name), MaximumBytes, ct);
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8)) != 13 || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("ScreenshotInvalid");
        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20));
        if (width is 0 or > 16_384 || height is 0 or > 16_384 || (ulong)width * height > 33_554_432)
            throw new InvalidDataException("ScreenshotInvalid");
        return bytes;
    }
}
