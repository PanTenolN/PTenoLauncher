using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Updates;
public interface ILauncherUpdateWorkflow
{
    LauncherBuild Build { get; }
    Task<LauncherUpdateCheck> CheckAsync(bool previews, CancellationToken ct);
    Task<LauncherUpdateTransaction?> PendingAsync(CancellationToken ct);
    Task InstallAsync(LauncherUpdateOffer offer, IProgress<DownloadProgress> progress, CancellationToken ct);
    Task RecoverAsync(CancellationToken ct);
}
public sealed class LauncherUpdateWorkflow(LauncherPaths paths, LauncherUpdater updater, LauncherUpdateTrust trust, string applicationRoot) : ILauncherUpdateWorkflow
{
    public LauncherBuild Build => updater.Build;
    private string Mode => File.Exists(SafePath.Join(applicationRoot, "portable.flag")) ? "Portable" : "Installed";
    public Task<LauncherUpdateCheck> CheckAsync(bool previews, CancellationToken ct) => updater.CheckAsync(Mode, previews, ct);
    public Task<LauncherUpdateTransaction?> PendingAsync(CancellationToken ct) => LauncherUpdateRuntime.PendingAsync(paths, applicationRoot, trust, ct);
    public async Task InstallAsync(LauncherUpdateOffer offer, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        using var profiles = await LauncherUpdateGuards.ProfileLeases(paths, ct);
        if (await PendingAsync(ct) is not null) throw new LauncherUpdateException("UpdaterRecovery");
        var prepared = await updater.PrepareAsync(offer, applicationRoot, confirmed: true, progress, ct);
        await LauncherUpdateRuntime.StartHelperAsync(paths, trust, prepared.Id, applicationRoot, rollback: false, ct);
    }
    public async Task RecoverAsync(CancellationToken ct)
    {
        using var profiles = await LauncherUpdateGuards.ProfileLeases(paths, ct);
        var pending = await PendingAsync(ct) ?? throw new LauncherUpdateException("UpdaterRecovery");
        if (pending.State == "Committed") throw new LauncherUpdateException("UpdaterRecovery");
        await LauncherUpdateRuntime.StartHelperAsync(paths, trust, pending.Id, applicationRoot, rollback: true, ct);
    }
}
