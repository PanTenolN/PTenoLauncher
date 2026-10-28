using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.AI;

public sealed record TranslatedValue(string Source, string Value, bool Manual = false, bool NeedsReview = false);
public sealed record TranslationDraft(string Namespace, string Locale, Dictionary<string, string> Sources, Dictionary<string, TranslatedValue> Values);
public sealed record TranslationPreview(string Namespace, int Strings, int Missing, int Manual, int Review)
{
    public override string ToString() => $"{Namespace}: {Strings}; pending={Missing}; manual={Manual}; review={Review}";
}
public sealed record TranslationPack(string Path, int Strings, int Missing, int Review);

/// <summary>Reads only selected modern en_us JSON resources; never executes or modifies a JAR.</summary>
public sealed class TranslationWorkspace(LauncherPaths paths, ILauncherStore store, ProfileUsage usage, AiPolicy policy, OpenAiClient provider)
{
    private string DraftPath(Guid id, string locale, string ns)
    {
        TranslationRules.Locale(locale); TranslationRules.Namespace(ns);
        if (!Directory.Exists(paths.Profile(id))) throw new InvalidOperationException("Select an existing saved profile first.");
        return SafePath.Join(paths.Profile(id), $"translations/{locale}/{ns}.json");
    }
    public async Task<IReadOnlyList<TranslationPreview>> ImportAsync(GameProfile profile, IEnumerable<string> files, string locale, CancellationToken ct = default)
    {
        TranslationRules.Locale(locale);
        using var lease = usage.Acquire(profile.Id);
        var sources = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var selected = files.Distinct(StringComparer.OrdinalIgnoreCase).Take(129).ToArray();
        if (selected.Length is < 1 or > 128) throw new ArgumentException("Select between 1 and 128 JAR files.");
        long total = 0;
        foreach (var file in selected)
        {
            ct.ThrowIfCancellationRequested(); SafePath.RejectLinks(file);
            if (!string.Equals(Path.GetExtension(file), ".jar", StringComparison.OrdinalIgnoreCase) || new FileInfo(file).Length > 256L * 1024 * 1024)
                throw new InvalidDataException("Selected JAR exceeds its file limit.");
            using var archive = ZipFile.OpenRead(file);
            if (archive.Entries.Count > 20000) throw new InvalidDataException("JAR contains too many entries.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                var match = Regex.Match(entry.FullName, @"\Aassets/([a-z0-9][a-z0-9_.-]{0,99})/lang/en_us\.json\z");
                if (!match.Success) continue;
                if (!names.Add(entry.FullName) || entry.Length > 1024 * 1024 || entry.Length > Math.Max(1, entry.CompressedLength) * 500)
                    throw new InvalidDataException("Unsafe or duplicate language entry.");
                total += entry.Length; if (total > 8 * 1024 * 1024) throw new InvalidDataException("Selected language data exceeds 8 MB.");
                await using var input = entry.Open(); using var buffer = new MemoryStream();
                var bytes = new byte[8192]; int n;
                while ((n = await input.ReadAsync(bytes, ct)) > 0)
                {
                    if (buffer.Length + n > 1024 * 1024) throw new InvalidDataException("Language entry is too large.");
                    await buffer.WriteAsync(bytes.AsMemory(0, n), ct);
                }
                using var json = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
                if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Language data must be a JSON object.");
                var ns = match.Groups[1].Value;
                if (!sources.TryGetValue(ns, out var map)) { if (sources.Count >= 64) throw new InvalidDataException("Too many namespaces."); sources.Add(ns, map = new(StringComparer.Ordinal)); }
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in json.RootElement.EnumerateObject())
                {
                    if (!keys.Add(item.Name) || item.Name.Length > 512 || item.Value.ValueKind != JsonValueKind.String || item.Value.GetString()!.Length > 16000 || map.Count >= 20000)
                        throw new InvalidDataException("Invalid language key or value.");
                    var value = item.Value.GetString()!;
                    if (map.TryGetValue(item.Name, out var previous) && previous != value) throw new InvalidDataException("Conflicting language keys in selected JARs.");
                    map[item.Name] = value;
                }
            }
        }
        if (sources.Count == 0) throw new InvalidDataException("No modern assets/<namespace>/lang/en_us.json resources were found.");
        foreach (var (ns, map) in sources)
        {
            var path = DraftPath(profile.Id, locale, ns);
            var old = await ReadAsync(profile.Id, locale, ns, ct);
            var values = new Dictionary<string, TranslatedValue>(StringComparer.Ordinal);
            foreach (var (key, source) in map)
            {
                if (old?.Values.TryGetValue(key, out var translated) == true)
                {
                    if (translated.Manual) values[key] = translated with { NeedsReview = translated.NeedsReview || translated.Source != source };
                    else if (translated.Source == source) values[key] = translated;
                }
            }
            // Previous state is retained before source updates so removed manual keys can be recovered.
            if (File.Exists(path)) await AtomicFile.WriteAsync(path + ".previous", await File.ReadAllBytesAsync(path, ct), ct);
            await AtomicFile.WriteJsonAsync(path, new TranslationDraft(ns, locale, map, values), ct);
        }
        return await ListAsync(profile.Id, locale, ct);
    }
    public async Task<TranslationDraft?> ReadAsync(Guid id, string locale, string ns, CancellationToken ct = default)
    {
        var draft = await AtomicFile.ReadJsonAsync<TranslationDraft>(DraftPath(id, locale, ns), ct);
        if (draft is null) return null;
        if (draft.Namespace != ns || draft.Locale != locale || draft.Sources is null || draft.Values is null || draft.Sources.Count > 20000 || draft.Values.Keys.Any(k => !draft.Sources.ContainsKey(k)))
            throw new InvalidDataException("Invalid saved translation draft.");
        if (draft.Sources.Any(p => p.Key.Length > 512 || p.Value is null || p.Value.Length > 16000)) throw new InvalidDataException("Invalid saved source text.");
        foreach (var (key, value) in draft.Values)
        {
            if (value is null || value.Source is null || value.Value is null || value.Source.Length > 16000 || value.Value.Length > 16000) throw new InvalidDataException("Translation exceeds its value limit.");
            if (!value.NeedsReview) TranslationRules.Value(draft.Sources[key], value.Value);
        }
        return draft;
    }
    public async Task<IReadOnlyList<TranslationPreview>> ListAsync(Guid id, string locale, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(DraftPath(id, locale, "probe"))!;
        if (!Directory.Exists(directory)) return [];
        var result = new List<TranslationPreview>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            var ns = Path.GetFileNameWithoutExtension(file);
            var draft = (await ReadAsync(id, locale, ns, ct))!;
            result.Add(new(ns, draft.Sources.Count, draft.Sources.Count - draft.Values.Count, draft.Values.Count(p => p.Value.Manual), draft.Values.Count(p => p.Value.NeedsReview)));
        }
        return result;
    }
    public Task TranslateAsync(GameProfile profile, string locale, string model, bool consent, IProgress<string>? progress = null, CancellationToken ct = default, string? onlyNamespace = null)
        => policy.ExecuteAsync(async token =>
        {
            if (!consent) throw new InvalidOperationException("Confirm the selected language-data transfer first.");
            using var lease = usage.Acquire(profile.Id);
            var previews = await ListAsync(profile.Id, locale, token);
            if (onlyNamespace is not null && !previews.Any(p => p.Namespace == onlyNamespace)) throw new InvalidOperationException("Select an imported language namespace first.");
            foreach (var preview in previews.Where(p => onlyNamespace is null || p.Namespace == onlyNamespace))
            {
                var draft = (await ReadAsync(profile.Id, locale, preview.Namespace, token))!;
                var path = DraftPath(profile.Id, locale, preview.Namespace);
                var expected = await File.ReadAllBytesAsync(path, token);
                var pending = draft.Sources.Where(p => !draft.Values.ContainsKey(p.Key)).OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
                var offset = 0;
                while (offset < pending.Length)
                {
                    var batch = new Dictionary<string, string>(StringComparer.Ordinal); var size = 0;
                    while (offset < pending.Length && batch.Count < 32 && (size < 10000 || batch.Count == 0))
                    {
                        var item = pending[offset++]; batch.Add(item.Key, item.Value); size += item.Key.Length + item.Value.Length;
                    }
                    progress?.Report($"{preview.Namespace}: {offset}/{pending.Length}");
                    var translated = await provider.TranslateAsync(model, locale, batch, true, token);
                    token.ThrowIfCancellationRequested();
                    SafePath.RejectLinks(path);
                    if (!(await File.ReadAllBytesAsync(path, token)).AsSpan().SequenceEqual(expected)) throw new IOException("Translation draft changed during the request. Reopen it before continuing.");
                    foreach (var (key, value) in translated) draft.Values.Add(key, new(batch[key], value));
                    await AtomicFile.WriteJsonAsync(path, draft, token);
                    expected = await File.ReadAllBytesAsync(path, token);
                }
            }
            return true;
        }, ct);

    public async Task SaveManualAsync(GameProfile profile, string locale, string ns, IReadOnlyDictionary<string, string> edits, CancellationToken ct = default)
    {
        using var lease = usage.Acquire(profile.Id);
        var draft = await ReadAsync(profile.Id, locale, ns, ct) ?? throw new InvalidOperationException("Import language data first.");
        foreach (var (key, value) in edits)
        {
            if (!draft.Sources.TryGetValue(key, out var original)) throw new InvalidDataException("Editor contains an unknown source key.");
            TranslationRules.Value(original, value);
        }
        foreach (var (key, value) in edits) draft.Values[key] = new(draft.Sources[key], value, true, false);
        await AtomicFile.WriteJsonAsync(DraftPath(profile.Id, locale, ns), draft, ct);
    }

    public async Task<TranslationPack> ExportAsync(GameProfile profile, string locale, int packFormat, CancellationToken ct = default)
    {
        if (packFormat is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(packFormat), "Enter the resource pack format for the selected Minecraft version.");
        using var lease = usage.Acquire(profile.Id); TranslationRules.Locale(locale);
        var drafts = new List<TranslationDraft>(); int missing = 0, review = 0, translated = 0;
        foreach (var item in await ListAsync(profile.Id, locale, ct))
        {
            var draft = (await ReadAsync(profile.Id, locale, item.Namespace, ct))!;
            drafts.Add(draft); missing += item.Missing; review += item.Review;
            translated += draft.Values.Count(p => !p.Value.NeedsReview);
        }
        if (translated == 0) throw new InvalidOperationException("There are no reviewed translations to export.");
        var target = SafePath.Join(paths.Game(profile.Id), $"resourcepacks/pteno-translations-{locale}.zip");
        var relative = Path.GetRelativePath(paths.Root, target).Replace('\\', '/');
        var artifacts = await store.GetArtifactsAsync(profile.Id, ct);
        if (File.Exists(target) && !artifacts.Any(a => a.Kind == "translation-resourcepack" && a.RelativePath == relative))
            throw new IOException("An untracked resource pack already exists at the destination; it was not overwritten.");
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            async Task Add(string name, object value)
            {
                var data = JsonSerializer.SerializeToUtf8Bytes(value, AtomicFile.JsonOptions);
                if (data.Length > 8 * 1024 * 1024 || buffer.Length > 32 * 1024 * 1024) throw new InvalidDataException("Resource pack exceeds its limit.");
                await using var entry = archive.CreateEntry(name).Open(); await entry.WriteAsync(data, ct);
            }
            await Add("pack.mcmeta", new { pack = new { pack_format = packFormat, description = "PTenoLauncher translations (review before use)" } });
            foreach (var draft in drafts)
            {
                var values = draft.Values.Where(p => !p.Value.NeedsReview).ToDictionary(p => p.Key, p => p.Value.Value);
                if (values.Count > 0) await Add($"assets/{draft.Namespace}/lang/{locale}.json", values);
            }
        }
        if (File.Exists(target))
        {
            if (new FileInfo(target).Length > 32 * 1024 * 1024) throw new IOException("Previous generated pack exceeds its backup limit.");
            await AtomicFile.WriteAsync(SafePath.Join(paths.Profile(profile.Id), $"translations/{locale}/previous-pack.zip"), await File.ReadAllBytesAsync(target, ct), ct);
        }
        await AtomicFile.WriteAsync(target, buffer.ToArray(), ct);
        // An interrupted/failed ownership commit leaves an untracked file which is
        // refused on retry, rather than granting permission to overwrite a user file.
        await store.RegisterArtifactAsync(new(profile.Id, relative, "translation-resourcepack"), ct);
        return new(target, translated, missing, review);
    }
}
