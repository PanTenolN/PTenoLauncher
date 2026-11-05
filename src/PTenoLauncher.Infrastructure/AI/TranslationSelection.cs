using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.AI;

public sealed record TranslationSelection(Guid Profile, string Namespace, string Source, string Target, string Model,
    string DraftFingerprint, IReadOnlyList<string> Keys, string Fingerprint);
public sealed record TranslationDeletePreview(Guid Profile, string Namespace, string Source, string Target, long Bytes, string DraftFingerprint, string FileSha256, string ResourcePack);
public sealed partial class TranslationWorkspace
{
    internal Action<string>? TranslationFault { get; set; }
    // One deliberately small trial request, separate from the existing explicitly confirmed full workflow.
    public async Task<TranslationSelection> PreviewSelectionAsync(GameProfile profile, string source, string target, string ns, string model, IReadOnlyList<string> keys, CancellationToken ct = default)
    {
        TranslationRules.Locale(source); TranslationRules.Locale(target); TranslationRules.Namespace(ns);
        if (source == target) throw new AiServiceException("AiUxSameLanguage");
        if (keys is null || keys.Count is < 1 or > 32 || keys.Distinct(StringComparer.Ordinal).Count() != keys.Count) throw new AiServiceException("AiUxTrialLimit");
        var draft = await ReadAsync(profile.Id, target, ns, ct, source) ?? throw new AiServiceException("AiUxDraftChanged");
        if (keys.Any(k => !draft.Sources.ContainsKey(k) || draft.Values.ContainsKey(k)) || keys.Sum(k => k.Length + draft.Sources[k].Length) > 10000) throw new AiServiceException("AiUxTrialLimit");
        var ordered = keys.Order(StringComparer.Ordinal).ToArray(); var state = Fingerprint(draft);
        var fingerprint = Modpacks.MrpackReader.Hash(JsonSerializer.SerializeToUtf8Bytes(new { profile.Id, source, target, ns, model, state, ordered }, AtomicFile.JsonOptions));
        return new(profile.Id, ns, source, target, model, state, ordered, fingerprint);
    }
    public async Task TranslateSelectionAsync(GameProfile profile, TranslationSelection selection, bool consent, CancellationToken ct = default)
    {
        if (selection.Source == selection.Target) throw new AiServiceException("AiUxSameLanguage");
        if (!consent) throw new AiServiceException("AiUxConsent");
        if (profile.Id != selection.Profile) throw new AiServiceException("AiUxDraftChanged");
        await policy.ExecuteAsync(async token =>
        {
            using var lease = usage.Acquire(profile.Id);
            var fresh = await PreviewSelectionAsync(profile, selection.Source, selection.Target, selection.Namespace, selection.Model, selection.Keys, token);
            if (fresh.Fingerprint != selection.Fingerprint) throw new AiServiceException("AiUxDraftChanged");
            var draft = (await ReadAsync(profile.Id, fresh.Target, fresh.Namespace, token, fresh.Source))!;
            var batch = fresh.Keys.ToDictionary(k => k, k => draft.Sources[k], StringComparer.Ordinal);
            var translated = await provider.TranslateAsync(fresh.Model.Trim(), fresh.Target, batch, true, token, fresh.Source);
            token.ThrowIfCancellationRequested();
            var current = await ReadAsync(profile.Id, fresh.Target, fresh.Namespace, token, fresh.Source);
            if (current is null || Fingerprint(current) != fresh.DraftFingerprint) throw new AiServiceException("AiUxDraftChanged");
            foreach (var (key, value) in translated) draft.Values.Add(key, new(batch[key], value));
            await AtomicFile.WriteJsonAsync(DraftPath(profile.Id, fresh.Target, fresh.Namespace, fresh.Source), draft with { UpdatedAt = DateTimeOffset.UtcNow }, token);
            return true;
        }, ct);
    }
    private string DeletedPath(Guid id, string source, string target, string ns)
    { TranslationRules.Locale(source); TranslationRules.Locale(target); TranslationRules.Namespace(ns); return SafePath.Join(paths.Profile(id), $"translations/deleted/{source}/{target}/{ns}.json"); }
    public async Task<TranslationDeletePreview> PreviewDeleteAsync(GameProfile profile, string source, string target, string ns, CancellationToken ct = default)
    {
        var draft = await ReadAsync(profile.Id, target, ns, ct, source) ?? throw new AiServiceException("AiUxDraftChanged");
        var file = DraftPath(profile.Id, target, ns, source);
        return new(profile.Id, ns, source, target, new FileInfo(file).Length, Fingerprint(draft), await Modpacks.MrpackReader.HashFile(file, ct),
            SafePath.Join(paths.Root, TranslationPackOwnership.RelativePath(paths, profile.Id, source, target)));
    }
    public async Task DeleteAsync(GameProfile profile, TranslationDeletePreview preview, bool confirmed, CancellationToken ct = default)
    {
        if (!confirmed) throw new AiServiceException("AiUxConsent");
        using var lease = usage.Acquire(profile.Id);
        var fresh = await PreviewDeleteAsync(profile, preview.Source, preview.Target, preview.Namespace, ct);
        if (fresh != preview || profile.Id != preview.Profile) throw new AiServiceException("AiUxDraftChanged");
        var file = DraftPath(profile.Id, preview.Target, preview.Namespace, preview.Source);
        ct.ThrowIfCancellationRequested();
        // Durable tombstone prevents retained legacy originals/backups from silently resurrecting this entry.
        // After this bounded commit begins, cancellation cannot leave deletion half acknowledged.
        await AtomicFile.WriteJsonAsync(DeletedPath(profile.Id, preview.Source, preview.Target, preview.Namespace), new { schema = 1, preview.Source, preview.Target, preview.Namespace, preview.FileSha256 }, CancellationToken.None);
        TranslationFault?.Invoke("delete-intent");
        if (await Modpacks.MrpackReader.HashFile(file, CancellationToken.None) != preview.FileSha256) throw new AiServiceException("AiUxDraftChanged");
        SafePath.RejectLinks(file); File.Delete(file);
        // Generated pack, its integrity proof/backups, migration backups and every other draft remain untouched.
    }
}
