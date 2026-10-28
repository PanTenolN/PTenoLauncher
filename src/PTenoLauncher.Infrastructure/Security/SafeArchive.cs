using System.IO.Compression;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Security;

public static class SafeArchive
{
    public static void Validate(ZipArchive archive, string destination, long maxExpanded = 1024L * 1024 * 1024, int maxEntries = 50000)
    {
        if (archive.Entries.Count > maxEntries) throw new InvalidDataException("Забагато файлів в архіві.");
        long total = 0; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.TrimEnd('/');
            if (name.Length == 0) continue;
            _ = SafePath.Join(destination, name);
            if (!names.Add(name)) throw new InvalidDataException("Дублікати шляхів в архіві.");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Посилання в архівах заборонені.");
            total = checked(total + entry.Length);
            if (total > maxExpanded || entry.Length > 512L * 1024 * 1024 ||
                (entry.Length > 1024 * 1024 && entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > 1000))
                throw new InvalidDataException("Архів перевищує дозволений розмір розпакування.");
        }
    }
    // Extract to a fresh staging directory. Caller commits or removes the staging directory.
    public static async Task ExtractAsync(string source, string destination, CancellationToken ct = default)
    {
        if (Directory.Exists(destination)) throw new IOException("Для розпакування потрібна нова директорія.");
        using var archive = ZipFile.OpenRead(source);
        Validate(archive, destination); Directory.CreateDirectory(destination);
        try
        {
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.FullName.EndsWith('/')) continue;
                var target = SafePath.Join(destination, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = entry.Open();
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                var buffer = new byte[81920]; long total = 0; int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    if ((total += read) > entry.Length) throw new InvalidDataException("Розмір розпакування не відповідає архіву.");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                if (total != entry.Length) throw new InvalidDataException("Неповний файл в архіві.");
            }
        }
        catch { Directory.Delete(destination, true); throw; }
    }
}