using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.IntegrationSmoke;

internal static class LiveLocalServers
{
    internal static async Task RunAsync(LauncherPaths paths, VersionManager versions, VerifiedDownloader downloader, NetworkPolicy network,
        string mode)
    {
        var vanillaCommands = mode == "--vanilla-commands";
        var forge = mode == "--forge-server";
        var operation = Environment.GetEnvironmentVariable("PTENO_VERIFICATION_ROOT");
        if (operation is null || !string.Equals(paths.Root, SafePath.Join(operation, forge ? "tmp/forge-servers" : "tmp/local-servers"), StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(SafePath.Join(operation, "owned-test-root.json")))
            throw new InvalidOperationException("Live server tests require an owned verification operation.");
        const string game = "1.21.1";
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        var ct = deadline.Token;
        var runtimes = new JavaRuntimeManager(paths, new JavaManager(), versions, downloader, network);
        var store = new SqliteLauncherStore(paths);
        if (forge) await store.InitializeAsync(ct);
        var manager = new VanillaServerManager(paths, versions, downloader, runtimes, forge ? store : null);
        await versions.RefreshAsync(ct);
        var failures = new List<Exception>();
        (ServerCore Core, string? Build)[] cores = vanillaCommands ? [(ServerCore.Vanilla, null)] :
            forge ? [(ServerCore.Forge, "52.1.0")] :
            [(ServerCore.Fabric, "0.20.1"), (ServerCore.Paper, "133"), (ServerCore.Purpur, "2329")];
        foreach (var (core, build) in cores)
        {
            foreach (var fromProfile in forge ? new[] { false, true } : [false])
            {
                try { await CheckAsync(core, build, fromProfile); }
                catch (Exception error) when (!ct.IsCancellationRequested)
                {
                    failures.Add(error);
                    Console.Error.WriteLine($"{core} {build} ({(fromProfile ? "profile" : "blank")}): {error}");
                }
            }
        }
        if (failures.Count != 0)
        {
            if (forge)
                foreach (var log in Directory.EnumerateFiles(paths.Logs, "forge-*.log"))
                {
                    Console.Error.WriteLine(Path.GetFileName(log));
                    foreach (var line in File.ReadLines(log).TakeLast(40)) Console.Error.WriteLine(line);
                }
            throw new AggregateException("Local server verification failed.", failures);
        }

        async Task CheckAsync(ServerCore core, string? build, bool fromProfile)
        {
            Console.WriteLine($"Checking {core} {build} with Minecraft {game} ({(fromProfile ? "profile" : "blank")})");
            if (!(await manager.VersionsAsync(core, ct: ct)).Any(version => version.Id == game) ||
                build is not null && !(await manager.BuildsAsync(core, game, ct)).Contains(build))
                throw new InvalidDataException($"Official catalog no longer lists {core} {build} for {game}.");
            var listener = new TcpListener(IPAddress.Loopback, 0) { ExclusiveAddressUse = true };
            listener.Start();
            LocalVanillaServer server;
            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                if (fromProfile)
                {
                    var profile = new GameProfile(Guid.NewGuid(), "Isolated Forge server source", game, ModLoader.Forge, 1536,
                        null, DateTimeOffset.UtcNow) { PreferredLoaderVersion = $"{game}-{build}" };
                    await store.SaveProfileAsync(profile, ct);
                    var plan = await manager.PreviewProfileAsync(profile, null, ct);
                    if (plan.Core != core || plan.Build != build || plan.Blockers.Count != 0 || plan.ForgeCopyBlockers is not { Count: 0 })
                        throw new InvalidDataException("Forge profile preview selected the wrong server core, build or dependencies.");
                    server = await manager.CreateFromProfileAsync(plan, core + " from profile", port, 1536, ct, privateServer: true);
                }
                else server = await manager.CreateAsync(core.ToString(), game, port, 1536, core, build, ct, privateServer: forge);
                if (forge)
                {
                    try { await manager.StartAsync(server, ct: ct); throw new IOException("Forge accepted startup without EULA."); }
                    catch (InvalidOperationException error) when (error.Message == "ServerEulaNeeded") { }
                }
                await manager.AcceptEulaAsync(server, ct); // Explicit opt-in for this synthetic disposable server only.
                try { await manager.StartAsync(server, ct: ct); throw new IOException("Occupied port accepted."); }
                catch (InvalidOperationException error) when (error.Message == "ServerPortBusy") { }
            }
            finally { listener.Stop(); }

            var folder = manager.DirectoryFor(server.Id);
            // Seed the public lookup cache only. Online-mode and the signed-profile requirement stay enabled.
            var playerId = Guid.Parse("069a79f4-44e9-4726-a5be-fca90e38aaf5");
            if (vanillaCommands)
                await AtomicFile.WriteJsonAsync(SafePath.Join(folder, "usercache.json"),
                    new[] { new { name = "Notch", uuid = playerId, expiresOn = "2099-01-01 00:00:00 +0000" } }, ct);
            for (var run = 0; run < 2; run++)
            {
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var replies = new System.Collections.Concurrent.ConcurrentQueue<string>();
                var opened = await manager.StartAsync(server, output: new Progress<string>(batch =>
                {
                    if (batch.Contains("Done (", StringComparison.Ordinal)) ready.TrySetResult();
                    if (batch.Contains("pteno_smoke", StringComparison.OrdinalIgnoreCase)) saved.TrySetResult();
                    if (vanillaCommands) foreach (var line in batch.Split('\n')) replies.Enqueue(line);
                }), ct: ct);
                try
                {
                    await AwaitOutputAsync(ready.Task, opened.Completion, ct);
                    await opened.SendAsync(run == 0 ? "scoreboard objectives add pteno_smoke dummy" : "scoreboard objectives list", ct);
                    await AwaitOutputAsync(saved.Task, opened.Completion, ct);
                    if (vanillaCommands && run == 0)
                    {
                        await SendAndConfirmAsync("list", "players online");
                        await SendAndConfirmAsync("say pteno_console_smoke", "pteno_console_smoke");
                        await SendAndConfirmAsync("/op Notch", "Made Notch a server operator");
                        await SendAndConfirmAsync("deop Notch", "Made Notch no longer a server operator");
                        await manager.SetWhitelistAsync(server, true, opened, ct);
                        await manager.AddPlayerAsync(server, ServerPlayerList.Allowed, "Notch", opened, ct);
                        var access = await manager.ReadPlayerAccessAsync(server, opened, ct);
                        if (!access.Whitelist || access.Players.Allowed.Single().Uuid != playerId || access.Players.Operators.Count != 0)
                            throw new IOException("Whitelist and operator roles were not independently persisted.");
                        await manager.RemovePlayerAsync(server, ServerPlayerList.Allowed, playerId, opened, ct);
                        await manager.AddPlayerAsync(server, ServerPlayerList.Blocked, "Notch", opened, ct);
                        await manager.RemovePlayerAsync(server, ServerPlayerList.Blocked, playerId, opened, ct);
                        await manager.SetWhitelistAsync(server, false, opened, ct);
                        Console.WriteLine("Real Vanilla commands and persisted player access confirmed.");
                    }
                    var settings = await File.ReadAllTextAsync(SafePath.Join(folder, "server.properties"), ct);
                    if (!settings.Contains("online-mode=true", StringComparison.Ordinal) ||
                        !settings.Contains("server-ip=127.0.0.1", StringComparison.Ordinal) ||
                        forge && (!settings.Contains("enforce-secure-profile=true", StringComparison.Ordinal) ||
                            !settings.Contains("white-list=true", StringComparison.Ordinal)))
                        throw new InvalidDataException("Server account or loopback protection changed.");
                }
                catch
                {
                    var logs = SafePath.Join(folder, "launcher-logs");
                    var file = Directory.EnumerateFiles(logs, "*.log").MaxBy(File.GetLastWriteTimeUtc)!;
                    foreach (var line in (await File.ReadAllLinesAsync(file)).TakeLast(38)) Console.Error.WriteLine(line);
                    throw;
                }
                finally
                {
                    try
                    {
                        if (await opened.Ready) await opened.StopAsync();
                        await opened.Completion;
                    }
                    catch
                    {
                        var identity = await AtomicFile.ReadJsonAsync<ActiveGameRecord>(SafePath.Join(folder, "active-server.json"));
                        if (identity is not null && ActiveGameRecord.IsRunning(identity) &&
                            identity.JavaPath.StartsWith(SafePath.Join(paths.Root, "runtimes") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        {
                            using var process = Process.GetProcessById(identity.Pid);
                            if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks == identity.StartedTicks)
                            {
                                process.Kill(entireProcessTree: true);
                                await process.WaitForExitAsync();
                            }
                        }
                        throw;
                    }
                }
                if (await opened.Completion != 0 || !File.Exists(SafePath.Join(folder, "world/level.dat")))
                    throw new IOException($"{core} failed to save the world and stop cleanly.");

                async Task SendAndConfirmAsync(string command, string expected)
                {
                    await opened.SendAsync(command, ct);
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (DateTime.UtcNow < deadline)
                    {
                        if (replies.FirstOrDefault(line => line.Contains(expected, StringComparison.OrdinalIgnoreCase)) is { } response)
                        {
                            Console.WriteLine(response);
                            return;
                        }
                        await Task.Delay(100, ct);
                    }
                    throw new IOException($"No console confirmation for '{command}'.");
                }
            }
            if (forge)
            {
                var argsFile = SafePath.Join(folder, "libraries/net/minecraftforge/forge/1.21.1-52.1.0/win_args.txt");
                await File.AppendAllTextAsync(argsFile, "\n-javaagent:unexpected.jar", ct);
                try { await manager.StartAsync(server, ct: ct); throw new IOException("Modified Forge arguments were accepted."); }
                catch (InvalidDataException error) when (error.Message == "ServerForgeIntegrity") { }
                Console.WriteLine("Forge refused changed win_args.txt.");
            }
            Console.WriteLine($"{core} {build}: verified artifact, occupied port, console, clean stop, reopened scoreboard and world. Licensed join remains manual.");
        }
    }

    private static async Task AwaitOutputAsync(Task output, Task<int> process, CancellationToken ct)
    {
        var winner = await Task.WhenAny(output, process).WaitAsync(TimeSpan.FromMinutes(3), ct);
        if (winner == process) throw new IOException($"Server exited before expected output ({await process}).");
        await output;
    }
}
