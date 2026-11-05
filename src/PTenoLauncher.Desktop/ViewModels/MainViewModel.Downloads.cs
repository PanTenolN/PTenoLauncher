using PTenoLauncher.Core;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Storage;
using System.Collections.ObjectModel;

namespace PTenoLauncher.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public sealed record DownloadHistoryRow(DownloadHistoryEntry Entry)
    {
        public string Result => TextCatalog.Get("DownloadResult" + Entry.Result);
        public string Time => Entry.At.ToLocalTime().ToString("g");
        public bool HasDetail => Entry.Detail is not null;
    }
    public ObservableCollection<DownloadHistoryRow> DownloadHistory { get; } = [];
    private SqliteLauncherStore DownloadStore => (SqliteLauncherStore)services.Store;
    public async Task RefreshDownloadHistoryAsync()
    {
        try
        {
            var entries = await DownloadStore.GetDownloadHistoryAsync();
            DownloadHistory.Clear();
            foreach (var entry in entries) DownloadHistory.Add(new(entry));
        }
        catch (Exception ex) { services.Log.Error("downloads.history", ex); }
    }
    private bool downloadActive;
    public bool DownloadActive { get => downloadActive; private set => Set(ref downloadActive, value); }
    private double? downloadPercent;
    public double? DownloadPercent
    {
        get => downloadPercent;
        private set { if (Set(ref downloadPercent, value)) { Changed(nameof(DownloadIndeterminate)); Changed(nameof(DownloadProgressValue)); } }
    }
    public bool DownloadIndeterminate => DownloadPercent is null;
    public double DownloadProgressValue => DownloadPercent ?? 0;
    private string downloadDetails = "";
    public string DownloadDetails { get => TextCatalog.Relocalize(downloadDetails); private set => Set(ref downloadDetails, value); }

    private async Task<T> TrackDownloadsAsync<T>(string title, string profile, Func<IProgress<DownloadProgress>, Task<T>> work, CancellationToken ct, Action<DownloadProgress>? report = null)
    {
        var id = Guid.NewGuid();
        DownloadDetails = TextCatalog.Format("DownloadPreparing", title, profile);
        DownloadPercent = null;
        DownloadActive = true;
        bool accepting = true;
        bool recorded = false;
        var result = "Failed";
        string? detail = null;
        var progress = new Progress<DownloadProgress>(p =>
        {
            if (!accepting) return;
            report?.Invoke(p);
            if (p.Installing)
            {
                DownloadPercent = null;
                DownloadDetails = TextCatalog.Format("DownloadInstallingMods", title, profile);
                return;
            }
            DownloadPercent = p.Verifying ? null : p.Total is > 0 ? Math.Clamp(100.0 * p.Received / p.Total.Value, 0, 99) : null;
            var name = p.Name.Length >= 32 && p.Name.All(Uri.IsHexDigit) ? title : p.Name;
            var file = p.Total is > 0
                ? TextCatalog.Format("DownloadProgress", name, p.Received / 1024.0, p.Total.Value / 1024.0)
                : TextCatalog.Format("DownloadWithoutTotal", name, p.Received / 1024.0);
            DownloadDetails = TextCatalog.Format(p.Verifying ? "DownloadVerifying" : "DownloadCurrent", title, profile, p.Verifying ? name : file);
        });
        try
        {
            try { await DownloadStore.StartDownloadAsync(id, title, profile); recorded = true; }
            catch (Exception ex) { services.Log.Error("downloads.start", ex); }
            var value = await work(progress);
            result = "Succeeded";
            return value;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { result = "Cancelled"; throw; }
        catch (ModIdentityConflictException ex) { detail = ex.Detail; throw; }
        finally
        {
            accepting = false;
            DownloadActive = false;
            if (recorded)
            {
                try { await DownloadStore.FinishDownloadAsync(id, result, detail); }
                catch (Exception ex) { services.Log.Error("downloads.finish", ex); }
            }
            if (IsDownloads) await RefreshDownloadHistoryAsync();
        }
    }
}
