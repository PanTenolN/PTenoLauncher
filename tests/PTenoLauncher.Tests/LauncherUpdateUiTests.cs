using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.Dialogs;
using PTenoLauncher.Desktop.Localization;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Updates;
using Xunit;
namespace PTenoLauncher.Tests;

public sealed class LauncherUpdateUiTests
{
    [Fact] public void NormalDesktopContainsNeitherCooperativeTestClosureNorTestTrustResource()
    {
        var assembly = typeof(MainWindow).Assembly;
        Assert.Null(assembly.GetType("PTenoLauncher.Desktop.IsolatedUpdaterClosure"));
        Assert.DoesNotContain(assembly.GetManifestResourceNames(), name => name.Contains("UpdaterTestPublicKey", StringComparison.Ordinal));
    }
    [AvaloniaFact] public async Task IdleRecoveryDialogDoesNotPreventNormalOwnerClosure()
    {
        using var ws = new TestWorkspace(); using var services = new LauncherServices(ws.Paths); var flow = new Workflow();
        flow.Pending = new(Guid.NewGuid(), ws.Paths.Root, ws.Paths.Root, flow.Offer().Offer!, new(flow.Build, []), [], "AwaitingHealth", new string('a', 64));
        var (vm, window) = await Open(services, flow);
        await vm.CheckLauncherUpdatesAsync(); var dialog = Assert.Single(window.OwnedWindows.OfType<LauncherUpdateDialog>());
        Assert.False(vm.IsBusy); window.Close(); Assert.False(dialog.IsVisible); Assert.False(window.IsVisible);
    }
    private sealed class Workflow : ILauncherUpdateWorkflow
    {
        public LauncherBuild Build => new(10, "ui-old", "0.2.0", "stable");
        public Func<bool>? Offline;
        public TaskCompletionSource<LauncherUpdateCheck>? CheckGate;
        public TaskCompletionSource? InstallGate;
        public LauncherUpdateTransaction? Pending;
        public int Requests, Installs, Recoveries;
        public LauncherUpdateCheck Offer()
        {
            var file = new LauncherUpdateFile("PTenoLauncher.exe", 100, new string('a', 64));
            var package = new LauncherUpdatePackage("Portable", new("https://github.com/PanTenolN/PTenoLauncher/releases/download/test/p.zip"), 100, new string('a', 64), [file]);
            var release = new LauncherRelease(new(11, "ui-new", "0.2.0", "stable"), "Synthetic UI changelog", [package]);
            return new("UpdaterAvailable", new(Build, release, package, new("ui", [], [])));
        }
        public Task<LauncherUpdateCheck> CheckAsync(bool previews, CancellationToken ct)
        { if (Offline?.Invoke() == true) return Task.FromResult(new LauncherUpdateCheck("UpdaterOffline")); Requests++; return CheckGate?.Task ?? Task.FromResult(Offer()); }
        public Task<LauncherUpdateTransaction?> PendingAsync(CancellationToken ct) => Task.FromResult(Pending);
        public async Task InstallAsync(LauncherUpdateOffer offer, IProgress<DownloadProgress> progress, CancellationToken ct)
        { Installs++; progress.Report(new("p.zip", 50, 100)); if (InstallGate is not null) await InstallGate.Task.WaitAsync(ct); }
        public Task RecoverAsync(CancellationToken ct) { Recoveries++; return Task.CompletedTask; }
    }
    private static async Task<(MainViewModel Vm, MainWindow Window)> Open(LauncherServices services, Workflow workflow, bool language = true)
    {
        if (language) await services.Store.InitializeAsync();
        if (language) await services.Store.SetSettingAsync("ui.language", "en");
        workflow.Offline = () => services.Network.Offline;
        var vm = new MainViewModel(services, launcherUpdates: workflow); await vm.InitializeAsync();
        var window = new MainWindow { DataContext = vm }; window.Show(); return (vm, window);
    }
    [AvaloniaFact] public async Task BackgroundCheckDoesNotBusyTheLauncherAndLaterSuppressesSessionOffersWithoutDownloading()
    {
        using var ws = new TestWorkspace(); using var services = new LauncherServices(ws.Paths); var flow = new Workflow { CheckGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var (vm, window) = await Open(services, flow);
        try
        {
            var check = vm.CheckLauncherUpdatesAsync(true); Assert.False(vm.IsBusy); Assert.Empty(window.OwnedWindows); Assert.Equal(0, flow.Installs);
            flow.CheckGate.SetResult(flow.Offer()); await check;
            var dialog = Assert.Single(window.OwnedWindows.OfType<LauncherUpdateDialog>());
            Assert.Contains("Synthetic UI changelog", vm.LauncherUpdateChanges);
            Assert.Contains("10", dialog.FindControl<TextBlock>("LauncherUpdateSummary")!.Text);
            dialog.FindControl<Button>("LauncherUpdateLater")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(dialog.IsVisible); await vm.CheckLauncherUpdatesAsync(); Assert.Equal(1, flow.Requests); Assert.Equal(0, flow.Installs);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact] public async Task ActualUpdateClickShowsProgressAndOnlyClosesAfterHelperHandoff()
    {
        using var ws = new TestWorkspace(); using var services = new LauncherServices(ws.Paths); var flow = new Workflow { InstallGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var (vm, window) = await Open(services, flow);
        try
        {
            await vm.CheckLauncherUpdatesAsync(); var dialog = Assert.Single(window.OwnedWindows.OfType<LauncherUpdateDialog>());
            dialog.FindControl<Button>("LauncherUpdateApply")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(20); Assert.True(vm.IsBusy); Assert.True(window.IsVisible); Assert.Equal(1, flow.Installs); Assert.Equal(50, vm.LauncherUpdateProgress);
            Assert.False(dialog.FindControl<Button>("LauncherUpdateLater")!.IsEnabled);
            flow.InstallGate.SetResult(); for (int i = 0; vm.IsBusy && i < 100; i++) await Task.Delay(10);
            Assert.True(vm.LauncherHandoffReady); Assert.False(window.IsVisible); Assert.False(services.Ai.Enabled);
        }
        finally { flow.InstallGate.TrySetResult(); window.Close(); }
    }
    [AvaloniaFact] public async Task OnboardingAutoSettingAndOfflineAreHonoredAndSettingsPersist()
    {
        using var ws = new TestWorkspace(); using var services = new LauncherServices(ws.Paths); var flow = new Workflow(); var (vm, window) = await Open(services, flow, false);
        try
        {
            await vm.CheckLauncherUpdatesAsync(true); Assert.Equal(0, flow.Requests);
            await vm.SaveLanguageAsync();
            vm.AutoLauncherUpdates = false; vm.PreviewLauncherUpdates = true; await vm.SaveLauncherUpdateSettingsAsync();
            await vm.CheckLauncherUpdatesAsync(true); Assert.Equal(0, flow.Requests);
            vm.Offline = true; window.FindControl<Button>("CheckLauncherUpdatesButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(10);
            Assert.Equal(0, flow.Requests); Assert.Equal(TextCatalog.Get("UpdaterOffline"), vm.LauncherUpdateStatus);
            var second = new MainViewModel(services, launcherUpdates: flow); await second.InitializeAsync(); Assert.False(second.AutoLauncherUpdates); Assert.True(second.PreviewLauncherUpdates);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact] public async Task AllEightLanguagesReachActualUpdateControlsAndRetainPlaceholderContracts()
    {
        using var ws = new TestWorkspace(); using var services = new LauncherServices(ws.Paths); var flow = new Workflow(); var (vm, window) = await Open(services, flow);
        try
        {
            await vm.CheckLauncherUpdatesAsync(); var dialog = Assert.Single(window.OwnedWindows.OfType<LauncherUpdateDialog>());
            foreach (var language in TextCatalog.Languages)
            {
                vm.SelectedLanguage = language; TextCatalog.ValidateCatalog(language.Code, TextCatalog.Catalog(language.Code));
                Assert.Equal(TextCatalog.Get("UpdaterLater"), dialog.FindControl<Button>("LauncherUpdateLater")!.Content);
                Assert.Equal(TextCatalog.Get("UpdaterUpdate"), dialog.FindControl<Button>("LauncherUpdateApply")!.Content);
                Assert.Equal(TextCatalog.Get("UpdaterCheck"), window.FindControl<Button>("CheckLauncherUpdatesButton")!.Content);
                for (int i = 0; vm.IsBusy && i < 100; i++) await Task.Delay(10); // The real settings ComboBox persists its selection asynchronously.
            }
        }
        finally { window.DataContext = null; TextCatalog.Apply("uk"); window.Close(); await Task.Delay(20); }
    }
    [AvaloniaFact] public async Task RecoveryIsExplicitAndBlocksOtherOperations()
    {
        using var ws = new TestWorkspace(); using var services = new LauncherServices(ws.Paths); var flow = new Workflow();
        flow.Pending = new(Guid.NewGuid(), ws.Paths.Root, ws.Paths.Root, flow.Offer().Offer!, new(flow.Build, []), [], "AwaitingHealth", new string('a', 64));
        var (vm, window) = await Open(services, flow);
        try
        {
            bool touched = false; await vm.RunAsync(_ => { touched = true; return Task.CompletedTask; }); Assert.False(touched);
            await vm.CheckLauncherUpdatesAsync(); var dialog = Assert.Single(window.OwnedWindows.OfType<LauncherUpdateDialog>());
            Assert.Equal(TextCatalog.Get("UpdaterRecover"), dialog.FindControl<Button>("LauncherUpdateApply")!.Content); Assert.Equal(0, flow.Recoveries);
            await vm.ApplyLauncherUpdateAsync(); Assert.Equal(1, flow.Recoveries); Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    }
}
