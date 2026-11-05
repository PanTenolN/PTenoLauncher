using PTenoLauncher.Core;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Updates;
namespace PTenoLauncher.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private readonly ILauncherUpdateWorkflow launcherUpdates;
    private LauncherUpdateOffer? launcherOffer;
    private string launcherUpdateCode = "UpdaterCurrent";
    private bool autoLauncherUpdates = true, previewLauncherUpdates, launcherRecoveryRequired, launcherLater, launcherChecking;
    private CancellationTokenSource? launcherCheck;
    private long launcherReceived, launcherTotal;
    public event EventHandler? LauncherUpdateOffered;
    public event EventHandler? LauncherRestartReady;
    public bool LauncherHandoffReady { get; private set; }
    public bool AutoLauncherUpdates { get => autoLauncherUpdates; set => Set(ref autoLauncherUpdates, value); }
    public bool PreviewLauncherUpdates { get => previewLauncherUpdates; set => Set(ref previewLauncherUpdates, value); }
    public bool LauncherRecoveryRequired { get => launcherRecoveryRequired; private set => Set(ref launcherRecoveryRequired, value); }
    public bool CanInstallLauncherUpdate => IsIdle && (launcherOffer is not null || LauncherRecoveryRequired);
    public string LauncherUpdateStatus => TextCatalog.Get(launcherUpdateCode);
    public string LauncherUpdateButtonText => TextCatalog.Get(LauncherRecoveryRequired ? "UpdaterRecover" : "UpdaterUpdate");
    public string LauncherUpdateSummary => launcherOffer is { } offer
        ? TextCatalog.Format("UpdaterSummary", offer.Current.Version, offer.Current.Sequence, offer.Release.Build.Version, offer.Release.Build.Sequence, offer.Package.Size / 1048576.0)
        : LauncherRecoveryRequired ? TextCatalog.Get("UpdaterRecoveryHint") : "";
    public string LauncherUpdateChanges => launcherOffer?.Release.Changes ?? "";
    public double LauncherUpdateProgress => launcherTotal > 0 ? Math.Clamp(100.0 * launcherReceived / launcherTotal, 0, 100) : 0;
    public string LauncherUpdateProgressText => launcherTotal > 0 ? TextCatalog.Format("UpdaterProgress", launcherReceived / 1048576.0, launcherTotal / 1048576.0) : "";
    private void SetLauncherUpdateStatus(string code)
    { launcherUpdateCode = code; Changed(nameof(LauncherUpdateStatus)); }
    private void RefreshLauncherUpdateTexts()
    { foreach (var name in new[] { nameof(LauncherUpdateStatus), nameof(LauncherUpdateButtonText), nameof(LauncherUpdateSummary), nameof(LauncherUpdateChanges), nameof(LauncherUpdateProgressText), nameof(CanInstallLauncherUpdate) }) Changed(name); }
    private async Task InitializeLauncherUpdates(CancellationToken ct)
    {
        AutoLauncherUpdates = await services.Store.GetSettingAsync("updates.auto", ct) is not "false";
        PreviewLauncherUpdates = await services.Store.GetSettingAsync("updates.preview", ct) == "true";
        await RefreshLauncherRecovery(ct);
    }
    private async Task RefreshLauncherRecovery(CancellationToken ct)
    {
        try { LauncherRecoveryRequired = await launcherUpdates.PendingAsync(ct) is { State: not ("Committed" or "RolledBack") }; }
        catch (Exception e) when (e is not OperationCanceledException) { services.Log.Error("updater.recovery.check", e); LauncherRecoveryRequired = true; }
        if (LauncherRecoveryRequired) SetLauncherUpdateStatus("UpdaterRecovery"); RefreshLauncherUpdateTexts();
    }
    public Task RefreshLauncherRecoveryAsync() => RefreshLauncherRecovery(CancellationToken.None);
    public Task SaveLauncherUpdateSettingsAsync() => RunAsync(async ct =>
    {
        await services.Store.SetSettingAsync("updates.auto", AutoLauncherUpdates ? "true" : "false", ct);
        await services.Store.SetSettingAsync("updates.preview", PreviewLauncherUpdates ? "true" : "false", ct);
        Status = TextCatalog.Get("Message010");
    });
    public async Task CheckLauncherUpdatesAsync(bool automatic = false)
    {
        if (!IsInitialized || NeedsLanguageSelection || launcherChecking || automatic && !AutoLauncherUpdates) return;
        if (launcherLater) { SetLauncherUpdateStatus("UpdaterDeferred"); return; }
        if (LauncherRecoveryRequired) { LauncherUpdateOffered?.Invoke(this, EventArgs.Empty); return; }
        launcherChecking = true; using var cancel = new CancellationTokenSource(); launcherCheck = cancel;
        try
        {
            var result = await launcherUpdates.CheckAsync(PreviewLauncherUpdates, cancel.Token);
            launcherOffer = result.Offer; SetLauncherUpdateStatus(result.State); RefreshLauncherUpdateTexts();
            if (launcherOffer is not null && !launcherLater) LauncherUpdateOffered?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { SetLauncherUpdateStatus(Offline ? "UpdaterOffline" : "Message020"); }
        catch (Exception e) { services.Log.Error("updater.check", e); SetLauncherUpdateStatus("UpdaterUnavailable"); }
        finally { launcherCheck = null; launcherChecking = false; }
    }
    public void DeferLauncherUpdate() { launcherLater = true; SetLauncherUpdateStatus("UpdaterDeferred"); }
    public void CancelLauncherUpdateCheck() { launcherCheck?.Cancel(); }
    public async Task ApplyLauncherUpdateAsync()
    {
        if (IsBusy || IsGameLaunchActive) { SetLauncherUpdateStatus("UpdaterBusy"); return; }
        bool handoff = false; string failure = "UpdaterHelper";
        await RunAsync(async ct =>
        {
          try
          {
            SetLauncherUpdateStatus("UpdaterPreparing"); launcherReceived = launcherTotal = 0; RefreshLauncherUpdateTexts();
            if (LauncherRecoveryRequired) await launcherUpdates.RecoverAsync(ct);
            else
            {
                var offer = launcherOffer ?? throw new LauncherUpdateException("UpdaterInvalid");
                var progress = new Progress<DownloadProgress>(p => { launcherReceived = p.Received; launcherTotal = p.Total ?? 0; Changed(nameof(LauncherUpdateProgress)); Changed(nameof(LauncherUpdateProgressText)); });
                await launcherUpdates.InstallAsync(offer, progress, ct);
            }
            SetLauncherUpdateStatus("UpdaterRestarting"); handoff = true;
          }
          catch (LauncherUpdateException e) { failure = e.Code; throw; }
          catch (OperationCanceledException) { failure = "Message020"; throw; }
          catch (System.IO.InvalidDataException) { failure = "UpdaterIntegrity"; throw; }
        }, allowUpdateRecovery: true);
        if (handoff) { LauncherHandoffReady = true; LauncherRestartReady?.Invoke(this, EventArgs.Empty); }
        else
        {
            await RefreshLauncherRecovery(CancellationToken.None);
            SetLauncherUpdateStatus(failure); Status = TextCatalog.Get(failure);
        }
    }
}
