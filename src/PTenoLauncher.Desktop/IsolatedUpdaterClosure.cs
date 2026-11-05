#if PTENO_UPDATE_TEST
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using PTenoLauncher.Infrastructure.Storage;
using PTenoLauncher.Infrastructure.Updates;
namespace PTenoLauncher.Desktop;

// Compiled only into opt-in test builds. No production argument/setting/feed can enable this IPC.
internal static class IsolatedUpdaterClosure
{
    public static void Trace(LauncherPaths paths, string phase)
    {
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "isolated-e2e.flag")))
            File.AppendAllText(SafePath.Join(paths.Root, "isolated-lifecycle.log"), DateTimeOffset.UtcNow.ToString("O") + " " + Environment.ProcessId + " " + phase + Environment.NewLine);
    }
    public static void Arm(IClassicDesktopStyleApplicationLifetime desktop, Window main, LauncherPaths paths)
    {
        var root = Path.GetFullPath(AppContext.BaseDirectory);
        if (!File.Exists(Path.Combine(root, "isolated-e2e.flag")) ||
            !paths.Root.Equals(Path.Combine(root, "portable-data"), StringComparison.OrdinalIgnoreCase)) return;
        var identity = UpdateProcessIdentity.Current;
        var request = SafePath.Join(root, "isolated-close-request.json");
        var evidence = SafePath.Join(paths.Root, "isolated-close-state.json");
        bool handling = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += async (_, _) =>
        {
            if (handling || !File.Exists(request)) return;
            handling = true;
            try
            {
                if (await AtomicFile.ReadJsonAsync<UpdateProcessIdentity>(request) != identity) { handling = false; return; }
                // Closing an owner while its recovery dialog is open can be cancelled by Avalonia.
                // Close owned dialogs first; all closures remain normal UI actions on this test process.
                var owned = desktop.Windows.Where(w => w != main && w.IsVisible).Reverse().ToArray();
                foreach (var dialog in owned) dialog.Close();
                await AtomicFile.WriteJsonAsync(evidence, new { process = identity, requestReceived = true, ownedDialogsClosed = owned.Length, normalClosure = true });
                timer.Stop(); main.Close();
            }
            catch (Exception e)
            {
                await AtomicFile.WriteJsonAsync(evidence, new { process = identity, error = e.GetType().Name });
                handling = false;
            }
        };
        main.Closed += (_, _) => { Trace(paths, "closed"); timer.Stop(); }; timer.Start(); Trace(paths, "close-hook-armed");
    }
}
#endif
