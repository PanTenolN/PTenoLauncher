using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.IntegrationSmoke;

internal static class LiveForgeComponents
{
    private sealed class Counter(bool offline) : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++; if (offline) throw new InvalidOperationException("Offline component check attempted HTTP.");
            return base.SendAsync(request, ct);
        }
    }
    public static async Task RunAsync(LauncherPaths paths, string[] args)
    {
        var marker = SafePath.Join(paths.Root, "forge-fixture.txt");
        if (!File.Exists(marker))
        {
            if (Directory.EnumerateDirectories(SafePath.Join(paths.Root, "instances")).Any()) throw new InvalidOperationException("Use a new isolated fixture root.");
            await File.WriteAllTextAsync(marker, "PTenoLauncher isolated Forge component check v1");
        }
        if (await File.ReadAllTextAsync(marker) != "PTenoLauncher isolated Forge component check v1") throw new InvalidOperationException("Unknown fixture root.");
        bool offline = args[1] == "--forge-components-offline";
        using var handler = new Counter(offline); using var http = new HttpClient(handler);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PTenoLauncher/0.2.0");
        var network = new NetworkPolicy { Offline = offline }; var downloads = new VerifiedDownloader(http, network); var catalog = new ForgeCatalog(paths, downloads, network);
        var results = new List<object>(); bool failed = false;
        if (args[1] == "--forge-catalogs")
        {
            foreach (var loader in new[] { ModLoader.Forge, ModLoader.NeoForge })
            {
                try
                {
                    var versions = await catalog.GetVersionsAsync(loader, refresh: true);
                    var before = handler.Requests; network.Offline = true;
                    var cached = await catalog.GetVersionsAsync(loader);
                    if (cached.Count != versions.Count || handler.Requests != before) throw new InvalidDataException("Offline catalog reuse mismatch.");
                    results.Add(new { loader = loader.ToString(), result = "PASS", versions = versions.Count, offlineRequests = handler.Requests - before });
                }
                catch (Exception e) { failed = true; results.Add(new { loader = loader.ToString(), result = "FAILED", errorType = e.GetType().Name, error = e.Message }); }
                finally { network.Offline = false; }
            }
        }
        else
        {
            if (args.Length != 5 || !Enum.TryParse<ModLoader>(args[2], out var loader) || loader is not (ModLoader.Forge or ModLoader.NeoForge))
                throw new ArgumentException("Expected <root> --forge-components[-offline] Forge|NeoForge <exact-loader-version> <minecraft-version>.");
            var store = new SqliteLauncherStore(paths); await store.InitializeAsync();
            var profile = new GameProfile(Guid.NewGuid(), "Isolated component check", args[4], loader, 2048, null, DateTimeOffset.UtcNow); await store.SaveProfileAsync(profile);
            var sentinel = SafePath.Join(paths.Game(profile.Id), "saves/synthetic-only.txt"); await File.WriteAllTextAsync(sentinel, "SYNTHETIC DATA - NOT A REAL WORLD");
            try
            {
                var preparer = new ForgeComponentPreparer(paths, catalog, downloads, store);
                var prepared = await preparer.PrepareAsync(profile, args[3]); var before = handler.Requests; network.Offline = true;
                await preparer.PrepareAsync(profile, args[3]);
                if (handler.Requests != before || await File.ReadAllTextAsync(sentinel) != "SYNTHETIC DATA - NOT A REAL WORLD" || File.Exists(paths.Receipt(profile.Id)))
                    throw new InvalidDataException("Component/offline/installation-receipt preservation mismatch.");
                foreach (var file in prepared.Files)
                    if (!await VerifiedDownloader.VerifyAsync(SafePath.Join(paths.Cache, file.RelativePath), file.Hash, file.Algorithm, file.Size)) throw new InvalidDataException("Component integrity failure.");
                results.Add(new { loader = loader.ToString(), result = "PASS", version = args[3], minecraft = args[4], files = prepared.Files.Count,
                    fml = prepared.Plan.FmlCoordinate, processorsPending = prepared.Plan.ClientProcessors.Count, generatedPending = prepared.Plan.Libraries.Count(l => l.Generated), offlineRequests = handler.Requests - before });
            }
            catch (Exception e) { failed = true; results.Add(new { loader = loader.ToString(), result = "FAILED", errorType = e.GetType().Name, error = e.Message }); }
        }
        var report = new { phase = failed ? "FAILED" : "PASS", results, requests = handler.Requests, processorsExecuted = false, gameLaunched = false, authenticationBypassed = false };
        await AtomicFile.WriteJsonAsync(SafePath.Join(paths.Root, "forge-result.json"), report);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report, AtomicFile.JsonOptions));
        if (failed) throw new InvalidOperationException("Live Forge check failed; see forge-result.json. No game or processor was executed.");
    }
}
