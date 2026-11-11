using System.Globalization;
using System.Net.Sockets;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Infrastructure.Minecraft;

namespace PTenoLauncher.Desktop.ViewModels;

public sealed record ServerCoreOption(ServerCore Core)
{
    public string Label => Core + " · " + TextCatalog.Get("ServerCore" + Core);
}

public sealed record ServerWorldOption(string? Name)
{
    public string Label => Name ?? TextCatalog.Get("ServerNewWorld");
}

public sealed partial class MainViewModel
{
    private LocalVanillaServer? selectedServer;
    private IReadOnlyList<LocalVanillaServer> localServers = [];
    private IReadOnlyList<VersionEntry> serverVersions = [];
    private VersionEntry? newServerVersion;
    private string newServerMemory = "2048";
    private ServerCoreOption newServerCore = new(ServerCore.Vanilla);
    private ServerCore? loadedServerCore;
    private string? loadedBuildVersion;
    private IReadOnlyList<string> serverBuilds = [];
    private string? newServerBuild;
    private bool serverFromProfile, newServerPrivate = true;
    private GameProfile? serverSourceProfile;
    private IReadOnlyList<ServerWorldOption> serverWorlds = [new(null)];
    private ServerWorldOption? serverWorld;
    private string newServerName = "", newServerPort = "25565", serverStatus = "";
    private string serverCatalogStatus = "", serverListStatus = "";
    private readonly Dictionary<Guid, LocalServerCard> serverCards = [];
    private CancellationTokenSource? serverAction;
    private CancellationTokenSource? serverCatalogRequest;
    private bool showServerEula;
    public IReadOnlyList<LocalVanillaServer> LocalServers { get => localServers; private set => Set(ref localServers, value); }
    public IReadOnlyList<VersionEntry> ServerVersions { get => serverVersions; private set { if (Set(ref serverVersions, value)) Changed(nameof(HasServerVersions)); } }
    public bool HasServerVersions => ServerVersions.Count != 0;
    public VersionEntry? NewServerVersion { get => newServerVersion; set { if (Set(ref newServerVersion, value)) Changed(nameof(CanCreateServer)); } }
    public string NewServerName { get => newServerName; set => Set(ref newServerName, value ?? ""); }
    public string NewServerPort { get => newServerPort; set => Set(ref newServerPort, value); }
    public string NewServerMemory { get => newServerMemory; set => Set(ref newServerMemory, value ?? ""); }
    public IReadOnlyList<ServerCoreOption> ServerCores { get; } = Enum.GetValues<ServerCore>().Select(core => new ServerCoreOption(core)).ToArray();
    public ServerCoreOption NewServerCore
    {
        get => newServerCore;
        set
        {
            if (value is null || !Set(ref newServerCore, value)) return;
            Changed(nameof(ShowServerBuilds));
            Changed(nameof(CanCreateServer));
        }
    }
    public IReadOnlyList<string> ServerBuilds { get => serverBuilds; private set { if (Set(ref serverBuilds, value)) Changed(nameof(HasServerBuilds)); } }
    public bool ShowServerBuilds => NewServerCore.Core != ServerCore.Vanilla;
    public bool HasServerBuilds => ServerBuilds.Count != 0;
    public string? NewServerBuild { get => newServerBuild; set { if (Set(ref newServerBuild, value)) Changed(nameof(CanCreateServer)); } }
    public bool ServerFromProfile { get => serverFromProfile; set { if (Set(ref serverFromProfile, value)) Changed(nameof(CanCreateServer)); } }
    public bool NewServerPrivate { get => newServerPrivate; set => Set(ref newServerPrivate, value); }
    public GameProfile? ServerSourceProfile { get => serverSourceProfile; set { if (Set(ref serverSourceProfile, value)) Changed(nameof(CanCreateServer)); } }
    public IReadOnlyList<ServerWorldOption> ServerWorlds { get => serverWorlds; private set => Set(ref serverWorlds, value); }
    public ServerWorldOption? ServerWorld { get => serverWorld; set => Set(ref serverWorld, value); }
    public IReadOnlyList<LocalServerCard> ServerCards { get; private set; } = [];
    public string ServerListStatus => TextCatalog.Get(serverListStatus);
    public bool HasServerListStatus => serverListStatus.Length > 0;
    public string ServerStatus { get => TextCatalog.Get(serverStatus); private set => Set(ref serverStatus, value); }
    public string ServerCatalogStatus
    {
        get => TextCatalog.Get(serverCatalogStatus);
        private set { if (Set(ref serverCatalogStatus, value)) Changed(nameof(HasServerCatalogStatus)); }
    }
    public bool HasServerCatalogStatus => serverCatalogStatus.Length != 0;
    public bool ShowServerEula { get => showServerEula; private set => Set(ref showServerEula, value); }

    public LocalVanillaServer? SelectedServer
    {
        get => selectedServer;
        set
        {
            Set(ref selectedServer, value);
        }
    }

    public bool ServerOperationActive => serverAction is not null || serverCards.Values.Any(card => card.IsActive);
    public bool CanEditServer => serverAction is null && !LauncherRecoveryRequired;
    public bool CanCreateServer => CanEditServer && (ServerFromProfile ? ServerSourceProfile is not null :
        serverCatalogRequest is null && loadedServerCore == NewServerCore.Core && NewServerVersion is not null &&
        (!ShowServerBuilds || loadedBuildVersion == NewServerVersion.Id && NewServerBuild is not null));
    internal bool ServerCatalogLoading => serverCatalogRequest is not null;

    private void RefreshServerControls()
    {
        Changed(nameof(CanEditServer));
        Changed(nameof(CanCreateServer));
        Changed(nameof(ServerOperationActive));
        Changed(nameof(CanInstallLauncherUpdate));
    }

    internal async Task LoadServersAsync()
    {
        await RunServerActionAsync(async ct =>
        {
            ShowServerEula = !await ServerManager.HasSharedEulaAsync(ct);
            await ReadServerListAsync(SelectedServer?.Id, ct);
        });
        if (!ServerCatalogLoading) await LoadServerChoicesAsync(refresh: false);
    }

    internal Task RefreshManagedServersAsync(Guid selectedId) => RunServerActionAsync(ct => ReadServerListAsync(selectedId, ct));

    internal void ShowServerDeleted(string? backup) =>
        ServerStatus = backup is null ? "ServerDeleted" : TextCatalog.Format("ServerDeletedBackup", backup);

    private async Task ReadServerListAsync(Guid? selectedId, CancellationToken ct)
    {
        try
        {
            LocalServers = await ServerManager.ListAsync(ct);
            foreach (var server in LocalServers)
            {
                if (!serverCards.TryGetValue(server.Id, out var card)) serverCards[server.Id] = card = new(server);
                if (card.IsActive) continue;
                card.Server = server;
                card.SetState(await ServerManager.IsInUseAsync(server, ct) ? "ServerInUse" : "ServerStopped");
            }
            foreach (var id in serverCards.Keys.Where(id => !serverCards[id].IsActive && LocalServers.All(server => server.Id != id)).ToArray())
                serverCards.Remove(id);
            ServerCards = LocalServers.Select(server => serverCards[server.Id]).ToArray();
            Changed(nameof(ServerCards));
            SelectedServer = LocalServers.FirstOrDefault(server => server.Id == selectedId) ?? LocalServers.FirstOrDefault();
            serverListStatus = LocalServers.Count == 0 ? "ServerListEmpty" : "";
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            services.Log.Error("server.list", error);
            serverListStatus = "ServerListReadFailed";
        }
        Changed(nameof(ServerListStatus));
        Changed(nameof(HasServerListStatus));
    }

    internal Task ReloadServerChoicesAsync(bool refresh = false) => !refresh && loadedServerCore == NewServerCore.Core
        ? Task.CompletedTask : LoadServerChoicesAsync(refresh);
    internal Task ReloadServerBuildsAsync() => !ShowServerBuilds || loadedServerCore != NewServerCore.Core ||
        NewServerVersion is not { } version || loadedBuildVersion == version.Id
        ? Task.CompletedTask : LoadServerChoicesAsync(refresh: false, version);

    private async Task LoadServerChoicesAsync(bool refresh, VersionEntry? buildFor = null)
    {
        var core = NewServerCore.Core;
        var prior = serverCatalogRequest;
        using var request = new CancellationTokenSource();
        serverCatalogRequest = request;
        prior?.Cancel();
        var ct = request.Token;
        bool IsCurrent() => ReferenceEquals(serverCatalogRequest, request) && NewServerCore.Core == core;

        var versionId = NewServerVersion?.Id;
        var previousBuild = loadedServerCore == core ? NewServerBuild : null;
        if (buildFor is null)
        {
            loadedServerCore = null;
            ServerVersions = [];
            NewServerVersion = null;
        }
        loadedBuildVersion = null;
        ServerBuilds = [];
        NewServerBuild = null;
        ServerCatalogStatus = "EditorCatalogLoading";
        Changed(nameof(CanCreateServer));
        try
        {
            if (buildFor is null)
            {
                var choices = await ServerManager.VersionsAsync(core, refresh && !Offline, ct, refreshIfEmpty: !Offline);
                if (!IsCurrent()) return;
                ServerVersions = choices;
                NewServerVersion = choices.FirstOrDefault(version => version.Id == versionId) ?? choices.FirstOrDefault();
                loadedServerCore = core;
                buildFor = NewServerVersion;
            }
            if (buildFor is null)
            {
                ServerCatalogStatus = Offline && core is ServerCore.Vanilla or ServerCore.Fabric or ServerCore.Forge or ServerCore.Paper or ServerCore.Purpur
                    ? "ServerCatalogOffline" : "ServerBuildUnavailable";
                return;
            }
            if (core == ServerCore.Vanilla)
            {
                ServerCatalogStatus = "";
                return;
            }
            var builds = await ServerManager.BuildsAsync(core, buildFor.Id, ct);
            if (!IsCurrent() || NewServerVersion?.Id != buildFor.Id) return;
            ServerBuilds = builds;
            NewServerBuild = builds.Contains(previousBuild) ? previousBuild : builds.FirstOrDefault();
            loadedBuildVersion = buildFor.Id;
            ServerCatalogStatus = HasServerBuilds ? "" : "ServerBuildUnavailable";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!IsCurrent()) return;
            services.Log.Error("server.catalog", error);
            ServerCatalogStatus = Offline || error.InnerException is SocketException { SocketErrorCode: SocketError.NetworkDown or SocketError.NetworkUnreachable }
                ? "ServerCatalogOffline" : TextCatalog.Format("ServerCatalogLoadFailed", error.Message);
        }
        finally
        {
            if (ReferenceEquals(serverCatalogRequest, request))
            {
                serverCatalogRequest = null;
                Changed(nameof(CanCreateServer));
            }
        }
    }

    internal void RefreshServerWorlds()
    {
        IReadOnlyList<string> worlds = [];
        try { if (ServerSourceProfile is { } profile) worlds = ServerManager.WorldsFor(profile.Id); }
        catch (Exception error) { ServerStatus = TextCatalog.Get(error.Message); }
        ServerWorlds = [new(null), .. worlds.Select(world => new ServerWorldOption(world))];
        ServerWorld = ServerWorlds[0];
    }

    internal async Task<ServerProfilePlan?> PreviewServerProfileAsync()
    {
        ServerProfilePlan? plan = null;
        await RunServerActionAsync(async ct =>
        {
            _ = ServerInputs();
            if (ServerSourceProfile is not { } profile) throw new InvalidOperationException("ServerProfileUnavailable");
            plan = await ServerManager.PreviewProfileAsync(profile, ServerWorld?.Name, ct);
        });
        return plan;
    }

    private (int Port, int Memory) ServerInputs()
    {
        if (string.IsNullOrWhiteSpace(NewServerName) || !int.TryParse(NewServerPort, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
            port is < 1024 or > 65535 || !MemoryInput.TryNormalize(NewServerMemory, out var memory))
            throw new InvalidDataException("ServerInvalid");
        NewServerMemory = memory.ToString(CultureInfo.InvariantCulture);
        return (port, memory);
    }

    internal Task CreateServerAsync(ServerProfilePlan? plan = null, bool copyReviewedForgeMods = false) => RunServerActionAsync(async ct =>
    {
        var (port, memory) = ServerInputs();
        LocalVanillaServer server;
        if (plan is null)
        {
            if (ServerFromProfile || NewServerVersion is not { } version) throw new InvalidOperationException("ServerVersionUnavailable");
            server = await ServerManager.CreateAsync(NewServerName, version.Id, port, memory, NewServerCore.Core, NewServerBuild, ct, privateServer: NewServerPrivate);
        }
        else
        {
            if (!ServerFromProfile || ServerSourceProfile != plan.Profile || ServerWorld?.Name != plan.World)
                throw new InvalidOperationException("ContentChanged");
            server = await ServerManager.CreateFromProfileAsync(plan, NewServerName, port, memory, ct,
                privateServer: NewServerPrivate, copyReviewedForgeMods: copyReviewedForgeMods);
        }
        await ReadServerListAsync(server.Id, ct);
        NewServerName = "";
        if (ShowServerEula) ServerStatus = TextCatalog.Get("ServerEulaNeeded");
    });

    internal Task AcceptServerEulaAsync() => RunServerActionAsync(async ct =>
    {
        await ServerManager.AcceptSharedEulaAsync(ct);
        ShowServerEula = false;
        ServerStatus = TextCatalog.Get("ServerEulaSaved");
    });

    private async Task RunServerActionAsync(Func<CancellationToken, Task> action)
    {
        if (!CanEditServer) return;
        using var request = new CancellationTokenSource();
        serverAction = request;
        RefreshServerControls();
        try { await action(request.Token); }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { ServerStatus = "Message020"; }
        catch (Exception error)
        {
            services.Log.Error("server.action", error);
            ServerStatus = TextCatalog.Get(error.Message);
        }
        finally { serverAction = null; RefreshServerControls(); }
    }

    internal async Task StartServerAsync(LocalServerCard card)
    {
        if (!card.CanModify || LauncherRecoveryRequired) return;
        var server = card.Server;
        if (serverCards.Values.Any(other => other.IsActive && other.Server.Port == server.Port))
        {
            card.Status = "ServerPortBusy";
            return;
        }
        using var preparation = new CancellationTokenSource();
        card.Preparation = preparation;
        card.Console = "";
        card.Status = "";
        card.SetState("ServerPreparing");
        RefreshServerControls();
        try
        {
            var progress = new Progress<DownloadProgress>(item =>
            {
                if (card.IsPreparing) card.Status = item.Name;
            });
            var output = new Progress<string>(batch =>
            {
                var text = card.Console + batch + Environment.NewLine;
                card.Console = text.Length > 131072 ? text[^131072..] : text;
            });
            var launch = await ServerManager.StartAsync(server, progress, output, preparation.Token);
            card.Preparation = null;
            card.Session = launch;
            card.SetState("ServerPreparing");
            if (await launch.Ready)
            {
                card.SetState("ServerRunning");
                card.Status = "";
            }
            card.Status = TextCatalog.Format("GameExit", await launch.Completion);
        }
        catch (OperationCanceledException) when (preparation.IsCancellationRequested) { card.Status = "Message020"; }
        catch (Exception error)
        {
            services.Log.Error("server.run", error);
            card.Status = TextCatalog.Get(error.Message);
        }
        finally
        {
            card.Preparation = null;
            card.Session = null;
            card.SetState("ServerStopped");
            RefreshServerControls();
        }
    }

    internal async Task StopServerAsync(LocalServerCard card)
    {
        if (!card.CanSend || card.Session is not { } session) return;
        card.SetState("ServerStopping");
        try { await session.StopAsync(); }
        catch (Exception error)
        {
            if (card.Session != session) return;
            card.Status = error is TimeoutException ? "ServerStopTimeout" : TextCatalog.Get(error.Message);
            card.SetState("ServerRunning");
        }
    }

    internal async Task SendServerCommandAsync(LocalServerCard card)
    {
        if (!card.CanSend || card.Session is not { } session || !card.BeginSending()) return;
        var command = card.Command;
        try
        {
            await session.SendAsync(command);
            if (card.Command == command) card.Command = "";
            card.Status = "";
        }
        catch (Exception error) { if (card.Session == session) card.Status = TextCatalog.Get(error.Message); }
        finally { card.EndSending(); }
    }
}
