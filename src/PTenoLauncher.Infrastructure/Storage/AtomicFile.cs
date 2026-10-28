using System.Text.Json;

namespace PTenoLauncher.Infrastructure.Storage;

public static class AtomicFile
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true, MaxDepth = 64 };
    public static void Write(string path, ReadOnlySpan<byte> bytes)
    {
        SafePath.RejectLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            SafePath.RejectLinks(path);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static async Task WriteAsync(string path, byte[] bytes, CancellationToken ct = default)
    {
        SafePath.RejectLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, ct);
                await stream.FlushAsync(ct);
                stream.Flush(true);
            }
            SafePath.RejectLinks(path);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static Task WriteJsonAsync<T>(string path, T value, CancellationToken ct = default) => WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), ct);
    public static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken ct = default)
    {
        SafePath.RejectLinks(path);
        if (!File.Exists(path)) return default;
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("JSON перевищує дозволений розмір.");
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
    }
}
