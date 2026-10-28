using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.AI;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private string aiKey = "", aiModel = "", aiInput = "", aiResponse = "", aiLocale = "uk_ua", aiEditor = "", aiPackFormat = "";
    private TranslationPreview? aiDraft;
    private bool aiConsent;
    public string AiKey { get => aiKey; set => Set(ref aiKey, value); }
    public string AiModel { get => aiModel; set { Set(ref aiModel, value); AiConsent = false; } }
    public string AiInput { get => aiInput; set { Set(ref aiInput, value); AiConsent = false; Changed(nameof(AiSendPreview)); } }
    public string AiSendPreview { get { try { return AiData.Redact(AiInput); } catch (ArgumentException) { return TextCatalog.Get("AiInputLimit"); } } }
    public string AiResponse { get => aiResponse; set => Set(ref aiResponse, value); }
    public bool AiConsent { get => aiConsent; set => Set(ref aiConsent, value); }
    public string[] AiLocales => TranslationRules.Locales;
    public string AiLocale { get => aiLocale; set { if (Set(ref aiLocale, value)) { AiConsent = false; TranslationDrafts.Clear(); SelectedTranslationDraft = null; } } }
    public ObservableCollection<TranslationPreview> TranslationDrafts { get; } = [];
    public TranslationPreview? SelectedTranslationDraft { get => aiDraft; set { if (Set(ref aiDraft, value)) { AiEditor = ""; AiConsent = false; } } }
    public string AiEditor { get => aiEditor; set => Set(ref aiEditor, value); }
    public string AiPackFormat { get => aiPackFormat; set => Set(ref aiPackFormat, value); }
    public Task SaveAiConfigurationAsync() => RunAsync(async ct =>
    {
        if (!string.IsNullOrEmpty(AiKey))
        {
            if (AiKey.Length is < 16 or > 512 || AiKey.Any(c => c <= 32 || c >= 127)) throw new ArgumentException("Invalid API key format.");
            services.AiSecrets.Write("openai-api-key", Encoding.UTF8.GetBytes(AiKey)); AiKey = "";
        }
        await services.Store.SetSettingAsync("ai.openai.model", AiModel.Trim(), ct);
        Status = TextCatalog.Get("AiConfigSaved");
    });
    public Task DeleteAiKeyAsync() => RunAsync(_ => { services.AiSecrets.Delete("openai-api-key"); AiKey = ""; AiConsent = false; Status = TextCatalog.Get("AiKeyRemoved"); return Task.CompletedTask; });
    public Task AskAiAsync(bool analyzeLog) => RunAsync(async ct =>
    {
        AiResponse = "";
        AiResponse = await services.OpenAi.AskAsync(AiModel.Trim(), AiInput, AiConsent, analyzeLog, ct);
        AiConsent = false; Status = TextCatalog.Get("AiDone");
    });
    public Task ImportTranslationJarsAsync(IEnumerable<string> files) => RunAsync(async ct =>
    {
        var previews = await services.Translations.ImportAsync(RequireProfile(), files, AiLocale, ct);
        SetTranslationDrafts(previews); AiConsent = false; Status = TextCatalog.Get("AiImported");
    });
    private void SetTranslationDrafts(IReadOnlyList<TranslationPreview> drafts)
    {
        var ns = SelectedTranslationDraft?.Namespace;
        TranslationDrafts.Clear(); foreach (var draft in drafts) TranslationDrafts.Add(draft);
        SelectedTranslationDraft = TranslationDrafts.FirstOrDefault(d => d.Namespace == ns) ?? TranslationDrafts.FirstOrDefault();
    }
    public Task LoadTranslationDraftsAsync() => RunAsync(async ct => SetTranslationDrafts(await services.Translations.ListAsync(RequireProfile().Id, AiLocale, ct)));
    public Task TranslateModsAsync(bool all = false) => RunAsync(async ct =>
    {
        var profile = RequireProfile();
        var selected = all ? null : SelectedTranslationDraft?.Namespace ?? throw new InvalidOperationException(TextCatalog.Get("AiSelectDraft"));
        await services.Translations.TranslateAsync(profile, AiLocale, AiModel.Trim(), AiConsent, new Progress<string>(p => Status = p), ct, selected);
        SetTranslationDrafts(await services.Translations.ListAsync(profile.Id, AiLocale, ct));
        AiConsent = false; Status = TextCatalog.Get("AiDone");
    });
    public Task LoadTranslationEditorAsync() => RunAsync(async ct =>
    {
        var ns = SelectedTranslationDraft?.Namespace ?? throw new InvalidOperationException(TextCatalog.Get("AiSelectDraft"));
        var draft = await services.Translations.ReadAsync(RequireProfile().Id, AiLocale, ns, ct) ?? throw new InvalidDataException("Translation draft is missing.");
        AiEditor = JsonSerializer.Serialize(draft.Sources.ToDictionary(p => p.Key, p => draft.Values.TryGetValue(p.Key, out var value) ? value.Value : p.Value), AtomicFile.JsonOptions);
    });
    public Task SaveTranslationEditorAsync() => RunAsync(async ct =>
    {
        if (AiEditor.Length > 8 * 1024 * 1024) throw new InvalidDataException("Editor exceeds 8 MB.");
        using var json = JsonDocument.Parse(AiEditor);
        var edits = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in json.RootElement.EnumerateObject())
            if (pair.Value.ValueKind != JsonValueKind.String || !edits.TryAdd(pair.Name, pair.Value.GetString()!)) throw new InvalidDataException("Invalid or duplicate translation key.");
        var profile = RequireProfile(); var ns = SelectedTranslationDraft?.Namespace ?? throw new InvalidOperationException(TextCatalog.Get("AiSelectDraft"));
        // Values left equal to the English source are not marked as manual translations.
        var draft = (await services.Translations.ReadAsync(profile.Id, AiLocale, ns, ct))!;
        var changed = edits.Where(p => !draft.Sources.TryGetValue(p.Key, out var original) || (draft.Values.TryGetValue(p.Key, out var current) ? current.Value != p.Value || current.NeedsReview : p.Value != original)).ToDictionary();
        await services.Translations.SaveManualAsync(profile, AiLocale, ns, changed, ct);
        SetTranslationDrafts(await services.Translations.ListAsync(profile.Id, AiLocale, ct)); Status = TextCatalog.Get("AiEditsSaved");
    });
    public Task ExportTranslationPackAsync() => RunAsync(async ct =>
    {
        if (!int.TryParse(AiPackFormat, out var format)) throw new ArgumentException(TextCatalog.Get("AiPackHint"));
        var pack = await services.Translations.ExportAsync(RequireProfile(), AiLocale, format, ct);
        Status = $"{pack.Path} · strings={pack.Strings}; missing={pack.Missing}; review={pack.Review}. " + TextCatalog.Get("AiPackHint");
    });
    private void ResetTranslationSelection() { TranslationDrafts.Clear(); SelectedTranslationDraft = null; AiEditor = ""; AiConsent = false; }
}
