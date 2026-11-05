using Avalonia.Headless.XUnit;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.ViewModels;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class OfflineCatalogTransitionTests
{
    [AvaloniaFact]
    public async Task SwitchingOfflineCancelsAnAlreadyStartedCatalogRequestAndReleasesUi()
    {
        using var workspace = new TestWorkspace();
        using var pending = new PendingCatalog();
        using var services = new LauncherServices(workspace.Paths, coreTransport: pending);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        var refresh = vm.RefreshCatalogIfStaleAsync();
        try
        {
            await pending.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(vm.IsBusy);
            vm.Offline = true;
            await refresh.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(pending.Cancelled);
            Assert.False(vm.IsBusy);
            Assert.False(vm.LastOperationSucceeded);
            Assert.Equal(1, pending.Calls);
            await vm.RefreshCatalogIfStaleAsync();
            Assert.Equal(1, pending.Calls);
            await vm.SaveLanguageAsync();
            Assert.True(vm.LastOperationSucceeded, vm.Status);
        }
        finally
        {
            vm.Offline = true;
            await refresh.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    private sealed class PendingCatalog : HttpMessageHandler
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Cancelled;
        internal int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("piston-meta.mojang.com", request.RequestUri!.Host);
            Calls++;
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            throw new InvalidOperationException("A pending catalog must be cancelled.");
        }
    }
}
