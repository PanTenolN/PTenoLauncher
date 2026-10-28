using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Mods;
public sealed record ModJournalEntry(string Temporary, string Target, string Sha256, long Size);
public sealed record ModInstallJournal(Guid Transaction, Guid ProfileId, string State, IReadOnlyList<ModJournalEntry> Entries);

public sealed partial class LocalModManager
{
    public async Task RecoverAsync(GameProfile profile, CancellationToken ct = default)
    {
        using var lease = new ProfileUsage(paths).Acquire(profile.Id);
        new ProfileUsage(paths).CheckWorldLocks(profile.Id);
        await RecoverUnderLeaseAsync(profile, ct);
    }
    internal async Task RecoverUnderLeaseAsync(GameProfile profile, CancellationToken ct)
    {
        var directory = SafePath.Join(paths.Profile(profile.Id), "transactions");
        if (!Directory.Exists(directory)) return;
        foreach (var journal in Directory.EnumerateFiles(directory, "mods-*.json"))
            await RecoverJournalAsync(profile, journal, ct);
    }
    private async Task RecoverJournalAsync(GameProfile profile, string journalPath, CancellationToken ct)
    {
        var journal = await AtomicFile.ReadJsonAsync<ModInstallJournal>(journalPath, ct) ?? throw new InvalidDataException("Порожній журнал модів.");
        if (journal.ProfileId != profile.Id || journal.State is not ("Prepared" or "Committed") ||
            Path.GetFileName(journalPath) != $"mods-{journal.Transaction:N}.json")
            throw new InvalidDataException("Непідтверджений журнал установлення модів.");
        if (journal.Entries is null || journal.Entries.Any(e => e is null)) throw new InvalidDataException("Пошкоджений журнал модів.");
        new ModInventoryStore(paths).Validate(profile.Id, new(profile.Id, journal.Entries.Select(e => new InstalledModFile(e.Target, e.Sha256, e.Size)).ToArray()));
        // Preflight every entry before removing anything. A changed target remains untouched.
        foreach (var item in journal.Entries)
        {
            if (!item.Target.StartsWith("mods/", StringComparison.Ordinal) || item.Target.Count(c => c == '/') != 1 ||
                !item.Target.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                item.Temporary != item.Target + ".pending-" + journal.Transaction.ToString("N"))
                throw new InvalidDataException("Небезпечний шлях у журналі модів.");
            var target = SafePath.Join(paths.Game(profile.Id), item.Target);
            var temporary = SafePath.Join(paths.Game(profile.Id), item.Temporary);
            if (File.Exists(target))
            {
                if (!await VerifiedDownloader.VerifyAsync(target, item.Sha256, "SHA256", item.Size, ct))
                    throw new InvalidDataException("Файл мода змінився після перерваного встановлення. Потрібна ручна перевірка.");

            }
            else if (journal.State == "Committed") throw new InvalidDataException("Завершена транзакція модів має відсутній файл.");
        }
        if (journal.State == "Committed")
            await new ModInventoryStore(paths).RecordCommittedAsync(profile.Id, journal.Entries.Select(e => new InstalledModFile(e.Target, e.Sha256, e.Size)).ToArray(), ct);
        foreach (var item in journal.Entries)
        {
            var target = SafePath.Join(paths.Game(profile.Id), item.Target);
            var temporary = SafePath.Join(paths.Game(profile.Id), item.Temporary);
            if (journal.State == "Prepared" && File.Exists(target)) File.Delete(target);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        File.Delete(journalPath);
    }
}