using System.Diagnostics;
using System.Text.RegularExpressions;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Authentication;
using PTenoLauncher.Infrastructure.Diagnostics;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Security;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed class GameLauncher(LauncherPaths paths, OfflineReadiness readiness, MicrosoftAuthentication auth,
    NetworkPolicy network, ILauncherStore store, LauncherLog log)
{
    public event Action<Guid, bool>? RunningChanged;
    public async Task<int> LaunchAsync(GameProfile profile, CancellationToken ct = default, IProgress<string>? console = null)
    {
        using var profileLease = new ProfileUsage(paths).Acquire(profile.Id);
        new ProfileUsage(paths).CheckWorldLocks(profile.Id);
        var report = await readiness.CheckAsync(profile, ct);
        if (!report.Ready) throw new InvalidOperationException(report.Summary);
        var receipt = (await AtomicFile.ReadJsonAsync<InstallationReceipt>(paths.Receipt(profile.Id), ct))!;
        var session = network.Offline ? auth.GetCachedSession()! : await auth.SignInAsync(false, ct);
        var nativeDir = SafePath.Join(paths.Profile(profile.Id), "natives-" + Guid.NewGuid().ToString("N"));
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
            var values = new Dictionary<string, string>
            {
                ["auth_player_name"] = session.Account.Name, ["version_name"] = receipt.Version,
                ["game_directory"] = paths.Game(profile.Id), ["assets_root"] = SafePath.Join(paths.Cache, "assets"),
                ["assets_index_name"] = receipt.AssetIndex, ["auth_uuid"] = session.Account.Uuid,
                ["auth_access_token"] = network.Offline ? "0" : session.AccessToken,
                ["user_type"] = "msa", ["version_type"] = "release", ["natives_directory"] = nativeDir,
                ["launcher_name"] = "PTenoLauncher", ["launcher_version"] = "0.2.0",
                ["classpath"] = string.Join(Path.PathSeparator, receipt.Classpath.Select(f => SafePath.Join(paths.Cache, f))),
                ["classpath_separator"] = Path.PathSeparator.ToString(), ["library_directory"] = paths.Cache,
                ["user_properties"] = "{}", ["clientid"] = "", ["auth_xuid"] = "",
                ["auth_session"] = network.Offline ? "0" : "token:" + session.AccessToken + ":" + session.Account.Uuid,
                ["game_assets"] = SafePath.Join(paths.Cache, "assets")
            };
            string Expand(string argument) => Regex.Replace(argument, @"\$\{([^}]+)\}", m =>
                values.TryGetValue(m.Groups[1].Value, out var value) ? value : throw new NotSupportedException("Непідтримуваний параметр запуску: " + m.Groups[1].Value));
            var psi = new ProcessStartInfo(profile.JavaPath!) { WorkingDirectory = paths.Game(profile.Id), UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("-Xms512M"); psi.ArgumentList.Add($"-Xmx{profile.MemoryMb}M");
            foreach (var arg in receipt.JvmArguments) psi.ArgumentList.Add(Expand(arg));
            psi.ArgumentList.Add(receipt.MainClass);
            foreach (var arg in receipt.GameArguments) psi.ArgumentList.Add(Expand(arg));
            var activeFile = SafePath.Join(paths.Profile(profile.Id), "active-game.json");
            var gameLog = SafePath.Join(paths.Profile(profile.Id), $"launcher-logs/game-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
            int code;
            try
            {
                code = await LiveProcessRunner.RunAsync(psi, gameLog, async process =>
                {
                    RunningChanged?.Invoke(profile.Id, true);
                    if (!process.HasExited)
                        await AtomicFile.WriteJsonAsync(activeFile, new ActiveGameRecord(process.Id, process.StartTime.ToUniversalTime().Ticks, profile.JavaPath!));
                    await store.AddHistoryAsync(profile.Id, "minecraft.started", receipt.Version, CancellationToken.None);
                }, console, [session.AccessToken], ct);
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
}