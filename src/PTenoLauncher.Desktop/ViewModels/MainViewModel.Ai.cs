using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.AI;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Desktop.ViewModels;

public sealed record TranslationLanguage(string Code, string Name) { public override string ToString() => Name + " (" + Code + ")"; }
public sealed record AiChatMessage(string Role, string Text) { public string Speaker => TextCatalog.Get(Role == "user" ? "AiUxYou" : "AiAssistant"); }
public sealed class TranslationRow(string key, string original, string translation, string state, Action changed) : ObservableObject
{
    public string Key { get; } = key;
    public string Original { get; } = original;
    public bool Missing => state == "AiUxPending";
    public bool NeedsReview => state == "AiUxReview";
    public bool Manual => state == "AiUxManual" || Dirty;
    private bool trialSelected;
    public bool TrialSelected { get => trialSelected; set { if (Set(ref trialSelected, value)) changed(); } }
    public string State => Dirty ? TextCatalog.Get("AiUxUnsaved") : TextCatalog.Get(state);
    private readonly string initial = translation;
    private string value = translation;
    private bool reviewConfirmed;
    public bool ReviewConfirmed { get => reviewConfirmed; set { if (Set(ref reviewConfirmed, value)) { Changed(nameof(State)); changed(); } } }
    public string Translation { get => value; set { if (Set(ref this.value, value)) { Changed(nameof(State)); changed(); } } }
    public bool Dirty => value != initial || ReviewConfirmed;
}

public sealed partial class MainViewModel
{
    private string aiKey = "", aiModel = "", aiInput = "", aiResponse = "", aiLocale = "uk_ua", aiSource = "", aiEditor = "", editorSnapshot = "", aiPackFormat = "", translationSearch = "";
    private TranslationPreview? aiDraft;
    private bool aiConsent, connectionConsent, connectionVerified, aiLogMode, aiRequest, translationConsent, previewAll, rowsLoading;
    private bool responsesVerified;
    private bool rebuildingTranslationList;
    public bool TranslationListRebuilding => rebuildingTranslationList;
    private TranslationSelection? trialPlan;
    private string? editorFingerprint, translationFingerprint;
    private IReadOnlyList<LanguageResourceFile> languageFiles = [];
    private readonly List<AiTurn> sessionContext = [];
    public ObservableCollection<AiChatMessage> AiConversation { get; } = [];
    public ObservableCollection<string> AiModels { get; } = [];
    public bool AiHasKey => services.AiSecrets.Contains("openai-api-key");
    public string AiKeyState => TextCatalog.Get(AiHasKey ? "AiUxKeySaved" : "AiUxMissingKey");
    public string AiConnectionState => TextCatalog.Get(!AiEnabled ? "AiUxDisabled" : Offline ? "AiUxOffline" : responsesVerified ? "AiUxResponsesChecked" : connectionVerified ? "AiUxCatalogChecked" : AiHasKey ? "AiUxKeySaved" : "AiUxMissingKey");
    public bool AiConnectionConsent { get => connectionConsent; set => Set(ref connectionConsent, value); }
    public string AiKey { get => aiKey; set { if (Set(ref aiKey, value ?? "")) { AiConnectionConsent = false; AiConsent = false; } } }
    public string AiModel { get => aiModel; set { if (Set(ref aiModel, value ?? "")) { responsesVerified = false; Changed(nameof(SelectedAiModel)); AiConsent = false; InvalidateTranslationConsent(); ClearAiConversation(); } } }
    public string? SelectedAiModel { get => AiModels.Contains(AiModel) ? AiModel : null; set { if (value is not null) AiModel = value; } }
    public bool AiLogMode { get => aiLogMode; set { if (Set(ref aiLogMode, value)) ClearAiConversation(); } }
    public string AiInput { get => aiInput; set { if (Set(ref aiInput, value ?? "")) { AiConsent = false; Changed(nameof(AiSendPreview)); } } }
    private IReadOnlyList<AiTurn> Context()
    {
        var context = sessionContext.TakeLast(12).ToList();
        while (context.Sum(t => t.Text.Length) + AiInput.Length > 32000 && context.Count > 0) context.RemoveAt(0);
        return context;
    }
    public string AiSendPreview
    { get { try { return string.Join(Environment.NewLine, Context().Append(new("user", AiInput)).Select(t => t.Role + ": " + AiData.Redact(t.Text))); } catch (ArgumentException) { return TextCatalog.Get("AiInputLimit"); } } }
    public string AiResponse { get => aiResponse; set => Set(ref aiResponse, value); }
    public bool AiConsent { get => aiConsent; set { Set(ref aiConsent, value); Changed(nameof(AiCanSend)); } }
    public bool AiCanSend => IsIdle && AiEnabled && !Offline && AiHasKey && AiConsent && !string.IsNullOrWhiteSpace(AiInput) && !string.IsNullOrWhiteSpace(AiModel);
    public bool AiWaiting => aiRequest && IsBusy;
    public bool AiChatEmpty => AiConversation.Count == 0;
    public void RefreshAiText()
    { foreach (var name in new[] { nameof(AiKeyState), nameof(AiConnectionState), nameof(AiCanSend), nameof(CanUseOnlineAi), nameof(AiWaiting), nameof(AiChatEmpty), nameof(AiSendPreview), nameof(TranslationDirty), nameof(TranslationPackResult) }) Changed(name); }
    public void ClearAiConversation()
    { sessionContext.Clear(); AiConversation.Clear(); AiResponse = ""; AiConsent = false; RefreshAiText(); }
    public Task SaveAiConfigurationAsync() => RunAsync(async ct =>
    {
        if (AiKey.Length > 0)
        {
            if (AiKey.Length is < 16 or > 512 || AiKey.Any(c => c <= 32 || c >= 127)) throw new AiServiceException("AiUxBadKey");
            services.AiSecrets.Write("openai-api-key", Encoding.UTF8.GetBytes(AiKey)); AiKey = ""; connectionVerified = responsesVerified = false; AiModels.Clear(); ClearAiConversation();
        }
        await services.Store.SetSettingAsync("ai.openai.model", AiModel.Trim(), ct); RefreshAiText(); Status = TextCatalog.Get("AiConfigSaved");
    });
    public void ReplaceAiKey() { AiKey = ""; connectionVerified = responsesVerified = false; AiConnectionConsent = false; RefreshAiText(); }
    public Task DeleteAiKeyAsync() => RunAsync(_ =>
    { services.AiSecrets.Delete("openai-api-key"); AiKey = ""; connectionVerified = responsesVerified = false; AiModels.Clear(); AiConnectionConsent = false; ClearAiConversation(); RefreshAiText(); Status = TextCatalog.Get("AiKeyRemoved"); return Task.CompletedTask; });
    public Task SaveAiModelAsync() => RunAsync(async ct => { await services.Store.SetSettingAsync("ai.openai.model", AiModel.Trim(), ct); Status = TextCatalog.Get("AiConfigSaved"); });
    public Task CheckAiConnectionAsync() => RunAsync(async ct =>
    {
        connectionVerified = false;
        try {
            var models = await services.OpenAi.CheckConnectionAsync(AiConnectionConsent, ct);
            AiModels.Clear(); foreach (var model in models) AiModels.Add(model);
            connectionVerified = true; Changed(nameof(SelectedAiModel)); Status = TextCatalog.Get("AiUxConnectedHint");
        } finally { AiConnectionConsent = false; RefreshAiText(); }
    });
    public async Task AskAiAsync(bool analyzeLog)
    {
        if (!AiEnabled) { Status = TextCatalog.Get("AiUxDisabled"); return; }
        if (Offline) { Status = TextCatalog.Get("AiUxOffline"); return; }
        await RunAsync(async ct =>
        {
            if (!AiConsent) throw new AiServiceException("AiUxConsent");
            var question = AiData.Redact(AiInput); var context = Context(); aiRequest = true; RefreshAiText();
            AiConversation.Add(new("user", question)); Changed(nameof(AiChatEmpty));
            try
            {
                AiResponse = AiData.Redact(await services.OpenAi.AskWithContextAsync(AiModel.Trim(), question, context, true, analyzeLog, ct));
                responsesVerified = true;
                AiConversation.Add(new("assistant", AiResponse));
                sessionContext.Add(new("user", question)); sessionContext.Add(new("assistant", AiResponse.Length > 16000 ? AiResponse[..16000] : AiResponse));
                while (sessionContext.Count > 12) sessionContext.RemoveRange(0, 2);
                while (AiConversation.Count > 24) AiConversation.RemoveAt(0);
                AiInput = ""; Status = TextCatalog.Get("AiDone");
            }
            finally { aiRequest = false; AiConsent = false; RefreshAiText(); }
        });
    }

    public static readonly TranslationLanguage[] TranslationLanguages = [new("en_us", "English"), new("uk_ua", "Українська"), new("pl_pl", "Polski"), new("de_de", "Deutsch"), new("es_es", "Español"), new("fr_fr", "Français"), new("pt_br", "Português (Brasil)"), new("ru_ru", "Русский")];
    public TranslationLanguage[] TargetLanguages => TranslationLanguages;
    public ObservableCollection<TranslationLanguage> SourceLanguages { get; } = [];
    public TranslationLanguage? SelectedSourceLanguage { get => TranslationLanguages.FirstOrDefault(l => l.Code == aiSource); set { if (value is not null) AiSourceLocale = value.Code; } }
    public TranslationLanguage? SelectedTargetLanguage { get => TranslationLanguages.FirstOrDefault(l => l.Code == aiLocale); set { if (value is not null) AiLocale = value.Code; } }
    public string[] AiLocales => TranslationRules.Locales;
    public string AiSourceLocale { get => aiSource; set { if (TranslationDirty) { Status = TextCatalog.Get("AiUxUnsaved"); Changed(nameof(SelectedSourceLanguage)); return; } if (Set(ref aiSource, value)) { SelectedTranslationDraft = null; InvalidateTranslationConsent(); Changed(nameof(SelectedSourceLanguage)); RefreshSourceFindings(); } } }
    public string AiLocale { get => aiLocale; set { if (TranslationDirty) { Status = TextCatalog.Get("AiUxUnsaved"); Changed(nameof(SelectedTargetLanguage)); return; } if (Set(ref aiLocale, value)) { SelectedTranslationDraft = null; InvalidateTranslationConsent(); Changed(nameof(SelectedTargetLanguage)); } } }
    public ObservableCollection<string> TranslationSourceFindings { get; } = [];
    private void RefreshSourceFindings()
    {
        TranslationSourceFindings.Clear();
        foreach (var f in languageFiles)
        {
            TranslationSourceFindings.Add(TextCatalog.Format(f.Locales.Contains(aiSource) ? "AiUxSourceFound" : "AiUxSourceAbsent", Path.GetFileName(f.File), string.Join(", ", f.Locales.Select(TranslationPreview.LanguageName))));
            foreach (var resource in f.Resources) TranslationSourceFindings.Add(TextCatalog.Format("AiUxResourceFound", resource.Namespace, TranslationPreview.LanguageName(resource.Locale), resource.Strings));
            if (f.Unsupported.Count > 0) TranslationSourceFindings.Add(TextCatalog.Format("AiUxUnsupportedResources", f.Unsupported.Count, string.Join(Environment.NewLine, f.Unsupported.Take(32))));
        }
    }
    public ObservableCollection<TranslationPreview> TranslationDrafts { get; } = [];
    public ObservableCollection<TranslationPreview> VisibleTranslationDrafts { get; } = [];
    public ObservableCollection<TranslationRow> TranslationRows { get; } = [];
    public string TranslationSearch { get => translationSearch; set { if (Set(ref translationSearch, value)) FilterTranslations(); } }
    private bool pendingOnly;
    public bool TranslationPendingOnly { get => pendingOnly; set { if (Set(ref pendingOnly, value)) FilterTranslations(); } }
    public bool TranslationDirty => TranslationRows.Any(r => r.Dirty) || aiEditor != editorSnapshot;
    public TranslationPreview? SelectedTranslationDraft
    {
        get => aiDraft;
        set
        {
            if (rebuildingTranslationList && value is null) return;
            if (TranslationDirty) { Status = TextCatalog.Get("AiUxUnsaved"); Changed(); return; }
            if (Set(ref aiDraft, value)) { TranslationRows.Clear(); VisibleTranslationRows.Clear(); translationDelete = null; TranslationDeleteConsent = false; Changed(nameof(TranslationDeleteText)); aiEditor = editorSnapshot = ""; Changed(nameof(AiEditor)); if (value is not null) { aiSource = value.SourceLocale; aiLocale = value.Locale; Changed(nameof(SelectedSourceLanguage)); Changed(nameof(SelectedTargetLanguage)); } InvalidateTranslationConsent(); }
        }
    }
    public string AiEditor { get => aiEditor; set { if (Set(ref aiEditor, value)) { InvalidateTranslationConsent(); Changed(nameof(TranslationDirty)); } } }
    public string AiPackFormat { get => aiPackFormat; set => Set(ref aiPackFormat, value); }
    public bool TranslationConsent { get => translationConsent; set => Set(ref translationConsent, value); }
    public string TranslationTransferPreview { get; private set; } = "";
    private TranslationPack? lastTranslationPack;
    public string TranslationPackPath => lastTranslationPack?.Path ?? "";
    public string TranslationPackResult => lastTranslationPack is { } p ? TextCatalog.Format("AiUxPackResult", p.Path, p.Strings, p.Missing, p.Review) : "";
    private void InvalidateTranslationConsent() { TranslationConsent = false; translationFingerprint = null; trialPlan = null; TranslationTransferPreview = ""; Changed(nameof(TranslationTransferPreview)); }
    private void FilterTranslations()
    {
        rebuildingTranslationList = true;
        try
        {
            VisibleTranslationDrafts.Clear(); foreach (var d in TranslationDrafts.Where(d => (!TranslationPendingOnly || d.Missing + d.Review > 0) && (!TranslationLibraryMissing || d.Missing > 0) && (!TranslationLibraryReview || d.Review > 0) && (!TranslationLibraryManual || d.Manual > 0) && (TranslationDirection is null || TranslationDirection.Source.Length == 0 || d.SourceLocale == TranslationDirection.Source && d.Locale == TranslationDirection.Target) && (d.ToString() + " " + d.Namespace).Contains(TranslationSearch, StringComparison.OrdinalIgnoreCase))) VisibleTranslationDrafts.Add(d);
            Changed(nameof(SelectedTranslationDraft));
        }
        finally { rebuildingTranslationList = false; }
    }
    public Task ImportTranslationJarsAsync(IEnumerable<string> files) => RunAsync(async ct =>
    {
        if (TranslationDirty) throw new AiServiceException("AiUxUnsaved");
        languageFiles = await services.Translations.DiscoverAsync(files, ct); SourceLanguages.Clear();
        foreach (var language in TranslationLanguages.Where(l => languageFiles.Any(f => f.Locales.Contains(l.Code)))) SourceLanguages.Add(language);
        aiSource = ""; Changed(nameof(SelectedSourceLanguage)); InvalidateTranslationConsent(); RefreshSourceFindings(); Status = TextCatalog.Get("AiUxChooseSource");
    });
    public Task ReadTranslationResourcesAsync() => RunAsync(async ct =>
    {
        if (TranslationDirty) throw new AiServiceException("AiUxUnsaved");
        if (languageFiles.Count == 0 || languageFiles.Any(f => !f.Locales.Contains(aiSource))) throw new AiServiceException("AiUxSourceMissing");
        foreach (var f in languageFiles) if (!await PTenoLauncher.Infrastructure.Network.VerifiedDownloader.VerifyAsync(f.File, f.Sha256, "SHA256", new FileInfo(f.File).Length, ct)) throw new AiServiceException("AiUxDraftChanged");
        await services.Translations.ImportAsync(RequireProfile(), languageFiles.Select(f => f.File), AiLocale, ct, aiSource);
        InvalidateTranslationConsent(); await ReloadTranslationLibrary(ct); Status = TextCatalog.Get("AiImported");
    });
    private void SetTranslationDrafts(IReadOnlyList<TranslationPreview> drafts)
    {
        var old = SelectedTranslationDraft;
        TranslationDrafts.Clear(); foreach (var draft in drafts) TranslationDrafts.Add(draft);
        if (!TranslationDirty) SelectedTranslationDraft = TranslationDrafts.FirstOrDefault(d => old is not null && d.Namespace == old.Namespace && d.SourceLocale == old.SourceLocale && d.Locale == old.Locale) ?? TranslationDrafts.FirstOrDefault(d => d.SourceLocale == aiSource && d.Locale == aiLocale) ?? TranslationDrafts.FirstOrDefault();
        RefreshTranslationDirections(); FilterTranslations();
    }
    private async Task ReloadTranslationLibrary(CancellationToken ct)
    {
        SetTranslationDrafts(await services.Translations.LibraryAsync(RequireProfile().Id, ct));
        if (SelectedTranslationDraft is not null && !TranslationDirty) await ReadTranslationEditor(ct);
    }
    public Task LoadTranslationDraftsAsync() => RunAsync(ReloadTranslationLibrary);
    private async Task ReadTranslationEditor(CancellationToken ct)
    {
        var selected = SelectedTranslationDraft ?? throw new AiServiceException("AiSelectDraft");
        var draft = await services.Translations.ReadAsync(RequireProfile().Id, selected.Locale, selected.Namespace, ct, selected.SourceLocale) ?? throw new AiServiceException("AiUxDraftChanged");
        rowsLoading = true; TranslationRows.Clear();
        foreach (var p in draft.Sources)
        {
            var value = draft.Values.GetValueOrDefault(p.Key);
            TranslationRows.Add(new(p.Key, p.Value, value?.Value ?? "", value?.NeedsReview == true ? "AiUxReview" : value is null ? "AiUxPending" : value.Manual ? "AiUxManual" : "AiUxReady", () => { if (!rowsLoading) { InvalidateTranslationConsent(); Changed(nameof(TranslationDirty)); } }));
        }
        rowsLoading = false; editorFingerprint = TranslationWorkspace.Fingerprint(draft);
        FilterTranslationRows();
        aiEditor = editorSnapshot = JsonSerializer.Serialize(draft.Sources.ToDictionary(p => p.Key, p => draft.Values.TryGetValue(p.Key, out var v) ? v.Value : p.Value), AtomicFile.JsonOptions);
        Changed(nameof(AiEditor)); Changed(nameof(TranslationDirty));
    }
    public Task LoadTranslationEditorAsync() => RunAsync(async ct => { if (TranslationDirty) throw new AiServiceException("AiUxUnsaved"); await ReadTranslationEditor(ct); });
    public Task DiscardTranslationEditsAsync() => RunAsync(ReadTranslationEditor);
    public Task SaveTranslationEditorAsync() => RunAsync(async ct =>
    {
        var selected = SelectedTranslationDraft ?? throw new AiServiceException("AiSelectDraft"); var profile = RequireProfile();
        var edits = TranslationRows.Where(r => r.Dirty).ToDictionary(r => r.Key, r => r.Translation, StringComparer.Ordinal);
        if (AiEditor != editorSnapshot)
        {
            if (AiEditor.Length > 8 * 1024 * 1024 || edits.Count > 0) throw new AiServiceException("AiUxEditorConflict");
            using var json = JsonDocument.Parse(AiEditor); var seen = new HashSet<string>(StringComparer.Ordinal);
            var draft = (await services.Translations.ReadAsync(profile.Id, selected.Locale, selected.Namespace, ct, selected.SourceLocale))!;
            foreach (var p in json.RootElement.EnumerateObject())
            { if (p.Value.ValueKind != JsonValueKind.String || !seen.Add(p.Name) || !draft.Sources.ContainsKey(p.Name)) throw new AiServiceException("AiUxEditorConflict"); var v = p.Value.GetString()!; if (v != (draft.Values.GetValueOrDefault(p.Name)?.Value ?? draft.Sources[p.Name])) edits.Add(p.Name, v); }
        }
        await services.Translations.SaveManualAsync(profile, selected.Locale, selected.Namespace, edits, ct, selected.SourceLocale, editorFingerprint);
        await ReadTranslationEditor(ct); await ReloadTranslationLibrary(ct); Status = TextCatalog.Get("AiEditsSaved");
    });
    public Task PreviewTranslationTransferAsync(bool all = false) => RunAsync(async ct =>
    {
        InvalidateTranslationConsent();
        if (TranslationDirty) throw new AiServiceException("AiUxUnsaved"); var d = SelectedTranslationDraft ?? throw new AiServiceException("AiSelectDraft");
        if (d.SourceLocale == d.Locale) throw new AiServiceException("AiUxSameLanguage");
        translationFingerprint = await services.Translations.TransferFingerprintAsync(RequireProfile().Id, d.SourceLocale, d.Locale, AiModel, all ? null : d.Namespace, ct); previewAll = all;
        var content = new StringBuilder();
        foreach (var item in await services.Translations.ListAsync(RequireProfile().Id, d.Locale, ct, d.SourceLocale))
        {
            if (!all && item.Namespace != d.Namespace) continue;
            var draft = (await services.Translations.ReadAsync(RequireProfile().Id, d.Locale, item.Namespace, ct, d.SourceLocale))!;
            foreach (var p in draft.Sources.Where(p => !draft.Values.ContainsKey(p.Key))) { if (content.Length + p.Key.Length + p.Value.Length > 8 * 1024 * 1024) throw new AiServiceException("AiUxPreviewLimit"); content.AppendLine(item.Namespace + " / " + p.Key + ": " + p.Value); }
        }
        TranslationTransferPreview = content.ToString(); Changed(nameof(TranslationTransferPreview)); TranslationConsent = false;
    });
    public Task TranslateModsAsync(bool all = false) => RunAsync(async ct =>
    {
        var d = SelectedTranslationDraft ?? throw new AiServiceException("AiSelectDraft");
        if (TranslationDirty) throw new AiServiceException("AiUxUnsaved");
        if (d.SourceLocale == d.Locale) throw new AiServiceException("AiUxSameLanguage");
        if (!TranslationConsent || previewAll != all || translationFingerprint is null) throw new AiServiceException("AiUxConsent");
        if (translationFingerprint != await services.Translations.TransferFingerprintAsync(RequireProfile().Id, d.SourceLocale, d.Locale, AiModel, all ? null : d.Namespace, ct)) throw new AiServiceException("AiUxDraftChanged");
        try { await services.Translations.TranslateAsync(RequireProfile(), d.Locale, AiModel.Trim(), true, new Progress<string>(p => Status = p), ct, all ? null : d.Namespace, d.SourceLocale); responsesVerified = true; RefreshAiText(); }
        finally { InvalidateTranslationConsent(); }
        await ReloadTranslationLibrary(ct); Status = TextCatalog.Get("AiDone");
    });
    public Task ExportTranslationPackAsync() => RunAsync(async ct =>
    {
        if (TranslationDirty) throw new AiServiceException("AiUxUnsaved"); var d = SelectedTranslationDraft ?? throw new AiServiceException("AiSelectDraft");
        var profile = RequireProfile();
        var format = string.IsNullOrWhiteSpace(AiPackFormat) ? (await TranslationPackMetadataResolver.ResolveAsync(services.Paths, profile, ct)).Format : int.TryParse(AiPackFormat, out var manual) ? manual : throw new AiServiceException("AiUxPackUnknown");
        try { lastTranslationPack = await services.Translations.ExportAsync(profile, d.Locale, format, ct, d.SourceLocale); }
        catch(IOException error) { services.Log.Error("translation.pack",error);throw new AiServiceException("AiUxPackConflict"); }
        Changed(nameof(TranslationPackPath)); Changed(nameof(TranslationPackResult)); Status = TranslationPackResult;
    });
    public string TranslationFolder => SafePath.Join(services.Paths.Profile(RequireProfile().Id), "translations");
    private void ResetTranslationSelection()
    {
        if (TranslationDirty) return;
        languageFiles=[];SourceLanguages.Clear();aiSource="";editorFingerprint=null;lastTranslationPack=null;Changed(nameof(SelectedSourceLanguage));Changed(nameof(TranslationPackPath));Changed(nameof(TranslationPackResult));
        TranslationDrafts.Clear(); VisibleTranslationDrafts.Clear(); SelectedTranslationDraft = null; TranslationRows.Clear(); VisibleTranslationRows.Clear(); aiEditor = editorSnapshot = ""; Changed(nameof(AiEditor)); InvalidateTranslationConsent();
    }
}
