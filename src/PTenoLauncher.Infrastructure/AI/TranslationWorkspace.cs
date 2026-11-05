using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.AI;

public sealed record TranslatedValue(string Source, string Value, bool Manual = false, bool NeedsReview = false);
public sealed record TranslationDraft(string Namespace, string Locale, Dictionary<string, string> Sources, Dictionary<string, TranslatedValue> Values)
{
    public string SourceLocale { get; init; } = "en_us";
    public string ModName { get; init; } = "";
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyList<TranslationOrigin> Origins { get; init; } = [];
}
public sealed record TranslationOrigin(string FileName, string Sha256);
public sealed record TranslationPreview(string Namespace, int Strings, int Missing, int Manual, int Review)
{
    public string SourceLocale { get; init; } = "en_us";
    public string Locale { get; init; } = "uk_ua";
    public string ModName { get; init; } = "";
    public DateTimeOffset UpdatedAt { get; init; }
    public string UpdatedDisplay => UpdatedAt == default ? "—" : UpdatedAt.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);
    public static string LanguageName(string code) => code switch { "en_us" => "English", "uk_ua" => "Українська", "pl_pl" => "Polski", "de_de" => "Deutsch", "es_es" => "Español", "fr_fr" => "Français", "pt_br" => "Português (Brasil)", "ru_ru" => "Русский", _ => code };
    public override string ToString() => $"{(ModName.Length > 0 ? ModName : Namespace)} · {LanguageName(SourceLocale)} → {LanguageName(Locale)} · {Strings - Missing - Review}/{Strings}";
}
public sealed record TranslationPack(string Path, int Strings, int Missing, int Review);

/// <summary>Reads only selected modern en_us JSON resources; never executes or modifies a JAR.</summary>
public sealed partial class TranslationWorkspace(LauncherPaths paths, ILauncherStore store, ProfileUsage usage, AiPolicy policy, OpenAiClient provider)
{
    private string DraftPath(Guid id, string locale, string ns, string sourceLocale = "en_us")
    {
        TranslationRules.Locale(locale); TranslationRules.Locale(sourceLocale); TranslationRules.Namespace(ns);
        if (!Directory.Exists(paths.Profile(id))) throw new InvalidOperationException("Select an existing saved profile first.");
        return SafePath.Join(paths.Profile(id), $"translations/v2/{sourceLocale}/{locale}/{ns}.json");
    }
    public async Task<IReadOnlyList<TranslationPreview>> ImportAsync(GameProfile profile, IEnumerable<string> files, string locale, CancellationToken ct = default, string sourceLocale = "en_us")
    {
        TranslationRules.Locale(locale);
        TranslationRules.Locale(sourceLocale);
        using var lease = usage.Acquire(profile.Id);
        var sources = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var origins = new List<TranslationOrigin>();
        var selected = files.Distinct(StringComparer.OrdinalIgnoreCase).Take(129).ToArray();
        if (selected.Length is < 1 or > 128) throw new ArgumentException("Select between 1 and 128 JAR files.");
        long total = 0;
        foreach (var file in selected)
        {
            ct.ThrowIfCancellationRequested(); SafePath.RejectLinks(file);
            if (!string.Equals(Path.GetExtension(file), ".jar", StringComparison.OrdinalIgnoreCase) || new FileInfo(file).Length > 256L * 1024 * 1024)
                throw new InvalidDataException("Selected JAR exceeds its file limit.");
            using var archive = ZipFile.OpenRead(file);
            origins.Add(new(Path.GetFileName(file), await Modpacks.MrpackReader.HashFile(file, ct)));
            Security.SafeArchive.Validate(archive, paths.Cache, maxEntries: 20000);
            if (archive.Entries.Count > 20000) throw new InvalidDataException("JAR contains too many entries.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            bool found = false;
            foreach (var entry in archive.Entries)
            {
                var match = Regex.Match(entry.FullName, @"\Aassets/([a-z0-9][a-z0-9_.-]{0,99})/lang/" + Regex.Escape(sourceLocale) + @"\.json\z");
                if (!match.Success) continue;
                found = true;
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
            if (!found) throw new AiServiceException("AiUxSourceMissing");
        }
        if (sources.Count == 0) throw new AiServiceException("AiUxSourceMissing");
        for (int i = 0; i < selected.Length; i++) if (await Modpacks.MrpackReader.HashFile(selected[i], ct) != origins[i].Sha256) throw new AiServiceException("AiUxDraftChanged");
        foreach (var (ns, map) in sources)
        {
            var path = DraftPath(profile.Id, locale, ns, sourceLocale);
            var old = await ReadAsync(profile.Id, locale, ns, ct, sourceLocale);
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
            await AtomicFile.WriteJsonAsync(path, new TranslationDraft(ns, locale, map, values) { SourceLocale = sourceLocale, UpdatedAt = DateTimeOffset.UtcNow, Origins = origins, ModName = old?.ModName ?? ns }, ct);
        }
        return await ListAsync(profile.Id, locale, ct, sourceLocale);
    }
    public async Task<TranslationDraft?> ReadAsync(Guid id, string locale, string ns, CancellationToken ct = default, string sourceLocale = "en_us")
    {
        await MigrateLegacyAsync(id, locale, ns, sourceLocale, ct);
        var draft = await AtomicFile.ReadJsonAsync<TranslationDraft>(DraftPath(id, locale, ns, sourceLocale), ct);
        if (draft is null) return null;
        if (draft.Namespace != ns || draft.Locale != locale || draft.SourceLocale != sourceLocale || draft.Sources is null || draft.Values is null || draft.Sources.Count > 20000 || draft.Values.Keys.Any(k => !draft.Sources.ContainsKey(k)))
            throw new InvalidDataException("Invalid saved translation draft.");
        if (draft.Sources.Any(p => p.Key.Length > 512 || p.Value is null || p.Value.Length > 16000)) throw new InvalidDataException("Invalid saved source text.");
        foreach (var (key, value) in draft.Values)
        {
            if (value is null || value.Source is null || value.Value is null || value.Source.Length > 16000 || value.Value.Length > 16000) throw new InvalidDataException("Translation exceeds its value limit.");
            if (!value.NeedsReview) TranslationRules.Value(draft.Sources[key], value.Value);
        }
        return draft;
    }
    public async Task<IReadOnlyList<TranslationPreview>> ListAsync(Guid id, string locale, CancellationToken ct = default, string sourceLocale = "en_us")
    {
        if (sourceLocale == "en_us")
        {
            var legacy = SafePath.Join(paths.Profile(id), "translations/" + locale);
            if (Directory.Exists(legacy)) foreach (var file in Directory.EnumerateFiles(legacy, "*.json").Take(65)) await MigrateLegacyAsync(id, locale, Path.GetFileNameWithoutExtension(file), sourceLocale, ct);
        }
        var directory = Path.GetDirectoryName(DraftPath(id, locale, "probe", sourceLocale))!;
        if (!Directory.Exists(directory)) return [];
        var result = new List<TranslationPreview>();
        var saved = Directory.EnumerateFiles(directory, "*.json").Take(65).ToArray(); if (saved.Length > 64) throw new AiServiceException("AiUxDraftLimit");
        foreach (var file in saved.Order(StringComparer.Ordinal))
        {
            var ns = Path.GetFileNameWithoutExtension(file);
            var draft = (await ReadAsync(id, locale, ns, ct, sourceLocale))!;
            result.Add(new(ns, draft.Sources.Count, draft.Sources.Count - draft.Values.Count, draft.Values.Count(p => p.Value.Manual), draft.Values.Count(p => p.Value.NeedsReview)) { SourceLocale = sourceLocale, Locale = locale, ModName = draft.ModName, UpdatedAt = draft.UpdatedAt });
        }
        return result;
    }
    public Task TranslateAsync(GameProfile profile, string locale, string model, bool consent, IProgress<string>? progress = null, CancellationToken ct = default, string? onlyNamespace = null, string sourceLocale = "en_us")
        => sourceLocale == locale ? Task.CompletedTask : policy.ExecuteAsync(async token =>
        {
            if (!consent) throw new InvalidOperationException("Confirm the selected language-data transfer first.");
            using var lease = usage.Acquire(profile.Id);
            var previews = await ListAsync(profile.Id, locale, token, sourceLocale);
            if (onlyNamespace is not null && !previews.Any(p => p.Namespace == onlyNamespace)) throw new InvalidOperationException("Select an imported language namespace first.");
            foreach (var preview in previews.Where(p => onlyNamespace is null || p.Namespace == onlyNamespace))
            {
                var draft = (await ReadAsync(profile.Id, locale, preview.Namespace, token, sourceLocale))!;
                var path = DraftPath(profile.Id, locale, preview.Namespace, sourceLocale);
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
                    var translated = await provider.TranslateAsync(model, locale, batch, true, token, sourceLocale);
                    token.ThrowIfCancellationRequested();
                    SafePath.RejectLinks(path);
                    if (!(await File.ReadAllBytesAsync(path, token)).AsSpan().SequenceEqual(expected)) throw new IOException("Translation draft changed during the request. Reopen it before continuing.");
                    foreach (var (key, value) in translated) draft.Values.Add(key, new(batch[key], value));
                    draft = draft with { UpdatedAt = DateTimeOffset.UtcNow }; await AtomicFile.WriteJsonAsync(path, draft, token);
                    expected = await File.ReadAllBytesAsync(path, token);
                }
            }
            return true;
        }, ct);

    public async Task SaveManualAsync(GameProfile profile, string locale, string ns, IReadOnlyDictionary<string, string> edits, CancellationToken ct = default, string sourceLocale = "en_us", string? expectedFingerprint = null)
    {
        using var lease = usage.Acquire(profile.Id);
        var draft = await ReadAsync(profile.Id, locale, ns, ct, sourceLocale) ?? throw new InvalidOperationException("Import language data first.");
        if (expectedFingerprint is not null && Fingerprint(draft) != expectedFingerprint) throw new AiServiceException("AiUxDraftChanged");
        foreach (var (key, value) in edits)
        {
            if (!draft.Sources.TryGetValue(key, out var original)) throw new InvalidDataException("Editor contains an unknown source key.");
            TranslationRules.Value(original, value);
        }
        foreach (var (key, value) in edits) draft.Values[key] = new(draft.Sources[key], value, true, false);
        await AtomicFile.WriteJsonAsync(DraftPath(profile.Id, locale, ns, sourceLocale), draft with { UpdatedAt = DateTimeOffset.UtcNow }, ct);
    }

    public async Task<TranslationPack> ExportAsync(GameProfile profile, string locale, int packFormat, CancellationToken ct = default, string sourceLocale = "en_us")
    {
        if (packFormat is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(packFormat), "Enter the resource pack format for the selected Minecraft version.");
        using var lease = usage.Acquire(profile.Id); TranslationRules.Locale(locale); TranslationRules.Locale(sourceLocale);
        var drafts = new List<TranslationDraft>(); int missing = 0, review = 0, translated = 0;
        foreach (var item in await ListAsync(profile.Id, locale, ct, sourceLocale))
        {
            var draft = (await ReadAsync(profile.Id, locale, item.Namespace, ct, sourceLocale))!;
            drafts.Add(draft); missing += item.Missing; review += item.Review;
            translated += draft.Values.Count(p => !p.Value.NeedsReview);
        }
        if (translated == 0) throw new InvalidOperationException("There are no reviewed translations to export.");
        var relative = TranslationPackOwnership.RelativePath(paths, profile.Id, sourceLocale, locale);
        var target = SafePath.Join(paths.Root, relative);
        var proofPath = SafePath.Join(paths.Profile(profile.Id), $"translations/generated/{sourceLocale}/{locale}/receipt.json");
        var pendingPath = proofPath + ".pending";
        await RecoverGeneratedPack(profile.Id, relative, target, proofPath, pendingPath, ct);
        var previous = await AtomicFile.ReadJsonAsync<GeneratedPackProof>(proofPath, ct);
        var artifacts = await store.GetArtifactsAsync(profile.Id, ct);
        if (File.Exists(target) && !artifacts.Any(a => a.Kind == "translation-resourcepack" && a.RelativePath == relative))
            throw new IOException("An untracked resource pack already exists at the destination; it was not overwritten.");
        if (File.Exists(target) && !await VerifyGeneratedPack(target, relative, previous, ct))
            throw new IOException("The generated resource pack was changed or has no integrity receipt. Move it aside before regenerating.");
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
            var oldBytes = await AtomicFile.ReadBytesAsync(target, 32 * 1024 * 1024, ct);
            if (previous is null || Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(oldBytes)) != previous.Sha256) throw new IOException("Previous pack changed before backup.");
            var backup = SafePath.Join(paths.Profile(profile.Id), $"translations/v2/{sourceLocale}/{locale}/previous-pack.zip");
            var backupProof = proofPath + ".backup";
            if (File.Exists(backup) && !await VerifyGeneratedPack(backup, relative, await AtomicFile.ReadJsonAsync<GeneratedPackProof>(backupProof, ct), ct)) throw new IOException("The previous pack backup was changed; it was preserved.");
            await AtomicFile.WriteAsync(backup, oldBytes, ct);
            await AtomicFile.WriteJsonAsync(backupProof, previous, ct);
        }
        var payload = buffer.ToArray();
        if (payload.Length > 32 * 1024 * 1024) throw new InvalidDataException("Generated pack exceeds its size limit.");
        var next = new GeneratedPackProof(relative, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)), payload.Length);
        await AtomicFile.WriteJsonAsync(pendingPath, new GeneratedPackTransaction(previous, next), ct);
        if (File.Exists(target) && !await VerifyGeneratedPack(target, relative, previous, ct)) throw new IOException("Generated pack changed before publication.");
        await AtomicFile.WriteAsync(target, payload, ct);
        await store.RegisterArtifactAsync(new(profile.Id, relative, "translation-resourcepack"), ct);
        await AtomicFile.WriteJsonAsync(proofPath, next, ct);
        File.Delete(pendingPath);
        return new(target, translated, missing, review);
    }
}
