using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Updates;

namespace PTenoLauncher.Desktop;

public sealed partial class App : Application
{
    public Themes.AppearanceManager Appearance { get; private set; } = null!;
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Appearance = new(this); Appearance.Initialize(); Localization.TextCatalog.Apply("uk");
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Program.Services is not null)
        {
            var vm = new MainViewModel(Program.Services);
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;
            window.Opened += async (_, _) =>
            {
                await vm.InitializeAsync();
                if (!vm.IsInitialized) return; // Keep initialization errors visible in the main window.
                // Unattended package smoke must not wait for user input or save a language choice.
                if (!Program.Arguments.Contains("--smoke-test") && !await window.EnsureLanguageSelectedAsync())
                {
                    window.Close(); return;
                }
                if (Program.Arguments.Contains("--open-translations"))
                {
                    vm.Page = LauncherPage.Settings;
                    window.FindControl<Avalonia.Controls.TabControl>("SettingsTabs")!.SelectedItem = window.FindControl<Avalonia.Controls.TabItem>("MyTranslationsTab");
                }
                var healthIndex = Array.IndexOf(Program.Arguments, "--launcher-update-health");
#if PTENO_UPDATE_TEST
                // Register test control before variable-cost health polling, rather than after it.
                IsolatedUpdaterClosure.Arm(desktop, window, Program.Services.Paths);
                IsolatedUpdaterClosure.Trace(Program.Services.Paths, "initialized-before-health");
#endif
                if (healthIndex >= 0)
                {
                    try
                    {
                        if (healthIndex + 1 >= Program.Arguments.Length || !Guid.TryParseExact(Program.Arguments[healthIndex + 1], "N", out var operation)) throw new LauncherUpdateException("UpdaterInvalid");
                        var ready = await Dispatcher.UIThread.InvokeAsync(() => window.IsVisible && window.Bounds.Width > 0 && window.Bounds.Height > 0, DispatcherPriority.Loaded);
#if !PTENO_UPDATE_TEST_NOHEALTH
                        await LauncherUpdateRuntime.WriteHealthAsync(Program.Services.Paths, AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), operation, vm.IsInitialized, ready);
#endif
                        // Briefly await helper acknowledgement without any network dependency; a timeout leaves recovery visible.
                        for (int i = 0; i < 30 && vm.LauncherRecoveryRequired; i++) {
#if PTENO_UPDATE_TEST
                            IsolatedUpdaterClosure.Trace(Program.Services.Paths, "health-poll-" + i);
                            if (!window.IsVisible) break;
#endif
                            await Task.Delay(100); await vm.RefreshLauncherRecoveryAsync();
                        }
                    }
                    catch (Exception e) { Program.Services.Log.Error("updater.health", e); await vm.RefreshLauncherRecoveryAsync(); }
                }
#if PTENO_UPDATE_TEST
                var stagedIndex = Array.IndexOf(Program.Arguments, "--isolated-update-operation");
                if (stagedIndex >= 0)
                {
                    if (stagedIndex + 1 >= Program.Arguments.Length || !Guid.TryParseExact(Program.Arguments[stagedIndex + 1], "N", out var operation)) { window.Close(); return; }
                    await LauncherUpdateRuntime.StartHelperAsync(Program.Services.Paths, LauncherUpdateTrust.Production, operation, AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), false,
                        healthTimeoutSeconds: Program.Arguments.Contains("--isolated-short-health-timeout") ? 5 : 90);
                    await Task.Delay(1800); // Isolated harness verifies the helper leaves A untouched until this exact parent exits.
                    window.Close(); return;
                }
#endif
                if (!Program.Arguments.Contains("--smoke-test") && !Program.Arguments.Contains("--skip-update-check")) _ = vm.CheckLauncherUpdatesAsync(automatic: !vm.LauncherRecoveryRequired);
                if (Program.Arguments.Contains("--smoke-test"))
                {
                    if (Program.Arguments.Contains("--smoke-fabric"))
                    {
                        vm.SelectedProfile = vm.Profiles.LastOrDefault(p => p.Loader == PTenoLauncher.Core.ModLoader.Fabric);
                        vm.Offline = true;
                        await vm.LoadFabricVersionsAsync();
                    }
                    if(Program.Arguments.Contains("--smoke-quilt"))
                    {
                        vm.SelectedProfile=vm.Profiles.LastOrDefault(p=>p.Loader==PTenoLauncher.Core.ModLoader.Quilt);
                        vm.Offline=true;await vm.LoadQuiltVersionsAsync();
                    }
                    var versionIndex=Array.IndexOf(Program.Arguments,"--smoke-cart-version");
                    if(versionIndex>=0 && versionIndex+1<Program.Arguments.Length)
                    {
                        vm.Offline=true;
                        var version=await Program.Services.Modrinth.GetVersionAsync(Program.Arguments[versionIndex+1]);
                        vm.SelectedRemoteVersion=version;
                        vm.QueueCatalogVersion(version, version.Name);
                        await vm.ResolveCartAsync();
                        DispatcherTimer.RunOnce(()=>window.ModsScroll.Offset=new Vector(0,window.ModsScroll.Extent.Height),TimeSpan.FromMilliseconds(700));
                    }
                    var pageIndex = Array.IndexOf(Program.Arguments, "--smoke-page");
                    if (pageIndex >= 0 && pageIndex + 1 < Program.Arguments.Length && Enum.TryParse<LauncherPage>(Program.Arguments[pageIndex + 1], out var page)) vm.Page = page;
                    var tabIndex = Array.IndexOf(Program.Arguments,"--smoke-settings-tab");
                    if(tabIndex >= 0 && tabIndex + 1 < Program.Arguments.Length && Program.Arguments[tabIndex+1] is "AiConnectionTab" or "AiAssistantTab" or "MyTranslationsTab")
                    {
                        var tab = window.FindControl<Avalonia.Controls.TabItem>(Program.Arguments[tabIndex+1]);
                        window.FindControl<Avalonia.Controls.TabControl>("SettingsTabs")!.SelectedItem = tab;
                    }
                    var sizeIndex=Array.IndexOf(Program.Arguments,"--smoke-size");
                    if(sizeIndex>=0 && sizeIndex+2<Program.Arguments.Length && int.TryParse(Program.Arguments[sizeIndex+1],out var width) && int.TryParse(Program.Arguments[sizeIndex+2],out var height) && width is >=980 and <=1920 && height is >=680 and <=1200) { window.Width=width;window.Height=height; }
                    Dialogs.ModBaselineDialog? reviewDialog = null;
                    if (Program.Arguments.Contains("--smoke-review-mods") && vm.CanManageMods)
                    {
                        reviewDialog = new(vm); await reviewDialog.RefreshAsync(); reviewDialog.Show(window);
                    }
                    var marker = Path.Combine(Program.Services.Paths.Root, "gui-smoke.txt");
                    await File.WriteAllTextAsync(marker, $"Window opened; {vm.Profiles.Count} profiles; page={vm.Page}; mods={vm.CanManageMods}; initialized={vm.IsInitialized}; fabric={vm.SelectedFabricVersion?.Version}; quilt={vm.SelectedQuiltVersion?.Version}; operation={vm.LastOperationSucceeded}; optional={vm.OptionalChoices.Count}; {vm.Status}");
                    DispatcherTimer.RunOnce(() => {
                        if (Program.Arguments.Contains("--capture-ui"))
                        {
                            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)(reviewDialog ?? (Avalonia.Controls.Window)window).Bounds.Width, (int)(reviewDialog ?? (Avalonia.Controls.Window)window).Bounds.Height));
                            bitmap.Render(reviewDialog ?? (Avalonia.Controls.Window)window); bitmap.Save(Path.Combine(Program.Services.Paths.Root, "window-preview.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                        }
                        reviewDialog?.Close(); window.Close();
                    }, TimeSpan.FromSeconds(2));
                }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
