using PTenoLauncher.Core;
namespace PTenoLauncher.Desktop.ViewModels;
public sealed partial class MainViewModel
{
    private VersionCatalog? currentCatalog;
    private bool updatingVersions;
    private bool showSnapshots = true, showHistorical;
    public bool ShowSnapshots { get => showSnapshots; set { if (Set(ref showSnapshots, value)) ApplyVersionFilter(); } }
    public bool ShowHistorical { get => showHistorical; set { if (Set(ref showHistorical, value)) ApplyVersionFilter(); } }
    private void ApplyVersionFilter()
    {
        var edited = VersionId;
        updatingVersions = true;
        try
        {
            Versions.Clear();
            if (currentCatalog is not null)
                foreach (var entry in currentCatalog.Versions.Where(v => v.Type == "release" || ShowSnapshots && v.Type == "snapshot" || ShowHistorical && v.Type is "old_alpha" or "old_beta").OrderByDescending(v => v.ReleasedAt))
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
            SetCatalog(await services.Versions.RefreshAsync(linked.Token));
        });
    }
    public async Task SaveVersionVisibilityAsync()
    {
        if (!IsInitialized) return;
        try
        {
            await services.Store.SetSettingAsync("versions.snapshots", ShowSnapshots.ToString());
            await services.Store.SetSettingAsync("versions.historical", ShowHistorical.ToString());
        }
        catch (Exception ex) { Status = ex.Message; }
    }
}