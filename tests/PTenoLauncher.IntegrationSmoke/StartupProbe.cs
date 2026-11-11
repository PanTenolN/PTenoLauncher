using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.IntegrationSmoke;

internal static class StartupProbe
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessageTimeoutW(nint window, uint message, nint wParam, nint lParam,
        uint flags, uint timeout, out nuint result);

    internal static async Task RunAsync(string exe, string root)
    {
        root = Path.GetFullPath(root);
        var operation = Environment.GetEnvironmentVariable("PTENO_VERIFICATION_ROOT");
        if (operation is null || !string.Equals(Path.GetDirectoryName(root), SafePath.Join(operation, "tmp"), StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(SafePath.Join(operation, "owned-test-root.json")) || Directory.Exists(root))
            throw new InvalidOperationException("Startup timing requires a fresh owned verification directory.");
        exe = Path.GetFullPath(exe);
        SafePath.RejectLinks(exe);
        var assembly = Path.Combine(Path.GetDirectoryName(exe)!, "PTenoLauncher.dll");
        await using (var file = File.OpenRead(assembly))
            Console.WriteLine($"Desktop DLL SHA-256 {Convert.ToHexString(await SHA256.HashDataAsync(file))}");
        var empty = await SeedAsync("empty", false, 0);
        var populated = await SeedAsync("profiles", false, 8);
        var offline = await SeedAsync("offline", true, 8);
        await MeasureAsync("first-process", empty);
        await MeasureAsync("repeat", empty);
        await MeasureAsync("profiles", populated);
        await MeasureAsync("profiles-offline", offline);

        async Task<string> SeedAsync(string name, bool offlineMode, int profileCount)
        {
            var paths = new LauncherPaths(SafePath.Join(root, name));
            var store = new SqliteLauncherStore(paths);
            await store.InitializeAsync();
            await store.SetSettingAsync("ui.language", "uk");
            await store.SetSettingAsync("network.offline", offlineMode.ToString());
            for (var i = 0; i < profileCount; i++)
                await store.SaveProfileAsync(new GameProfile(Guid.NewGuid(), $"Startup fixture {i + 1}", "1.21.1",
                    ModLoader.Fabric, 2048, null, DateTimeOffset.UtcNow));
            return paths.Root;
        }

        async Task MeasureAsync(string scenario, string data)
        {
            var info = new ProcessStartInfo(exe)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = data,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var arg in new[] { "--data-root", data, "--skip-update-check", "--trace-startup" }) info.ArgumentList.Add(arg);
            var clock = Stopwatch.StartNew();
            using var process = Process.Start(info) ?? throw new IOException("Launcher process did not start.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                nint window = 0;
                while (window == 0 && clock.Elapsed < TimeSpan.FromSeconds(25) && !process.HasExited)
                {
                    process.Refresh();
                    window = process.MainWindowHandle;
                    if (window == 0) await Task.Delay(8);
                }
                if (window == 0) throw new IOException("Launcher did not show a Windows window.");
                var appeared = clock.Elapsed.TotalMilliseconds;
                double? responsive = null;
                double? stalledAt = null;
                double longestStall = 0;
                var timeouts = 0;
                while (clock.Elapsed.TotalMilliseconds < appeared + 2400 && !process.HasExited)
                {
                    var started = clock.Elapsed.TotalMilliseconds;
                    if (SendMessageTimeoutW(window, 0, 0, 0, 2, 100, out _) == 0)
                    {
                        timeouts++;
                        stalledAt ??= started;
                    }
                    else
                    {
                        responsive ??= clock.Elapsed.TotalMilliseconds;
                        longestStall = Math.Max(longestStall, clock.Elapsed.TotalMilliseconds - (stalledAt ?? started));
                        stalledAt = null;
                    }
                    await Task.Delay(8);
                }
                if (responsive is null || process.HasExited) throw new IOException("Launcher window did not respond.");
                if (stalledAt is { } blocked) longestStall = Math.Max(longestStall, clock.Elapsed.TotalMilliseconds - blocked);
                if (!process.CloseMainWindow()) throw new IOException("Could not close the measured launcher.");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(timeout.Token);
                if (process.ExitCode != 0) throw new IOException($"Measured launcher exited with code {process.ExitCode}.");
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    scenario, windowMs = Math.Round(appeared, 1), responsiveMs = Math.Round(responsive.Value, 1),
                    longestUiResponseGapMs = Math.Round(longestStall, 1), timeouts, trace = await stdout
                }));
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
                var error = await stderr;
                if (error.Length > 0) Console.Error.WriteLine(error);
            }
        }
    }
}
