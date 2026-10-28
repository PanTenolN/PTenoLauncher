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

namespace PTenoLauncher.Desktop;

public sealed class LauncherServices : IDisposable
{
    public LauncherPaths Paths { get; }
    public ILauncherStore Store { get; }
    public NetworkPolicy Network { get; } = new();
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
    public GameLauncher Launcher { get; }
    public ModrinthClient Modrinth { get; }
    public RemoteModInstaller RemoteMods { get; }
    public LocalModManager Mods { get; }
    public BackupManager Backups { get; }
    public PTenoLauncher.Infrastructure.Settings.PreferencesService Preferences { get; }
    public PTenoLauncher.Infrastructure.AI.AiPolicy Ai { get; } = new();
    public ProfileUsage Usage { get; }
    public ProfileDeletionService Deletion { get; }
    public OpenAiClient OpenAi { get; }
    public TranslationWorkspace Translations { get; }
    public SecretVault AiSecrets { get; }
    private readonly HttpClient aiHttp;
    private readonly HttpClient http = NetworkPolicy.CreateHttpClient();
    public LauncherServices(LauncherPaths paths)
    {
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
        Launcher = new(paths, Readiness, Authentication, Network, Store, Log);
        Modrinth = new(paths, downloads, Network); RemoteMods = new(paths, Store, Modrinth, downloads, Mods); Backups = new(paths, Store);
        Preferences = new(Store); Usage = new(paths); Deletion = new(paths, Store, Usage);
        AiSecrets = new(paths);
        aiHttp = new(new AiRequestGateHandler(Ai, new HttpClientHandler { AllowAutoRedirect = false })) { Timeout = TimeSpan.FromMinutes(2) };
        OpenAi = new(aiHttp, Ai, Network, () => AiSecrets.Read("openai-api-key") is { } bytes ? Encoding.UTF8.GetString(bytes) : null);
        Translations = new(paths, Store, Usage, Ai, OpenAi);
    }
    public void Dispose() { Ai.SetEnabled(false); aiHttp.Dispose(); http.Dispose(); }
}
