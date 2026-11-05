using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

/// <summary>Verified objects in shared cache; virtual projections shared by index hash; mapped resources profile-owned.</summary>
public sealed class AssetManager(LauncherPaths paths, VerifiedDownloader downloads)
{
    private sealed record ProjectionJournal(int Schema, string IndexSha1, bool InProgress, IReadOnlyList<InstalledFile> Owned, IReadOnlyList<InstalledFile>? Pending = null);
    public sealed record PreparedAssets(IReadOnlyList<InstalledFile> Objects, LegacyAssetLayout? Layout);
    public static string ObjectPath(InstalledFile entry) => $"assets/objects/{entry.Hash[..2].ToLowerInvariant()}/{entry.Hash.ToLowerInvariant()}";
    public static string VirtualRelative(string id, LegacyAssetLayout layout) => $"assets/virtual/{AssetIndexPlan.ValidateId(id)}-{layout.IndexSha1.ToLowerInvariant()}";
    public static string GameAssets(LauncherPaths paths, Guid profile, InstallationReceipt receipt) => receipt.LegacyAssets is { Virtual: true } layout
        ? SafePath.Join(paths.Cache, VirtualRelative(receipt.AssetIndex, layout))
        : receipt.LegacyAssets is { MapToResources: true } ? SafePath.Join(paths.Game(profile), "resources") : SafePath.Join(paths.Cache, "assets");

    public async Task<PreparedAssets> PrepareAsync(GameProfile profile, string id, InstalledFile index,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        AssetIndexPlan.ValidateId(id);
        if (index.RelativePath != $"assets/indexes/{id}.json" || index.Algorithm != "SHA1" || index.Size > 16 * 1024 * 1024 ||
            !await VerifiedDownloader.VerifyAsync(SafePath.Join(paths.Cache, index.RelativePath), index.Hash, index.Algorithm, index.Size, ct))
            throw new InvalidDataException("The asset index has not been verified.");
        var plan = AssetIndexPlan.Parse(await File.ReadAllBytesAsync(SafePath.Join(paths.Cache, index.RelativePath), ct), paths.Cache);
        var unique = plan.Entries.DistinctBy(f => f.Hash, StringComparer.OrdinalIgnoreCase).ToArray();
        await Parallel.ForEachAsync(unique, new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = ct }, async (entry, token) =>
        {
            var relative = ObjectPath(entry);
            var hash = entry.Hash.ToLowerInvariant();
            await downloads.DownloadAsync(new($"https://resources.download.minecraft.net/{hash[..2]}/{hash}"),
                SafePath.Join(paths.Cache, relative), entry.Hash, entry.Size, progress: progress, ct: token);
        });
        LegacyAssetLayout? layout = plan.Virtual || plan.MapToResources ? new(index.Hash, plan.Virtual, plan.MapToResources, plan.Entries) : null;
        if (layout is not null)
        {
            foreach (var target in Targets(paths, profile.Id, id, layout))
                await ProjectAsync(target.Root, target.Journal, layout, progress, ct);
        }
        return new(unique.Select(f => f with { RelativePath = ObjectPath(f) }).ToArray(), layout);
    }
    private static IEnumerable<(string Root, string Journal)> Targets(LauncherPaths paths, Guid profile, string id, LegacyAssetLayout layout)
    {
        if (layout.Virtual) yield return (SafePath.Join(paths.Cache, VirtualRelative(id, layout)),
            SafePath.Join(paths.Cache, $"assets/projections/{id}-{layout.IndexSha1.ToLowerInvariant()}.json"));
        if (layout.MapToResources) yield return (SafePath.Join(paths.Game(profile), "resources"),
            SafePath.Join(paths.Profile(profile), "mapped-assets.json"));
    }
    private static async Task<ProjectionJournal?> ReadJournal(string journal, string root, CancellationToken ct)
    {
        var value = await AtomicFile.ReadJsonAsync<ProjectionJournal>(journal, ct);
        if (value is not null)
        {
            if (value.Schema != 1 || value.IndexSha1 is null || value.IndexSha1.Length != 40 || !value.IndexSha1.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid asset ownership journal.");
            AssetIndexPlan.ValidateEntries(root, value.Owned);
            if (value.Pending is not null) AssetIndexPlan.ValidateEntries(root, value.Pending);
        }
        return value;
    }
    private async Task ProjectAsync(string root, string journal, LegacyAssetLayout layout, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
        SafePath.RejectLinks(journal + ".lock");
        // Cross-profile/process serialization for shared virtual data. Never delete another owner's lock path.
        using var lease = new FileStream(journal + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var old = await ReadJournal(journal, root, ct);
        var owned = old?.Owned.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
        var pending = old is { InProgress: true, Pending: not null } ? old.Pending.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);
        var nextOwned = new Dictionary<string, InstalledFile>(owned, StringComparer.OrdinalIgnoreCase);
        var changes = new List<InstalledFile>();
        foreach (var file in layout.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var destination = SafePath.Join(root, file.RelativePath);
            if (Directory.Exists(destination)) throw new IOException("Asset destination is a directory: " + file.RelativePath);
            // Only an exact, hash-verified previous pending creation can be recovered across the copy/journal crash boundary.
            if (File.Exists(destination) && !owned.ContainsKey(file.RelativePath))
            {
                if (!pending.TryGetValue(file.RelativePath, out var intended) || !await VerifiedDownloader.VerifyAsync(destination, intended.Hash, intended.Algorithm, intended.Size, ct))
                    throw new IOException("An untracked file blocks asset preparation: " + file.RelativePath);
                owned[file.RelativePath] = intended;
            }
            if (!await VerifiedDownloader.VerifyAsync(destination, file.Hash, file.Algorithm, file.Size, ct)) changes.Add(file);
            nextOwned[file.RelativePath] = file;
        }
        AssetIndexPlan.ValidateEntries(root, nextOwned.Values.ToArray());
        if (changes.Count == 0 && old is { InProgress: false } && old.IndexSha1 == layout.IndexSha1) return;
        // Durable intent precedes file creation. A failed/cancelled operation is resumed by the next preparation.
        await AtomicFile.WriteJsonAsync(journal, new ProjectionJournal(1, layout.IndexSha1, true, owned.Values.ToArray(), layout.Entries), ct);
        foreach (var file in changes)
        {
            ct.ThrowIfCancellationRequested();
            var source = SafePath.Join(paths.Cache, ObjectPath(file));
            var destination = SafePath.Join(root, file.RelativePath);
            if (!await VerifiedDownloader.VerifyAsync(source, file.Hash, file.Algorithm, file.Size, ct))
                throw new InvalidDataException("Cached asset changed before projection.");
            if (File.Exists(destination))
            {
                if (!owned.ContainsKey(file.RelativePath))
                    throw new IOException("An untracked file appeared during asset preparation: " + file.RelativePath);
                // Preserve changed tracked bytes; SHA-1 integrity is not an antivirus check.
                var backup = SafePath.Join(journal + ".backups", Guid.NewGuid().ToString("N") + "/" + file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(destination, backup, false);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var part = destination + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                await using (var input = File.OpenRead(source))
                await using (var output = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                { await input.CopyToAsync(output, ct); await output.FlushAsync(ct); output.Flush(true); }
                if (!await VerifiedDownloader.VerifyAsync(part, file.Hash, file.Algorithm, file.Size, ct)) throw new InvalidDataException("Asset projection hash mismatch.");
                ct.ThrowIfCancellationRequested(); SafePath.RejectLinks(destination); File.Move(part, destination, owned.ContainsKey(file.RelativePath));
            }
            finally { if (File.Exists(part)) File.Delete(part); }
            progress?.Report(new("assets/" + file.RelativePath, file.Size, file.Size));
        }
        ct.ThrowIfCancellationRequested();
        await AtomicFile.WriteJsonAsync(journal, new ProjectionJournal(1, layout.IndexSha1, false, nextOwned.Values.ToArray()), ct);
    }
    public static async Task<IReadOnlyList<ReadinessIssue>> CheckAsync(LauncherPaths paths, Guid profile, InstallationReceipt receipt, CancellationToken ct = default)
    {
        var issues = new List<ReadinessIssue>();
        if (receipt.LegacyAssets is not { } layout) return issues;
        var descriptor = receipt.Files.Single(f => f.RelativePath == $"assets/indexes/{receipt.AssetIndex}.json");
        if (descriptor.Size > 16 * 1024 * 1024 || !await VerifiedDownloader.VerifyAsync(SafePath.Join(paths.Cache, descriptor.RelativePath), layout.IndexSha1, "SHA1", descriptor.Size, ct))
            throw new InvalidDataException("The projected asset index is corrupt.");
        var plan = AssetIndexPlan.Parse(await File.ReadAllBytesAsync(SafePath.Join(paths.Cache, descriptor.RelativePath), ct), paths.Cache);
        var expected = plan.Entries.ToDictionary(f => f.RelativePath, StringComparer.Ordinal);
        if (plan.Virtual != layout.Virtual || plan.MapToResources != layout.MapToResources || expected.Count != layout.Entries.Count ||
            layout.Entries.Any(f => !expected.TryGetValue(f.RelativePath, out var actual) || actual.Size != f.Size || !actual.Hash.Equals(f.Hash, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The asset layout does not match the verified index contents.");
        foreach (var target in Targets(paths, profile, receipt.AssetIndex, layout))
        {
            var journal = await ReadJournal(target.Journal, target.Root, ct);
            if (journal is null || journal.InProgress || journal.IndexSha1 != layout.IndexSha1)
                issues.Add(new("file.invalid", "Asset projection requires repair: " + receipt.AssetIndex));
            var owned = journal?.Owned.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
            foreach (var file in layout.Entries)
                if (owned is null || !owned.TryGetValue(file.RelativePath, out var tracked) || !tracked.Hash.Equals(file.Hash, StringComparison.OrdinalIgnoreCase) || tracked.Size != file.Size ||
                    !await VerifiedDownloader.VerifyAsync(SafePath.Join(target.Root, file.RelativePath), file.Hash, file.Algorithm, file.Size, ct))
                    issues.Add(new("file.invalid", "Missing or corrupt projected asset: " + file.RelativePath));
        }
        return issues;
    }
}
