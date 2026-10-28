using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using PTenoLauncher.Core;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
namespace PTenoLauncher.Infrastructure.Mods;

public sealed partial class LocalModManager
{
    private static string ForgeText(TomlTable table, string key, string? fallback = null)
    {
        if (!table.TryGetValue(key, out var raw)) return fallback ?? throw new InvalidDataException("Відсутнє поле TOML: " + key);
        if (raw is not string value || value.Length > 4096 || value.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            throw new InvalidDataException("Некоректне текстове поле TOML: " + key);
        return value;
    }
    private static bool ForgeBool(TomlTable table, string key, bool? fallback = null)
    {
        if (!table.TryGetValue(key, out var value)) return fallback ?? throw new InvalidDataException("Відсутній boolean TOML: " + key);
        return value is bool flag ? flag : throw new InvalidDataException("Некоректний boolean TOML: " + key);
    }
    private static void ForgeId(string value)
    {
        if (!Regex.IsMatch(value, @"^[a-z][a-z0-9_]{1,63}$", RegexOptions.None, TimeSpan.FromSeconds(1)))
            throw new InvalidDataException("Некоректний Forge/NeoForge Mod ID.");
    }
    private static TomlTable ForgeTable(object? value) => value as TomlTable ?? throw new InvalidDataException("Очікувалась TOML-таблиця.");
    private static TomlTableArray ForgeTables(object? value) => value is TomlTableArray tables && tables.Count <= 512 ? tables
        : throw new InvalidDataException("Очікувався обмежений масив TOML-таблиць.");

    private async Task<ForgeModMetadata> ReadForgeAsync(ZipArchive zip, ZipArchiveEntry entry, ModLoader loader, List<string> warnings, CancellationToken ct)
    {
        TomlTable document;
        try
        {
            var text = new UTF8Encoding(false, true).GetString(await ReadBoundedEntryAsync(entry, 1024 * 1024, ct));
            var options = new TomlSerializerOptions { MaxDepth = 32, DuplicateKeyHandling = TomlDuplicateKeyHandling.Error };
            _ = SyntaxParser.ParseStrict(text, options, sourceName: entry.FullName, validate: true);
            document = TomlSerializer.Deserialize<TomlTable>(text, options)
                ?? throw new InvalidDataException("Порожні TOML-метадані.");
        }
        catch (Exception ex) when (ex is TomlException or DecoderFallbackException)
        { throw new InvalidDataException("Некоректні TOML-метадані Forge/NeoForge.", ex); }
        var language = ForgeText(document, "modLoader"); var languageRange = ForgeText(document, "loaderVersion");
        _ = ForgeText(document, "license");
        if (language is not ("javafml" or "lowcodefml")) warnings.Add("Не підтверджено версію стороннього FML language loader: " + language);
        if (document.TryGetValue("dependencies", out var depValue) && depValue is not TomlTable) throw new InvalidDataException("Некоректна таблиця dependencies.");
        var deps = depValue as TomlTable;
        var declarations = ForgeTables(document.TryGetValue("mods", out var modsValue) ? modsValue : null);
        if (declarations.Count == 0 || declarations.Count > 128) throw new InvalidDataException("Некоректна кількість модів у JAR.");
        var mods = new List<ForgeModDeclaration>(); var ids = new HashSet<string>(StringComparer.Ordinal); int total = 0;
        var jarVersion = await ReadJarImplementationVersionAsync(zip, ct);
        foreach (var mod in declarations)
        {
            ct.ThrowIfCancellationRequested(); var id = ForgeText(mod, "modId"); ForgeId(id);
            if (!ids.Add(id)) throw new InvalidDataException("Повторний Mod ID у TOML: " + id);
            string? version = ForgeText(mod, "version", "1");
            if (version == "${file.jarVersion}") version = jarVersion;
            else if (Regex.Match(version, @"^\$\{file\.([a-zA-Z0-9_-]+)\}$", RegexOptions.None, TimeSpan.FromSeconds(1)) is { Success: true } substitution)
                version = document.TryGetValue("properties", out var properties) && properties is TomlTable values
                    ? ForgeText(values, substitution.Groups[1].Value, "") : null;
            if (string.IsNullOrWhiteSpace(version) || version.Contains("${", StringComparison.Ordinal))
            { version = null; warnings.Add(id + ": версію мода неможливо підтвердити з метаданих/маніфесту."); }
            var requirements = new List<ForgeModDependency>();
            if (deps is not null && deps.TryGetValue(id, out var dependencies))
                foreach (var requirement in ForgeTables(dependencies))
                {
                    if (++total > 2048) throw new InvalidDataException("Перевищено ліміт Forge/NeoForge залежностей.");
                    var dependency = ForgeText(requirement, "modId"); ForgeId(dependency);
                    var kind = loader == ModLoader.Forge ? ForgeBool(requirement, "mandatory") ? "required" : "optional"
                        : ForgeText(requirement, "type", "required").ToLowerInvariant();
                    var order = ForgeText(requirement, "ordering", "NONE").ToUpperInvariant();
                    var side = ForgeText(requirement, "side", "BOTH").ToUpperInvariant();
                    if (kind is not ("required" or "optional" or "incompatible" or "discouraged") || order is not ("BEFORE" or "AFTER" or "NONE") || side is not ("CLIENT" or "SERVER" or "BOTH"))
                        throw new InvalidDataException("Невідомий тип, порядок або сторона залежності.");
                    if (loader == ModLoader.NeoForge && requirement.ContainsKey("mandatory") || loader == ModLoader.Forge && requirement.ContainsKey("type"))
                        warnings.Add(id + ": поле залежності належить іншому формату Forge/NeoForge; семантика невідома.");
                    var known = new HashSet<string>(["modId", "mandatory", "type", "versionRange", "ordering", "side", "reason", "referralUrl"], StringComparer.Ordinal);
                    if (requirement.Keys.Any(k => !known.Contains(k))) warnings.Add(id + ": невідоме розширення TOML-залежності.");
                    requirements.Add(new(dependency, ForgeText(requirement, "versionRange", ""), kind, order, side));
                }
            var features = new Dictionary<string, string>(StringComparer.Ordinal);
            void ReadFeatures(TomlTable table)
            {
                foreach (var feature in table)
                {
                    var key = feature.Key is "java_version" or "javaVersion" ? "java" : feature.Key;
                    if (!features.TryAdd(key, ForgeText(table, feature.Key))) throw new InvalidDataException("Повторна вимога feature.");
                    if (key != "java") warnings.Add(id + ": не підтверджена апаратна/програмна feature " + key);
                }
            }
            if (mod.TryGetValue("features", out var inlineFeatures)) ReadFeatures(ForgeTable(inlineFeatures));
            if (document.TryGetValue("features", out var featureRoot) && ForgeTable(featureRoot).TryGetValue(id, out var featureTable)) ReadFeatures(ForgeTable(featureTable));
            mods.Add(new(id, version, ForgeText(mod, "displayName", id), requirements, features));
        }
        if (deps is not null && deps.Keys.Any(key => !ids.Contains(key))) warnings.Add("TOML містить залежності для неоголошеного Mod ID.");
        if (zip.GetEntry("META-INF/jarjar/metadata.json") is not null)
            warnings.Add("Jar-in-Jar Forge/NeoForge: вибір вкладених компонентів ще не підтримується.");
        return new(language, languageRange, mods);
    }
    private async Task<string?> ReadJarImplementationVersionAsync(ZipArchive zip, CancellationToken ct)
    {
        var entry = zip.GetEntry("META-INF/MANIFEST.MF"); if (entry is null) return null;
        string text;
        try { text = new UTF8Encoding(false, true).GetString(await ReadBoundedEntryAsync(entry, 64 * 1024, ct)); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Некоректне кодування MANIFEST.MF.", ex); }
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); string? key = null;
        foreach (var line in lines)
        {
            if (line.Length == 0) break; // Only main attributes, never per-entry sections.
            if (line.StartsWith(' '))
            {
                if (key is null) throw new InvalidDataException("Некоректне продовження MANIFEST.MF.");
                fields[key] += line[1..]; continue;
            }
            int colon = line.IndexOf(": ", StringComparison.Ordinal);
            if (colon <= 0) throw new InvalidDataException("Некоректний MANIFEST.MF.");
            key = line[..colon]; if (!fields.TryAdd(key, line[(colon + 2)..])) throw new InvalidDataException("Повторний атрибут MANIFEST.MF.");
        }
        return fields.GetValueOrDefault("Implementation-Version");
    }
}
