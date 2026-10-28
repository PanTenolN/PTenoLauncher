using System.Security.Cryptography;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Network;

public sealed class VerifiedDownloader(HttpClient client, NetworkPolicy policy)
{
    public async Task<byte[]> GetBytesAsync(Uri uri, int maxBytes, CancellationToken ct = default, DownloadSource source = DownloadSource.Minecraft)
    {
        policy.RequireOnline(); TrustedSources.Validate(uri, source);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var response = await SendAsync(uri, source, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException("Відповідь завелика.");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var output = new MemoryStream(); var buffer = new byte[81920]; int count;
        while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
        {
            if (output.Length + count > maxBytes) throw new InvalidDataException("Відповідь завелика.");
            await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
        }
        return output.ToArray();
    }
    public static async Task<bool> VerifyAsync(string path, string hash, string algorithm, long size, CancellationToken ct = default)
    {
        SafePath.RejectLinks(path);
        if (!File.Exists(path) || new FileInfo(path).Length != size) return false;
        await using var stream = File.OpenRead(path);
        var actual = algorithm switch {
            "SHA1" => await SHA1.HashDataAsync(stream, ct),
            "SHA256" => await SHA256.HashDataAsync(stream, ct),
            "SHA512" => await SHA512.HashDataAsync(stream, ct),
            _ => throw new InvalidDataException("Непідтримуваний алгоритм хешу.")
        };
        return Convert.ToHexString(actual).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }
    public async Task DownloadAsync(Uri uri, string destination, string hash, long size, string algorithm = "SHA1",
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default, DownloadSource source = DownloadSource.Minecraft)
    {
        if (size is < 0 or > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("Недопустимий розмір завантаження.");
        if (algorithm is not ("SHA1" or "SHA256" or "SHA512")) throw new InvalidDataException("Unknown hash algorithm.");
        if (hash.Length != (algorithm == "SHA1" ? 40 : algorithm == "SHA512" ? 128 : 64) || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Некоректний хеш.");
        if (await VerifyAsync(destination, hash, algorithm, size, ct)) return;
        policy.RequireOnline(); TrustedSources.Validate(uri, source); SafePath.RejectLinks(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var response = await SendAsync(uri, source, ct);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != size) throw new InvalidDataException("Розмір не відповідає маніфесту.");
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920]; long total = 0; int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    if ((total += read) > size) throw new InvalidDataException("Завантаження перевищує розмір маніфесту.");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    progress?.Report(new(Path.GetFileName(destination), total, size));
                }
                await output.FlushAsync(ct); output.Flush(true);
            }
            if (!await VerifyAsync(temp, hash, algorithm, size, ct)) throw new InvalidDataException("Перевірка цілісності завантаження не пройдена.");
            SafePath.RejectLinks(destination); File.Move(temp, destination, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private async Task<HttpResponseMessage> SendAsync(Uri uri, DownloadSource source, CancellationToken ct)
    {
        for (int redirects = 0; redirects <= 4; redirects++)
        {
            policy.RequireOnline(); TrustedSources.Validate(uri, source);
            var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (source != DownloadSource.Temurin || (int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location; response.Dispose();
            if (location is null) throw new HttpRequestException("Redirect without Location.");
            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
        }
        throw new HttpRequestException("Too many download redirects.");
    }
}