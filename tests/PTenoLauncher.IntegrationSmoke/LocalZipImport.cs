using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Modpacks;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.IntegrationSmoke;

internal static class LocalZipImport
{
    // Explicit opt-in, fresh isolated data root, no network client, no account, no game or scripts.
    public static async Task RunAsync(LauncherPaths paths, string[] args)
    {
        if (args.Length != 7) throw new ArgumentException("root --zip-content-import archive minecraft loader exact-loader-version root-or-/");
        if (File.Exists(paths.Database) || Directory.EnumerateDirectories(Path.Combine(paths.Root, "instances")).Any()) throw new InvalidOperationException("Use a fresh isolated root.");
        var store = new SqliteLauncherStore(paths); await store.InitializeAsync();
        using var http = new HttpClient(new Deny()); var network = new NetworkPolicy { Offline = true };
        var importer = new MrpackImporter(paths, store, new(http, network), network);
        var request = new ZipPackRequest("Local ZIP content check", args[3], Enum.Parse<ModLoader>(args[4], false), args[5] == "-" ? "" : args[5], args[6] == "/" ? "" : args[6]);
        var preview = await importer.ZipReader.PreviewAsync(args[2], request);
        if (preview.IsMrpack || !preview.Plan.CanImport) throw new MrpackException("PackBlocked", JsonSerializer.Serialize(preview.Plan.Blockers));
        var result = await importer.ImportZipAsync(preview, true);
        var persisted = await MrpackRequirements.ReadAsync(paths, result.Profile) ?? throw new InvalidDataException();
        if (persisted.ArchiveSha256 != preview.Plan.ArchiveSha256 || (await store.GetProfilesAsync()).Count != 1 || (await importer.PendingAsync()).Count != 0) throw new InvalidDataException("Publication verification failed.");
        var report = new { state = "PASSED_LOCAL_CONTENT_IMPORT", archive = Path.GetFullPath(args[2]), archiveSha256 = persisted.ArchiveSha256, profile = result.Profile, persisted.ContentRoot, persisted.SourceKind, files = persisted.Files,
            excluded = persisted.Excluded, offline = true, networkRequests = 0, loaderInstalled = false, javaInstalled = false, gameLaunched = false, accountUsed = false, thirdPartyModpackVerified = false };
        await AtomicFile.WriteJsonAsync(Path.Combine(paths.Root, "zip-import-result.json"), report); Console.WriteLine(JsonSerializer.Serialize(report, AtomicFile.JsonOptions));
    }
    private sealed class Deny : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new InvalidOperationException("ZIP local check must not issue HTTP."); }
}
