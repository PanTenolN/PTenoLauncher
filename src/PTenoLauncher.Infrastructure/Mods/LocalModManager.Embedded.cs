using System.IO.Compression;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Security;
namespace PTenoLauncher.Infrastructure.Mods;
public sealed partial class LocalModManager
{
    private static string RequiredFabricText(JsonElement root, string key)
    {
        var value = root.GetProperty(key).GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || value.Any(char.IsControl)) throw new InvalidDataException("Неприпустиме поле Fabric: " + key);
        return value;
    }
    private static string ReadFabricEnvironment(JsonElement root)
    {
        if (!root.TryGetProperty("environment", out var env)) return "*";
        if (env.ValueKind == JsonValueKind.String) return env.GetString() ?? "unknown";
        if (env.ValueKind != JsonValueKind.Array) return "unknown";
        if (env.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.String)) return "unknown";
        var values = env.EnumerateArray().Select(e => e.GetString()).ToArray();
        if (values.Length == 0 || values.Any(v => v is not ("*" or "client" or "server"))) return "unknown";
        return values.Contains("*") || values.Contains("client") && values.Contains("server") ? "*" : values[0]!;
    }
    private sealed class EmbeddedBudget { public int Count; public long Bytes; }
    private static async Task<byte[]> ReadBoundedEntryAsync(ZipArchiveEntry entry, int maximum, CancellationToken ct)
    {
        if (entry.Length > maximum) throw new InvalidDataException("Завеликий вкладений файл JAR.");
        await using var input = entry.Open(); using var output = new MemoryStream();
        var buffer = new byte[81920]; int read;
        while ((read = await input.ReadAsync(buffer, ct)) != 0)
        {
            if (output.Length + read > maximum || output.Length + read > entry.Length) throw new InvalidDataException("Перевищено розмір вкладеного файла.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (output.Length != entry.Length) throw new InvalidDataException("Неповний вкладений файл.");
        return output.ToArray();
    }
    private async Task ReadEmbeddedFabricAsync(ZipArchive zip, int depth, EmbeddedBudget budget,
        List<ProvidedMod> provided, List<ModRequirement> requirements, List<string> metadataWarnings, CancellationToken ct)
    {
        if (depth > 4 || ++budget.Count > 65) throw new InvalidDataException("Перевищено ліміт вкладених JAR.");
        var meta = zip.GetEntry("fabric.mod.json");
        if (meta is null) return; // No invented identifiers for unknown embedded metadata.
        using var doc = JsonDocument.Parse(await ReadBoundedEntryAsync(meta, 1024 * 1024, ct));
        var root = doc.RootElement;
        var environment = ReadFabricEnvironment(root);
        if (environment == "server") return;
        if (environment == "unknown") metadataWarnings.Add("Невідоме середовище вкладеного Fabric JAR.");
        var version = RequiredFabricText(root, "version");
        if (depth > 0 && (!root.TryGetProperty("schemaVersion", out var schema) || !schema.TryGetInt32(out var schemaNumber) || schemaNumber != 1))
            metadataWarnings.Add("Непідтримувана схема вкладеного Fabric JAR.");
        if (depth > 0) provided.Add(new(RequiredFabricText(root, "id"), version));
        if (root.TryGetProperty("provides", out var aliases))
            foreach (var alias in aliases.EnumerateArray())
            {
                var id = alias.GetString();
                if (string.IsNullOrWhiteSpace(id) || id.Length > 64) throw new InvalidDataException("Неприпустимий Fabric alias.");
                provided.Add(new(id, version));
            }
        if (depth > 0)
            foreach (var kind in new[] { ("depends", "required"), ("recommends", "optional"), ("breaks", "incompatible") })
                if (root.TryGetProperty(kind.Item1, out var group))
                    foreach (var requirement in group.EnumerateObject())
                    {
                        var alternatives = requirement.Value.ValueKind == JsonValueKind.String ? new[] { requirement.Value.GetString()! } :
                            requirement.Value.ValueKind == JsonValueKind.Array ? requirement.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray() : [];
                        requirements.Add(new(requirement.Name, alternatives, kind.Item2));
                    }
        if (!root.TryGetProperty("jars", out var jars)) return;
        foreach (var child in jars.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            var name = child.GetProperty("file").GetString()!;
            _ = PTenoLauncher.Infrastructure.Storage.SafePath.Join(paths.Cache, name);
            var entry = zip.GetEntry(name) ?? throw new InvalidDataException("Не знайдено заявлений вкладений JAR: " + name);
            if ((budget.Bytes += entry.Length) > 128L * 1024 * 1024) throw new InvalidDataException("Завеликі вкладені JAR.");
            var bytes = await ReadBoundedEntryAsync(entry, 32 * 1024 * 1024, ct);
            using var memory = new MemoryStream(bytes, false); using var nested = new ZipArchive(memory, ZipArchiveMode.Read);
            SafeArchive.Validate(nested, paths.Cache);
            await ReadEmbeddedFabricAsync(nested, depth + 1, budget, provided, requirements, metadataWarnings, ct);
        }
    }
}
