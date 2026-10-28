using System.Text.RegularExpressions;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.Mods;

public sealed record InstalledModFile(string RelativePath, string Sha256, long Size);
public sealed record InstalledModInventory(Guid ProfileId, IReadOnlyList<InstalledModFile> Files, int Schema = 1);

/// <summary>A local integrity baseline, not an antivirus verdict or publisher signature.</summary>
public sealed class ModInventoryStore(LauncherPaths paths)
{
    public const int MaximumFiles = 2048;
    public string PathFor(Guid id) => SafePath.Join(paths.Profile(id), "installed-mods.json");
    public async Task<InstalledModInventory?> ReadAsync(Guid id, CancellationToken ct = default)
    {
        var inventory = await AtomicFile.ReadJsonAsync<InstalledModInventory>(PathFor(id), ct);
        if (inventory is not null) Validate(id, inventory);
        return inventory;
    }
    public void Validate(Guid id, InstalledModInventory inventory)
    {
        if (inventory.ProfileId != id || inventory.Schema != 1 || inventory.Files is null || inventory.Files.Count > MaximumFiles)
            throw new InvalidDataException("Непідтверджений список контрольних хешів модів.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in inventory.Files)
        {
            if (file is null || file.RelativePath is null || !file.RelativePath.StartsWith("mods/", StringComparison.Ordinal) ||
                file.RelativePath.Count(c => c == '/') != 1 || !file.RelativePath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                file.Sha256 is null || !Regex.IsMatch(file.Sha256, "^[a-fA-F0-9]{64}$") ||
                file.Size is <= 0 or > 512L * 1024 * 1024 || !seen.Add(file.RelativePath))
                throw new InvalidDataException("Небезпечний запис контрольних хешів модів.");
            _ = SafePath.Join(paths.Game(id), file.RelativePath);
        }
    }
    public async Task RecordCommittedAsync(Guid id, IReadOnlyList<InstalledModFile> additions, CancellationToken ct = default)
    {
        Validate(id, new(id, additions));
        var existing = await ReadAsync(id, ct);
        var merged = (existing?.Files ?? []).ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
        foreach (var file in additions)
        {
            if (merged.TryGetValue(file.RelativePath, out var prior) &&
                (!prior.Sha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase) || prior.Size != file.Size))
                throw new InvalidDataException("Контрольний хеш уже встановленого мода змінився. Потрібна ручна перевірка.");
            merged[file.RelativePath] = file;
        }
        var inventory = new InstalledModInventory(id, merged.Values.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToArray());
        Validate(id, inventory);
        await AtomicFile.WriteJsonAsync(PathFor(id), inventory, ct);
    }
}
