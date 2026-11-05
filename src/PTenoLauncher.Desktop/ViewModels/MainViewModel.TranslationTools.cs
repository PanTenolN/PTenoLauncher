using System.Collections.ObjectModel;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.AI;
namespace PTenoLauncher.Desktop.ViewModels;

public sealed record TranslationDirectionChoice(string Source, string Target)
{ public override string ToString() => Source.Length == 0 ? TextCatalog.Get("AiUxAllDirections") : TranslationPreview.LanguageName(Source) + " → " + TranslationPreview.LanguageName(Target); }
public sealed partial class MainViewModel
{
    public ObservableCollection<TranslationRow> VisibleTranslationRows { get; } = [];
    public ObservableCollection<TranslationDirectionChoice> TranslationDirections { get; } = [];
    private TranslationDirectionChoice? translationDirection;
    public TranslationDirectionChoice? TranslationDirection { get => translationDirection; set { if (Set(ref translationDirection, value)) FilterTranslations(); } }
    private bool libraryReview, libraryManual, libraryMissing, rowMissing, rowReview, rowManual;
    private string rowSearch = "", translationProfileName = "", translationMinecraft = "";
    public bool TranslationLibraryReview { get => libraryReview; set { if (Set(ref libraryReview, value)) FilterTranslations(); } }
    public bool TranslationLibraryManual { get => libraryManual; set { if (Set(ref libraryManual, value)) FilterTranslations(); } }
    public bool TranslationLibraryMissing { get => libraryMissing; set { if (Set(ref libraryMissing, value)) FilterTranslations(); } }
    public bool TranslationRowMissing { get => rowMissing; set { if (Set(ref rowMissing, value)) FilterTranslationRows(); } }
    public bool TranslationRowReview { get => rowReview; set { if (Set(ref rowReview, value)) FilterTranslationRows(); } }
    public bool TranslationRowManual { get => rowManual; set { if (Set(ref rowManual, value)) FilterTranslationRows(); } }
    public string TranslationRowSearch { get => rowSearch; set { if (Set(ref rowSearch, value)) FilterTranslationRows(); } }
    public string TranslationProfileName { get => translationProfileName; set => Set(ref translationProfileName, value); }
    public string TranslationMinecraft { get => translationMinecraft; set => Set(ref translationMinecraft, value); }
    private TranslationDeletePreview? translationDelete;
    private bool deleteConsent;
    public bool TranslationDeleteConsent { get => deleteConsent; set => Set(ref deleteConsent, value); }
    public string TranslationDeleteText => translationDelete is { } p ? TextCatalog.Format("AiUxDeleteScope", p.Namespace, TranslationPreview.LanguageName(p.Source), TranslationPreview.LanguageName(p.Target), p.Bytes, p.ResourcePack) : "";
    private void RefreshTranslationDirections()
    {
        var prior = translationDirection; TranslationDirections.Clear(); TranslationDirections.Add(new("", ""));
        foreach (var d in TranslationDrafts.DistinctBy(d => (d.SourceLocale, d.Locale))) TranslationDirections.Add(new(d.SourceLocale, d.Locale));
        translationDirection = TranslationDirections.FirstOrDefault(d => d == prior) ?? TranslationDirections[0]; Changed(nameof(TranslationDirection));
    }
    private void FilterTranslationRows()
    {
        VisibleTranslationRows.Clear();
        foreach (var row in TranslationRows.Where(r => (!TranslationRowMissing || r.Missing) && (!TranslationRowReview || r.NeedsReview) && (!TranslationRowManual || r.Manual) &&
            (r.Key.Contains(rowSearch, StringComparison.OrdinalIgnoreCase) || r.Original.Contains(rowSearch, StringComparison.OrdinalIgnoreCase) || r.Translation.Contains(rowSearch, StringComparison.OrdinalIgnoreCase)))) VisibleTranslationRows.Add(row);
    }
    public void SelectTranslationTrialSample()
    {
        foreach (var row in TranslationRows) row.TrialSelected = false;
        int count = 0, size = 0;
        foreach (var row in VisibleTranslationRows.Where(r => r.Missing))
        { if (count >= 5) break; if (size + row.Key.Length + row.Original.Length > 10000) continue; row.TrialSelected = true; count++; size += row.Key.Length + row.Original.Length; }
        InvalidateTranslationConsent();
    }
    public Task PreviewTranslationTrialAsync() => RunAsync(async ct =>
    {
        InvalidateTranslationConsent(); if (TranslationDirty) throw new AiServiceException("AiUxUnsaved");
        var d = SelectedTranslationDraft ?? throw new AiServiceException("AiSelectDraft");
        trialPlan = await services.Translations.PreviewSelectionAsync(RequireProfile(), d.SourceLocale, d.Locale, d.Namespace, AiModel,
            TranslationRows.Where(r => r.TrialSelected).Select(r => r.Key).ToArray(), ct);
        var draft = (await services.Translations.ReadAsync(RequireProfile().Id, d.Locale, d.Namespace, ct, d.SourceLocale))!;
        TranslationTransferPreview = TextCatalog.Format("AiUxTrialScope", trialPlan.Keys.Count, TranslationPreview.LanguageName(d.SourceLocale), TranslationPreview.LanguageName(d.Locale), AiModel) + Environment.NewLine +
            JsonSerializer.Serialize(trialPlan.Keys.ToDictionary(k => k, k => draft.Sources[k]), PTenoLauncher.Infrastructure.Storage.AtomicFile.JsonOptions);
        Changed(nameof(TranslationTransferPreview));
    });
    public Task TranslateTrialAsync() => RunAsync(async ct =>
    {
        var plan = trialPlan ?? throw new AiServiceException("AiUxConsent"); var d = SelectedTranslationDraft ?? throw new AiServiceException("AiSelectDraft");
        if (TranslationDirty || plan.Namespace != d.Namespace || plan.Source != d.SourceLocale || plan.Target != d.Locale || plan.Model != AiModel || !plan.Keys.SequenceEqual(TranslationRows.Where(r => r.TrialSelected).Select(r => r.Key).Order(StringComparer.Ordinal))) throw new AiServiceException("AiUxDraftChanged");
        try { await services.Translations.TranslateSelectionAsync(RequireProfile(), plan, TranslationConsent, ct); responsesVerified = true; }
        finally { InvalidateTranslationConsent(); RefreshAiText(); }
        await ReloadTranslationLibrary(ct); Status = TextCatalog.Get("AiDone");
    });
    public Task PreviewDeleteTranslationAsync() => RunAsync(async ct =>
    {
        translationDelete = null; TranslationDeleteConsent = false; Changed(nameof(TranslationDeleteText));
        if (TranslationDirty) throw new AiServiceException("AiUxUnsaved"); var d = SelectedTranslationDraft ?? throw new AiServiceException("AiSelectDraft");
        translationDelete = await services.Translations.PreviewDeleteAsync(RequireProfile(), d.SourceLocale, d.Locale, d.Namespace, ct); Changed(nameof(TranslationDeleteText));
    });
    public Task DeleteTranslationAsync() => RunAsync(async ct =>
    {
        var p = translationDelete ?? throw new AiServiceException("AiUxConsent"); var d = SelectedTranslationDraft ?? throw new AiServiceException("AiSelectDraft");
        if (TranslationDirty || d.Namespace != p.Namespace || d.SourceLocale != p.Source || d.Locale != p.Target) throw new AiServiceException("AiUxDraftChanged");
        await services.Translations.DeleteAsync(RequireProfile(), p, TranslationDeleteConsent, ct);
        translationDelete = null; TranslationDeleteConsent = false; SelectedTranslationDraft = null; await ReloadTranslationLibrary(ct); Changed(nameof(TranslationDeleteText)); Status = TextCatalog.Get("AiUxDeleted");
    });
    public Task CreateTranslationProfileAsync() => RunAsync(async ct =>
    {
        if (TranslationDirty) throw new AiServiceException("AiUxUnsaved");
        if (!TranslationMinecraft.Any(char.IsAsciiDigit) || TranslationMinecraft.Contains("latest", StringComparison.OrdinalIgnoreCase)) throw new AiServiceException("AiUxExactVersion");
        var p = new GameProfile(Guid.NewGuid(), TranslationProfileName.Trim(), TranslationMinecraft.Trim(), ModLoader.Vanilla, 4096, null, DateTimeOffset.UtcNow);
        ProfileRules.Validate(p); await services.Store.SaveProfileAsync(p, ct); await ReloadProfilesAsync(ct); SelectedProfile = Profiles.Single(x => x.Id == p.Id);
        Status = TextCatalog.Get("AiUxLocalProfileCreated");
    });
    public Task ImportTranslationClientMetadataAsync(string jar) => RunAsync(async ct =>
    {
        var proof = await TranslationPackMetadataResolver.ImportLocalClientAsync(services.Paths, RequireProfile(), jar, ct);
        AiPackFormat = ""; lastTranslationPack = null; Changed(nameof(TranslationPackPath)); Changed(nameof(TranslationPackResult)); Status = TextCatalog.Format("AiUxMetadataImported", proof.Evidence);
    });
}
