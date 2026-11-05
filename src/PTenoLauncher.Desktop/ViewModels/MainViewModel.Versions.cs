using PTenoLauncher.Core;
namespace PTenoLauncher.Desktop.ViewModels;
public sealed partial class MainViewModel
{
    private VersionCatalog? currentCatalog;
    private CancellationTokenSource? catalogRefresh;
    private void CancelCatalogRefresh() => catalogRefresh?.Cancel();
    private bool updatingVersions;
    private bool showReleases = true, showSnapshots, showHistorical;
    private string versionSearch = "";
    public string VersionSearch { get => versionSearch; set { if (Set(ref versionSearch, value ?? "")) ApplyVersionFilter(); } }
    public bool ShowReleases { get => showReleases; set { if (Set(ref showReleases, value)) ApplyVersionFilter(); } }
    public bool ShowSnapshots { get => showSnapshots; set { if (Set(ref showSnapshots, value)) ApplyVersionFilter(); } }
    public bool ShowHistorical { get => showHistorical; set { if (Set(ref showHistorical, value)) ApplyVersionFilter(); } }
    private void ApplyVersionFilter()
    {
        var edited = VersionId;
        updatingVersions = true;
        try
        {
            Versions.Clear();
            var query = VersionSearch.Trim();
            if (currentCatalog is not null)
                foreach (var entry in currentCatalog.Versions
                    .Where(v => (ShowReleases && v.Type == "release" || ShowSnapshots && v.Type == "snapshot" ||
                        ShowHistorical && v.Type is "old_alpha" or "old_beta") &&
                        v.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(v => v.ReleasedAt))
                    Versions.Add(entry);
            SelectedVersion = Versions.FirstOrDefault(v => v.Id == edited);
        }
        finally { updatingVersions = false; }
    }
    public async Task RefreshCatalogIfStaleAsync(CancellationToken ct = default)
    {
        if (IsBusy || !IsInitialized || services.Network.Offline ||
            currentCatalog is not null && DateTimeOffset.UtcNow - currentCatalog.FetchedAt < TimeSpan.FromMinutes(15)) return;
        await RunAsync(async operationToken =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, operationToken);
            catalogRefresh = linked;
            try { SetCatalog(await services.Versions.RefreshAsync(linked.Token)); }
            finally { if (ReferenceEquals(catalogRefresh, linked)) catalogRefresh = null; }
        });
    }
    public async Task SaveVersionVisibilityAsync()
    {
        if (!IsInitialized) return;
        try
        {
            await services.Store.SetSettingAsync("versions.releases", ShowReleases.ToString());
            await services.Store.SetSettingAsync("versions.snapshots", ShowSnapshots.ToString());
            await services.Store.SetSettingAsync("versions.historical", ShowHistorical.ToString());
        }
        catch (Exception ex) { Status = ex.Message; }
    }
}
