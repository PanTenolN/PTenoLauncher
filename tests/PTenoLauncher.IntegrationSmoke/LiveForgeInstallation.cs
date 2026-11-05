using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.IntegrationSmoke;

internal static class LiveForgeInstallation
{
    public static async Task RunAsync(LauncherPaths paths, string[] args)
    {
        if (args.Length != 5 || !Enum.TryParse<ModLoader>(args[2], out var loader) || loader is not (ModLoader.Forge or ModLoader.NeoForge))
            throw new ArgumentException("Expected <new-root> --forge-install Forge|NeoForge <exact-loader-version> <minecraft-version>.");
        if (Directory.EnumerateDirectories(SafePath.Join(paths.Root, "instances")).Any() || Directory.EnumerateFileSystemEntries(paths.Cache).Any())
            throw new InvalidOperationException("Use an empty isolated root. This opt-in check executes verified official client processor JARs, never Minecraft.");
        await AtomicFile.WriteAsync(SafePath.Join(paths.Root, "forge-install-fixture.txt"), "Isolated official component installation; no account or gameplay"u8.ToArray());
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        using var http = NetworkPolicy.CreateHttpClient(); var network = new NetworkPolicy(); var downloads = new VerifiedDownloader(http, network);
        var store = new SqliteLauncherStore(paths); await store.InitializeAsync(timeout.Token);
        var profile = new GameProfile(Guid.NewGuid(), "Official isolated loader installation", args[4], loader, 2048, null, DateTimeOffset.UtcNow);
        await store.SaveProfileAsync(profile, timeout.Token);
        var catalog = new ForgeCatalog(paths, downloads, network); var versions = new VersionManager(paths, downloads); var java = new JavaManager();
        var runtime = new JavaRuntimeManager(paths, java, versions, downloads, network);
        var vanilla = new VanillaInstaller(paths, versions, downloads, store); var preparation = new ForgeComponentPreparer(paths, catalog, downloads, store);
        var installer = new ForgeInstaller(paths, preparation, vanilla, runtime, java, store);
        var stage = "official-catalog"; bool success = false;
        try
        {
            var available = await catalog.GetVersionsAsync(loader, profile.MinecraftVersion, ct: timeout.Token);
            if (!available.Any(v => v.Version == args[3])) throw new InvalidDataException("Exact selected version is absent from the official catalog.");
            stage = "minecraft-metadata"; await versions.RefreshAsync(timeout.Token);
            stage = "official-installation"; var receipt = await installer.InstallAsync(profile, args[3], ct: timeout.Token);
            ReceiptValidator.Validate(paths, receipt); success = true;
            await AtomicFile.WriteJsonAsync(SafePath.Join(paths.Root, "forge-install-result.json"), new {
                result = "PASS", loader = loader.ToString(), requestedVersion = args[3], minecraft = args[4], files = receipt.Files.Count,
                fmlCoordinate = receipt.Forge?.FmlCoordinate, installationComplete = true, gameplayVerified = false, authenticationBypassed = false
            });
        }
        catch (Exception e)
        {
            await AtomicFile.WriteJsonAsync(SafePath.Join(paths.Root, "forge-install-result.json"), new {
                result = "FAILED", stage, loader = loader.ToString(), requestedVersion = args[3], minecraft = args[4],
                errorType = e.GetType().Name, error = e.Message, installationComplete = false, gameplayVerified = false, authenticationBypassed = false
            });
            throw;
        }
        finally { Console.WriteLine("Official installation check: " + (success ? "PASS" : "FAILED") + "; gameplay NOT RUN; see forge-install-result.json"); }
    }
}
