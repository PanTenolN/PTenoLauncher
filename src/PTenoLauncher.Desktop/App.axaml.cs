using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System.Diagnostics;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Updates;

namespace PTenoLauncher.Desktop;

public sealed partial class App : Application
{
    public Themes.AppearanceManager Appearance { get; private set; } = null!;
    public override void Initialize()
    {
        var started = Stopwatch.GetTimestamp();
        AvaloniaXamlLoader.Load(this);
        Appearance = new(this); Appearance.Initialize(); Localization.TextCatalog.Apply("uk");
        Program.WriteStartupSpan("application-resources", started);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Program.Services is not null)
        {
            var started = Stopwatch.GetTimestamp();
            var vm = new MainViewModel(Program.Services);
            var window = new MainWindow { DataContext = vm };
            Program.WriteStartupSpan("window-construction", started);
            desktop.MainWindow = window;
            window.Opened += async (_, _) =>
            {
                var initialization = Stopwatch.GetTimestamp();
                await vm.InitializeAsync();
                Program.WriteStartupSpan("load-local-data", initialization);
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
                if (!Program.Arguments.Contains("--smoke-test")) _ = MonitorConnectivityAsync(window, vm);
                if (Program.Arguments.Contains("--smoke-test"))
                {
                    var forgeSmoke = Array.IndexOf(Program.Arguments, "--smoke-forge-lifecycle");
                    if (forgeSmoke >= 0)
                    {
                        try
                        {
                            await ExerciseLiveForgeAsync(window, vm, Program.Arguments.ElementAtOrDefault(forgeSmoke + 1));
                            desktop.Shutdown(0);
                        }
                        catch (Exception error)
                        {
                            Console.Error.WriteLine(error);
                            desktop.Shutdown(1);
                        }
                        return;
                    }
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
                    }
                    var pageIndex = Array.IndexOf(Program.Arguments, "--smoke-page");
                    if (pageIndex >= 0 && pageIndex + 1 < Program.Arguments.Length)
                    {
                        var target = Program.Arguments[pageIndex + 1];
                        if (target == nameof(ProfileSection.Mods) && vm.SelectedProfile is { } profile)
                        {
                            vm.OpenProfileWorkspace(profile.Id);
                            vm.ChooseWorkspaceSection(ProfileSection.Mods);
                        }
                        else if (Enum.TryParse<LauncherPage>(target, out var page)) vm.Page = page;
                    }
                    var tabIndex = Array.IndexOf(Program.Arguments,"--smoke-settings-tab");
                    if(tabIndex >= 0 && tabIndex + 1 < Program.Arguments.Length && Program.Arguments[tabIndex+1] is "AiConnectionTab" or "AiAssistantTab" or "MyTranslationsTab")
                    {
                        var tab = window.FindControl<Avalonia.Controls.TabItem>(Program.Arguments[tabIndex+1]);
                        window.FindControl<Avalonia.Controls.TabControl>("SettingsTabs")!.SelectedItem = tab;
                    }
                    var sizeIndex=Array.IndexOf(Program.Arguments,"--smoke-size");
                    if(sizeIndex>=0 && sizeIndex+2<Program.Arguments.Length && int.TryParse(Program.Arguments[sizeIndex+1],out var width) && int.TryParse(Program.Arguments[sizeIndex+2],out var height) && width is >=980 and <=1920 && height is >=680 and <=1200) { window.Width=width;window.Height=height; }
                    var serverForm = Array.IndexOf(Program.Arguments, "--smoke-server-form");
                    var serverMarker = serverForm >= 0
                        ? await ExerciseServerFormAsync(window, vm, Program.Arguments.ElementAtOrDefault(serverForm + 1) ?? "")
                        : "";
                    Dialogs.ModBaselineDialog? reviewDialog = null;
                    if (Program.Arguments.Contains("--smoke-review-mods") && vm.CanManageMods)
                    {
                        reviewDialog = new(vm); await reviewDialog.RefreshAsync(); reviewDialog.Show(window);
                    }
                    var marker = Path.Combine(Program.Services.Paths.Root, "gui-smoke.txt");
                    await File.WriteAllTextAsync(marker, $"Window opened; {vm.Profiles.Count} profiles; page={vm.Page}; mods={vm.CanManageMods}; initialized={vm.IsInitialized}; fabric={vm.SelectedFabricVersion?.Version}; quilt={vm.SelectedQuiltVersion?.Version}; operation={vm.LastOperationSucceeded}; optional={vm.OptionalChoices.Count}; {vm.Status}{serverMarker}");
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

    private static async Task<string> ExerciseServerFormAsync(MainWindow window, MainViewModel vm, string mode)
    {
        if (mode is not ("scratch" or "profile")) throw new ArgumentException("Invalid server form smoke mode.");
        window.FindControl<Button>("ServersNavButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitAsync(() => vm.IsServers && vm.CanEditServer && !vm.ServerCatalogLoading && vm.HasServerVersions);
        var core = window.FindControl<ComboBox>("ServerCorePicker")!;
        core.SelectedItem = vm.ServerCores.Single(option => option.Core == ServerCore.Forge);
        await WaitAsync(() => !vm.ServerCatalogLoading && vm.NewServerBuild == "52.1.0");
        var version = window.FindControl<ComboBox>("ServerMinecraftVersionPicker")!;
        var build = window.FindControl<ComboBox>("ServerCoreBuildPicker")!;
        if (version.SelectedItem is not PTenoLauncher.Core.VersionEntry { Id: "1.21.1" } || build.SelectedItem is not "52.1.0")
            throw new InvalidDataException("The Forge server selectors do not display their catalog choices.");
        window.FindControl<TextBox>("NewServerNameInput")!.Text = "Forge 1.21.1";
        if (mode == "profile")
        {
            window.FindControl<RadioButton>("ServerFromProfileButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.FindControl<ComboBox>("ServerSourceProfilePicker")!.SelectedItem = vm.Profiles.Single();
        }
        window.UpdateLayout();
        var scratch = window.FindControl<RadioButton>("ServerFromScratchButton")!;
        var profile = window.FindControl<RadioButton>("ServerFromProfileButton")!;
        if (scratch.Bounds.Height != 40 || profile.Bounds.Height != 40 ||
            scratch.IsChecked != (mode == "scratch") || profile.IsChecked != (mode == "profile") ||
            !window.FindControl<Button>("CreateLocalServerButton")!.IsEffectivelyEnabled)
            throw new InvalidDataException("The server form mode, controls or create button did not initialize.");
        window.FindControl<Border>("ServerCreationForm")!.BringIntoView();
        return $"; serverForm={mode}; core=Forge; minecraft=1.21.1; build=52.1.0; create=True";

        static async Task WaitAsync(Func<bool> ready)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!ready()) await Task.Delay(20, timeout.Token);
        }
    }

    private static async Task MonitorConnectivityAsync(MainWindow window, MainViewModel vm)
    {
        using var lifetime = new CancellationTokenSource();
        void Stop(object? sender, EventArgs args) => lifetime.Cancel();
        window.Closed += Stop;
        try
        {
            if (!vm.ManualOffline) await vm.RefreshConnectivityAsync(lifetime.Token);
            if (!Program.Arguments.Contains("--skip-update-check"))
                _ = vm.CheckLauncherUpdatesAsync(automatic: !vm.LauncherRecoveryRequired);
            while (!lifetime.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(vm.Offline ? 6 : 15), lifetime.Token);
                if (!vm.ManualOffline) await vm.RefreshConnectivityAsync(lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { vm.Services.Log.Error("network.connectivity", error); }
        finally { window.Closed -= Stop; }
    }
}
