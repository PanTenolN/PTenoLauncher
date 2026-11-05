using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PTenoLauncher.Infrastructure.Modpacks;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
using PTenoLauncher.Testing;
namespace PTenoLauncher.IntegrationSmoke;

// Explicit synthetic demonstration and real native-window startup, never a user's source profile.
public static class InstanceNativeProbe
{
    public static async Task RunAsync(bool localApp = false)
    {
        using var owned = new OwnedTestRoot("instance-native", 64L * 1024 * 1024);
        try
        {
            var paths = new LauncherPaths(owned.Root); paths.Initialize(); var store = new SqliteLauncherStore(paths); await store.InitializeAsync();
            await store.SetSettingAsync("ui.language", "uk"); await store.SetSettingAsync("updates.auto", "false"); await store.SetSettingAsync("network.offline", "true");
            var source = SafePath.Join(owned.Root, "synthetic-instance"); Directory.CreateDirectory(SafePath.Join(source, ".minecraft/config"));
            var data = "Owned synthetic content, not a real modpack"u8.ToArray();
            await File.WriteAllTextAsync(SafePath.Join(source, "instance.cfg"), "InstanceType=OneSix\nname=Synthetic offline import\n");
            await AtomicFile.WriteJsonAsync(SafePath.Join(source, "mmc-pack.json"), new { formatVersion = 1, components = new[] { new { uid = "net.minecraft", version = "1.21.1" }, new { uid = "net.fabricmc.fabric-loader", version = "0.16.10" } } });
            await File.WriteAllBytesAsync(SafePath.Join(source, ".minecraft/config/example.txt"), data);
            var network = new NetworkPolicy { Offline = true }; using var handler = new NoNetwork(); using var http = new HttpClient(handler);
            var importer = new MrpackImporter(paths, store, new(http, network), network); var plan = await importer.InstanceReader.PreviewAsync(source);
            var imported = await importer.ImportInstanceAsync(plan, true); var receipt = await MrpackRequirements.ReadAsync(paths, imported.Profile);
            if (receipt?.LoaderVersion != "0.16.10" || !File.ReadAllBytes(SafePath.Join(paths.Game(imported.Profile.Id), "config/example.txt")).AsSpan().SequenceEqual(data) ||
                !File.ReadAllBytes(SafePath.Join(source, ".minecraft/config/example.txt")).AsSpan().SequenceEqual(data) || File.Exists(paths.Receipt(imported.Profile.Id))) throw new IOException("Synthetic import proof failed.");
            SqliteConnection.ClearAllPools();
            var dotnet = Path.Combine(OwnedTestRoot.ProjectRoot, ".tools/dotnet/dotnet.exe");
            var app = Path.Combine(OwnedTestRoot.ProjectRoot, localApp
                ? "src/PTenoLauncher.Desktop/bin/UiContinuation/PTenoLauncher.dll"
                : "src/PTenoLauncher.Desktop/bin/Release/net10.0/PTenoLauncher.dll");
            var start = new ProcessStartInfo(dotnet) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = owned.Root };
            foreach (var arg in new[] { app, "--data-root", owned.Root, "--smoke-test", "--smoke-page", "Modpacks", "--smoke-size", "980", "680", "--capture-ui" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("Cannot start owned native probe.");
            var identity = owned.RegisterProcess(process); await AtomicFile.WriteJsonAsync(SafePath.Join(owned.Root, "probe-process.json"), identity);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException) { owned.Retain("Native probe is still running; no forced termination: PID " + identity.Pid); throw; }
            var smoke = await File.ReadAllTextAsync(SafePath.Join(owned.Root, "gui-smoke.txt"));
            if (process.ExitCode != 0 || !smoke.Contains("initialized=True") || !smoke.Contains("page=Modpacks") || !smoke.Contains("1 profiles") || !File.Exists(SafePath.Join(owned.Root, "window-preview.png"))) throw new IOException("Native Modpacks startup was not confirmed.");
            await AtomicFile.WriteJsonAsync(SafePath.Join(owned.Root, "instance-native-result.json"), new { state = "PASSED", importedFiles = receipt.Files.Count,
                exactLoader = receipt.LoaderVersion, parentHttpRequests = handler.Requests, sourceUnchanged = true, syntheticSource = true,
                nativeStartup = true, process.ExitCode, logicalWidth = 980, logicalHeight = 680, installedGame = false, gameplay = false, smoke });
            Console.WriteLine("Instance native evidence: " + owned.Evidence);
        }
        catch (Exception error) { await File.WriteAllTextAsync(SafePath.Join(owned.Root, "native-failure.log"), error.ToString()); throw; }
        finally { SqliteConnection.ClearAllPools(); try { owned.PreserveRunnerEvidence(); } catch (Exception e) { owned.Retain("Evidence preservation failed: " + e.Message); throw; } owned.CheckBudget(); }
    }
    private sealed class NoNetwork : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Requests++; throw new IOException("Synthetic import must use no HTTP."); }
    }
}
