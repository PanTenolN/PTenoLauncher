using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Security;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed record ForgeLibrary(string Coordinate, string MavenPath, string? Sha1, long Size, Uri? DownloadUri, bool Embedded, bool Generated, bool Runtime);
public sealed record ForgeProcessor(string Jar, IReadOnlyList<string> Classpath, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Outputs);
public sealed record ForgeInstallPlan(ModLoader Loader, string LoaderVersion, string MinecraftVersion, string VersionId, string MainClass,
    IReadOnlyList<ForgeLibrary> Libraries, IReadOnlyList<ForgeProcessor> ClientProcessors, IReadOnlyDictionary<string, string> ClientData,
    IReadOnlyList<string> JvmArguments, IReadOnlyList<string> GameArguments, string? FmlCoordinate,
    string InstallProfileSha256, string VersionJsonSha256, int? MinimumJava = null)
{
    // This preparatory plan is deliberately not an InstallationReceipt and cannot authorize game launch.
    public bool GameInstallationComplete => false;
}

public static class ForgeInstallerMetadata
{
    private static string Text(JsonElement value, int maximum = 65536)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text || text.Length > maximum || text.Contains('\0'))
            throw new InvalidDataException("Invalid installer text field.");
        return text;
    }
    private static void UniqueProperties(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in root.EnumerateObject())
            { if (!names.Add(p.Name)) throw new InvalidDataException("Duplicate installer JSON properties."); UniqueProperties(p.Value); }
        }
        else if (root.ValueKind == JsonValueKind.Array) foreach (var item in root.EnumerateArray()) UniqueProperties(item);
    }
    internal static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, long maximum, CancellationToken ct)
    {
        if (entry.Length < 0 || entry.Length > maximum) throw new InvalidDataException("Embedded installer entry exceeds its size bound.");
        await using var input = entry.Open(); using var output = new MemoryStream(); var buffer = new byte[81920]; int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + read > entry.Length || output.Length + read > maximum) throw new InvalidDataException("Embedded entry expanded beyond its metadata.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (output.Length != entry.Length) throw new InvalidDataException("Incomplete embedded installer entry.");
        return output.ToArray();
    }
    public static async Task<ForgeInstallPlan> InspectAsync(string installer, string validationRoot, ModLoader loader, string selectedVersion, string minecraft, CancellationToken ct = default)
    {
        try { return await InspectCoreAsync(installer, validationRoot, loader, selectedVersion, minecraft, ct); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("Malformed or unsupported installer metadata.", e); }
    }
    private static async Task<ForgeInstallPlan> InspectCoreAsync(string installer, string validationRoot, ModLoader loader, string selectedVersion, string minecraft, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ForgeMaven.Segment(selectedVersion); ForgeMaven.Segment(minecraft); _ = ForgeMaven.Source(loader);
        SafePath.RejectLinks(installer);
        using var archive = ZipFile.OpenRead(installer);
        SafeArchive.Validate(archive, validationRoot, 512L * 1024 * 1024, 20000);
        var entry = archive.GetEntry("install_profile.json") ?? throw new NotSupportedException("Installer has no supported install_profile.json.");
        var installBytes = await ReadEntryAsync(entry, 4 * 1024 * 1024, ct);
        using var install = JsonDocument.Parse(installBytes); UniqueProperties(install.RootElement);
        var profile = install.RootElement;
        if (!profile.TryGetProperty("spec", out var spec) || spec.GetInt32() != 1 || profile.TryGetProperty("install", out _))
            throw new NotSupportedException("Only explicit spec-1 installer profiles are currently supported; legacy installers are not executed.");
        if (Text(profile.GetProperty("minecraft"), 128) != minecraft) throw new InvalidDataException("Installer Minecraft version differs from the selected profile.");
        var jsonPath = Text(profile.GetProperty("json"), 200); if (jsonPath.StartsWith('/')) jsonPath = jsonPath[1..];
        _ = SafePath.Join(validationRoot, jsonPath);
        var versionBytes = await ReadEntryAsync(archive.GetEntry(jsonPath) ?? throw new InvalidDataException("Version metadata entry is absent."), 4 * 1024 * 1024, ct);
        using var version = JsonDocument.Parse(versionBytes); UniqueProperties(version.RootElement); var root = version.RootElement;
        var versionId = Text(root.GetProperty("id"), 128); ForgeMaven.Segment(versionId);
        if (Text(profile.GetProperty("version"), 128) != versionId || Text(root.GetProperty("inheritsFrom"), 128) != minecraft)
            throw new InvalidDataException("Installer/version inheritance metadata mismatch.");
        var main = Text(root.GetProperty("mainClass"), 200);
        if (!Regex.IsMatch(main, @"^[A-Za-z_$][A-Za-z0-9_$.]*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) throw new InvalidDataException("Invalid loader main class.");
        var libraries = new Dictionary<string, ForgeLibrary>(StringComparer.Ordinal);
        var mavenPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void ReadLibraries(JsonElement array, bool runtime)
        {
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 2048) throw new InvalidDataException("Invalid installer library count.");
            foreach (var lib in array.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested(); if (!MinecraftRules.Allowed(lib)) continue;
                var name = Text(lib.GetProperty("name"), 400); var coordinate = ForgeCoordinate.Parse(name);
                if (mavenPaths.TryGetValue(coordinate.Path, out var otherName) && otherName != name) throw new InvalidDataException("Colliding Maven library paths.");
                mavenPaths[coordinate.Path] = name;
                var artifact = lib.GetProperty("downloads").GetProperty("artifact");
                if (Text(artifact.GetProperty("path"), 600) != coordinate.Path) throw new InvalidDataException("Library path differs from its Maven coordinate.");
                var embedded = archive.GetEntry("maven/" + coordinate.Path) is not null;
                var rawUrl = artifact.TryGetProperty("url", out var u) ? Text(u, 2048) : "";
                Uri? uri = null;
                if (rawUrl.Length > 0)
                { if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out uri)) throw new InvalidDataException("Invalid library URL."); ForgeMaven.ValidateArtifactUri(uri, coordinate, loader); }
                var generated = !embedded && uri is null;
                var sha = artifact.TryGetProperty("sha1", out var hash) ? Text(hash, 40).ToLowerInvariant() : null;
                var size = artifact.TryGetProperty("size", out var length) ? length.GetInt64() : 0;
                if (size is < 0 or > 256L * 1024 * 1024 || !generated && (sha is null || sha.Length != 40 || !sha.All(Uri.IsHexDigit)))
                    throw new NotSupportedException("Downloadable/embedded libraries require explicit size and SHA-1 metadata.");
                if (sha is { Length: > 0 } && (sha.Length != 40 || !sha.All(Uri.IsHexDigit))) throw new InvalidDataException("Invalid library checksum.");
                var item = new ForgeLibrary(name, coordinate.Path, sha, size, uri, embedded, generated, runtime);
                if (libraries.TryGetValue(name, out var prior))
                {
                    if (prior with { Runtime = runtime } != item) throw new InvalidDataException("Conflicting installer/runtime library metadata.");
                    item = item with { Runtime = prior.Runtime || runtime };
                }
                libraries[name] = item;
            }
        }
        ReadLibraries(profile.GetProperty("libraries"), false); ReadLibraries(root.GetProperty("libraries"), true);
        if (libraries.Values.Sum(l => l.Size) > 4L * 1024 * 1024 * 1024) throw new InvalidDataException("Installer libraries exceed 4 GiB.");
        var selected = ForgeMaven.Installer(loader, selectedVersion);
        if (!libraries.Keys.Select(ForgeCoordinate.Parse).Any(c => c.Group == selected.Group && c.Artifact == selected.Artifact && c.Version == selected.Version))
            throw new InvalidDataException("Installer does not contain the exact selected loader artifact.");
        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        if (profile.TryGetProperty("data", out var d))
        {
            if (d.ValueKind != JsonValueKind.Object || d.EnumerateObject().Count() > 256) throw new InvalidDataException("Invalid processor data map.");
            foreach (var property in d.EnumerateObject())
            {
                if (!Regex.IsMatch(property.Name, @"^[A-Z0-9_]{1,80}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) throw new InvalidDataException("Invalid processor variable name.");
                if (property.Name is "ROOT" or "SIDE" or "MINECRAFT_JAR" or "LIBRARY_DIR" or "MINECRAFT_VERSION" or "INSTALLER") throw new InvalidDataException("Reserved processor variable override.");
                var value = Text(property.Value.GetProperty("client"));
                if (value.StartsWith('[') && value.EndsWith(']')) _ = ForgeCoordinate.Parse(value[1..^1]);
                else if (!(value.Length >= 2 && value.StartsWith('\'') && value.EndsWith('\'')))
                { var path = value.StartsWith('/') ? value[1..] : value; _ = SafePath.Join(validationRoot, path); if (archive.GetEntry(path) is null) throw new InvalidDataException("Missing embedded client processor data."); }
                data.Add(property.Name, value);
            }
        }
        var processors = new List<ForgeProcessor>();
        if (profile.TryGetProperty("processors", out var ps))
        {
            if (ps.ValueKind != JsonValueKind.Array || ps.GetArrayLength() > 128) throw new InvalidDataException("Invalid processor count.");
            foreach (var processor in ps.EnumerateArray())
            {
                if (processor.TryGetProperty("sides", out var sides))
                {
                    if (sides.ValueKind != JsonValueKind.Array || sides.GetArrayLength() > 3) throw new InvalidDataException("Invalid processor sides.");
                    var names = sides.EnumerateArray().Select(x => Text(x, 10)).ToArray();
                    if (names.Any(x => x is not ("client" or "server" or "extract"))) throw new InvalidDataException("Unknown processor side.");
                    if (!names.Contains("client", StringComparer.Ordinal)) continue;
                }
                var jar = Text(processor.GetProperty("jar"), 400); _ = ForgeCoordinate.Parse(jar);
                var cp = processor.TryGetProperty("classpath", out var classpath) ? classpath.EnumerateArray().Select(x => Text(x, 400)).ToArray() : [];
                if (cp.Length > 256 || cp.Append(jar).Any(x => !libraries.TryGetValue(x, out var lib) || lib.Generated)) throw new InvalidDataException("Processor requires an undeclared or generated executable library.");
                var args = processor.TryGetProperty("args", out var arguments) ? arguments.EnumerateArray().Select(x => Text(x)).ToArray() : [];
                if (args.Length > 512) throw new InvalidDataException("Too many processor arguments.");
                var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
                if (processor.TryGetProperty("outputs", out var os))
                {
                    if (os.ValueKind != JsonValueKind.Object || os.EnumerateObject().Count() > 256) throw new InvalidDataException("Invalid processor output map.");
                    foreach (var output in os.EnumerateObject()) outputs.Add(output.Name, Text(output.Value, 65536));
                }
                // Raw metadata is retained for the next processor adapter; nothing in this plan is executed.
                processors.Add(new(jar, cp, args, outputs));
            }
        }
        var fml = libraries.Values.Where(l => l.Runtime).Where(l =>
        {
            var c = ForgeCoordinate.Parse(l.Coordinate);
            return c.Classifier is null && c.Extension == "jar" && (loader == ModLoader.NeoForge
                ? c.Group == "net.neoforged.fancymodloader" && c.Artifact == "loader" : c.Group == "net.minecraftforge" && c.Artifact == "fmlloader");
        }).Select(l => l.Coordinate).ToArray();
        var fmlCoordinate = fml.Length == 1 ? fml[0] : null;
        var jvm = new List<string>(); var game = new List<string>();
        if (root.TryGetProperty("arguments", out var launch))
        {
            if (launch.TryGetProperty("jvm", out var ja)) jvm.AddRange(MinecraftRules.Arguments(ja));
            if (launch.TryGetProperty("game", out var ga)) game.AddRange(MinecraftRules.Arguments(ga));
        }
        else if (root.TryGetProperty("minecraftArguments", out var legacy)) game.AddRange(LegacyArguments.Split(Text(legacy)));
        else throw new NotSupportedException("Installer version metadata contains no supported launch arguments.");
        int? minimumJava = root.TryGetProperty("javaVersion", out var java) ? java.GetProperty("majorVersion").GetInt32() : null;
        if (minimumJava is < 8 or > 100) throw new InvalidDataException("Invalid installer Java requirement.");
        return new(loader, selectedVersion, minecraft, versionId, main, libraries.Values.ToArray(), processors, data, jvm, game, fmlCoordinate,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(installBytes)), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(versionBytes)), minimumJava);
    }
}
