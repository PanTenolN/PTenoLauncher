using System.Collections.ObjectModel;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Desktop.Localization;

namespace PTenoLauncher.Desktop.ViewModels;

public sealed class ModUpdateChoice(ModUpdateRow row, Action changed) : ObservableObject
{
    public ModUpdateRow Row { get; } = row;
    private bool selected, remove;
    public bool IsSelected { get => selected; set { if (Set(ref selected, value)) changed(); } }
    public bool Remove { get => remove; set { if (Set(ref remove, value)) changed(); } }
    public string Details => TextCatalog.Format("UpdatesRow", Row.File.Path, Row.Current?.Number ?? "?", Row.Available?.Number ?? "?", Row.Available?.Channel ?? Row.Current?.Channel ?? "?",
        Row.Current is null ? TextCatalog.Get("UpdatesLocal") : "Modrinth " + Row.Current.ProjectId + " / " + Row.Current.Id,
        Row.Available?.Id ?? "?", Row.Pinned ? TextCatalog.Get("UpdatesPinned") : "", Row.Note is null ? "" : TextCatalog.Get(Row.Note)) +
        " · Minecraft " + string.Join(", ", (Row.Available ?? Row.Current)?.GameVersions ?? []) + " · " + string.Join(", ", (Row.Available ?? Row.Current)?.Loaders ?? []) + Environment.NewLine + "SHA512: " + Row.File.Sha512;
    public void RefreshLanguage() => Changed(nameof(Details));
}
public sealed class ModUpdateHistoryChoice(ModUpdateHistory item) : ObservableObject
{
    public ModUpdateHistory Item { get; } = item;
    public string Title => Item.At.ToLocalTime().ToString("g") + " · " + TextCatalog.Get("Updates" + Item.Kind) + " · " + TextCatalog.Get("Updates" + Item.State) + " · " + Item.Id.ToString("N");
    public void RefreshLanguage() => Changed(nameof(Title));
}
public sealed partial class MainViewModel
{
    private readonly ModUpdateManager modUpdates;
    public ObservableCollection<ModUpdateChoice> UpdateChoices { get; } = [];
    public ObservableCollection<ModUpdateHistoryChoice> ModUpdateHistory { get; } = [];
    private ModUpdateChoice? selectedUpdate;
    public ModUpdateChoice? SelectedUpdate { get => selectedUpdate; set { if (Set(ref selectedUpdate, value)) UpdatesConfirmed = false; } }
    private ModUpdateHistoryChoice? selectedUpdateHistory;
    public ModUpdateHistoryChoice? SelectedUpdateHistory { get => selectedUpdateHistory; set { if (Set(ref selectedUpdateHistory, value)) InvalidateUpdatePreview(); } }
    private bool includeModPrereleases, updatesConfirmed;
    public bool IncludeModPrereleases { get => includeModPrereleases; set { if (Set(ref includeModPrereleases, value)) ResetModUpdates(); } }
    public bool UpdatesConfirmed { get => updatesConfirmed; set => Set(ref updatesConfirmed, value); }
    private ModUpdatePlan? updatePlan;
    private ModUpdateScan? updateScan;
    private int updateRevision;
    public string ModUpdateSummary => updatePlan is { } p
        ? TextCatalog.Get(p.Kind == "Rollback" ? "UpdatesRollbackHint" : "UpdatesPlanHint") + Environment.NewLine +
            string.Join(Environment.NewLine, p.Changes.Select(c => TextCatalog.Get("Updates" + c.Kind) + " · " + (c.Before?.Path ?? c.After!.Path) + " · SHA512: " + (c.Before?.Sha512 ?? "—") + " → " + (c.After?.Sha512 ?? "—") + " · " + (c.Version is { } v ? "Modrinth " + v.ProjectId + " / " + v.Id + " / " + v.File.Name + " · " + v.File.Sha512 + " · " + v.File.Size + " B · " + string.Join(", ", v.Dependencies.Select(d => d.Kind + ":" + (d.ProjectId ?? "?") + "/" + (d.VersionId ?? "?"))) : ""))) +
            Environment.NewLine + string.Join(Environment.NewLine, p.Blockers.Concat(p.Warnings).Select(UpdateFinding))
        : updateScan is { } scan ? TextCatalog.Format("UpdatesScan", scan.Rows.Count, scan.Cached ? TextCatalog.Get("UpdatesCached") : TextCatalog.Get("UpdatesOnline"), scan.MetadataAt?.ToLocalTime().ToString("g") ?? "?") : "";
    private static string UpdateFinding(string text)
    {
        var split = text.IndexOf(':'); var code = split < 0 ? text : text[..split];
        return TextCatalog.Keys.Contains(code) ? TextCatalog.Get(code) + (split < 0 ? "" : text[split..]) : text;
    }
    private void InvalidateUpdatePreview() { updateRevision++; updatePlan = null; UpdatesConfirmed = false; Changed(nameof(ModUpdateSummary)); }
    private void ResetModUpdates()
    {
        InvalidateUpdatePreview(); updateScan = null; UpdateChoices.Clear(); SelectedUpdate = null; ModUpdateHistory.Clear(); SelectedUpdateHistory = null; Changed(nameof(ModUpdateSummary));
    }
    private async Task LoadUpdateHistory(CancellationToken ct)
    {
        var profile = RequireProfile(); var history = await modUpdates.HistoryAsync(profile, ct);
        if (SelectedProfile != profile) throw new ModUpdateException("UpdatesStale");
        ModUpdateHistory.Clear(); foreach (var item in history) ModUpdateHistory.Add(new(item)); SelectedUpdateHistory = ModUpdateHistory.FirstOrDefault();
    }
    public Task CheckModUpdatesAsync() => RunAsync(async ct =>
    {
        ResetModUpdates(); var p = RequireProfile(); var scan = await modUpdates.CheckAsync(p, IncludeModPrereleases, ct);
        if (SelectedProfile != p) throw new ModUpdateException("UpdatesStale");
        updateScan = scan;
        foreach (var row in updateScan.Rows) UpdateChoices.Add(new(row, InvalidateUpdatePreview));
        SelectedUpdate = UpdateChoices.FirstOrDefault(); await LoadUpdateHistory(ct); Changed(nameof(ModUpdateSummary));
        Status = TextCatalog.Get(updateScan.Findings.Count > 0 ? "UpdatesUnavailable" : "UpdatesChecked");
    });
    public Task PreviewModUpdatesAsync() => RunAsync(async ct =>
    {
        var requests = UpdateChoices.Where(x => x.IsSelected).Select(x => new ModUpdateRequest(x.Row.File.Path, x.Remove ? null : x.Row.Available?.Id, x.Remove)).ToArray();
        InvalidateUpdatePreview(); var revision = updateRevision; var p = RequireProfile(); var plan = await modUpdates.PreviewAsync(p, requests, IncludeModPrereleases, ct);
        if (SelectedProfile != p || revision != updateRevision) throw new ModUpdateException("UpdatesStale"); updatePlan = plan;
        Changed(nameof(ModUpdateSummary)); Status = TextCatalog.Get(updatePlan.CanApply ? "UpdatesPreviewReady" : "UpdatesCompatibility");
    });
    public Task SetModPinAsync(bool pin) => RunAsync(async ct =>
    {
        var row = SelectedUpdate ?? throw new ModUpdateException("UpdatesSelect");
        await modUpdates.SetPinAsync(RequireProfile(), row.Row.File.Path, pin, UpdatesConfirmed, ct);
        ResetModUpdates(); Status = TextCatalog.Get("UpdatesPinSaved");
    });
    public Task ApplyModUpdatesAsync() => RunAsync(async ct =>
    {
        await modUpdates.ApplyAsync(RequireProfile(), updatePlan ?? throw new ModUpdateException("UpdatesSelect"), UpdatesConfirmed, ct);
        ResetModUpdates(); ClearInstallationCart(); await LoadUpdateHistory(ct);
        await InvalidateWorkspaceModsAsync(RequireProfile().Id);
        Status = TextCatalog.Get("UpdatesApplied");
    });
    public Task RefreshModUpdateHistoryAsync() => RunAsync(LoadUpdateHistory);
    public Task PreviewModRollbackAsync() => RunAsync(async ct =>
    {
        var id = SelectedUpdateHistory?.Item.Id ?? throw new ModUpdateException("UpdatesSelect"); InvalidateUpdatePreview(); var revision = updateRevision; var p = RequireProfile();
        var plan = await modUpdates.PreviewRollbackAsync(p, id, ct);
        if (SelectedProfile != p || revision != updateRevision) throw new ModUpdateException("UpdatesStale"); updatePlan = plan;
        Changed(nameof(ModUpdateSummary)); Status = TextCatalog.Get("UpdatesPreviewReady");
    });
    public Task RecoverModUpdatesAsync() => RunAsync(async ct =>
    {
        await modUpdates.RecoverAsync(RequireProfile(), UpdatesConfirmed, ct); ResetModUpdates(); await LoadUpdateHistory(ct);
        await InvalidateWorkspaceModsAsync(RequireProfile().Id);
        Status = TextCatalog.Get("UpdatesRecovered");
    });
    private void RefreshModUpdateLanguage()
    { foreach (var row in UpdateChoices) row.RefreshLanguage(); foreach (var item in ModUpdateHistory) item.RefreshLanguage(); Changed(nameof(ModUpdateSummary)); Changed(nameof(ModUpdateHistory)); }
}
