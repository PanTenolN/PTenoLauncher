using System.IO.Compression;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Security;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.Modpacks;

// PTenoLauncher client-content subset, not a purported standard for third-party ZIP manifests.
public sealed record ZipPackRequest(string ProfileName = "", string Minecraft = "", ModLoader Loader = ModLoader.Vanilla,
    string LoaderVersion = "", string? ContentRoot = null, bool PinMods = false);
public sealed record ZipPackPreview(ZipPackRequest Request, IReadOnlyList<string> Roots, MrpackPreview Plan, ModCartReview? Review, bool IsMrpack);
internal sealed record ZipImportTarget(int Schema, string Origin, string Minecraft, ModLoader Loader, string? LoaderVersion, string ContentRoot);

public sealed class ZipPackReader(LauncherPaths paths, ILauncherStore store, MrpackReader mrpack)
{
    private static readonly string[] Categories = ["mods", "config", "resourcepacks", "shaderpacks"];
    private static readonly string[] ForeignMarkers = ["manifest.json", "minecraftinstance.json", "mmc-pack.json", "instance.cfg", "pack.toml", "index.toml"];
    private static readonly string[] Executables = [".exe", ".dll", ".com", ".msi", ".bat", ".cmd", ".ps1", ".sh", ".py", ".js", ".vbs", ".lnk", ".url", ".reg", ".scr"];
    private static readonly string[] PrivateSegments = ["accounts", "tokens", "credentials", "secrets", "auth", "updater", "java", "runtime", "libraries", "versions", "assets"];
    private static bool Exact(string value) => value.Length is > 0 and <= 100 && value.Any(char.IsAsciiDigit) && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '+');
    internal static bool PrivatePath(string path) => path.Split('/').Any(p => PrivateSegments.Contains(Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase) || p.StartsWith(".", StringComparison.Ordinal) || p.Contains("token", StringComparison.OrdinalIgnoreCase) || p.Contains("account", StringComparison.OrdinalIgnoreCase) || p.Contains("credential", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".db", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase));
    internal static bool Eligible(string path)
    {
        var parts = path.Split('/');
        if (parts.Length < 2 || !Categories.Contains(parts[0], StringComparer.Ordinal) || PrivatePath(path) || Executables.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) ||
            parts[0] == "mods" && !MrpackReader.RootJar(path) || !MrpackReader.RootJar(path) && path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) return false;
        try { MrpackReader.PathRule(path); return true; } catch (Exception e) when (e is MrpackException or InvalidDataException) { return false; }
    }
    public async Task<ZipPackPreview> PreviewAsync(string archive, ZipPackRequest request, CancellationToken ct = default)
    {
        if (request is null || request.ProfileName is null || request.Minecraft is null || request.LoaderVersion is null) throw new MrpackException("ZipRequirements");
        try { return await Read(archive, request, ct); }
        catch (Exception e) when (e is not MrpackException && e is InvalidDataException or ArgumentException or OverflowException or JsonException or InvalidOperationException)
        { throw new MrpackException("ZipInvalid", "", e); }
    }
    private async Task<ZipPackPreview> Read(string archive, ZipPackRequest request, CancellationToken ct)
    {
        archive = Path.GetFullPath(archive); SafePath.RejectLinks(archive); var info = new FileInfo(archive);
        if (info.Length > MrpackReader.MaxArchiveBytes) throw new MrpackException("PackBudget");
        var hash = await MrpackReader.HashFile(archive, ct); MrpackReader.CheckContainer(archive);
        using var zip = ZipFile.OpenRead(archive);
        SafeArchive.Validate(zip, paths.Cache, MrpackReader.MaxExpandedBytes, MrpackReader.MaxEntries);
        MrpackReader.PathsRule(zip.Entries.Select(e => (e.FullName.TrimEnd('/'), e.FullName.EndsWith('/'))), allowExcludedNames: true);
        if (zip.GetEntry("modrinth.index.json") is not null)
        {
            var plan = await mrpack.PreviewAsync(archive, request.ProfileName, pinMods: request.PinMods, ct: ct);
            return new(request, [], plan, null, true);
        }
        var entries = zip.Entries.Where(e => !e.FullName.EndsWith('/')).OrderBy(e => e.FullName, StringComparer.Ordinal).ToArray();
        var blockers = new List<MrpackProblem>();
        foreach (var entry in entries.Where(e => (ForeignMarkers.Contains(e.Name, StringComparer.OrdinalIgnoreCase) && e.FullName.Count(c => c == '/') <= 1) || e.Name.Equals("modrinth.index.json", StringComparison.OrdinalIgnoreCase)))
            blockers.Add(new("ZipAdapter", entry.FullName));
        var roots = entries.Select(e => e.FullName.Split('/')).Select(p => Categories.Contains(p[0], StringComparer.Ordinal) ? "" : p.Length > 2 && Categories.Contains(p[1], StringComparer.Ordinal) ? p[0] : null)
            .Where(r => r is not null).Cast<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string root = request.ContentRoot ?? (roots.Length == 1 ? roots[0] : "");
        bool validRoot = roots.Contains(root, StringComparer.Ordinal) && (request.ContentRoot is not null || roots.Length == 1);
        if (!validRoot) blockers.Add(new("ZipRoot", string.Join(", ", roots.Select(r => r.Length == 0 ? "/" : r))));
        var name = string.IsNullOrWhiteSpace(request.ProfileName) ? Path.GetFileNameWithoutExtension(archive) : request.ProfileName.Trim();
        if (name.Length > 80 || name.Length == 0 || name.Any(char.IsControl)) blockers.Add(new("ZipRequirements", "profile"));
        bool targetValid = Exact(request.Minecraft) && !request.Minecraft.Contains('+') && Enum.IsDefined(request.Loader) &&
            (request.Loader == ModLoader.Vanilla ? request.LoaderVersion.Length == 0 : Exact(request.LoaderVersion));
        if (!targetValid) blockers.Add(new("ZipRequirements", "Minecraft / loader"));
        var files = new List<MrpackFile>(); var shown = new List<MrpackFilePreview>(); var excluded = new List<string>();
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            string? path = !validRoot ? null : root.Length == 0 ? entry.FullName : entry.FullName.StartsWith(root + "/", StringComparison.Ordinal) ? entry.FullName[(root.Length + 1)..] : null;
            var parts = path?.Split('/') ?? [];
            bool selected = path is not null && Eligible(path);
            if (selected) files.Add(new(path!, entry.Length, entry.FullName, null, parts[0]));
            else excluded.Add(entry.FullName);
            shown.Add(new(path ?? entry.FullName, "required", "unsupported", selected, false, true, entry.Length, selected ? parts[0] : "excluded"));
        }
        if (files.Count == 0) blockers.Add(new("ZipContent", ""));
        if (files.Count > MrpackReader.MaxFiles || files.Count(f => MrpackReader.RootJar(f.Path)) > 128) blockers.Add(new("PackBudget", "ZIP: 2048 files / 128 root JARs"));
        MrpackReader.PathsRule(files.Select(f => (f.Path, false)));
        ModCartReview? review = null;
        if (blockers.Count == 0)
        {
            var temp = SafePath.Join(paths.Cache, "zip-previews/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp); var extracted = new List<string>();
            try
            {
                var inspector = new LocalModManager(paths, store); var mods = new List<ModCartItem>();
                foreach (var file in files.Where(f => MrpackReader.RootJar(f.Path)))
                {
                    var target = SafePath.Join(temp, Path.GetFileName(file.Path)); extracted.Add(target);
                    await CopyEntry(zip.GetEntry(file.Entry!)!, target, null, ct);
                    mods.Add(new(target, await inspector.InspectAsync(target, ct)));
                }
                if (mods.Count > 0)
                {
                    review = await inspector.ReviewResultSetAsync(new(Guid.NewGuid(), name, request.Minecraft, request.Loader, 4096, null, DateTimeOffset.UtcNow), mods, ct);
                    blockers.AddRange(review.Blockers.Select(b => new MrpackProblem("PackCompatibility", b)));
                    foreach (var mod in mods.Where(m => m.Inspection.Environment == "server")) blockers.Add(new("PackCompatibility", mod.Inspection.FileName + " [server]"));
                }
            }
            finally
            {
                // Remove only the known files in this fresh preview directory. Never recursively traverse arbitrary contents.
                foreach (var file in extracted) { SafePath.RejectLinks(file); File.Delete(file); }
                SafePath.RejectLinks(temp); if (!Directory.EnumerateFileSystemEntries(temp).Any()) Directory.Delete(temp);
            }
        }
        if (await MrpackReader.HashFile(archive, ct) != hash) throw new MrpackException("PackChanged");
        var effective = request with { ProfileName = name, ContentRoot = validRoot ? root : request.ContentRoot };
        var fingerprint = MrpackReader.Hash(JsonSerializer.SerializeToUtf8Bytes(new { hash, effective, files, excluded }, AtomicFile.JsonOptions));
        var preview = new MrpackPreview(archive, info.Length, hash, name, "local-content", "", name, request.Minecraft, request.Loader,
            request.Loader == ModLoader.Vanilla ? null : request.LoaderVersion, request.PinMods, [], files, shown, blockers, fingerprint)
            { SourceKind = "local-zip", ContentRoot = root, Excluded = excluded };
        return new(effective, roots, preview, review, false);
    }
    internal static byte[] TargetBytes(MrpackPreview p) => JsonSerializer.SerializeToUtf8Bytes(new ZipImportTarget(1, "explicit-user-selection", p.Minecraft, p.Loader, p.LoaderVersion, p.ContentRoot), AtomicFile.JsonOptions);
    internal static async Task CopyEntry(ZipArchiveEntry entry, string target, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        await using var input = entry.Open(); await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
        var buffer = new byte[81920]; long size = 0; int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        { if ((size += read) > entry.Length || size > MrpackReader.MaxArchiveBytes) throw new MrpackException("PackIntegrity", entry.FullName); await output.WriteAsync(buffer.AsMemory(0, read), ct); progress?.Report(new(entry.FullName, size, entry.Length)); }
        if (size != entry.Length) throw new MrpackException("PackIntegrity", entry.FullName); await output.FlushAsync(ct); output.Flush(true);
    }
}
