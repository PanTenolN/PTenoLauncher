using System.IO.Compression;
using System.Text.Json;
using PTenoLauncher.Infrastructure.Modpacks;
using PTenoLauncher.Infrastructure.Security;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public enum ProfileContentKind { ResourcePacks, Shaders, Worlds }
public sealed record ProfileContentEntry(string FileName, string Name, bool Directory);

public sealed partial class ProfileContent(LauncherPaths paths)
{
    private const int MetadataLimit = 128 * 1024;
    private const int PackEntryLimit = 50000;
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint value = (uint)n;
        for (var bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) == 0 ? 0u : 0xedb88320u);
        return value;
    }).ToArray();

    public string Folder(Guid profile, ProfileContentKind kind) => SafePath.Join(paths.Game(profile), kind switch
    {
        ProfileContentKind.ResourcePacks => "resourcepacks",
        ProfileContentKind.Shaders => "shaderpacks",
        ProfileContentKind.Worlds => "saves",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    });

    public async Task<IReadOnlyList<ProfileContentEntry>> ListAsync(Guid profile, ProfileContentKind kind, CancellationToken ct = default)
    {
        var folder = Folder(profile, kind);
        if (!Directory.Exists(folder)) return [];
        var rows = new List<ProfileContentEntry>();
        foreach (var path in Directory.EnumerateFileSystemEntries(folder).Take(1000).Order(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
            var directory = (attributes & FileAttributes.Directory) != 0;
            var file = Path.GetFileName(path);
            if (file.StartsWith(".pteno-", StringComparison.OrdinalIgnoreCase)) continue;
            if (kind == ProfileContentKind.Worlds)
            {
                if (directory && File.Exists(Path.Combine(path, "level.dat"))) rows.Add(new(file, file, true));
                continue;
            }
            if (!directory && !file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
            string? name = null;
            if (kind == ProfileContentKind.ResourcePacks)
            {
                try
                {
                    if (directory)
                    {
                        var metadata = SafePath.Join(path, "pack.mcmeta");
                        if (File.Exists(metadata))
                        {
                            await using var stream = File.OpenRead(metadata);
                            if (stream.Length <= MetadataLimit)
                            {
                                var bytes = new byte[(int)stream.Length];
                                await stream.ReadExactlyAsync(bytes, ct);
                                name = ResourceName(bytes);
                            }
                        }
                    }
                    else if (new FileInfo(path).Length <= MrpackReader.MaxArchiveBytes)
                    {
                        MrpackReader.CheckContainer(path, PackEntryLimit);
                        using var zip = ZipFile.OpenRead(path);
                        if (zip.GetEntry("pack.mcmeta") is { } entry)
                            name = ResourceName(await MrpackReader.EntryBytes(entry, MetadataLimit, ct));
                    }
                }
                catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException) { }
            }
            rows.Add(new(file, name ?? (directory ? file : Path.GetFileNameWithoutExtension(file)), directory));
        }
        return rows;
    }

    public async Task<ProfileContentEntry> ImportAsync(Guid profile, ProfileContentKind kind, string source, CancellationToken ct = default,
        string? fileName = null, string? expectedSha512 = null)
    {
        if (kind == ProfileContentKind.Worlds) throw new ArgumentOutOfRangeException(nameof(kind));
        source = Path.GetFullPath(source);
        SafePath.RejectLinks(source);
        fileName ??= Path.GetFileName(source);
        if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || fileName != Path.GetFileName(fileName) ||
            fileName.StartsWith(".pteno-", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("ContentInvalidZip");
        var usage = new ProfileUsage(paths);
        using var lease = usage.Acquire(profile);
        usage.CheckWorldLocks(profile);
        var folder = Folder(profile, kind);
        var destination = SafePath.Join(folder, fileName);
        if (!Directory.Exists(paths.Game(profile))) throw new IOException("ContentMissing");
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("ContentCollision");
        Directory.CreateDirectory(folder);
        var pending = SafePath.Join(folder, ".pteno-import-" + Guid.NewGuid().ToString("N") + ".part");
        var owned = false;
        try
        {
            await using (var input = File.OpenRead(source))
            {
                if (input.Length is <= 0 or > MrpackReader.MaxArchiveBytes) throw new InvalidDataException("ContentInvalidZip");
                await using var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                owned = true;
                await input.CopyToAsync(output, ct);
                await output.FlushAsync(ct);
                output.Flush(true);
            }
            if (expectedSha512 is not null)
            {
                await using var staged = File.OpenRead(pending);
                var sha512 = Convert.ToHexString(await System.Security.Cryptography.SHA512.HashDataAsync(staged, ct));
                if (!sha512.Equals(expectedSha512, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("ContentCatalogMismatch");
            }
            var name = await ValidatePackAsync(pending, kind, ct) ?? Path.GetFileNameWithoutExtension(fileName);
            ct.ThrowIfCancellationRequested();
            SafePath.RejectLinks(destination);
            File.Move(pending, destination, false);
            owned = false;
            return new(fileName, name, false);
        }
        finally
        {
            if (owned) { SafePath.RejectLinks(pending); File.Delete(pending); }
        }
    }

    private async Task<string?> ValidatePackAsync(string file, ProfileContentKind kind, CancellationToken ct)
    {
        try
        {
            MrpackReader.CheckContainer(file, PackEntryLimit);
            using var zip = ZipFile.OpenRead(file);
            SafeArchive.Validate(zip, paths.Cache, maxEntries: PackEntryLimit);
            MrpackReader.PathsRule(zip.Entries.Select(e => (e.FullName.TrimEnd('/'), e.FullName.EndsWith('/'))), allowExcludedNames: true);
            string? name = null;
            if (kind == ProfileContentKind.ResourcePacks)
            {
                var metadata = zip.GetEntry("pack.mcmeta") ?? throw new InvalidDataException("ContentResourceRoot");
                name = ResourceName(await MrpackReader.EntryBytes(metadata, MetadataLimit, ct));
            }
            else if (!zip.Entries.Any(e => e.FullName.StartsWith("shaders/", StringComparison.Ordinal) &&
                Path.GetExtension(e.FullName) is ".vsh" or ".fsh" or ".csh"))
                throw new InvalidDataException("ContentShaderRoot");
            var buffer = new byte[81920];
            foreach (var entry in zip.Entries.Where(e => !e.FullName.EndsWith('/')))
            {
                await using var stream = entry.Open();
                uint crc = uint.MaxValue;
                long size = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer, ct)) != 0)
                {
                    if ((size += read) > entry.Length) throw new InvalidDataException("ContentInvalidZip");
                    for (var i = 0; i < read; i++) crc = CrcTable[(crc ^ buffer[i]) & 255] ^ (crc >> 8);
                }
                if (size != entry.Length || ~crc != entry.Crc32) throw new InvalidDataException("ContentInvalidZip");
            }
            return name;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or MrpackException)
        { throw new InvalidDataException("ContentInvalidZip", e); }
    }

    private static string? ResourceName(byte[] bytes)
    {
        using var json = MrpackReader.Json(bytes);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("pack", out var pack) || pack.ValueKind != JsonValueKind.Object ||
            !pack.TryGetProperty("description", out var description) ||
            description.ValueKind is not (JsonValueKind.String or JsonValueKind.Array or JsonValueKind.Object) ||
            !(pack.TryGetProperty("pack_format", out var format) && format.ValueKind == JsonValueKind.Number && format.TryGetInt32(out var major) && major >= 0 ||
              pack.TryGetProperty("min_format", out var min) && FormatNumber(min) &&
              pack.TryGetProperty("max_format", out var max) && FormatNumber(max)))
            throw new InvalidDataException("ContentResourceRoot");
        var text = new System.Text.StringBuilder();
        void Append(JsonElement element)
        {
            if (text.Length >= 160) return;
            if (element.ValueKind == JsonValueKind.String)
            {
                foreach (var c in element.GetString()!)
                {
                    if (!char.IsControl(c)) text.Append(c);
                    if (text.Length >= 160) break;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) Append(child);
            else if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("text", out var value)) Append(value);
                if (element.TryGetProperty("extra", out var extra)) Append(extra);
            }
        }
        Append(description);
        return text.ToString().Trim() is { Length: > 0 } title ? title : null;
    }

    private static bool FormatNumber(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) && n >= 0 ||
        value.ValueKind == JsonValueKind.Array && value.GetArrayLength() is 1 or 2 &&
        value.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var part) && part >= 0);
}
