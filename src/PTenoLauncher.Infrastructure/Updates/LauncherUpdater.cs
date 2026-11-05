using System.Text.Json;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Updates;

public sealed partial class LauncherUpdater(LauncherPaths paths, VerifiedDownloader downloads, NetworkPolicy network, LauncherUpdateTrust trust, LauncherBuild current)
{
    public static Uri Feed { get; } = new("https://github.com/PanTenolN/PTenoLauncher/releases/latest/download/launcher-updates.json");
    public LauncherBuild Build => current;
    internal TimeSpan CheckTimeout { get; set; } = TimeSpan.FromSeconds(6);
    internal TimeProvider Clock { get; set; } = TimeProvider.System;
    public async Task<LauncherUpdateCheck> CheckAsync(string mode, bool previews, CancellationToken ct = default)
    {
        if (network.Offline) return new("UpdaterOffline");
        if (!trust.Configured) return new("UpdaterTrust");
        if (current.Sequence <= 0) return new("UpdaterBuild");
        ct.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(CheckTimeout);
        try
        {
            var bytes = await downloads.GetBytesAsync(Feed, 2 * 1024 * 1024, timeout.Token, DownloadSource.LauncherUpdate);
            var signed = LauncherUpdateTrust.Parse<SignedLauncherUpdates>(bytes);
            var index = trust.Verify(signed, Clock.GetUtcNow());
            if (index.Releases.Any(r => r.Build.Sequence == current.Sequence && r.Build != current)) throw new LauncherUpdateException("UpdaterBuild");
            var highestPath = SafePath.Join(paths.Root, "launcher-updates/highest-seen.json");
            var latest = index.Releases.Select(r => r.Build.Sequence).DefaultIfEmpty(0).Max();
            Directory.CreateDirectory(Path.GetDirectoryName(highestPath)!);
            await using (var gate = await CatalogGate(highestPath + ".lock", timeout.Token))
            {
                var highest = await AtomicFile.ReadJsonAsync<long>(highestPath, timeout.Token);
                if (highest < 0) throw new LauncherUpdateException("UpdaterInvalid");
                if (latest < highest) throw new LauncherUpdateException("UpdaterReplay");
                if (latest > highest) await AtomicFile.WriteJsonAsync(highestPath, latest, timeout.Token);
            }
            var release = index.Releases.Where(r => r.Build.Sequence > current.Sequence && (previews || r.Build.Channel == "stable") && r.Build.DataSchema == current.DataSchema && r.Packages.Any(p => p.Mode == mode)).OrderByDescending(r => r.Build.Sequence).FirstOrDefault();
            return release is null ? new("UpdaterCurrent") : new("UpdaterAvailable", new(current, release, release.Packages.Single(p => p.Mode == mode), signed));
        }
        catch (LauncherUpdateException e) { return new(e.Code); }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or InvalidDataException or System.Security.Cryptography.CryptographicException || e is OperationCanceledException && !ct.IsCancellationRequested)
        { return new("UpdaterUnavailable"); }
    }
    private static async Task<FileStream> CatalogGate(string file, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested(); SafePath.RejectLinks(file);
            try { return new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(50, ct); }
        }
    }
    private void VerifyOffer(LauncherUpdateOffer offer, bool allowExpiredForRecovery = false)
    {
        if (offer is null || offer.Release is null || offer.Package is null) throw new LauncherUpdateException("UpdaterInvalid");
        var index = trust.Verify(offer.Signed, Clock.GetUtcNow(), allowExpiredForRecovery);
        if (offer.Current != current || offer.Release.Build.Sequence <= current.Sequence || offer.Release.Build.DataSchema != current.DataSchema ||
            !index.Releases.Any(r => JsonSerializer.Serialize(r, AtomicFile.JsonOptions) == JsonSerializer.Serialize(offer.Release, AtomicFile.JsonOptions)) ||
            !offer.Release.Packages.Any(p => JsonSerializer.Serialize(p, AtomicFile.JsonOptions) == JsonSerializer.Serialize(offer.Package, AtomicFile.JsonOptions))) throw new LauncherUpdateException("UpdaterInvalid");
    }
}
