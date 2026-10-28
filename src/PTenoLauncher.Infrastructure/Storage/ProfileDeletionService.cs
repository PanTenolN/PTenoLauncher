using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PTenoLauncher.Core;
namespace PTenoLauncher.Infrastructure.Storage;

public sealed record DeletionFile(string RelativePath, long Size, long LastWriteTicks);
public sealed record ProfileDeletionPlan(Guid ProfileId, string ProfileName, bool IncludeBackups,
    IReadOnlyList<string> SelectedExports, IReadOnlyList<string> Roots, IReadOnlyList<DeletionFile> Files, string Fingerprint)
{
    public long TotalBytes => Files.Sum(f => f.Size);
}
public sealed record DeletionResult(bool CleanupPending);
internal sealed record DeletionMove(string Source, string Destination, bool Directory);
internal sealed record DeletionJournal(Guid Operation, Guid ProfileId, IReadOnlyList<DeletionMove> Moves);

/// <summary>Delete only an explicitly previewed profile subtree and explicitly selected owned artifacts.
/// Staging + journal allow rollback before the database commit and cleanup after it.</summary>
public sealed class ProfileDeletionService(LauncherPaths paths, ILauncherStore store, ProfileUsage usage)
{
    public async Task<ProfileDeletionPlan> PreviewAsync(Guid profileId, bool includeBackups,
        IReadOnlyList<string>? selectedExports = null, CancellationToken ct = default)
    {
        using var lease = usage.Acquire(profileId);
        usage.CheckWorldLocks(profileId);
        return await BuildPlanAsync(profileId, includeBackups, selectedExports ?? [], ct);
    }
    private async Task<ProfileDeletionPlan> BuildPlanAsync(Guid profileId, bool includeBackups, IReadOnlyList<string> exports, CancellationToken ct)
    {
        var profile = (await store.GetProfilesAsync(ct)).SingleOrDefault(p => p.Id == profileId) ?? throw new InvalidOperationException("Профіль більше не існує.");
        var artifacts = (await store.GetArtifactsAsync(profileId, ct)).ToList();
        // 0.1 backups predate the ownership table. Inspect only the launcher's own backup directory.
        if (includeBackups && Directory.Exists(paths.Backups))
        {
            foreach (var file in Directory.EnumerateFiles(paths.Backups, profileId.ToString("N") + "-*.zip", SearchOption.TopDirectoryOnly))
            {
                SafePath.RejectLinks(file);
                try
                {
                    using var zip = ZipFile.OpenRead(file); var entry = zip.GetEntry("backup.json");
                    if (entry is null || entry.Length > 1024 * 1024) continue;
                    using var json = JsonDocument.Parse(entry.Open());
                    if (json.RootElement.GetProperty("profile").GetProperty("id").GetGuid() == profileId)
                        artifacts.Add(new(profileId, Path.GetRelativePath(paths.Root, file).Replace('\\', '/'), "backup"));
                }
                catch (Exception ex) when (ex is InvalidDataException or JsonException or KeyNotFoundException) { /* Unverifiable files are never included. */ }
            }
        }
        var roots = new List<string>();
        var profileRelative = $"instances/{profileId:N}";
        if (Directory.Exists(paths.Profile(profileId))) roots.Add(profileRelative);
        foreach (var artifact in artifacts.DistinctBy(a => a.RelativePath))
        {
            ValidateArtifact(artifact, profileId);
            if (artifact.Kind == "backup" && includeBackups || artifact.Kind == "export" && exports.Contains(artifact.RelativePath, StringComparer.Ordinal))
                if (File.Exists(SafePath.Join(paths.Root, artifact.RelativePath))) roots.Add(artifact.RelativePath);
        }
        if (exports.Any(p => !artifacts.Any(a => a.Kind == "export" && a.RelativePath == p)))
            throw new InvalidOperationException("Обраний ZIP не є відстежуваним файлом цього профілю.");
        var files = await Task.Run(() =>
        {
            var entries = new List<DeletionFile>();
            void Walk(string path)
            {
                ct.ThrowIfCancellationRequested(); SafePath.RejectLinks(path);
                if (Directory.Exists(path))
                    foreach (var child in Directory.EnumerateFileSystemEntries(path)) Walk(child);
                else if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    entries.Add(new(Path.GetRelativePath(paths.Root, path).Replace('\\', '/'), info.Length, info.LastWriteTimeUtc.Ticks));
                }
            }
            foreach (var relative in roots) Walk(SafePath.Join(paths.Root, relative));
            return entries.OrderBy(e => e.RelativePath, StringComparer.Ordinal).ToArray();
        }, ct);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            profile.Id + "\n" + profile.Name + "\n" + string.Join("\n", roots.Order()) + "\n" +
            string.Join("\n", files.Select(f => $"{f.RelativePath}|{f.Size}|{f.LastWriteTicks}")))));
        return new(profileId, profile.Name, includeBackups, exports.ToArray(), roots, files, fingerprint);
    }
    private static void ValidateArtifact(OwnedArtifact artifact, Guid id)
    {
        if (artifact.Kind == "translation-resourcepack" && artifact.ProfileId == id &&
            System.Text.RegularExpressions.Regex.IsMatch(artifact.RelativePath, @"\Ainstances/" + id.ToString("N") +
                @"/game/resourcepacks/pteno-translations-(?:uk_ua|en_us|pl_pl|de_de|es_es|fr_fr|pt_br|ru_ru)\.zip\z")) return;
        if (artifact.ProfileId != id || artifact.Kind is not ("backup" or "export") ||
            !(artifact.Kind == "backup" && artifact.RelativePath.StartsWith("backups/", StringComparison.Ordinal) ||
              artifact.Kind == "export" && artifact.RelativePath.StartsWith("exports/", StringComparison.Ordinal)))
            throw new InvalidDataException("Непідтверджена належність файла профілю.");
    }
    public async Task<DeletionResult> DeleteAsync(ProfileDeletionPlan approved, string typedName, CancellationToken ct = default)
    {
        if (!string.Equals(approved.ProfileName, typedName, StringComparison.Ordinal))
            throw new InvalidOperationException("Для підтвердження введіть точну назву профілю.");
        using var lease = usage.Acquire(approved.ProfileId); usage.CheckWorldLocks(approved.ProfileId);
        var current = await BuildPlanAsync(approved.ProfileId, approved.IncludeBackups, approved.SelectedExports, ct);
        if (current.Fingerprint != approved.Fingerprint || current.ProfileName != approved.ProfileName)
            throw new InvalidOperationException("Файли або налаштування змінилися. Перегляньте список і підтвердьте повторно.");
        var operation = Guid.NewGuid(); var quarantine = SafePath.Join(paths.Root, $"trash/{operation:N}");
        Directory.CreateDirectory(quarantine);
        var moves = current.Roots.Select((r, i) => new DeletionMove(r, $"trash/{operation:N}/item-{i}", Directory.Exists(SafePath.Join(paths.Root, r)))).ToArray();
        var journal = new DeletionJournal(operation, approved.ProfileId, moves);
        var journalFile = SafePath.Join(quarantine, "deletion.json");
        await AtomicFile.WriteJsonAsync(journalFile, journal, ct);
        bool committed = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            foreach (var move in moves)
            {
                var source = SafePath.Join(paths.Root, move.Source); var destination = SafePath.Join(paths.Root, move.Destination);
                if (move.Directory) Directory.Move(source, destination); else File.Move(source, destination, false);
            }
            // No cancellation between filesystem staging and metadata commit.
            await store.DeleteProfileMetadataAsync(approved.ProfileId, CancellationToken.None); committed = true;
            try { Purge(quarantine); return new(false); }
            catch (IOException) { return new(true); }
            catch (UnauthorizedAccessException) { return new(true); }
        }
        catch
        {
            if (!committed) { Rollback(journal); Purge(quarantine); }
            throw;
        }
    }
    public async Task RecoverAsync(CancellationToken ct = default)
    {
        var trash = SafePath.Join(paths.Root, "trash"); if (!Directory.Exists(trash)) return;
        foreach (var directory in Directory.EnumerateDirectories(trash))
        {
            ct.ThrowIfCancellationRequested(); SafePath.RejectLinks(directory);
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var operation)) continue;
            var file = SafePath.Join(directory, "deletion.json");
            var journal = await AtomicFile.ReadJsonAsync<DeletionJournal>(file, ct);
            if (journal is null || journal.Operation != operation) continue;
            using var lease = usage.Acquire(journal.ProfileId);
            var exists = (await store.GetProfilesAsync(ct)).Any(p => p.Id == journal.ProfileId);
            ValidateJournal(journal);
            if (exists) Rollback(journal);
            Purge(directory);
        }
    }
    private void ValidateJournal(DeletionJournal journal)
    {
        foreach (var move in journal.Moves)
        {
            _ = SafePath.Join(paths.Root, move.Source); _ = SafePath.Join(paths.Root, move.Destination);
            if (!move.Destination.StartsWith($"trash/{journal.Operation:N}/item-", StringComparison.Ordinal) ||
                !(move.Source == $"instances/{journal.ProfileId:N}" ||
                  !move.Directory && (move.Source.StartsWith("backups/", StringComparison.Ordinal) || move.Source.StartsWith("exports/", StringComparison.Ordinal))))
                throw new InvalidDataException("Небезпечний журнал видалення.");
        }
    }
    private void Rollback(DeletionJournal journal)
    {
        ValidateJournal(journal);
        foreach (var move in journal.Moves.Reverse())
        {
            var source = SafePath.Join(paths.Root, move.Source); var staged = SafePath.Join(paths.Root, move.Destination);
            if (!File.Exists(staged) && !Directory.Exists(staged)) continue;
            if (File.Exists(source) || Directory.Exists(source)) throw new IOException("Відновлення видалення потребує ручної перевірки конфлікту.");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            if (move.Directory) Directory.Move(staged, source); else File.Move(staged, source, false);
        }
    }
    private void Purge(string directory)
    {
        var full = Path.GetFullPath(directory);
        if (!full.StartsWith(Path.GetFullPath(Path.Combine(paths.Root, "trash")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Очищення поза trash заборонено.");
        void Check(string path)
        {
            SafePath.RejectLinks(path);
            if (Directory.Exists(path)) foreach (var entry in Directory.EnumerateFileSystemEntries(path)) Check(entry);
        }
        Check(full); Directory.Delete(full, true);
    }
}
