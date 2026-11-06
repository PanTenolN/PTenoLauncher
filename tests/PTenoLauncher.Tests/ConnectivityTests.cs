using Avalonia.Headless.XUnit;
using PTenoLauncher.Core;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Authentication;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Network;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class ConnectivityTests
{
    [AvaloniaFact]
    public async Task StartupOutageNotifiesOnceAndRecoveryPreservesManualChoice()
    {
        using var ws = new TestWorkspace();
        var available = false;
        using var services = new LauncherServices(ws.Paths, connectivityTransport: new StubHandler(_ =>
            available ? new(System.Net.HttpStatusCode.OK) : throw new HttpRequestException("No route")));
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        var notices = 0;
        vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.ShowNetworkNotice)) notices++; };

        Assert.False(await vm.RefreshConnectivityAsync(default));
        Assert.True(vm.Offline);
        Assert.True(vm.ShowNetworkNotice);
        Assert.False(vm.ManualOffline);
        Assert.False(await vm.RefreshConnectivityAsync(default));
        Assert.Equal(1, notices);
        await vm.SaveSettingsCommand.ExecuteAsync();
        Assert.Equal("False", await services.Store.GetSettingAsync("network.offline"));

        available = true;
        Assert.True(await vm.RefreshConnectivityAsync(default));
        Assert.True(vm.Offline);
        vm.ObserveConnectivity(true, DateTimeOffset.UtcNow.AddSeconds(6));
        Assert.False(vm.Offline);
        Assert.False(vm.ShowNetworkNotice);

        vm.ManualOffline = true;
        await vm.SaveManualOfflineAsync();
        vm.ObserveConnectivity(false, DateTimeOffset.UtcNow);
        vm.ObserveConnectivity(true, DateTimeOffset.UtcNow);
        vm.ObserveConnectivity(true, DateTimeOffset.UtcNow.AddSeconds(6));
        Assert.True(vm.Offline);
        Assert.False(vm.ShowNetworkNotice);
        Assert.Equal("True", await services.Store.GetSettingAsync("network.offline"));
    }

    [AvaloniaFact]
    public async Task CatalogFailureDoesNotSwitchTheLauncherOffline()
    {
        using var ws = new TestWorkspace();
        using var services = new LauncherServices(ws.Paths, coreTransport: new StubHandler(_ =>
            new(System.Net.HttpStatusCode.ServiceUnavailable)), connectivityTransport: new StubHandler(request =>
            request.RequestUri!.Host == "www.microsoft.com" ? throw new HttpRequestException("One probe unavailable") :
                new(System.Net.HttpStatusCode.Forbidden)));
        await services.Store.InitializeAsync();
        await services.Store.SaveProfileAsync(ws.Profile(ModLoader.Fabric));
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        Assert.True(await vm.RefreshConnectivityAsync(default));
        vm.RemoteQuery = "test mod";
        await vm.SearchRemoteAsync();
        Assert.False(vm.Offline);
        Assert.Equal(PTenoLauncher.Desktop.Localization.TextCatalog.Get("CatalogSearchFailed"), vm.CatalogSearchStatus);
    }

    [Fact]
    public async Task InterruptedSignInUsesOnlyThePreviouslyVerifiedSession()
    {
        var network = new NetworkPolicy();
        var session = VerifiedSession();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var launch = GameLauncher.SelectSessionAsync(network, async ct =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException();
        }, () => session, null, default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        network.Offline = true;
        Assert.Equal((session, true), await launch.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => GameLauncher.SelectSessionAsync(network,
            _ => throw new InvalidOperationException("Unexpected online sign-in"), () => null, null, default));
    }

    [Fact]
    public async Task TransportFailureFallsBackOnlyDuringAnOutageAndLicenseErrorsArePreserved()
    {
        var session = VerifiedSession();
        var network = new NetworkPolicy();
        Task<MinecraftSession> TransportError(CancellationToken _) => throw new HttpRequestException("No route");
        Assert.Equal((session, true), await GameLauncher.SelectSessionAsync(network, TransportError, () => session,
            _ => Task.FromResult(false), default));
        await Assert.ThrowsAsync<HttpRequestException>(() => GameLauncher.SelectSessionAsync(network, TransportError,
            () => session, _ => Task.FromResult(true), default));
        await Assert.ThrowsAsync<AuthenticationServiceException>(() => GameLauncher.SelectSessionAsync(network,
            _ => throw new AuthenticationServiceException("api.minecraftservices.com", System.Net.HttpStatusCode.Forbidden,
                AuthenticationStage.Entitlements), () => session, _ => Task.FromResult(false), default));
    }

    private static MinecraftSession VerifiedSession() => new(new("00000000000000000000000000000001", "Synthetic",
        DateTimeOffset.UtcNow), "synthetic-verified-token", DateTimeOffset.UtcNow.AddHours(-1));
}
