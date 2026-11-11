using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Minecraft;

namespace PTenoLauncher.Desktop.ViewModels;

public sealed class LocalServerCard : ObservableObject
{
    private LocalVanillaServer server;
    private string state = "ServerStopped", status = "", console = "", command = "";
    private bool canSend;
    private bool sending;
    internal VanillaServerSession? Session { get; set; }
    internal CancellationTokenSource? Preparation { get; set; }

    internal LocalServerCard(LocalVanillaServer server) => this.server = server;

    public LocalVanillaServer Server
    {
        get => server;
        internal set
        {
            if (!Set(ref server, value)) return;
            Changed(nameof(CoreLabel));
        }
    }
    public string CoreLabel => Server.Core + (Server.Build is null ? "" : " " + Server.Build);
    public string State => TextCatalog.Get(state);
    public string Status { get => TextCatalog.Get(status); internal set => Set(ref status, value); }
    public string Console { get => console; internal set => Set(ref console, value); }
    public string Command { get => command; set => Set(ref command, value ?? ""); }
    public bool CanSend { get => canSend; private set => Set(ref canSend, value); }
    public bool IsActive => state is "ServerPreparing" or "ServerRunning" or "ServerStopping";
    public bool IsPreparing => state == "ServerPreparing" && Preparation is not null;
    public bool CanModify => !IsActive && state != "ServerInUse";
    public bool CanManagePlayers => CanModify || CanSend;
    internal bool BeginSending()
    {
        if (sending) return false;
        sending = true;
        return true;
    }
    internal void EndSending() => sending = false;

    internal void SetState(string value)
    {
        state = value;
        CanSend = state == "ServerRunning" && Session is { Completion.IsCompleted: false };
        Changed(nameof(State));
        Changed(nameof(IsActive));
        Changed(nameof(IsPreparing));
        Changed(nameof(CanModify));
        Changed(nameof(CanManagePlayers));
    }
}
