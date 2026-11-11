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
            using var gate = AcquireGate(path, CancellationToken.None);
            SafePath.RejectLinks(path); File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static async Task WriteAsync(string path, byte[] bytes, CancellationToken ct = default, bool overwrite = true)
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
            using var gate = AcquireGate(path, ct);
            SafePath.RejectLinks(path); File.Move(temp, path, overwrite);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static Task WriteJsonAsync<T>(string path, T value, CancellationToken ct = default) => WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), ct);
    public static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken ct = default)
    {
        byte[] snapshot;
        try { snapshot = await ReadBytesAsync(path, 32 * 1024 * 1024, ct).ConfigureAwait(false); }
        catch (FileNotFoundException) { return default; }
        catch (DirectoryNotFoundException) { return default; }
        ct.ThrowIfCancellationRequested();
        // No file handle or gate survives into deserialization/validation. Interpret one bounded immutable snapshot.
        return JsonSerializer.Deserialize<T>(snapshot, JsonOptions);
    }
    public static async Task<byte[]> ReadBytesAsync(string path, int maximumBytes, CancellationToken ct = default)
    {
        if (maximumBytes is < 1 or > 32 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        return await Task.Run(() =>
        {
            using var gate = AcquireGate(path, ct);
            SafePath.RejectLinks(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > maximumBytes) throw new InvalidDataException("Metadata exceeds the read limit.");
            using var bytes = new MemoryStream(); var buffer = new byte[81920]; int read;
            while ((read = stream.Read(buffer)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                if (bytes.Length + read > maximumBytes) throw new InvalidDataException("Metadata exceeds the read limit.");
                bytes.Write(buffer, 0, read);
            }
            return bytes.ToArray();
        }, ct).ConfigureAwait(false);
    }
    internal static string GateName(string path) => (OperatingSystem.IsWindows() ? @"Local\PTenoAtomic-" : "PTenoAtomic-") +
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
    private static IDisposable AcquireGate(string path, CancellationToken ct)
    {
        var mutex = new Mutex(false, GateName(path)); var until = DateTime.UtcNow.AddSeconds(5);
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested(); bool acquired;
                try { acquired = mutex.WaitOne(25); } catch (AbandonedMutexException) { acquired = true; }
                if (acquired) return new MetadataGate(mutex);
                if (DateTime.UtcNow >= until) throw new IOException("Metadata snapshot is busy; retry the operation.");
            }
        }
        catch { mutex.Dispose(); throw; }
    }
    private sealed class MetadataGate(Mutex mutex) : IDisposable
    {
        // Only used in synchronous regions, so Windows thread-affine ownership never crosses an await.
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }
}
