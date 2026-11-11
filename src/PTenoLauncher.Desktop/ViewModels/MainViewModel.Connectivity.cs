using PTenoLauncher.Desktop.Localization;

namespace PTenoLauncher.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private readonly SemaphoreSlim connectivityCheck = new(1, 1);
    private bool manualOffline, automaticOffline;
    private DateTimeOffset? internetReturnedAt;

    public bool Offline { get => manualOffline || automaticOffline; set => ManualOffline = value; }
    public bool ManualOffline
    {
        get => manualOffline;
        set
        {
            var previous = Offline;
            if (!Set(ref manualOffline, value)) return;
            UpdateOfflineMode(previous);
        }
    }
    public bool ShowNetworkNotice => automaticOffline && !manualOffline;
    public bool CanUseOnlineContent => IsIdle && !Offline;
    public bool CanUseOnlineAi => CanUseOnlineContent && AiEnabled;

    internal Task SaveManualOfflineAsync() => RunAsync(ct =>
        services.Store.SetSettingAsync("network.offline", ManualOffline.ToString(), ct));

    internal void ObserveConnectivity(bool available, DateTimeOffset now)
    {
        var previous = Offline;
        if (!available)
        {
            internetReturnedAt = null;
            if (automaticOffline) return;
            automaticOffline = true;
        }
        else
        {
            if (!automaticOffline) return;
            internetReturnedAt ??= now;
            if (now - internetReturnedAt < TimeSpan.FromSeconds(5)) return;
            automaticOffline = false;
            internetReturnedAt = null;
        }
        UpdateOfflineMode(previous);
    }

    internal async Task<bool> RefreshConnectivityAsync(CancellationToken ct)
    {
        await connectivityCheck.WaitAsync(ct);
        try
        {
            var available = await services.Connectivity.IsAvailableAsync(ct);
            ObserveConnectivity(available, DateTimeOffset.UtcNow);
            return available;
        }
        finally { connectivityCheck.Release(); }
    }

    private void UpdateOfflineMode(bool previous)
    {
        services.Network.Offline = Offline;
        Changed(nameof(ShowNetworkNotice));
        if (previous == Offline) return;
        if (Offline)
        {
            CancelLauncherUpdateCheck();
            CancelCatalogRefresh();
            AiConsent = false;
            AiConnectionConsent = false;
            InvalidateTranslationConsent();
        }
        foreach (var name in new[] { nameof(Offline), nameof(ModeText), nameof(CatalogSearchStatus),
            nameof(CanUseOnlineCatalog), nameof(CanAddCatalogVersion), nameof(CanUseOnlineContent), nameof(CanUseOnlineAi), nameof(WorkspacePackCatalogOffline) })
            Changed(name);
        RefreshAiText();
    }
}
