using System.Text;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.AI;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed partial class ProfileContent
{
    private sealed record PackOptions(string[] Lines, List<string> Selected, List<string> Incompatible, string NewLine, bool Bom)
    {
        public bool IsEnabled(string name) => Selected.Any(value => IsLocalPack(value, name));
    }

    private static bool IsLocalPack(string id, string name) => id == "file/" + name ||
        id == name && name is not ("vanilla" or "programmer_art" or "high_contrast");

    private async Task<int?> FindResourceFormatAsync(GameProfile profile, CancellationToken ct)
    {
        try { return (await TranslationPackMetadataResolver.ResolveAsync(paths, profile, ct)).Format; }
        catch (Exception error) when (error is AiServiceException or IOException or InvalidDataException or JsonException or InvalidOperationException)
        { return null; }
    }

    private static (int Min, int Max)? ReadResourceRange(JsonElement pack)
    {
        static int? Number(JsonElement element) => element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out var number) && number >= 0 ? number : null;
        static (int Min, int Max)? Range(int? min, int? max) =>
            min is { } low && max is { } high && low <= high ? (low, high) : null;
        if (pack.TryGetProperty("min_format", out var min) && pack.TryGetProperty("max_format", out var max))
            return Range(Number(min), Number(max));
        if (pack.TryGetProperty("supported_formats", out var supported))
        {
            if (supported.ValueKind == JsonValueKind.Array && supported.GetArrayLength() == 2)
                return Range(Number(supported[0]), Number(supported[1]));
            if (supported.ValueKind == JsonValueKind.Object && supported.TryGetProperty("min_inclusive", out min) &&
                supported.TryGetProperty("max_inclusive", out max))
                return Range(Number(min), Number(max));
            return Range(Number(supported), Number(supported));
        }
        if (!pack.TryGetProperty("pack_format", out var current)) return null;
        return Range(Number(current), Number(current));
    }

    private string OptionsPath(Guid profile) => SafePath.Join(paths.Game(profile), "options.txt");

    private async Task<PackOptions> ReadPackOptionsAsync(Guid profile, CancellationToken ct)
    {
        var path = OptionsPath(profile);
        if (!File.Exists(path)) return new([], [], [], Environment.NewLine, false);
        return ParsePackOptions(await AtomicFile.ReadBytesAsync(path, 2 * 1024 * 1024, ct));
    }

    private static PackOptions ParsePackOptions(byte[] bytes)
    {
        var bom = bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble());
        string content;
        try { content = new UTF8Encoding(false, true).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)); }
        catch (DecoderFallbackException error) { throw new InvalidDataException("ResourceOptionsInvalid", error); }
        var lines = content.Split('\n');
        List<string> ReadList(string key)
        {
            var values = lines.Where(line => line.StartsWith(key, StringComparison.Ordinal)).ToArray();
            if (values.Length == 0) return [];
            try
            {
                if (values.Length == 1 && JsonSerializer.Deserialize<List<string>>(values[0][key.Length..]) is { } list &&
                    list.All(value => value is not null)) return list;
            }
            catch (JsonException) { }
            throw new InvalidDataException("ResourceOptionsInvalid");
        }
        return new(lines, ReadList("resourcePacks:"), ReadList("incompatibleResourcePacks:"),
            content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n", bom);
    }

    internal static byte[] RebasePackOptions(byte[] before, string from, string to)
    {
        var options = ParsePackOptions(before);
        var changed = false;
        void Rebase(string key, List<string> entries)
        {
            if (!entries.Any(value => IsLocalPack(value, from))) return;
            var index = Array.FindIndex(options.Lines, line => line.StartsWith(key, StringComparison.Ordinal));
            var mapped = entries.Select(value => IsLocalPack(value, from)
                ? (value.StartsWith("file/", StringComparison.Ordinal) ? "file/" : "") + to : value);
            options.Lines[index] = key + JsonSerializer.Serialize(mapped) + (options.Lines[index].EndsWith('\r') ? "\r" : "");
            changed = true;
        }
        Rebase("resourcePacks:", options.Selected);
        Rebase("incompatibleResourcePacks:", options.Incompatible);
        if (!changed) return before;
        var result = Encoding.UTF8.GetBytes(string.Join('\n', options.Lines));
        return options.Bom ? [..Encoding.UTF8.GetPreamble(), ..result] : result;
    }

    public async Task SetResourcePackEnabledAsync(Guid profile, ProfileContentEntry entry, bool enabled, CancellationToken ct = default)
    {
        if (entry.FileName != Path.GetFileName(entry.FileName) || entry.FileName.StartsWith(".pteno-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ContentMissing");
        var usage = new ProfileUsage(paths);
        using var lease = usage.Acquire(profile);
        usage.CheckWorldLocks(profile);
        EnsureExists(entry, SafePath.Join(Folder(profile, ProfileContentKind.ResourcePacks), entry.FileName));
        var options = await ReadPackOptionsAsync(profile, ct);
        if (options.IsEnabled(entry.FileName) == enabled) return;
        var id = "file/" + entry.FileName;
        options.Selected.RemoveAll(value => IsLocalPack(value, entry.FileName));
        options.Incompatible.RemoveAll(value => IsLocalPack(value, entry.FileName));
        if (enabled)
        {
            if (options.Selected.Count == 0 && !options.Lines.Any(line => line.StartsWith("resourcePacks:", StringComparison.Ordinal)))
                options.Selected.Add("vanilla");
            options.Selected.Add(id);
            if (entry.Compatibility == ResourcePackCompatibility.Incompatible) options.Incompatible.Add(id);
        }
        var lines = options.Lines.ToList();
        void WriteList(string key, List<string> list)
        {
            var index = lines.FindIndex(line => line.StartsWith(key, StringComparison.Ordinal));
            var line = key + JsonSerializer.Serialize(list);
            if (index >= 0) lines[index] = line + (lines[index].EndsWith('\r') ? "\r" : "");
            else
            {
                if (lines.Count > 0 && lines[^1].Length == 0)
                    lines.Insert(lines.Count - 1, line + (options.NewLine == "\r\n" ? "\r" : ""));
                else
                {
                    if (lines.Count > 0 && options.NewLine == "\r\n") lines[^1] += "\r";
                    lines.Add(line);
                }
            }
        }
        WriteList("resourcePacks:", options.Selected);
        WriteList("incompatibleResourcePacks:", options.Incompatible);
        var result = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        await AtomicFile.WriteAsync(OptionsPath(profile), options.Bom ? [..Encoding.UTF8.GetPreamble(), ..result] : result, ct);
    }
}
