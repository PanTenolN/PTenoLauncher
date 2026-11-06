using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Mods;

namespace PTenoLauncher.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private WorkingConfiguration? workingSummary;
    public bool CanMarkWorking => IsIdle && !IsGameLaunchActive && WorkspaceProfileId == SelectedProfile?.Id &&
        SelectedRun?.WorkingCandidate is not null;
    public string WorkingSummary => workingSummary is { SavedAt: { } saved } snapshot && snapshot.ProfileId == WorkspaceProfileId
        ? TextCatalog.Format("WorkingSaved", saved.ToLocalTime())
        : TextCatalog.Get("WorkingNone");

    private async Task LoadWorkingSummaryAsync(Guid id)
    {
        try
        {
            var snapshot = await services.ModUpdates.ReadWorkingAsync(id);
            if (WorkspaceProfileId != id) return;
            workingSummary = snapshot;
            Changed(nameof(WorkingSummary));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            services.Log.Error("working.read", error);
        }
    }

    internal Task MarkWorkingAsync() => RunAsync(async ct =>
    {
        var profile = RequireProfile();
        if (WorkspaceProfileId != profile.Id || SelectedRun?.WorkingCandidate is not { } launch || IsGameLaunchActive)
            throw new ModUpdateException("UpdatesStale");
        await services.ModUpdates.MarkWorkingAsync(profile, launch, confirmed: true, ct);
        await LoadWorkingSummaryAsync(profile.Id);
        Status = WorkingSummary;
    });

    internal async Task<WorkingRestorePreview?> PreviewWorkingAsync(Guid id)
    {
        WorkingRestorePreview? preview = null;
        await RunAsync(async ct =>
        {
            if (WorkspaceProfileId != id || IsGameLaunchActive) throw new ModUpdateException("UpdatesStale");
            if (services.ModUpdates.HasPendingWorking(id))
            {
                await services.ModUpdates.RecoverWorkingAsync(id, ct);
                await RefreshWorkingProfile(id, ct);
            }
            preview = await services.ModUpdates.PreviewWorkingAsync(RequireProfile(), ct);
        });
        return LastOperationSucceeded ? preview : null;
    }

    internal Task SupplyWorkingAsync(Guid id, Guid snapshot, string relative, string? file, bool gameFile) => RunAsync(async ct =>
    {
        if (WorkspaceProfileId != id || IsGameLaunchActive || file is null && Offline) throw new ModUpdateException("UpdatesStale");
        if (gameFile && file is not null) await services.ModUpdates.SupplyWorkingGameFileAsync(id, snapshot, relative, file, ct);
        else await services.ModUpdates.SupplyWorkingFileAsync(id, snapshot, relative, file, ct);
    });

    internal Task ApplyWorkingAsync(WorkingRestorePreview preview) => RunAsync(async ct =>
    {
        if (WorkspaceProfileId != preview.Plan.ProfileId || !preview.CanApply || IsGameLaunchActive)
            throw new ModUpdateException("UpdatesStale");
        var id = preview.Plan.ProfileId;
        try { await services.ModUpdates.ApplyAsync(RequireProfile(), preview.Plan, confirmed: true, ct); }
        catch
        {
            if (services.ModUpdates.HasPendingWorking(id))
            {
                await services.ModUpdates.RecoverWorkingAsync(id, CancellationToken.None);
                await RefreshWorkingProfile(id, CancellationToken.None);
            }
            throw;
        }
        await RefreshWorkingProfile(id, CancellationToken.None);
        Status = TextCatalog.Get("WorkingRestored");
    });

    private async Task RefreshWorkingProfile(Guid id, CancellationToken ct)
    {
        workspaceResolvedLoader = null;
        readinessReport = null;
        await ReloadProfilesAsync(ct);
        await InvalidateWorkspaceModsAsync(id);
        Changed(nameof(WorkspaceVersion));
        Changed(nameof(Readiness));
    }
}
