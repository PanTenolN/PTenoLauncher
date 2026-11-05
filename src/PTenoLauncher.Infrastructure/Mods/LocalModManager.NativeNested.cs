using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Security;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Mods;

public sealed partial class LocalModManager
{
    private sealed class NativeBudget { public int Count; public long CompressedBytes; public long ExpandedBytes; }
    private async Task<NativeNestedMetadata> ReadNativeNestedAsync(ZipArchive zip, ModLoader loader, NativeBudget budget, int depth, CancellationToken ct)
    {
        var candidates = new List<NativeNestedCandidate>(); var issues = new List<ReadinessIssue>();
        void Unknown(string reason) => issues.Add(new("mods.nested.metadata.unknown", reason));
        var entries = new List<(string Path, string? Group, string? Artifact, string? Version, string? Range, bool Obfuscated)>();
        bool library = false; string? libraryIdentity = null;
        if (loader == ModLoader.Quilt && zip.GetEntry("quilt.mod.json") is { } quilt)
        {
            using var doc = JsonDocument.Parse(await ReadBoundedEntryAsync(quilt, 1024 * 1024, ct), new() { MaxDepth = 32 });
            var ql = doc.RootElement.GetProperty("quilt_loader");
            if (ql.TryGetProperty("jars", out var jars))
            {
                if (jars.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Quilt jars must be an array.");
                if (jars.GetArrayLength() > 64) { Unknown("Quilt nested candidate count exceeds 64 per archive."); return new([], issues); }
                foreach (var item in jars.EnumerateArray()) entries.Add((item.GetString() ?? throw new InvalidDataException("Missing nested path."), null, null, null, null, false));
            }
        }
        if (loader is ModLoader.Forge or ModLoader.NeoForge)
        {
            library = zip.GetEntry("META-INF/mods.toml") is null && zip.GetEntry("META-INF/neoforge.mods.toml") is null;
            if (library)
            {
                // A library has no Mod ID. FML's explicit manifest type is required for this bounded reader.
                var manifest = zip.GetEntry("META-INF/MANIFEST.MF");
                var text = manifest is null ? "" : System.Text.Encoding.UTF8.GetString(await ReadBoundedEntryAsync(manifest, 65536, ct));
                var fields = text.Replace("\r\n", "\n").Replace("\n ", "", StringComparison.Ordinal).Split("\n\n", 2)[0].Split('\n')
                    .Where(l => l.StartsWith("FMLModType:", StringComparison.OrdinalIgnoreCase)).Select(l => l[11..].Trim()).ToArray();
                if (fields.Length != 1 || fields[0] is not ("LIBRARY" or "GAMELIBRARY")) Unknown("Nested library has no supported explicit FMLModType; Mod ID is not inferred from Maven coordinates.");
                var modules = text.Replace("\r\n", "\n").Replace("\n ", "", StringComparison.Ordinal).Split("\n\n", 2)[0].Split('\n')
                    .Where(l => l.StartsWith("Automatic-Module-Name:", StringComparison.OrdinalIgnoreCase)).Select(l => l[22..].Trim()).ToArray();
                if (modules.Length != 1 || !System.Text.RegularExpressions.Regex.IsMatch(modules[0], @"^[A-Za-z_$][A-Za-z0-9_$]*(?:\.[A-Za-z_$][A-Za-z0-9_$]*)*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) ||
                    zip.Entries.Any(e => e.FullName == "module-info.class" || e.FullName.StartsWith("META-INF/versions/", StringComparison.Ordinal) && e.FullName.EndsWith("/module-info.class", StringComparison.Ordinal)))
                    Unknown("Library module identity requires one Automatic-Module-Name and no explicit module-info.class in this supported subset.");
                else libraryIdentity = (loader == ModLoader.NeoForge ? "library:" : "") + modules[0];
            }
            if (zip.GetEntry("META-INF/jarjar/metadata.json") is { } metadata)
            {
                try
                {
                    using var doc = JsonDocument.Parse(await ReadBoundedEntryAsync(metadata, 1024 * 1024, ct), new() { MaxDepth = 24 });
                    ValidateQuiltKeys(doc.RootElement);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(p => p.Name != "jars")) Unknown("Unsupported Jar-in-Jar metadata fields.");
                    var jars = root.GetProperty("jars");
                    if (jars.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Jar-in-Jar jars must be an array.");
                    if (jars.GetArrayLength() > 64) { Unknown("Jar-in-Jar candidate count exceeds 64 per archive."); return new([], issues, library, libraryIdentity); }
                    foreach (var item in jars.EnumerateArray())
                    {
                        var identifier = item.GetProperty("identifier"); var v = item.GetProperty("version");
                        var group = RequiredFabricText(identifier, "group"); var artifact = RequiredFabricText(identifier, "artifact");
                        var version = RequiredFabricText(v, "artifactVersion"); var range = RequiredFabricText(v, "range");
                        _ = ForgeCoordinate.Parse(group + ":" + artifact + ":" + version);
                        if (item.EnumerateObject().Any(p => p.Name is not ("identifier" or "version" or "path" or "isObfuscated")) ||
                            identifier.EnumerateObject().Any(p => p.Name is not ("group" or "artifact")) || v.EnumerateObject().Any(p => p.Name is not ("range" or "artifactVersion")))
                            Unknown("Unsupported Jar-in-Jar descriptor extension.");
                        entries.Add((RequiredFabricText(item, "path"), group, artifact, version, range,
                            item.TryGetProperty("isObfuscated", out var obfuscated) && obfuscated.GetBoolean()));
                    }
                }
                catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
                { Unknown("Malformed Jar-in-Jar metadata: " + e.GetType().Name); return new([], issues, library, libraryIdentity); }
            }
        }
        if (entries.Count == 0) return new([], issues, library, libraryIdentity);
        if (depth >= 4) { Unknown("Nested archive depth exceeds 4."); return new([], issues, library, libraryIdentity); }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries.OrderBy(e => e.Path, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested(); _ = SafePath.Join(paths.Cache, e.Path);
            if (!e.Path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || !names.Add(e.Path)) throw new InvalidDataException("Invalid or repeated nested archive path.");
            var entry = zip.GetEntry(e.Path) ?? throw new InvalidDataException("Declared nested JAR is absent: " + e.Path);
            if (++budget.Count > 64 || entry.Length > 32L * 1024 * 1024 || (budget.CompressedBytes += entry.Length) > 128L * 1024 * 1024)
            { Unknown("Nested archive budget exceeded: 64 candidates / 32 MiB per JAR / 128 MiB cumulative JAR bytes."); break; }
            var bytes = await ReadBoundedEntryAsync(entry, 32 * 1024 * 1024, ct);
            using var stream = new MemoryStream(bytes, false); using var child = new ZipArchive(stream, ZipArchiveMode.Read);
            SafeArchive.Validate(child, paths.Cache, 256L * 1024 * 1024, 20000);
            if ((budget.ExpandedBytes += child.Entries.Sum(x => x.Length)) > 512L * 1024 * 1024)
            { Unknown("Cumulative nested expanded-size budget exceeds 512 MiB."); break; }
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            ModInspection inspection;
            try { inspection = await InspectArchiveAsync(child, e.Path, hash, bytes.Length, budget, depth + 1, loader, ct); }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or InvalidDataException)
            {
                var reason = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message;
                Unknown(e.Path + ": nested metadata/structure rejected: " + reason); continue;
            }
            candidates.Add(new(e.Path, hash, bytes.Length, inspection, e.Group, e.Artifact, e.Version, e.Range, e.Obfuscated));
        }
        return new(candidates, issues, library, libraryIdentity);
    }
}
