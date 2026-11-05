using System.IO.Compression;
using System.Security.Cryptography;
using PTenoLauncher.Infrastructure.Security;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.Modpacks;

internal sealed record InstanceSourceEntry(string Path, bool Directory, long Size);
// Only the explicitly selected instance. Never recurse into saves, runtime or account-store directories.
internal sealed class InstanceSource : IDisposable
{
    private readonly string source;
    private readonly ZipArchive? zip;
    private readonly bool clientContent;
    private long total;
    public bool IsDirectory => zip is null;
    public IReadOnlyList<InstanceSourceEntry> Entries { get; }
    public InstanceSource(string path, bool clientContent = false)
    {
        this.clientContent = clientContent;
        source = Path.GetFullPath(path); SafePath.RejectLinks(source);
        var entries = new List<InstanceSourceEntry>();
        if (System.IO.Directory.Exists(source)) Walk(source, "", entries);
        else
        {
            if (new FileInfo(source).Length > MrpackReader.MaxArchiveBytes) throw new MrpackException("PackBudget");
            MrpackReader.CheckContainer(source); zip = ZipFile.OpenRead(source);
            try { SafeArchive.Validate(zip, Path.GetTempPath(), MrpackReader.MaxExpandedBytes, MrpackReader.MaxEntries); entries.AddRange(zip.Entries.Select(e => new InstanceSourceEntry(e.FullName.TrimEnd('/'), e.FullName.EndsWith('/'), e.Length))); }
            catch { zip.Dispose(); throw; }
        }
        try { MrpackReader.PathsRule(entries.Select(e => (e.Path, e.Directory)), true); }
        catch { zip?.Dispose(); throw; }
        Entries = entries.OrderBy(e => e.Path, StringComparer.Ordinal).ToArray();
    }
    private void Walk(string folder, string relative, List<InstanceSourceEntry> entries)
    {
        SafePath.RejectLinks(folder);
        foreach (var path in System.IO.Directory.EnumerateFileSystemEntries(folder))
        {
            if (entries.Count >= MrpackReader.MaxEntries) throw new MrpackException("PackBudget");
            SafePath.RejectLinks(path); var name = relative.Length == 0 ? Path.GetFileName(path) : relative + "/" + Path.GetFileName(path);
            MrpackReader.PathRule(name, true); bool directory = System.IO.Directory.Exists(path);
            var size = directory ? 0 : new FileInfo(path).Length;
            if (size > MrpackReader.MaxArchiveBytes || (total = checked(total + size)) > MrpackReader.MaxExpandedBytes) throw new MrpackException("PackBudget");
            entries.Add(new(name, directory, size));
            if (!directory) continue;
            var parts = name.Split('/');
            bool game = parts[0] is "minecraft" or ".minecraft";
            bool payload = parts.Length > 1 && parts[1] is "mods" or "config" or "resourcepacks" or "shaderpacks";
            bool allowedContent = parts[0] is "mods" or "config" or "resourcepacks" or "shaderpacks";
            if (clientContent ? allowedContent && !ZipPackReader.PrivatePath(name) && (parts[0] != "mods" || parts.Length == 1) : game && (parts.Length == 1 || payload && !ZipPackReader.PrivatePath(string.Join('/', parts.Skip(1))) && (parts[1] != "mods" || parts.Length == 2))) Walk(path, name, entries);
            // patches/jarmods presence is sufficient to block; their bytes are never needed.
        }
    }
    public bool Exists(string path) => Entries.Any(e => e.Path == path);
    public Stream Open(string path)
    {
        var entry = Entries.SingleOrDefault(e => e.Path == path && !e.Directory) ?? throw new MrpackException("PackChanged");
        if (zip is not null) return zip.GetEntry(entry.Path)!.Open();
        return new FileStream(SafePath.Join(source, path), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
    }
    public async Task<byte[]> Bytes(string path, int limit, CancellationToken ct)
    {
        var entry = Entries.SingleOrDefault(e => e.Path == path && !e.Directory) ?? throw new MrpackException("InstanceMetadata");
        if (entry.Size > limit) throw new MrpackException("PackBudget");
        await using var stream = Open(path); using var result = new MemoryStream(); var buffer = new byte[81920]; int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0)
        { if (result.Length + count > limit || result.Length + count > entry.Size) throw new MrpackException("PackChanged"); result.Write(buffer, 0, count); }
        if (result.Length != entry.Size) throw new MrpackException("PackChanged"); return result.ToArray();
    }
    public async Task<string> Hash(string path, CancellationToken ct)
    {
        var entry = Entries.Single(e => e.Path == path && !e.Directory);
        await using var stream = Open(path); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920]; long count = 0; int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        { if ((count += read) > entry.Size) throw new MrpackException("PackChanged"); hash.AppendData(buffer, 0, read); }
        if (count != entry.Size) throw new MrpackException("PackChanged"); return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    public void Dispose() => zip?.Dispose();
}
