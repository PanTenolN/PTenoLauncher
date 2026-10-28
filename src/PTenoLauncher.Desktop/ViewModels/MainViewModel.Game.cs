namespace PTenoLauncher.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public Guid? GameLaunchId { get; private set; }
    public bool IsGameLaunchActive => GameLaunchId is not null;
    public bool CanCancelOperation => IsBusy && !IsGameLaunchActive;

    private void SetGameLaunch(Guid? id)
    {
        GameLaunchId = id;
        Changed(nameof(GameLaunchId)); Changed(nameof(IsGameLaunchActive)); Changed(nameof(CanCancelOperation));
        CancelCommand.Refresh();
    }

    public async Task<bool> StopGameAsync(Guid launchId, bool confirmed)
    {
        // A confirmation for an old launch must never stop a newer operation.
        if (!confirmed || GameLaunchId != launchId || operation is null) return false;
        await operation.CancelAsync();
        return true;
    }
}
