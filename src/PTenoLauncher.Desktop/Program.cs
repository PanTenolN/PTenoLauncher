using System.Security.Cryptography;
using System.Text;
using Avalonia;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Desktop;

internal static class Program
{
    internal static LauncherServices? Services { get; private set; }
    internal static string[] Arguments { get; private set; } = [];
    [STAThread]
    public static int Main(string[] args)
    {
        Arguments = args;
        var paths = LauncherPaths.Default(args);
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(paths.Root.ToUpperInvariant())))[..24];
        using var mutex = new Mutex(true, @"Local\PTenoLauncher-" + suffix, out var first);
        if (!first) return 2;
        try
        {
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
            services.Log.Write("startup", "PTenoLauncher 0.2.0");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            if (Services is not null) Services.Log.Error("startup", ex);
            Console.Error.WriteLine("PTenoLauncher: " + ex.GetType().Name);
            return 1;
        }
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}