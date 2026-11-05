using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.IntegrationSmoke;
internal static class LiveModUpdates
{
    public static async Task RunAsync(LauncherPaths paths)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        using var http = NetworkPolicy.CreateHttpClient(); http.Timeout = TimeSpan.FromSeconds(20);
        var policy = new NetworkPolicy(); var downloads = new VerifiedDownloader(http, policy);
        var catalog = new ModrinthClient(paths, downloads, policy); var store = new SqliteLauncherStore(paths); await store.InitializeAsync(timeout.Token);
        var local = new LocalModManager(paths, store); var updates = new ModUpdateManager(paths, store, catalog, downloads, local);
        string stage = "project-metadata"; string? oldVersion = null, newVersion = null;
        try
        {
            var project = await catalog.GetProjectAsync("sodium", timeout.Token); stage = "versions";
            var releases = (await catalog.GetVersionsAsync(project.Id, "1.21.1", ModLoader.Fabric, timeout.Token)).Where(v => v.Channel == "release").OrderByDescending(v => v.Published).ToArray();
            var latest = releases.First(); var older = releases.First(v => v.Published < latest.Published); oldVersion = older.Id; newVersion = latest.Id;
            var p = new GameProfile(Guid.NewGuid(), "Isolated official mod update check", "1.21.1", ModLoader.Fabric, 4096, null, DateTimeOffset.UtcNow); await store.SaveProfileAsync(p, timeout.Token);
            await AtomicFile.WriteJsonAsync(paths.Receipt(p.Id), new InstallationReceipt(p.MinecraftVersion, 21, "fixture-not-a-game-launch", DateTimeOffset.UtcNow, [], [], [], [], [], "fixture", ModLoader.Fabric, "0.16.14"), timeout.Token);
            var sentinel = SafePath.Join(paths.Game(p.Id), "saves/synthetic-only.txt"); await File.WriteAllTextAsync(sentinel, "NOT A REAL WORLD", timeout.Token);
            stage = "older-download"; var file = SafePath.Join(paths.Game(p.Id), "mods/sodium.jar");
            await downloads.DownloadAsync(older.File.Url, file, older.File.Sha512, older.File.Size, "SHA512", ct: timeout.Token, source: DownloadSource.Modrinth);
            var original = await File.ReadAllBytesAsync(file, timeout.Token);
            await new ModInventoryStore(paths).RecordCommittedAsync(p.Id, [new("mods/sodium.jar", Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant(), original.Length)], timeout.Token);
            stage = "identity-and-preview"; var scan = await updates.CheckAsync(p, ct: timeout.Token);
            if (scan.Rows.Single().Current?.Id != older.Id) throw new InvalidDataException("Provider identity mismatch.");
            var plan = await updates.PreviewAsync(p, [new("mods/sodium.jar", latest.Id)], ct: timeout.Token);
            if (!plan.CanApply) throw new InvalidDataException(string.Join("; ", plan.Blockers));
            stage = "apply"; await updates.ApplyAsync(p, plan, true, timeout.Token);
            policy.Offline = true; stage = "offline-rollback";
            await updates.ApplyAsync(p, await updates.PreviewRollbackAsync(p, plan.Id, timeout.Token), true, timeout.Token);
            if (!(await File.ReadAllBytesAsync(file, timeout.Token)).SequenceEqual(original) || await File.ReadAllTextAsync(sentinel, timeout.Token) != "NOT A REAL WORLD") throw new InvalidDataException("Preservation check failed.");
            await Save(new { result = "PASSED", stage, project = project.Id, oldVersion, newVersion, actualJars = true, jarExecuted = false, gameplayVerified = false });
        }
        catch (Exception e)
        {
            SocketException? socket = null; for (Exception? inner = e; inner is not null; inner = inner.InnerException) if (inner is SocketException s) socket = s;
            await Save(new { result = "FAILED", stage, oldVersion, newVersion, errorType = e.GetType().Name, error = e.Message, socketCode = socket?.NativeErrorCode, jarExecuted = false, gameplayVerified = false }); Environment.ExitCode = 1;
        }
        async Task Save(object result)
        { await AtomicFile.WriteJsonAsync(SafePath.Join(paths.Root, "mod-updates-result.json"), result); Console.WriteLine(JsonSerializer.Serialize(result, AtomicFile.JsonOptions)); }
    }
}
