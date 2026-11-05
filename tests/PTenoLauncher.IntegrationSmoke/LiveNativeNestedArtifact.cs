using System.Net.Sockets;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.IntegrationSmoke;

internal static class LiveNativeNestedArtifact
{
    public static async Task RunAsync(LauncherPaths paths, string[] args)
    {
        if (args.Length != 5 || !Enum.TryParse<ModLoader>(args[2], out var loader) || loader is not (ModLoader.Quilt or ModLoader.Forge or ModLoader.NeoForge))
            throw new ArgumentException("Expected <fresh-root> --native-nested-artifact Quilt|Forge|NeoForge <Modrinth-project> <minecraft-version>.");
        if (Directory.EnumerateDirectories(SafePath.Join(paths.Root, "instances")).Any() || Directory.EnumerateFileSystemEntries(paths.Cache).Any())
            throw new InvalidOperationException("Use an empty isolated data root for this public, unpaid, read-only artifact check.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var http = NetworkPolicy.CreateHttpClient(); var network = new NetworkPolicy(); var downloads = new VerifiedDownloader(http, network);
        var catalog = new ModrinthClient(paths, downloads, network); var store = new SqliteLauncherStore(paths); await store.InitializeAsync(timeout.Token);
        var inspector = new LocalModManager(paths, store); string stage = "project-metadata";
        try
        {
            var project = await catalog.GetProjectAsync(args[3], timeout.Token); stage = "version-metadata";
            var versions = await catalog.GetVersionsAsync(project.Id, args[4], loader, timeout.Token);
            var version = versions.FirstOrDefault() ?? throw new InvalidDataException("No published compatible artifact in this filtered catalog.");
            var file = SafePath.Join(paths.Cache, "native-artifacts/" + version.File.Sha512 + ".jar"); stage = "artifact-download";
            await downloads.DownloadAsync(version.File.Url, file, version.File.Sha512, version.File.Size, "SHA512", ct: timeout.Token, source: DownloadSource.Modrinth);
            stage = "static-inspection"; var inspection = await inspector.InspectAsync(file, timeout.Token);
            network.Offline = true; var repeated = await inspector.InspectAsync(file, timeout.Token);
            if (inspection.Sha256 != repeated.Sha256) throw new InvalidDataException("Static repeat changed artifact hash.");
            var profile = new GameProfile(Guid.NewGuid(), "Isolated native artifact inspection", args[4], loader, 2048, null, DateTimeOffset.UtcNow);
            // No invented loader receipt or Java/FML runtime versions: missing context stays Unknown.
            var selection = NativeNestedResolver.Analyze(profile, null, [inspection], timeout.Token);
            await AtomicFile.WriteJsonAsync(SafePath.Join(paths.Root, "native-artifact-result.json"), new {
                result = "STATIC_INSPECTION_PASS", loader = loader.ToString(), requestedProject = args[3], minecraft = args[4], projectId = project.Id,
                versionId = version.Id, artifact = version.File.Name, sha512 = version.File.Sha512, sha256 = inspection.Sha256,
                nestedDescriptors = inspection.Nested?.Candidates.Count ?? 0, staticSelectionConfirmed = selection.Confirmed,
                selection.Issues, selection.Artifacts, offlineRepeat = true, jarExecuted = false, gameplayVerified = false, authenticationBypassed = false
            });
        }
        catch (Exception e)
        {
            await AtomicFile.WriteJsonAsync(SafePath.Join(paths.Root, "native-artifact-result.json"), new {
                result = "FAILED", stage, loader = loader.ToString(), requestedProject = args[3], minecraft = args[4], errorType = e.GetType().Name,
                error = e.Message, socketCode = e.GetBaseException() is SocketException socket ? socket.NativeErrorCode : (int?)null,
                jarExecuted = false, gameplayVerified = false, authenticationBypassed = false
            }); throw;
        }
        finally { Console.WriteLine("See native-artifact-result.json. No JAR or Minecraft process was executed."); }
    }
}
