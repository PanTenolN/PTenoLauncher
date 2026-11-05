using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Security;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.Modpacks;

public sealed class MrpackReader(LauncherPaths paths, NetworkPolicy network)
{
    public const int MaxFiles = 2048, MaxEntries = 8192, MaxIndexBytes = 4 * 1024 * 1024;
    public const long MaxArchiveBytes = 512L * 1024 * 1024, MaxExpandedBytes = 1024L * 1024 * 1024;
    internal static MrpackException Error(string code = "PackInvalid", string detail = "") => new(code, detail);
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static async Task<string> HashFile(string file, CancellationToken ct)
    { SafePath.RejectLinks(file); await using var stream = File.OpenRead(file); return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant(); }
    public string CachePath(MrpackDownload file) => SafePath.Join(paths.Cache, "modpacks/files/" + file.Sha512 + ".bin");
    public async Task<bool> CachedAsync(MrpackDownload f, CancellationToken ct = default) =>
        await VerifiedDownloader.VerifyAsync(CachePath(f), f.Sha512, "SHA512", f.Size, ct) && await VerifiedDownloader.VerifyAsync(CachePath(f), f.Sha1, "SHA1", f.Size, ct);
    internal static void PathRule(string path, bool allowExcludedNames = false)
    {
        if (path.Length > 240 || path.Split('/').Length > 24 || path != path.Normalize(NormalizationForm.FormC)) throw Error("PackPath", path);
        _ = SafePath.Join(Path.GetTempPath(), path);
        if (allowExcludedNames) return;
        var forbidden = new[] { "launcher_accounts.json", "launcher_profiles.json", "accounts.json", "settings.db", "session.lock", "portable.flag", "launcher-installation.json" };
        if (path.Split('/').Any(s => s.StartsWith(".pteno", StringComparison.OrdinalIgnoreCase) || s is ".git" or ".codex" ||
            forbidden.Contains(s, StringComparer.OrdinalIgnoreCase))) throw Error("PackPath", path);
    }
    // Directory aliases, case aliases, and file/directory conflicts are rejected on every target platform.
    internal static void PathsRule(IEnumerable<(string Path, bool Directory)> pathsToCheck, bool allowExcludedNames = false)
    {
        var nodes = new Dictionary<string, (string Original, bool File)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in pathsToCheck)
        {
            PathRule(item.Path, allowExcludedNames); var parts = item.Path.Split('/');
            for (int i = 1; i <= parts.Length; i++)
            {
                var key = string.Join('/', parts.Take(i)); bool file = i == parts.Length && !item.Directory;
                if (nodes.TryGetValue(key, out var prior))
                { if (prior.Original != key || prior.File != file && (prior.File || file)) throw Error("PackPath", key); }
                else nodes.Add(key, (key, file));
            }
        }
    }
    internal static async Task<byte[]> EntryBytes(ZipArchiveEntry entry, int maximum, CancellationToken ct)
    {
        if (entry.Length > maximum) throw Error("PackBudget", entry.FullName);
        await using var input = entry.Open(); using var bytes = new MemoryStream(); var buffer = new byte[81920]; int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        { if (bytes.Length + read > maximum || bytes.Length + read > entry.Length) throw Error("PackBudget", entry.FullName); bytes.Write(buffer, 0, read); }
        if (bytes.Length != entry.Length) throw Error("PackIntegrity", entry.FullName); return bytes.ToArray();
    }
    internal static JsonDocument Json(byte[] bytes)
    {
        _ = new UTF8Encoding(false, true).GetString(bytes);
        var doc = JsonDocument.Parse(bytes.AsMemory(bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }) ? 3 : 0), new() { MaxDepth = 32 });
        int nodes = 0;
        void Visit(JsonElement e)
        {
            if (++nodes > 100000) throw Error("PackBudget");
            if (e.ValueKind == JsonValueKind.Object)
            { var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach (var p in e.EnumerateObject()) { if (!names.Add(p.Name)) throw Error("PackInvalid", p.Name); Visit(p.Value); } }
            else if (e.ValueKind == JsonValueKind.Array) foreach (var v in e.EnumerateArray()) Visit(v);
        }
        try { Visit(doc.RootElement); return doc; } catch { doc.Dispose(); throw; }
    }
    private static string Text(JsonElement e, string key, int maximum = 200)
    {
        if (!e.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String || value.GetString() is not { } s ||
            string.IsNullOrWhiteSpace(s) || s.Length > maximum || s.Any(char.IsControl)) throw Error("PackInvalid", key);
        return s;
    }
    private static string Side(JsonElement e, string key)
    { var value = Text(e, key); if (value is not ("required" or "optional" or "unsupported")) throw Error("PackUnsupported", key); return value; }
    private static string Digest(JsonElement e, string key, int length)
    { var s = Text(e, key); if (s.Length != length || !s.All(Uri.IsHexDigit)) throw Error("PackInvalid", key); return s.ToLowerInvariant(); }
    public async Task<MrpackPreview> PreviewAsync(string archive, string? profileName = null, IReadOnlyList<string>? optional = null, bool pinMods = false, CancellationToken ct = default)
    {
        try { return await ReadAsync(archive, profileName, optional ?? [], pinMods, ct); }
        catch (Exception e) when (e is not MrpackException && e is JsonException or DecoderFallbackException or InvalidDataException or ArgumentException or OverflowException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new MrpackException("PackInvalid", "", e); }
    }
    private async Task<MrpackPreview> ReadAsync(string archive, string? profileName, IReadOnlyList<string> optional, bool pinMods, CancellationToken ct)
    {
        archive = Path.GetFullPath(archive); SafePath.RejectLinks(archive); var info = new FileInfo(archive);
        if (info.Length > MaxArchiveBytes) throw Error("PackBudget");
        var fingerprint = await HashFile(archive, ct);
        CheckContainer(archive);
        using var zip = ZipFile.OpenRead(archive);
        SafeArchive.Validate(zip, paths.Cache, MaxExpandedBytes, MaxEntries);
        PathsRule(zip.Entries.Where(e => e.FullName.TrimEnd('/').Length > 0).Select(e => (e.FullName.TrimEnd('/'), e.FullName.EndsWith('/'))));
        var index = zip.GetEntry("modrinth.index.json") ?? throw Error();
        using var json = Json(await EntryBytes(index, MaxIndexBytes, ct)); var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("formatVersion", out var format) || !format.TryGetInt32(out var number) || number != 1 || Text(root, "game") != "minecraft") throw Error("PackUnsupported");
        var name = Text(root, "name"); var version = Text(root, "versionId");
        var summary = root.TryGetProperty("summary", out var description) ? description.GetString() ?? throw Error() : "";
        if (summary.Length > 4096) throw Error("PackBudget");
        profileName = string.IsNullOrWhiteSpace(profileName) ? name[..Math.Min(80, name.Length)] : profileName.Trim();
        var deps = root.GetProperty("dependencies"); var minecraft = Text(deps, "minecraft", 100);
        if (deps.EnumerateObject().Count() > 16) throw Error("PackBudget");
        var problems = new List<MrpackProblem>(); ModLoader loader = ModLoader.Vanilla; string? loaderVersion = null; int loaders = 0;
        foreach (var dependency in deps.EnumerateObject())
        {
            if (dependency.Name == "minecraft") continue;
            var choice = dependency.Name switch { "fabric-loader" => ModLoader.Fabric, "quilt-loader" => ModLoader.Quilt, "forge" => ModLoader.Forge, "neoforge" => ModLoader.NeoForge, _ => (ModLoader?)null };
            if (choice is null) { problems.Add(new("PackUnsupported", dependency.Name)); continue; }
            loader = choice.Value; loaderVersion = Text(deps, dependency.Name, 100); loaders++;
            if (loaderVersion.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-' and not '+')) problems.Add(new("PackUnsupported", dependency.Name + "=" + loaderVersion));
        }
        if (loaders > 1) problems.Add(new("PackUnsupported", string.Join(", ", deps.EnumerateObject().Select(p => p.Name))));
        ProfileRules.Validate(new(Guid.NewGuid(), profileName, minecraft, loader, 4096, null, DateTimeOffset.UtcNow));
        var optionalSet = optional.ToHashSet(StringComparer.Ordinal); if (optionalSet.Count != optional.Count || optional.Count > MaxFiles) throw Error();
        var downloads = new List<MrpackDownload>(); var selected = new Dictionary<string, MrpackFile>(StringComparer.Ordinal); var entries = new List<MrpackFilePreview>();
        var fileArray = root.GetProperty("files"); if (fileArray.ValueKind != JsonValueKind.Array || fileArray.GetArrayLength() > MaxFiles) throw Error("PackBudget");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in fileArray.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested(); var path = Text(f, "path", 240); PathRule(path); if (!seen.Add(path)) throw Error("PackPath", path);
            var size = f.GetProperty("fileSize").GetInt64(); if (size is < 0 or > MaxArchiveBytes) throw Error("PackBudget", path);
            var hashes = f.GetProperty("hashes"); var sha1 = Digest(hashes, "sha1", 40); var sha512 = Digest(hashes, "sha512", 128);
            string client = "required", server = "required";
            if (f.TryGetProperty("env", out var env)) { client = Side(env, "client"); server = Side(env, "server"); }
            var urls = f.GetProperty("downloads"); if (urls.ValueKind != JsonValueKind.Array || urls.GetArrayLength() is < 1 or > 8) throw Error();
            var allowed = new List<Uri>(); var rejected = new List<string>(); var seenUrls = new HashSet<string>(StringComparer.Ordinal);
            foreach (var u in urls.EnumerateArray())
            {
                if (u.ValueKind != JsonValueKind.String || u.GetString() is not { } raw || raw.Length > 2048 || raw.Any(char.IsWhiteSpace) ||
                    !Uri.TryCreate(raw, UriKind.Absolute, out var uri) || !uri.IsWellFormedOriginalString()) throw Error("PackOrigin", path);
                if (!seenUrls.Add(uri.AbsoluteUri)) throw Error("PackInvalid", path);
                try { TrustedSources.ValidateMrpack(uri, initial: true); allowed.Add(uri); } catch (UntrustedMrpackSourceException ex) { rejected.Add(ex.Host); }
            }
            bool chosen = client == "required" || client == "optional" && optionalSet.Contains(path);
            if (optionalSet.Contains(path) && client != "optional") throw Error("PackChanged", path);
            var d = new MrpackDownload(path, size, sha1, sha512, allowed, client, server, rejected); downloads.Add(d);
            bool cached = await CachedAsync(d, ct);
            entries.Add(new(path, client, server, chosen, client == "optional", cached, size, "download"));
            if (chosen) selected.Add(path, new(path, size, null, d, "download"));
        }
        if (optionalSet.Except(downloads.Where(d => d.Client == "optional").Select(d => d.Path)).Any()) throw Error("PackChanged");
        if (downloads.GroupBy(d => d.Sha512, StringComparer.Ordinal).Any(g => g.Select(d => (d.Size, d.Sha1)).Distinct().Count() != 1)) throw Error("PackInvalid");
        // Apply only the client layers; inspect all archive paths, including the excluded server layer.
        foreach (var layer in new[] { "overrides", "client-overrides", "server-overrides" })
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(layer + "/", StringComparison.Ordinal) && !e.FullName.EndsWith('/')).OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                var path = entry.FullName[(layer.Length + 1)..]; PathRule(path);
                if (layer == "server-overrides") { entries.Add(new(path, "unsupported", "required", false, false, true, entry.Length, layer)); continue; }
                selected.TryGetValue(path, out var previous);
                selected[path] = new(path, entry.Length, entry.FullName, null, layer, previous?.Source);
                entries.Add(new(path, "required", "required", true, false, true, entry.Length, layer));
            }
        foreach (var entry in zip.Entries)
            if (entry.FullName != "modrinth.index.json" && !new[] { "overrides", "client-overrides", "server-overrides" }.Any(l => entry.FullName == l + "/" || entry.FullName.StartsWith(l + "/", StringComparison.Ordinal))) throw Error("PackUnsupported", entry.FullName);
        PathsRule(downloads.Select(d => (d.Path, false)).Concat(selected.Keys.Select(p => (p, false))));
        var files = selected.Values.OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
        foreach (var f in files.Where(f => f.Entry is not null && downloads.Any(d => d.Path == f.Path && d.Client == "unsupported")))
            problems.Add(new("PackUnsupported", f.Path + " [client=unsupported / override]"));
        if (files.Length > MaxFiles || files.Sum(f => f.Size) > MaxExpandedBytes) throw Error("PackBudget");
        var manualFiles = await PackExportIndex.ValidateAsync(root, zip, downloads, ct);
        foreach (var f in files.Where(f => f.Download is not null))
        {
            if (f.Download!.Downloads.Count == 0) problems.Add(new("PackOrigin", f.Path + " [" + string.Join(", ", f.Download.RejectedOrigins ?? []) + "]"));
            if (network.Offline && !await CachedAsync(f.Download, ct)) problems.Add(new("PackOfflineMissing", f.Path));
        }
        if (pinMods && files.Count(f => RootJar(f.Path)) > 128) problems.Add(new("PackBudget", "pins: 128"));
        if (await HashFile(archive, ct) != fingerprint) throw Error("PackChanged");
        var optionalSorted = optionalSet.Order(StringComparer.Ordinal).ToArray();
        var planHash = Hash(JsonSerializer.SerializeToUtf8Bytes(new { fingerprint, profileName, optionalSorted, pinMods, files }, AtomicFile.JsonOptions));
        return new(archive, info.Length, fingerprint, name, version, summary, profileName, minecraft, loader, loaderVersion, pinMods, optionalSorted, files, entries, problems, planHash)
            { ManualFiles = manualFiles };
    }
    internal static bool RootJar(string p) => p.StartsWith("mods/", StringComparison.Ordinal) && p.Count(c => c == '/') == 1 && p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
    internal static void CheckContainer(string archive, int maxEntries = MaxEntries)
    {
        // Bound the central directory before ZipArchive allocates entry objects. ZIP64/multi-disk are explicit unsupported formats.
        using var file = File.OpenRead(archive); int size = (int)Math.Min(file.Length, 65557); var tail = new byte[size]; file.Position = file.Length - size; file.ReadExactly(tail);
        for (int i = size - 22; i >= 0; i--)
        {
            if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) != 0x06054b50) continue;
            var e = tail.AsSpan(i); int comment = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(e[20..]); if (i + 22 + comment != size) continue;
            int count = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(e[10..]);
            if (count == 65535 || System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(e[4..]) != 0 || System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(e[6..]) != 0 || System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(e[8..]) != count) throw Error("PackUnsupported", "ZIP64/multi-disk");
            long length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(e[12..]); long start = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(e[16..]);
            if (count > maxEntries || length > 16 * 1024 * 1024 || start + length > file.Length - size + i) throw Error("PackBudget");
            file.Position = start; var header = new byte[46]; long end = start + length;
            for (int entry = 0; entry < count; entry++)
            {
                if (file.Position + 46 > end) throw Error(); file.ReadExactly(header);
                if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x02014b50) throw Error();
                int name = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
                int extra = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(30));
                int note = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(32));
                if (name > 1024 || extra > 4096 || note > 4096 || file.Position + name + extra + note > end) throw Error("PackBudget");
                if ((System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8)) & 1) != 0 || System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(34)) != 0) throw Error("PackUnsupported", "encrypted/multi-disk ZIP");
                file.Position += name + extra + note;
            }
            if (file.Position != end) throw Error();
            return;
        }
        throw Error();
    }
}
