using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.AI;

public sealed record LanguageResource(string Namespace, string Locale, int Strings, string Path);
public sealed record LanguageResourceFile(string File, string Sha256, IReadOnlyList<string> Locales)
{
    public IReadOnlyList<LanguageResource> Resources { get; init; } = [];
    public IReadOnlyList<string> Unsupported { get; init; } = [];
}
public sealed partial class TranslationWorkspace
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> MigrationGates = new(StringComparer.OrdinalIgnoreCase);
    public static string Fingerprint(TranslationDraft draft) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(draft, AtomicFile.JsonOptions)));
    private async Task MigrateLegacyAsync(Guid id, string locale, string ns, string source, CancellationToken ct)
    {
        var target = DraftPath(id, locale, ns, source); if (source != "en_us" || File.Exists(target) || File.Exists(DeletedPath(id, source, locale, ns))) return;
        var legacy = SafePath.Join(paths.Profile(id), $"translations/{locale}/{ns}.json"); if (!File.Exists(legacy)) return;
        var gate = MigrationGates.GetOrAdd(target, _ => new(1, 1)); await gate.WaitAsync(ct);
        try
        {
            if (File.Exists(target)) return;
            var original = await AtomicFile.ReadBytesAsync(legacy, 8 * 1024 * 1024, ct);
            var draft = JsonSerializer.Deserialize<TranslationDraft>(original, AtomicFile.JsonOptions) ?? throw new AiServiceException("AiUxDraftChanged");
            if (draft.SourceLocale != "en_us" || draft.Locale != locale || draft.Namespace != ns) throw new AiServiceException("AiUxDraftChanged");
            if (draft.Sources is null || draft.Values is null || draft.Sources.Count > 20000 || draft.Values.Any(p => !draft.Sources.ContainsKey(p.Key) || p.Value is null || p.Value.Source is null || p.Value.Value is null)) throw new AiServiceException("AiUxDraftChanged");
            foreach (var pair in draft.Values.Where(p => !p.Value.NeedsReview)) TranslationRules.Value(draft.Sources[pair.Key], pair.Value.Value);
            var backup = legacy + ".legacy-v1-backup";
            if (!File.Exists(backup)) await AtomicFile.WriteAsync(backup, original, ct);
            if (!(await AtomicFile.ReadBytesAsync(backup, 8 * 1024 * 1024, ct)).AsSpan().SequenceEqual(original) || !(await AtomicFile.ReadBytesAsync(legacy, 8 * 1024 * 1024, ct)).AsSpan().SequenceEqual(original)) throw new AiServiceException("AiUxDraftChanged");
            await AtomicFile.WriteJsonAsync(target, draft with { SourceLocale = "en_us", UpdatedAt = new FileInfo(legacy).LastWriteTimeUtc }, ct);
            // Original plus byte-exact backup remain; cancellation before publication is safely retryable.
        }
        finally { gate.Release(); }
    }
    public async Task<IReadOnlyList<TranslationPreview>> LibraryAsync(Guid id, CancellationToken ct = default)
    {
        var result = new List<TranslationPreview>();
        foreach (var source in TranslationRules.Locales)
            foreach (var target in TranslationRules.Locales)
                result.AddRange(await ListAsync(id, target, ct, source));
        return result.OrderBy(p => p.ModName, StringComparer.Ordinal).ThenBy(p => p.SourceLocale, StringComparer.Ordinal).ThenBy(p => p.Locale, StringComparer.Ordinal).ToArray();
    }
    public async Task<IReadOnlyList<LanguageResourceFile>> DiscoverAsync(IEnumerable<string> files, CancellationToken ct = default)
    {
        var selected = files.Distinct(StringComparer.OrdinalIgnoreCase).Take(129).ToArray();
        if (selected.Length is < 1 or > 128) throw new AiServiceException("AiUxDraftLimit");
        var result = new List<LanguageResourceFile>();
        foreach (var file in selected)
        {
            SafePath.RejectLinks(file);
            if (!Path.GetExtension(file).Equals(".jar", StringComparison.OrdinalIgnoreCase) || new FileInfo(file).Length > 256L * 1024 * 1024) throw new InvalidDataException("Selected JAR exceeds its file limit.");
            Modpacks.MrpackReader.CheckContainer(file);
            using var zip = ZipFile.OpenRead(file); Security.SafeArchive.Validate(zip, paths.Cache, maxEntries: 20000);
            var locales = new HashSet<string>(StringComparer.Ordinal);
            var resources = new List<LanguageResource>(); var unsupported = new List<string>();
            foreach (var e in zip.Entries)
            {
                var match = Regex.Match(e.FullName, @"\Aassets/([a-z0-9][a-z0-9_.-]{0,99})/lang/([a-z]{2}_[a-z]{2})\.json\z");
                if (!match.Success || !TranslationRules.Locales.Contains(match.Groups[2].Value))
                {
                    if (e.FullName.EndsWith(".lang", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                        e.FullName.Contains("/patchouli_books/", StringComparison.OrdinalIgnoreCase) || e.FullName.Contains("/books/", StringComparison.OrdinalIgnoreCase) || e.FullName.Contains("/guidebook/", StringComparison.OrdinalIgnoreCase) || e.FullName.Contains("/lang/", StringComparison.OrdinalIgnoreCase)) unsupported.Add(e.FullName);
                    continue;
                }
                using var doc = Modpacks.MrpackReader.Json(await Modpacks.MrpackReader.EntryBytes(e, 1024 * 1024, ct));
                if (doc.RootElement.ValueKind != JsonValueKind.Object || doc.RootElement.EnumerateObject().Any(p => p.Value.ValueKind != JsonValueKind.String)) { unsupported.Add(e.FullName); continue; }
                if (doc.RootElement.EnumerateObject().Count() > 20000) throw new AiServiceException("AiUxDraftLimit");
                locales.Add(match.Groups[2].Value); resources.Add(new(match.Groups[1].Value, match.Groups[2].Value, doc.RootElement.EnumerateObject().Count(), e.FullName));
            }
            result.Add(new(file, await Modpacks.MrpackReader.HashFile(file, ct), locales.Order(StringComparer.Ordinal).ToArray()) { Resources = resources, Unsupported = unsupported });
        }
        return result;
    }
    public async Task<string> TransferFingerprintAsync(Guid profile, string source, string target, string model, string? onlyNamespace, CancellationToken ct)
    {
        var drafts = new List<TranslationDraft>();
        foreach (var item in await ListAsync(profile, target, ct, source))
            if (onlyNamespace is null || item.Namespace == onlyNamespace) drafts.Add((await ReadAsync(profile, target, item.Namespace, ct, source))!);
        return Modpacks.MrpackReader.Hash(JsonSerializer.SerializeToUtf8Bytes(new { profile, source, target, model, onlyNamespace, drafts }, AtomicFile.JsonOptions));
    }
}
