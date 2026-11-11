using System.Diagnostics;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Authentication;
using PTenoLauncher.Infrastructure.Diagnostics;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Security;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed class GameLauncher(LauncherPaths paths, OfflineReadiness readiness, MicrosoftAuthentication auth,
    NetworkPolicy network, ILauncherStore store, LauncherLog log, ModUpdateManager? modUpdates = null)
{
    public event Action<Guid, bool>? RunningChanged;
    public async Task<int> LaunchAsync(GameProfile profile, CancellationToken ct = default, IProgress<string>? console = null,
        Action? onStarted = null, Action<string>? onLogOpened = null, Func<CancellationToken, Task<bool>>? checkConnectivity = null,
        Action<WorkingConfiguration>? onWorkingLaunch = null)
    {
        using var profileLease = new ProfileUsage(paths).Acquire(profile.Id);
        new ProfileUsage(paths).CheckWorldLocks(profile.Id);
        var report = await readiness.CheckAsync(profile, ct);
        if (!report.Ready) throw new InvalidOperationException(report.Summary);
        var receipt = (await AtomicFile.ReadJsonAsync<InstallationReceipt>(paths.Receipt(profile.Id), ct))!;
        var (session, launchOffline) = await SelectSessionAsync(network, token => auth.SignInAsync(false, token), auth.GetCachedSession, checkConnectivity, ct);
        var nativeDir = NativeDirectory(paths, profile.Id, Guid.NewGuid());
        Directory.CreateDirectory(nativeDir);
        try
        {
            foreach (var archive in receipt.NativeArchives)
            {
                var staging = nativeDir + "-" + Guid.NewGuid().ToString("N");
                await SafeArchive.ExtractAsync(SafePath.Join(paths.Cache, archive), staging, ct);
                try
                {
                    foreach (var file in Directory.EnumerateFiles(staging, "*.dll", SearchOption.AllDirectories))
                        File.Copy(file, SafePath.Join(nativeDir, Path.GetFileName(file)), false);
                }
                finally { Directory.Delete(staging, true); }
            }
            var psi = CreateStartInfo(paths, profile, receipt, nativeDir, session, launchOffline);
            var activeFile = SafePath.Join(paths.Profile(profile.Id), "active-game.json");
            var gameLog = ReadableName.Free(SafePath.Join(paths.Profile(profile.Id), "launcher-logs"),
                ReadableName.Stem(profile.Name, "Minecraft", 64) + $" — {DateTime.UtcNow:yyyy-MM-dd HH-mm-ss}", ".log");
            using var working = onWorkingLaunch is null ? null : await TryCaptureWorkingLaunch(profile, receipt, ct);
            int code;
            try
            {
                code = await LiveProcessRunner.RunAsync(psi, gameLog, async process =>
                {
                    onLogOpened?.Invoke(gameLog);
                    RunningChanged?.Invoke(profile.Id, true);
                    onStarted?.Invoke();
                    if (working is not null) onWorkingLaunch?.Invoke(working.Configuration);
                    if (!process.HasExited)
                        await AtomicFile.WriteJsonAsync(activeFile, new ActiveGameRecord(process.Id, process.StartTime.ToUniversalTime().Ticks, profile.JavaPath!));
                    await store.AddHistoryAsync(profile.Id, "minecraft.started", receipt.Version, CancellationToken.None);
                }, console, [session.AccessToken], ct, formatMinecraftLog: true);
            }
            finally
            {
                RunningChanged?.Invoke(profile.Id, false);
                if (File.Exists(activeFile)) File.Delete(activeFile);
            }
            log.Write("minecraft.exit", $"Profile {profile.Id:N}, code {code}");
            await store.AddHistoryAsync(profile.Id, "minecraft.exited", code.ToString(), CancellationToken.None);
            return code;
        }
        finally { if (Directory.Exists(nativeDir)) Directory.Delete(nativeDir, true); }
    }

    internal static string NativeDirectory(LauncherPaths paths, Guid profileId, Guid launchId)
    {
        var root = paths.Profile(profileId);
        if (OperatingSystem.IsWindows() && !root.All(char.IsAscii))
        {
            // Some native loaders still need an ASCII path even after Java resolves it.
            root = paths.Cache;
            if (!root.All(char.IsAscii))
            {
                var temp = Path.GetTempPath();
                if (temp.All(char.IsAscii)) return SafePath.Join(temp, $"PTenoLauncher-natives-{launchId:N}");
            }
        }
        return SafePath.Join(root, $"natives-{launchId:N}");
    }

    internal static ProcessStartInfo CreateStartInfo(LauncherPaths paths, GameProfile profile, InstallationReceipt receipt,
        string nativeDir, MinecraftSession session, bool launchOffline)
    {
        var gameDirectory = paths.Game(profile.Id);
        // The Windows Java launcher can lose characters while reading the command line. The working
        // directory is passed separately and keeps its Unicode name.
        string JavaPath(string path) => OperatingSystem.IsWindows() && !path.All(char.IsAscii)
            ? Path.GetRelativePath(gameDirectory, path) : path;

        var values = new Dictionary<string, string>
        {
            ["auth_player_name"] = session.Account.Name, ["version_name"] = receipt.Forge?.VersionId ?? receipt.Version,
            ["game_directory"] = JavaPath(gameDirectory), ["assets_root"] = JavaPath(SafePath.Join(paths.Cache, "assets")),
            ["assets_index_name"] = receipt.AssetIndex, ["auth_uuid"] = session.Account.Uuid,
            ["auth_access_token"] = launchOffline ? "0" : session.AccessToken,
            ["user_type"] = "msa", ["version_type"] = receipt.VersionType, ["natives_directory"] = JavaPath(nativeDir),
            ["launcher_name"] = "PTenoLauncher", ["launcher_version"] = Updates.LauncherBuild.Current.Version,
            ["classpath"] = string.Join(Path.PathSeparator, receipt.Classpath.Select(f => JavaPath(SafePath.Join(paths.Cache, f)))),
            ["classpath_separator"] = Path.PathSeparator.ToString(), ["library_directory"] = JavaPath(paths.Cache),
            ["user_properties"] = "{}", ["clientid"] = "", ["auth_xuid"] = "",
            ["auth_session"] = launchOffline ? "0" : "token:" + session.AccessToken + ":" + session.Account.Uuid,
            ["game_assets"] = JavaPath(AssetManager.GameAssets(paths, profile.Id, receipt))
        };
        var start = new ProcessStartInfo(profile.JavaPath!) { WorkingDirectory = gameDirectory, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-Xms512M");
        start.ArgumentList.Add($"-Xmx{profile.MemoryMb}M");
        foreach (var arg in receipt.JvmArguments) start.ArgumentList.Add(LaunchArgumentExpansion.Expand(arg, values));
        start.ArgumentList.Add(receipt.MainClass);
        foreach (var arg in receipt.GameArguments) start.ArgumentList.Add(LaunchArgumentExpansion.Expand(arg, values));
        return start;
    }

    private async Task<WorkingLaunchCapture?> TryCaptureWorkingLaunch(GameProfile profile, InstallationReceipt receipt, CancellationToken ct)
    {
        if (modUpdates is null) return null;
        try { return await modUpdates.CaptureWorkingLaunchUnderLeaseAsync(profile, receipt, ct); }
        catch (Exception error) when (InstalledModReadiness.IsFileError(error))
        {
            log.Error("working.capture", error);
            return null;
        }
    }

    internal static async Task<(MinecraftSession Session, bool Offline)> SelectSessionAsync(NetworkPolicy network,
        Func<CancellationToken, Task<MinecraftSession>> signIn, Func<MinecraftSession?> cached,
        Func<CancellationToken, Task<bool>>? checkConnectivity, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!network.Offline)
        {
            using var onlineOnly = CancellationTokenSource.CreateLinkedTokenSource(ct);
            void StopOnline()
            {
                try { onlineOnly.Cancel(); }
                catch (ObjectDisposedException) { }
            }
            network.WentOffline += StopOnline;
            try
            {
                if (network.Offline) StopOnline();
                var online = await signIn(onlineOnly.Token);
                return (online, network.Offline);
            }
            catch (Exception error) when (!ct.IsCancellationRequested && IsTransportFailure(error))
            {
                if (!network.Offline && (checkConnectivity is null || await checkConnectivity(ct))) throw;
            }
            finally { network.WentOffline -= StopOnline; }
        }
        ct.ThrowIfCancellationRequested();
        return (cached() ?? throw new InvalidOperationException("Спочатку увійдіть онлайн і підтвердьте ліцензію Minecraft Java Edition."), true);
    }

    private static bool IsTransportFailure(Exception error) => error switch
    {
        NetworkOfflineException or System.Net.Sockets.SocketException or OperationCanceledException => true,
        HttpRequestException { StatusCode: null, HttpRequestError: not HttpRequestError.SecureConnectionError } => true,
        Microsoft.Identity.Client.MsalServiceException { InnerException: { } inner } => IsTransportFailure(inner),
        _ => false
    };
}
