using System.Security.Cryptography;
using System.Text;
using Avalonia;
using PTenoLauncher.Infrastructure.Storage;
using PTenoLauncher.Infrastructure.Updates;

namespace PTenoLauncher.Desktop;

internal static class Program
{
    internal static LauncherServices? Services { get; private set; }
    internal static string[] Arguments { get; private set; } = [];
    [STAThread]
    public static int Main(string[] args)
    {
        Arguments = args;
#if PTENO_UPDATE_TEST_FAIL
        if (args.Contains("--launcher-update-health")) return 73;
#endif
        var paths = LauncherPaths.Default(args);
        using var mutex = new Mutex(false, LauncherUpdateGuards.MutexName(paths));
        try { if (!mutex.WaitOne(0)) return 2; } catch (AbandonedMutexException) { }
        try
        {
            using var installationLease = LauncherUpdateGuards.ApplicationReadLease(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            if (args.Contains("--purge-user-data"))
            {
                var confirmIndex = Array.IndexOf(args, "--confirm-data-deletion");
                var confirmation = confirmIndex >= 0 && confirmIndex + 1 < args.Length ? args[confirmIndex + 1] : "";
                new PTenoLauncher.Infrastructure.Security.UserDataCleaner(paths).CleanAsync(confirmation).GetAwaiter().GetResult();
                return 0;
            }
            using var services = new LauncherServices(paths); Services = services;
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            { if (e.ExceptionObject is Exception ex) services.Log.Error("unhandled", ex); };
            TaskScheduler.UnobservedTaskException += (_, e) => { services.Log.Error("unobserved", e.Exception); e.SetObserved(); };
            services.Log.Write("startup", "PTenoLauncher " + LauncherBuild.Current.Version);
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            if (Services is not null) Services.Log.Error("startup", ex);
            Console.Error.WriteLine("PTenoLauncher: " + ex.GetType().Name);
            return 1;
        }
        finally { mutex.ReleaseMutex(); }
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
