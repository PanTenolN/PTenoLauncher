using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.IntegrationSmoke;

/// <summary>Explicit component preparation check. No Microsoft session, Java or Minecraft execution.</summary>
internal static class LiveHistoricalAssets
{
    private sealed class Counter(bool deny = false) : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            if (deny) throw new InvalidOperationException("Offline historical check attempted HTTP.");
            return base.SendAsync(request, ct);
        }
    }
    public static async Task RunAsync(LauncherPaths paths, bool cacheOnly)
    {
        // Only a fresh isolated data root, or the explicitly labelled cache fixture, is accepted.
        var marker = SafePath.Join(paths.Root, "historical-fixture.txt");
        if (!File.Exists(marker))
        {
            if (Directory.EnumerateDirectories(SafePath.Join(paths.Root, "instances")).Any())
                throw new InvalidOperationException("Use a new isolated historical test root.");
            await File.WriteAllTextAsync(marker, "PTenoLauncher isolated historical asset check v1");
        }
        if (await File.ReadAllTextAsync(marker) != "PTenoLauncher isolated historical asset check v1") throw new InvalidOperationException("Unknown fixture marker.");
        using var handler = new Counter(cacheOnly); using var http = new HttpClient(handler);
        var policy = new NetworkPolicy { Offline = cacheOnly }; var downloads = new VerifiedDownloader(http, policy);
        var versions = new VersionManager(paths, downloads); var store = new SqliteLauncherStore(paths); await store.InitializeAsync();
        var results = new List<object>();
        try
        {
            if (!cacheOnly) await versions.RefreshAsync();
            foreach (var version in new[] { "1.6.4", "1.7.10" })
            {
                policy.Offline = cacheOnly;
                var profile = new GameProfile(Guid.NewGuid(), "Historical component check " + version, version, ModLoader.Vanilla, 2048, null, DateTimeOffset.UtcNow);
                await store.SaveProfileAsync(profile);
                var sentinel = SafePath.Join(paths.Game(profile.Id), "saves/synthetic-only.txt");
                await File.WriteAllTextAsync(sentinel, "SYNTHETIC DATA - NOT A MINECRAFT WORLD");
                var installer = new VanillaInstaller(paths, versions, downloads, store);
                Console.WriteLine("Preparing real official components: " + version + "; Minecraft is NOT started.");
                var receipt = await installer.InstallAsync(profile);
                ReceiptValidator.Validate(paths, receipt);
                foreach (var file in receipt.Files)
                    if (!await VerifiedDownloader.VerifyAsync(SafePath.Join(paths.Cache, file.RelativePath), file.Hash, file.Algorithm, file.Size))
                        throw new InvalidDataException("Installed component failed verification: " + file.RelativePath);
                if ((version == "1.6.4") != (receipt.LegacyAssets is { Virtual: true })) throw new InvalidDataException("Unexpected real asset layout.");
                if ((await AssetManager.CheckAsync(paths, profile.Id, receipt)).Count != 0) throw new InvalidDataException("Projection verification failed.");
                var requests = handler.Requests; policy.Offline = true;
                await installer.InstallAsync(profile);
                bool repaired = false;
                if (receipt.LegacyAssets is { Virtual: true } layout)
                {
                    var entry = layout.Entries.First(f => f.Size > 0 && f.Size < 4096);
                    var target = SafePath.Join(AssetManager.GameAssets(paths, profile.Id, receipt), entry.RelativePath);
                    var original = await File.ReadAllBytesAsync(target);
                    try
                    {
                        await File.WriteAllTextAsync(target, "isolated corruption fixture");
                        if ((await AssetManager.CheckAsync(paths, profile.Id, receipt)).Count == 0) throw new InvalidDataException("Damage was not detected.");
                        await installer.InstallAsync(profile); repaired = true;
                        var restored = await File.ReadAllBytesAsync(target);
                        if (!original.SequenceEqual(restored)) throw new InvalidDataException("Cached repair failed.");
                    }
                    finally { await AtomicFile.WriteAsync(target, original); }
                }
                if (handler.Requests != requests || await File.ReadAllTextAsync(sentinel) != "SYNTHETIC DATA - NOT A MINECRAFT WORLD")
                    throw new InvalidDataException("Offline request or preservation failure.");
                var item = new { version, files = receipt.Files.Count, classpath = receipt.Classpath.Count, nativeArchives = receipt.NativeArchives.Count,
                    virtualAssets = receipt.LegacyAssets?.Virtual ?? false, mappedResources = receipt.LegacyAssets?.MapToResources ?? false,
                    projectedEntries = receipt.LegacyAssets?.Entries.Count ?? 0, offlineRequests = handler.Requests - requests,
                    offlineRepair = repaired, syntheticDataPreserved = true, gameLaunched = false };
                results.Add(item); Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(item));
                await AtomicFile.WriteJsonAsync(SafePath.Join(paths.Root, "historical-result.json"), new { phase = "RUNNING", cacheOnly, results });
            }
            await AtomicFile.WriteJsonAsync(SafePath.Join(paths.Root, "historical-result.json"), new { phase = "PASS", cacheOnly, results, requests = handler.Requests, gameLaunched = false });
        }
        catch (Exception ex)
        {
            await AtomicFile.WriteJsonAsync(SafePath.Join(paths.Root, "historical-result.json"), new { phase = "FAILED", cacheOnly, results, requests = handler.Requests, gameLaunched = false, errorType = ex.GetType().Name, error = ex.Message });
            throw;
        }
    }
}
