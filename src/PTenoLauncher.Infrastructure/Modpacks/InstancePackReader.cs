using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.Modpacks;

public sealed record InstancePackRequest(string ProfileName = "", bool PinMods = false);
public sealed record InstanceContentFile(string SourcePath, string Path, long Size, string Sha256);
public sealed record InstanceComponent(string Uid, string Version);
public sealed record InstancePackPreview(InstancePackRequest Request, MrpackPreview Plan,
    IReadOnlyList<InstanceContentFile> Content, IReadOnlyList<InstanceComponent> Components,
    IReadOnlyList<string> Warnings, ModCartReview? Review, string MetadataSha256);
internal sealed record InstanceImportTarget(int Schema, string Origin, string Minecraft, ModLoader Loader, string? LoaderVersion,
    string ContentRoot, string SourceMetadataSha256, IReadOnlyList<InstanceComponent> Components, string? SourceProfileId = null);
internal sealed record LocalInstancePayload(List<InstanceContentFile> Content, List<MrpackFilePreview> Entries, List<string> Excluded);

// Data-only reader for the common OneSix/component-format-v1 subset, not a launcher executor.
public sealed class InstancePackReader(LauncherPaths paths, ILauncherStore store)
{
    private static readonly HashSet<string> ComponentKeys = new(StringComparer.Ordinal)
    { "uid", "version", "dependencyOnly", "important", "disabled", "cachedVersion", "cachedName", "cachedRequires", "cachedConflicts", "cachedVolatile", "volatile" };
    internal static bool Exact(string s) => s.Length is > 0 and <= 100 && s.Any(char.IsAsciiDigit) &&
        s.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '+') && s is not "." and not "..";
    public async Task<InstancePackPreview> PreviewAsync(string source, InstancePackRequest? request = null, CancellationToken ct = default)
    {
        try { return await Read(source, request ?? new(), ct); }
        catch (Exception e) when (e is not MrpackException && e is InvalidDataException or JsonException or DecoderFallbackException or ArgumentException or OverflowException or InvalidOperationException)
        { throw new MrpackException("InstanceMetadata", "", e); }
    }
    private async Task<InstancePackPreview> Read(string source, InstancePackRequest request, CancellationToken ct)
    {
        source = Path.GetFullPath(source); using var input = new InstanceSource(source);
        var blockers = new List<MrpackProblem>(); var warnings = new List<string> { "InstanceExclusions", "InstanceDefaults" };
        if (input.IsDirectory && (paths.Root.Equals(source, StringComparison.OrdinalIgnoreCase) || paths.Root.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new MrpackException("InstanceExternal");
        var roots = input.Entries.Where(e => !e.Directory && (e.Path == "instance.cfg" || e.Path.EndsWith("/instance.cfg", StringComparison.Ordinal) && e.Path.Count(c => c == '/') == 1))
            .Select(e => e.Path[..^"instance.cfg".Length]).ToArray();
        if (roots.Length != 1 || input.IsDirectory && roots[0] != "") throw new MrpackException("InstanceLayout");
        string root = roots[0];
        if (!input.Exists(root + "mmc-pack.json")) throw new MrpackException("InstanceMetadata");
        var cfgBytes = await input.Bytes(root + "instance.cfg", 256 * 1024, ct);
        var packBytes = await input.Bytes(root + "mmc-pack.json", 256 * 1024, ct);
        var cfg = Config(cfgBytes);
        if (!cfg.TryGetValue("InstanceType", out var type) || type != "OneSix") blockers.Add(new("InstanceMetadata", "InstanceType"));
        foreach (var key in cfg.Keys)
        {
            bool command = key.Contains("Command", StringComparison.OrdinalIgnoreCase) || key.Contains("Hook", StringComparison.OrdinalIgnoreCase) || key is "JvmArgs" or "OverrideJavaArgs" or "OverrideEnv" or "Env" or "OverrideLaunchCmd";
            if (command && cfg[key] is not ("" or "false" or "False" or "0")) blockers.Add(new("InstanceCustom", key));
            if ((key.Contains("GameDir", StringComparison.OrdinalIgnoreCase) || key.Contains("MinecraftPath", StringComparison.OrdinalIgnoreCase) || key.Contains("MinecraftDir", StringComparison.OrdinalIgnoreCase)) && cfg[key].Length > 0)
                blockers.Add(new("InstanceExternal", key));
        }
        if (input.Entries.Any(e => e.Path == root + "patches" || e.Path.StartsWith(root + "patches/", StringComparison.Ordinal) || e.Path == root + "jarmods" || e.Path.StartsWith(root + "jarmods/", StringComparison.Ordinal)))
            blockers.Add(new("InstanceCustom", "patches / jarmods"));
        using var doc = MrpackReader.Json(packBytes); var json = doc.RootElement;
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("formatVersion", out var format) || !format.TryGetInt32(out var number) || number != 1 ||
            !json.TryGetProperty("components", out var comps) || comps.ValueKind != JsonValueKind.Array || comps.GetArrayLength() is < 1 or > 32) throw new MrpackException("InstanceMetadata");
        if (json.EnumerateObject().Any(p => p.Name is not ("formatVersion" or "components"))) blockers.Add(new("InstanceCustom", "mmc-pack.json"));
        var components = new List<InstanceComponent>(); var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string minecraft = ""; ModLoader loader = ModLoader.Vanilla; string? loaderVersion = null; int loaders = 0;
        foreach (var c in comps.EnumerateArray())
        {
            if (c.ValueKind != JsonValueKind.Object || !c.TryGetProperty("uid", out var id) || id.ValueKind != JsonValueKind.String || id.GetString() is not { } uid || uid.Length is < 1 or > 100 || !uid.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_') || !identities.Add(uid)) throw new MrpackException("InstanceMetadata", "component identity");
            if (c.EnumerateObject().Any(p => !ComponentKeys.Contains(p.Name))) blockers.Add(new("InstanceCustom", uid));
            foreach (var field in new[] { "disabled", "dependencyOnly", "important", "cachedVolatile", "volatile" })
                if (c.TryGetProperty(field, out var flag) && flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new MrpackException("InstanceMetadata", field);
            if (c.TryGetProperty("disabled", out var disabled) && disabled.GetBoolean()) { warnings.Add("InstanceDisabled: " + uid); continue; }
            string version = c.TryGetProperty("version", out var ver) && ver.ValueKind == JsonValueKind.String ? ver.GetString()! : "";
            // cachedVersion is presentation/cache data, never an exact authoritative request.
            if (!Exact(version)) { blockers.Add(new("InstanceComponent", uid)); continue; }
            components.Add(new(uid, version));
            var mapped = uid switch { "net.fabricmc.fabric-loader" => ModLoader.Fabric, "org.quiltmc.quilt-loader" => ModLoader.Quilt, "net.minecraftforge" => ModLoader.Forge, "net.neoforged" => ModLoader.NeoForge, _ => (ModLoader?)null };
            if (uid == "net.minecraft") minecraft = version;
            else if (mapped is { } supported) { loader = supported; loaderVersion = version; loaders++; }
            else blockers.Add(new("InstanceComponent", uid)); // Includes explicit ancillary LWJGL/intermediary overrides.
        }
        if (minecraft.Length == 0 || loaders > 1) blockers.Add(new("InstanceComponent", "Minecraft / loader"));
        var gameRoots = new[] { root + "minecraft", root + ".minecraft" }.Where(r => input.Entries.Any(e => e.Path == r && e.Directory || e.Path.StartsWith(r + "/", StringComparison.Ordinal))).ToArray();
        if (gameRoots.Length != 1) blockers.Add(new("InstanceLayout", "minecraft / .minecraft"));
        string game = gameRoots.Length == 1 ? gameRoots[0] : "";
        string name = string.IsNullOrWhiteSpace(request.ProfileName) ? cfg.GetValueOrDefault("name", "Imported instance") : request.ProfileName.Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl)) blockers.Add(new("ZipRequirements", "profile"));
        var content = new List<InstanceContentFile>(); var entries = new List<MrpackFilePreview>(); var excluded = new List<string>();
        foreach (var e in input.Entries.Where(e => !e.Directory))
        {
            ct.ThrowIfCancellationRequested();
            var relative = game.Length > 0 && e.Path.StartsWith(game + "/", StringComparison.Ordinal) ? e.Path[(game.Length + 1)..] : null;
            bool selected = relative is not null && ZipPackReader.Eligible(relative);
            if (selected) content.Add(new(e.Path, relative!, e.Size, await input.Hash(e.Path, ct)));
            else excluded.Add(e.Path);
            entries.Add(new(relative ?? e.Path, "required", "unsupported", selected, false, true, e.Size, selected ? relative!.Split('/')[0] : "excluded"));
        }
        foreach (var e in input.Entries.Where(e => e.Directory))
        {
            var relative = game.Length > 0 && e.Path.StartsWith(game + "/", StringComparison.Ordinal) ? e.Path[(game.Length + 1)..] : "";
            if (relative.Length > 0 && !new[] { "mods", "config", "resourcepacks", "shaderpacks" }.Contains(relative.Split('/')[0], StringComparer.Ordinal))
                entries.Add(new(e.Path, "unsupported", "unsupported", false, false, true, 0, "excluded-directory"));
        }
        if (content.Count == 0) blockers.Add(new("ZipContent", ""));
        if (content.Count > MrpackReader.MaxFiles || content.Count(f => MrpackReader.RootJar(f.Path)) > 128) throw new MrpackException("PackBudget");
        MrpackReader.PathsRule(content.Select(f => (f.Path, false)));
        var metadataHash = MrpackReader.Hash(JsonSerializer.SerializeToUtf8Bytes(new { cfg = MrpackReader.Hash(cfgBytes), pack = MrpackReader.Hash(packBytes) }));
        var layout = input.Entries.Select(e => new { e.Path, e.Directory, e.Size }).ToArray();
        var sourceHash = input.IsDirectory ? MrpackReader.Hash(JsonSerializer.SerializeToUtf8Bytes(new { layout, metadataHash, content })) : await MrpackReader.HashFile(source, ct);
        ModCartReview? review = blockers.Count == 0 ? await Review(input, content, name, minecraft, loader, ct) : null;
        if (review is not null) blockers.AddRange(review.Blockers.Select(b => new MrpackProblem("PackCompatibility", b)));
        // Reopen later at apply boundaries; the read-only handle is not an immutable filesystem snapshot.
        var files = content.Select(f => new MrpackFile(f.Path, f.Size, f.SourcePath, null, f.Path.Split('/')[0])).ToArray();
        var effective = request with { ProfileName = name };
        var fingerprint = MrpackReader.Hash(JsonSerializer.SerializeToUtf8Bytes(new { source, sourceHash, effective, metadataHash, components, content, game, excluded }, AtomicFile.JsonOptions));
        var plan = new MrpackPreview(source, input.IsDirectory ? content.Sum(f => f.Size) : new FileInfo(source).Length, sourceHash, name, "component-format-1", "", name, minecraft, loader, loaderVersion,
            request.PinMods, [], files, entries, blockers, fingerprint) { SourceKind = "local-prism-multimc", ContentRoot = game, Excluded = excluded };
        return new(effective, plan, content, components, warnings, review, metadataHash);
    }
    internal async Task<ModCartReview?> ReviewSourceAsync(string directory, List<InstanceContentFile> content, string name, string minecraft, ModLoader loader, CancellationToken ct)
    { using var source = new InstanceSource(directory, clientContent: true); return await Review(source, content, name, minecraft, loader, ct); }

    internal static async Task<LocalInstancePayload> ReadLocalPayloadAsync(LauncherPaths paths, string directory, CancellationToken ct)
    {
        static bool Within(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (directory.StartsWith("\\\\", StringComparison.Ordinal) || !Directory.Exists(directory) || Within(paths.Root, directory) ||
            new[] { paths.Cache, Path.Combine(paths.Root, "instances"), Path.Combine(paths.Root, "modpack-imports") }.Any(p => Within(directory, p)))
            throw new MrpackException("InstanceExternal");
        using var source = new InstanceSource(directory, clientContent: true);
        var payload = new LocalInstancePayload([], [], []);
        foreach (var entry in source.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.Directory && entry.Path.Split('/')[0] is "mods" or "config" or "resourcepacks" or "shaderpacks") continue;
            var include = !entry.Directory && ZipPackReader.Eligible(entry.Path);
            if (include) payload.Content.Add(new(entry.Path, entry.Path, entry.Size, await source.Hash(entry.Path, ct)));
            else payload.Excluded.Add(entry.Path);
            payload.Entries.Add(new(entry.Path, "required", "unsupported", include, false, true, entry.Size,
                include ? entry.Path.Split('/')[0] : "excluded"));
        }
        if (payload.Content.Count > MrpackReader.MaxFiles || payload.Content.Count(f => MrpackReader.RootJar(f.Path)) > 128)
            throw new MrpackException("PackBudget");
        return payload;
    }
    private async Task<ModCartReview?> Review(InstanceSource input, List<InstanceContentFile> content, string name, string minecraft, ModLoader loader, CancellationToken ct)
    {
        var temp = SafePath.Join(paths.Cache, "instance-previews/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp); var created = new List<string>();
        try
        {
            var inspector = new LocalModManager(paths, store); var jars = new List<ModCartItem>();
            foreach (var file in content.Where(f => MrpackReader.RootJar(f.Path)))
            {
                var target = SafePath.Join(temp, Path.GetFileName(file.Path)); created.Add(target);
                await Copy(input, file, target, null, ct); var inspection = await inspector.InspectAsync(target, ct);
                if (inspection.Environment == "server") throw new MrpackException("PackCompatibility", inspection.FileName + " [server]");
                jars.Add(new(target, inspection));
            }
            return jars.Count == 0 ? null : await inspector.ReviewResultSetAsync(new(Guid.NewGuid(), name, minecraft, loader, 4096, null, DateTimeOffset.UtcNow), jars, ct);
        }
        finally
        {
            foreach (var file in created) { SafePath.RejectLinks(file); File.Delete(file); }
            SafePath.RejectLinks(temp); if (!Directory.EnumerateFileSystemEntries(temp).Any()) Directory.Delete(temp);
        }
    }
    internal static async Task Copy(InstanceSource input, InstanceContentFile file, string target, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        await using var source = input.Open(file.SourcePath); await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[81920]; long size = 0; int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        { if ((size += read) > file.Size) throw new MrpackException("PackChanged"); hash.AppendData(buffer, 0, read); await output.WriteAsync(buffer.AsMemory(0, read), ct); progress?.Report(new(file.Path, size, file.Size)); }
        if (size != file.Size || Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != file.Sha256) throw new MrpackException("PackChanged");
        await output.FlushAsync(ct); output.Flush(true);
    }
    internal static byte[] TargetBytes(InstancePackPreview p) => JsonSerializer.SerializeToUtf8Bytes(new InstanceImportTarget(1, "prism-multimc-components-v1", p.Plan.Minecraft, p.Plan.Loader, p.Plan.LoaderVersion, p.Plan.ContentRoot, p.MetadataSha256, p.Components), AtomicFile.JsonOptions);
    private static Dictionary<string, string> Config(byte[] bytes)
    {
        var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim(); if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
            if (line == "[General]") continue; if (line.StartsWith('[')) throw new MrpackException("InstanceMetadata", "INI section");
            int equals = line.IndexOf('='); if (equals <= 0) throw new MrpackException("InstanceMetadata", "INI line");
            var key = line[..equals].Trim(); var value = line[(equals + 1)..].Trim(); var decoded = new StringBuilder();
            // The source launcher strips comments before unescaping, using the immediately preceding character.
            for (int i = 0; i < value.Length; i++) if (value[i] == '#' && (i == 0 || value[i - 1] != '\\')) { value = value[..i]; break; }
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\') { if (++i == value.Length) throw new MrpackException("InstanceMetadata", "INI escape"); c = value[i] switch { 'n' => '\n', 't' => '\t', var other => other }; }
                decoded.Append(c);
            }
            value = decoded.ToString().Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"' && value.IndexOfAny([';', '=', ',']) >= 0) value = value[1..^1];
            if (key.Length > 100 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-') || !result.TryAdd(key, value)) throw new MrpackException("InstanceMetadata", "INI duplicate/key");
            if (result.Count > 512 || value.Length > 8192) throw new MrpackException("PackBudget");
        }
        return result;
    }
}
