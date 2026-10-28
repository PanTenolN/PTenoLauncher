using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Security;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.Mods;

public sealed partial class LocalModManager
{
    private sealed record QuiltInspection(string Id, string Name, string Version, string Environment,
        QuiltModMetadata Metadata, IReadOnlyList<ProvidedMod> Provided);

    private async Task<QuiltInspection> ReadQuiltAsync(ZipArchive zip, List<string> warnings, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await ReadBoundedEntryAsync(zip.GetEntry("quilt.mod.json")!, 1024 * 1024, ct),
            new() { MaxDepth = 32 });
        var root = doc.RootElement; ValidateQuiltKeys(root);
        if (!root.TryGetProperty("schema_version", out var schema) || !schema.TryGetInt32(out int number) || number != 1)
            warnings.Add("Непідтримувана схема quilt.mod.json; сумісність невідома.");
        var loader = root.GetProperty("quilt_loader");
        var id = RequiredFabricText(loader, "id"); var version = RequiredFabricText(loader, "version");
        var group = RequiredFabricText(loader, "group"); ValidateQuiltIdentity(id, group);
        var name = id;
        if (loader.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("name", out var title))
            name = title.GetString() ?? id;
        var environment = "*";
        if (root.TryGetProperty("minecraft", out var minecraft) && minecraft.TryGetProperty("environment", out var env))
            environment = env.GetString() switch { "*" or "" => "*", "client" => "client", "dedicated_server" => "server", _ => "unknown" };
        if (!loader.TryGetProperty("intermediate_mappings", out var mappings) || mappings.GetString() != "net.fabricmc:intermediary")
            warnings.Add("Не підтверджено підтримку Quilt intermediate_mappings для цього профілю.");
        int budget = 1024;
        List<QuiltDependency> Read(string key, bool all)
        {
            if (!loader.TryGetProperty(key, out var list)) return [];
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > 256) throw new InvalidDataException("Некоректна група залежностей Quilt.");
            var output = new List<QuiltDependency>();
            foreach (var value in list.EnumerateArray()) output.Add(ReadQuiltDependency(value, all, 0, ref budget));
            return output;
        }
        var depends = Read("depends", false); var breaks = Read("breaks", true);
        var provided = new List<ProvidedMod>();
        if (loader.TryGetProperty("provides", out var aliases))
        {
            if (aliases.ValueKind != JsonValueKind.Array || aliases.GetArrayLength() > 256) throw new InvalidDataException("Некоректні Quilt provides.");
            foreach (var alias in aliases.EnumerateArray())
            {
                var text = alias.ValueKind == JsonValueKind.String ? alias.GetString()! : RequiredFabricText(alias, "id");
                var parts = text.Split(':');
                var aliasGroup = parts.Length == 2 ? parts[0] : group; var aliasId = parts[^1];
                if (parts.Length > 2) throw new InvalidDataException("Некоректний Quilt alias.");
                ValidateQuiltIdentity(aliasId, aliasGroup);
                var aliasVersion = alias.ValueKind == JsonValueKind.Object && alias.TryGetProperty("version", out _) ? RequiredFabricText(alias, "version") : version;
                provided.Add(new(aliasId, aliasVersion, aliasGroup));
            }
        }
        if (loader.TryGetProperty("jars", out var jars) && jars.GetArrayLength() > 0)
        {
            // Quilt selects conditional nested candidates with its solver. Never assume all are active.
            warnings.Add("Вкладені Quilt JAR перевірено як архіви; вибір умовних модулів поки невідомий.");
            await ValidateQuiltNestedAsync(zip, loader, 0, new EmbeddedBudget(), ct);
        }
        if (root.TryGetProperty("plugins", out _) || loader.TryGetProperty("plugins", out _))
            warnings.Add("Quilt loader plugins не виконуються під час перевірки; їхній вплив невідомий.");
        return new(id, name, version, environment, new(group, depends, breaks), provided);
    }
    private static void ValidateQuiltIdentity(string id, string group)
    {
        if (!Regex.IsMatch(id, @"^[a-z][a-z0-9_-]{1,63}$", RegexOptions.None, TimeSpan.FromSeconds(1)) ||
            group.Length is 0 or > 256 || !Regex.IsMatch(group, @"^[a-zA-Z0-9_.-]+$", RegexOptions.None, TimeSpan.FromSeconds(1)))
            throw new InvalidDataException("Некоректний Quilt Mod ID або Maven group.");
    }
    private static void ValidateQuiltKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in value.EnumerateObject())
            {
                if (!keys.Add(field.Name)) throw new InvalidDataException("Повторне поле Quilt metadata: " + field.Name);
                ValidateQuiltKeys(field.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var child in value.EnumerateArray()) ValidateQuiltKeys(child);
    }
    private static QuiltDependency ReadQuiltDependency(JsonElement value, bool all, int depth, ref int budget)
    {
        if (depth > 16 || --budget < 0) throw new InvalidDataException("Перевищено ліміт залежностей Quilt.");
        if (value.ValueKind == JsonValueKind.Array)
        {
            if (value.GetArrayLength() is 0 or > 256) throw new InvalidDataException("Некоректна група залежностей Quilt.");
            var children = new List<QuiltDependency>();
            foreach (var child in value.EnumerateArray()) children.Add(ReadQuiltDependency(child, all, depth + 1, ref budget));
            return new(null, "\"*\"", Children: children, All: all);
        }
        var id = value.ValueKind == JsonValueKind.String ? value.GetString()! : RequiredFabricText(value, "id");
        var parts = id.Split(':');
        if (parts.Length > 2) throw new InvalidDataException("Некоректний ідентифікатор залежності Quilt.");
        ValidateQuiltIdentity(parts[^1], parts.Length == 2 ? parts[0] : "unqualified");
        string versions = "\"*\""; bool optional = false, supported = true; QuiltDependency? unless = null;
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("versions", out var expression)) versions = expression.GetRawText();
            if (value.TryGetProperty("optional", out var flag)) optional = flag.GetBoolean();
            if (value.TryGetProperty("unless", out var condition)) unless = ReadQuiltDependency(condition, false, depth + 1, ref budget);
            // Loader plugins may define additional constraints. They cannot be guessed or executed here.
            supported = value.EnumerateObject().All(p => p.Name is "id" or "versions" or "optional" or "unless" or "reason");
        }
        return new(id, versions, optional, unless, Supported: supported);
    }
    private async Task ValidateQuiltNestedAsync(ZipArchive zip, JsonElement loader, int depth, EmbeddedBudget budget, CancellationToken ct)
    {
        if (depth > 4) throw new InvalidDataException("Перевищено ліміт вкладених Quilt JAR.");
        if (!loader.TryGetProperty("jars", out var jars)) return;
        foreach (var child in jars.EnumerateArray())
        {
            if (++budget.Count > 64 || depth >= 4) throw new InvalidDataException("Перевищено ліміт вкладених Quilt JAR.");
            ct.ThrowIfCancellationRequested(); var name = child.GetString()!;
            _ = SafePath.Join(paths.Cache, name);
            var entry = zip.GetEntry(name) ?? throw new InvalidDataException("Відсутній вкладений Quilt JAR: " + name);
            if ((budget.Bytes += entry.Length) > 128L * 1024 * 1024) throw new InvalidDataException("Перевищено бюджет вкладених Quilt JAR.");
            using var memory = new MemoryStream(await ReadBoundedEntryAsync(entry, 32 * 1024 * 1024, ct), false);
            using var nested = new ZipArchive(memory, ZipArchiveMode.Read); SafeArchive.Validate(nested, paths.Cache);
            var metadata = nested.GetEntry("quilt.mod.json");
            if (metadata is null) continue;
            using var doc = JsonDocument.Parse(await ReadBoundedEntryAsync(metadata, 1024 * 1024, ct), new() { MaxDepth = 32 });
            ValidateQuiltKeys(doc.RootElement);
            await ValidateQuiltNestedAsync(nested, doc.RootElement.GetProperty("quilt_loader"), depth + 1, budget, ct);
        }
    }
}
