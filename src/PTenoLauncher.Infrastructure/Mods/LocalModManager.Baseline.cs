using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.Mods;

public sealed record ModBaselinePreview(Guid ProfileId, IReadOnlyList<InstalledModFile> Files,
    IReadOnlyList<string> Removed, string Snapshot);

public sealed partial class LocalModManager
{
    public async Task<ModBaselinePreview> PreviewBaselineAsync(GameProfile profile, CancellationToken ct = default)
    {
        using var lease = new ProfileUsage(paths).Acquire(profile.Id);
        new ProfileUsage(paths).CheckWorldLocks(profile.Id);
        return await BuildBaselinePreviewAsync(profile, ct);
    }
    private async Task<ModBaselinePreview> BuildBaselinePreviewAsync(GameProfile profile, CancellationToken ct)
    {
        if (profile.Loader == ModLoader.Vanilla) throw new InvalidOperationException("Vanilla не завантажує моди.");
        var transactions = SafePath.Join(paths.Profile(profile.Id), "transactions");
        var directory = SafePath.Join(paths.Game(profile.Id), "mods");
        if (Directory.Exists(transactions) && Directory.EnumerateFiles(transactions, "mods-*.json").Any() ||
            Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*.pending-*").Any())
            throw new InvalidOperationException("Спочатку відновіть незавершене встановлення модів.");
        var prior = await new ModInventoryStore(paths).ReadAsync(profile.Id, ct);
        var files = Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.jar").Take(ModInventoryStore.MaximumFiles + 1).Order(StringComparer.Ordinal).ToArray() : [];
        if (files.Length > ModInventoryStore.MaximumFiles) throw new InvalidDataException("Перевищено ліміт 2048 модів.");
        var entries = new List<InstalledModFile>();
        foreach (var file in files)
        {
            var mod = await InspectAsync(file, ct);
            entries.Add(new("mods/" + mod.FileName, mod.Sha256, mod.Size));
        }
        var present = entries.Select(e => e.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = (prior?.Files ?? []).Where(f => !present.Contains(f.RelativePath)).Select(f => f.RelativePath).ToArray();
        var snapshot = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { profile.Id, profile.MinecraftVersion, profile.Loader, entries, prior }, AtomicFile.JsonOptions)));
        return new(profile.Id, entries, removed, snapshot);
    }
    public async Task ConfirmBaselineAsync(GameProfile profile, ModBaselinePreview preview, bool confirmed, CancellationToken ct = default)
    {
        if (!confirmed) throw new InvalidOperationException("Потрібне явне підтвердження переліку та контрольних хешів модів.");
        using var lease = new ProfileUsage(paths).Acquire(profile.Id);
        new ProfileUsage(paths).CheckWorldLocks(profile.Id);
        var current = await BuildBaselinePreviewAsync(profile, ct);
        if (preview.ProfileId != profile.Id || preview.Snapshot != current.Snapshot ||
            !preview.Files.SequenceEqual(current.Files) || !preview.Removed.SequenceEqual(current.Removed))
            throw new InvalidOperationException("Файли або профіль змінилися. Відкрийте перелік і підтвердьте його повторно.");
        var inventory = new InstalledModInventory(profile.Id, current.Files);
        new ModInventoryStore(paths).Validate(profile.Id, inventory);
        await AtomicFile.WriteJsonAsync(new ModInventoryStore(paths).PathFor(profile.Id), inventory, ct);
        await store.AddHistoryAsync(profile.Id, "mods.baseline.confirmed", current.Files.Count + " files; SHA256 snapshot " + current.Snapshot, CancellationToken.None);
    }
}
