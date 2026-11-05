using System.Text.Json;
using PTenoLauncher.Infrastructure.Modpacks;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.IntegrationSmoke;

internal static class LiveMrpackImport
{
    public static async Task RunAsync(LauncherPaths paths)
    {
        using var http = NetworkPolicy.CreateHttpClient(); var network = new NetworkPolicy(); var downloader = new VerifiedDownloader(http, network);
        var store = new SqliteLauncherStore(paths); await store.InitializeAsync();
        if ((await store.GetProfilesAsync()).Count != 0) throw new InvalidOperationException("Use a fresh isolated test root.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3)); string stage = "official-catalog"; object? artifact = null;
        try
        {
            var bytes = await downloader.GetBytesAsync(new("https://api.modrinth.com/v2/project/fabulously-optimized/version"), 8 * 1024 * 1024, deadline.Token, DownloadSource.Modrinth);
            using var doc = JsonDocument.Parse(bytes);
            var version = doc.RootElement.EnumerateArray().Where(v => v.GetProperty("version_type").GetString() == "release")
                .OrderByDescending(v => v.GetProperty("date_published").GetDateTimeOffset()).First();
            var file = version.GetProperty("files").EnumerateArray().First(f => f.GetProperty("filename").GetString()!.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase));
            var url = new Uri(file.GetProperty("url").GetString()!); TrustedSources.ValidateMrpack(url, true);
            var hashes = file.GetProperty("hashes"); string sha512 = hashes.GetProperty("sha512").GetString()!, sha1 = hashes.GetProperty("sha1").GetString()!; long size = file.GetProperty("size").GetInt64();
            artifact = new { project = "fabulously-optimized", version = version.GetProperty("id").GetString(), versionNumber = version.GetProperty("version_number").GetString(), url, sha512, sha1, size };
            stage = "archive-download"; var archive = SafePath.Join(paths.Root, "official-pack.mrpack");
            await downloader.DownloadAsync(url, archive, sha512, size, "SHA512", ct: deadline.Token, source: DownloadSource.Mrpack);
            if (!await VerifiedDownloader.VerifyAsync(archive, sha1, "SHA1", size, deadline.Token)) throw new InvalidDataException("Archive SHA1 mismatch.");
            var importer = new MrpackImporter(paths, store, downloader, network); stage = "preview"; var preview = await importer.Reader.PreviewAsync(archive, ct: deadline.Token);
            if (!preview.CanImport) throw new MrpackException("PackBlocked", JsonSerializer.Serialize(preview.Blockers));
            stage = "content-import"; var result = await importer.ImportAsync(preview, true, ct: deadline.Token);
            var report = new { state = "PASSED_CONTENT_IMPORT", stage, artifact, profile = result.Profile, files = result.Receipt.Files.Count, warnings = result.Receipt.Review?.Warnings, loaderInstalled = false, javaInstalled = false, gameLaunched = false, accountUsed = false };
            await AtomicFile.WriteJsonAsync(SafePath.Join(paths.Root, "mrpack-live-result.json"), report); Console.WriteLine(JsonSerializer.Serialize(report, AtomicFile.JsonOptions));
        }
        catch (Exception e)
        {
            int? socket = null; for (Exception? current = e; current is not null; current = current.InnerException) if (current is System.Net.Sockets.SocketException s) socket = s.NativeErrorCode;
            var report = new { state = "FAILED", stage, artifact, error = e.GetType().Name, detail = e.Message, socket, loaderInstalled = false, gameLaunched = false, accountUsed = false };
            await AtomicFile.WriteJsonAsync(SafePath.Join(paths.Root, "mrpack-live-result.json"), report); Console.WriteLine(JsonSerializer.Serialize(report, AtomicFile.JsonOptions));
            Environment.ExitCode = 1;
        }
    }
}
