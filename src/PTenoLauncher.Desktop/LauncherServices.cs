using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Authentication;
using PTenoLauncher.Infrastructure.Diagnostics;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Security;
using PTenoLauncher.Infrastructure.Storage;
using PTenoLauncher.Infrastructure.AI;
using System.Text;
using PTenoLauncher.Infrastructure.Updates;

namespace PTenoLauncher.Desktop;

public sealed class LauncherServices : IDisposable
{
    public LauncherPaths Paths { get; }
    public SqliteLauncherStore Store { get; }
    public NetworkPolicy Network { get; } = new();
    public InternetConnectivity Connectivity { get; }
    public LauncherLog Log { get; }
    public VersionManager Versions { get; }
    public MicrosoftAuthentication Authentication { get; }
    public SkinCache Skins { get; }
    public JavaManager Java { get; } = new();
    public JavaRuntimeManager Runtimes { get; }
    public OfflineReadiness Readiness { get; }
    public VanillaInstaller Installer { get; }
    public QuiltInstaller Quilt { get; }
    public FabricInstaller Fabric { get; }
    public ForgeCatalog ForgeVersions { get; }
    public ForgeComponentPreparer ForgeComponents { get; }
    public ForgeInstaller Forge { get; }
    public GameLauncher Launcher { get; }
    public ModrinthClient Modrinth { get; }
    public RemoteModInstaller RemoteMods { get; }
    public ModUpdateManager ModUpdates { get; }
    public PTenoLauncher.Infrastructure.Modpacks.MrpackImporter Modpacks { get; }
    public PTenoLauncher.Infrastructure.Modpacks.PackExporter PackExporter { get; }
    public ILauncherUpdateWorkflow LauncherUpdates { get; }
    public LocalModManager Mods { get; }
    public BackupManager Backups { get; }
    public PTenoLauncher.Infrastructure.Settings.PreferencesService Preferences { get; }
    public PTenoLauncher.Infrastructure.AI.AiPolicy Ai { get; } = new();
    public ProfileUsage Usage { get; }
    public ProfileContent Content { get; }
    public WorldManager Worlds { get; }
    public ProfileScreenshots Screenshots { get; }
    public StorageUsageReport StorageUsage { get; }
    public VanillaServerManager Servers { get; }
    public ProfileDeletionService Deletion { get; }
    public ProfileFolderMover ProfileFolders { get; }
    public ProfileFileRenamer ProfileFiles { get; }
    public OpenAiClient OpenAi { get; }
    public TranslationWorkspace Translations { get; }
    public SecretVault AiSecrets { get; }
    private readonly HttpClient aiHttp;
    private readonly HttpClient http;
    public LauncherServices(LauncherPaths paths, HttpMessageHandler? aiTransport = null, Func<string?>? aiKeyReader = null,
        HttpMessageHandler? coreTransport = null, HttpMessageHandler? connectivityTransport = null)
    {
        http = coreTransport is null ? NetworkPolicy.CreateHttpClient() : new(coreTransport) { Timeout = TimeSpan.FromMinutes(10) };
        Connectivity = new(connectivityTransport);
        Paths = paths; Paths.Initialize();
        Store = new SqliteLauncherStore(paths); Log = new(paths);
        var downloads = new VerifiedDownloader(http, Network);
        Versions = new(paths, downloads);
        Runtimes = new(paths, Java, Versions, downloads, Network);
        Authentication = new(new SecretVault(paths), http, Network); Skins = new(paths, downloads);
        Mods = new(paths, Store);
        Readiness = new(paths, Java, Authentication, Mods);
        Installer = new(paths, Versions, downloads, Store);
        Fabric = new(paths, downloads, Network, Installer, Store);
        Quilt = new(paths, downloads, Network, Installer, Store);
        ForgeVersions = new(paths, downloads, Network);
        ForgeComponents = new(paths, ForgeVersions, downloads, Store);
        Forge = new(paths, ForgeComponents, Installer, Runtimes, Java, Store);
        Modrinth = new(paths, downloads, Network); RemoteMods = new(paths, Store, Modrinth, downloads, Mods); Backups = new(paths, Store);
        ModUpdates = new(paths, Store, Modrinth, downloads, Mods);
        Launcher = new(paths, Readiness, Authentication, Network, Store, Log, ModUpdates);
        Modpacks = new(paths, Store, downloads, Network);
        PackExporter = new(paths, Store, Modrinth);
        LauncherUpdates = new LauncherUpdateWorkflow(paths, new(paths, downloads, Network, LauncherUpdateTrust.Production, LauncherBuild.Current), LauncherUpdateTrust.Production, AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        Preferences = new(Store); Usage = new(paths); Deletion = new(paths, Store, Usage); Content = new(paths, Backups);
        Worlds = new(paths, Store, Content, Backups);
        Screenshots = new(paths);
        StorageUsage = new(paths, Store, Backups);
        Servers = new(paths, Versions, downloads, Runtimes, Store);
        ProfileFolders = new(paths, Store, Usage);
        ProfileFiles = new(paths, Store, Mods, Content);
        AiSecrets = new(paths);
        aiHttp = new(new AiRequestGateHandler(Ai, aiTransport ?? new HttpClientHandler { AllowAutoRedirect = false })) { Timeout = TimeSpan.FromMinutes(2) };
        OpenAi = new(aiHttp, Ai, Network, aiKeyReader ?? (() => AiSecrets.Read("openai-api-key") is { } bytes ? Encoding.UTF8.GetString(bytes) : null));
        Translations = new(paths, Store, Usage, Ai, OpenAi);
    }
    public void Dispose() { Ai.SetEnabled(false); Connectivity.Dispose(); aiHttp.Dispose(); http.Dispose(); }
}
