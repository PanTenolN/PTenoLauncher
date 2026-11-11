using System.Security.Cryptography;
using System.Text;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Mods;

public sealed partial class RemoteModInstaller(LauncherPaths paths, ILauncherStore store, ModrinthClient catalog,
    VerifiedDownloader downloads, LocalModManager local)
{
    private sealed record Snapshot(string Fingerprint, IReadOnlyList<InstalledCatalogMod> Known, IReadOnlyList<string> Warnings,
        IReadOnlyDictionary<string, long> Artifacts);
    private async Task<Snapshot> ScanAsync(GameProfile profile, bool identify, CancellationToken ct)
    {
        var directory = SafePath.Join(paths.Game(profile.Id), "mods");
        SafePath.RejectLinks(directory);
        var files = Directory.EnumerateFiles(directory, "*.jar").Take(2049).OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToArray();
        if (files.Length > 2048) throw new InvalidDataException("Завелика кількість модів.");
        var known = new List<InstalledCatalogMod>(); var warnings = new List<string>(); var fingerprint = new StringBuilder();
        var artifacts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            SafePath.RejectLinks(file);
            if (new FileInfo(file).Length > 512L * 1024 * 1024) throw new InvalidDataException("Завеликий встановлений JAR.");
            await using var stream = File.OpenRead(file);
            var hash = Convert.ToHexString(await SHA512.HashDataAsync(stream, ct)).ToLowerInvariant();
            fingerprint.Append(Path.GetFileName(file)).Append(':').Append(hash).Append('\n');
            artifacts[hash] = stream.Length;
            if (!identify) continue;
            try
            {
                var version = await catalog.FindBySha512Async(hash, ct);
                if (version is not null) known.Add(new(version));
                else warnings.Add(Path.GetFileName(file) + ": не знайдено в Modrinth; залежності не вигадуються.");
            }
            catch (Exception ex) when (ex is InvalidDataException or HttpRequestException)
            { warnings.Add(Path.GetFileName(file) + ": ідентифікація недоступна; сумісність невідома."); }
        }
        return new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString()))), known, warnings, artifacts);
    }
    internal static IEnumerable<ModPlanEntry> DistinctArtifacts(IEnumerable<ModPlanEntry> entries) =>
        entries.GroupBy(e => e.Version.File.Sha512, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First() with { Reason = string.Join("; ", group.Select(e => e.Reason).Distinct()) });

    private static DependencyResolution WithInstalledArtifacts(DependencyResolution resolution, Snapshot snapshot)
    {
        var blockers = resolution.Blockers.ToList();
        foreach (var group in resolution.Entries.GroupBy(e => e.Version.File.Sha512, StringComparer.OrdinalIgnoreCase))
        {
            var size = group.First().Version.File.Size;
            if (group.Any(e => e.Version.File.Size != size) || snapshot.Artifacts.TryGetValue(group.Key, out var actual) && actual != size)
                blockers.Add("Несумісні розміри файла в метаданих Modrinth: " + group.First().Version.File.Name);
        }
        return resolution with
        {
            Entries = resolution.Entries.Select(e => e with
            {
                AlreadyInstalled = e.AlreadyInstalled || snapshot.Artifacts.ContainsKey(e.Version.File.Sha512)
            }).ToArray(),
            Blockers = blockers
        };
    }
    public async Task<RemoteInstallPlan> PreviewAsync(GameProfile profile, IReadOnlyList<RemoteModRequest> requests, CancellationToken ct = default)
    {
        using var lease = new ProfileUsage(paths).Acquire(profile.Id);
        new ProfileUsage(paths).CheckWorldLocks(profile.Id);
        catalog.BeginOperation();
        var snapshot = await ScanAsync(profile, true, ct);
        var resolution = await new RemoteDependencyResolver(catalog).ResolveAsync(profile, requests, snapshot.Known, ct);
        resolution = WithInstalledArtifacts(resolution, snapshot) with { Warnings = resolution.Warnings.Concat(snapshot.Warnings).ToArray() };
        return new(profile.Id, profile.MinecraftVersion, profile.Loader, snapshot.Fingerprint, resolution, catalog.UsedCache);
    }
    public async Task InstallAsync(GameProfile profile, RemoteInstallPlan plan, bool confirmed,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        if (!confirmed) throw new InvalidOperationException("Підтвердьте повний перелік модів перед встановленням.");
        if (plan.ProfileId != profile.Id || plan.MinecraftVersion != profile.MinecraftVersion || plan.Loader != profile.Loader ||
            !plan.Resolution.CanInstall) throw new InvalidOperationException("План несумісний із профілем або містить помилки.");
        using var lease = new ProfileUsage(paths).Acquire(profile.Id);
        new ProfileUsage(paths).CheckWorldLocks(profile.Id);
        await local.RecoverUnderLeaseAsync(profile, ct);
        if ((await ScanAsync(profile, false, ct)).Fingerprint != plan.InstalledFingerprint)
            throw new InvalidOperationException("Моди профілю змінилися. Повторно розрахуйте кошик.");
        var cart = new List<ModCartItem>();
        foreach (var entry in DistinctArtifacts(plan.Resolution.Entries.Where(e => !e.AlreadyInstalled)))
        {
            cart.Add(await PrepareCatalogJarAsync(entry.Version, entry.Reason, entry.Title, progress, ct));
        }
        if ((await ScanAsync(profile, false, ct)).Fingerprint != plan.InstalledFingerprint)
            throw new InvalidOperationException("Локальні моди змінилися під час завантаження. Повторіть перевірку.");
        if (cart.Count == 0) return;
        await local.InstallUnderLeaseAsync(profile, cart, true, ct);
        await store.AddHistoryAsync(profile.Id, "mods.modrinth", string.Join(", ", plan.Resolution.Entries.Select(e => e.Version.ProjectId + "@" + e.Version.Id)), CancellationToken.None);
    }
}
