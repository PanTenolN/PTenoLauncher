using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
using PTenoLauncher.Infrastructure.Updates;
using Xunit;
namespace PTenoLauncher.Tests;

public sealed class UpdaterHardeningTests
{
    [Theory] [InlineData("Committed", false)] [InlineData("RolledBack", false)] [InlineData("Committed", true)]
    public async Task CrashAfterTerminalCommitDoesNotStrandNextUpdateAndTamperedFilesArePreserved(string state, bool changed)
    {
        using var f = new LauncherUpdaterTests.Fixture(); var t = await f.Updater.PrepareAsync(await f.Offer(), f.AppRoot, true); await f.Updater.ApplyPreparedAsync(t.Id);
        if (state == "Committed") await f.Updater.ConfirmHealthAsync(t.Id, t.HealthToken, t.Offer.Release.Build);
        else await f.Updater.RollbackAsync(t.Id);
        var marker = LauncherUpdateRuntime.Marker(f.AppRoot); await AtomicFile.WriteJsonAsync(marker, new LauncherUpdateBinding(t.Id, f.Paths.Root));
        if (changed)
        {
            await File.WriteAllTextAsync(Path.Combine(f.AppRoot, "PTenoLauncher.dll"), "user changes");
            await Assert.ThrowsAsync<LauncherUpdateException>(() => LauncherUpdateRuntime.PendingAsync(f.Paths, f.AppRoot, f.Trust)); Assert.True(File.Exists(marker));
        }
        else { Assert.Null(await LauncherUpdateRuntime.PendingAsync(f.Paths, f.AppRoot, f.Trust)); Assert.False(File.Exists(marker)); }
    }
    [Theory]
    [InlineData("null")][InlineData("{}")][InlineData("{\"schema\":1,\"releases\":null}")]
    [InlineData("{\"schema\":1,\"schema\":2}")][InlineData("{\"schema\":1,\"Schema\":1}")]
    [InlineData("{\"schema\":1,\"releases\":[null]}")][InlineData("{\"unknown\":false}")]
    public void SignedMalformedPayloadIsRejectedAfterValidSignature(string json)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var data = System.Text.Encoding.UTF8.GetBytes(json);
        var trust = new LauncherUpdateTrust(new Dictionary<string, string> { ["test"] = key.ExportSubjectPublicKeyInfoPem() });
        var envelope = new SignedLauncherUpdates("test", data, key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Assert.ThrowsAny<Exception>(() => trust.Verify(envelope, DateTimeOffset.UtcNow));
    }
    [Theory] [InlineData("null")] [InlineData("{}")] [InlineData("{\"keyId\":null,\"payload\":null,\"signature\":null}")]
    [InlineData("{\"keyId\":\"test\",\"KeyId\":\"other\"}")]
    public async Task MalformedEnvelopeReturnsStatusWithoutCrashingStartup(string body)
    {
        using var f = new LauncherUpdaterTests.Fixture(); using var http = new HttpClient(new StubHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(body) }));
        var updater = new LauncherUpdater(f.Paths, new(http, f.Network), f.Network, f.Trust, f.Current);
        Assert.Null((await updater.CheckAsync("Portable", true)).Offer);
    }
    [Theory] [InlineData("lib", "lib/a.dll")] [InlineData("Lib/a.dll", "lib/b.dll")]
    [InlineData("a.dll", "A.dll")] [InlineData(".pteno-running.lock", "b.dll")]
    public void FileSetRejectsFileDirectoryAndCaseCollisions(string a, string b)
    {
        var hash = new string('a', 64);
        Assert.ThrowsAny<Exception>(() => LauncherUpdateTrust.ValidateFiles([new(a, 1, hash), new(b, 1, hash)]));
    }
    private sealed class Delayed : HttpMessageHandler
    { protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); } }
    [Fact] public async Task TimeoutIsUnavailableButCallerCancellationPropagates()
    {
        using var f = new LauncherUpdaterTests.Fixture(); using var http = new HttpClient(new Delayed());
        var updater = new LauncherUpdater(f.Paths, new(http, f.Network), f.Network, f.Trust, f.Current) { CheckTimeout = TimeSpan.FromMilliseconds(30) };
        Assert.Equal("UpdaterUnavailable", (await updater.CheckAsync("Portable", true)).State);
        using var cts = new CancellationTokenSource(); cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => updater.CheckAsync("Portable", true, cts.Token));
    }
    [Fact] public async Task CatalogReplayDoesNotOfferOlderSignedFeed()
    {
        using var f = new LauncherUpdaterTests.Fixture(); Assert.NotNull((await f.Updater.CheckAsync("Portable", true)).Offer);
        f.Envelope = f.Sign(new(1, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1), []));
        Assert.Equal("UpdaterReplay", (await f.Updater.CheckAsync("Portable", true)).State);
    }
    private sealed class Future : TimeProvider { public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddDays(10); }
    [Fact] public async Task ExpiredMetadataCanRecoverButCannotApplyPreparedUpdate()
    {
        using var f = new LauncherUpdaterTests.Fixture(); var t = await f.Updater.PrepareAsync(await f.Offer(), f.AppRoot, true);
        f.Updater.Clock = new Future();
        await Assert.ThrowsAsync<LauncherUpdateException>(() => f.Updater.ApplyPreparedAsync(t.Id));
        f.Network.Offline = true; await f.Updater.RollbackAsync(t.Id);
        Assert.Equal("RolledBack", (await f.Updater.ReadTransactionAsync(t.Id)).State);
    }
    [Fact] public async Task ParentIdentityCannotWaitForReusedOrForeignProcessAndReadLeaseBlocksReplacement()
    {
        using var current = Process.GetCurrentProcess(); var identity = UpdateProcessIdentity.Capture(current);
        await Assert.ThrowsAsync<LauncherUpdateException>(() => (identity with { StartTicks = identity.StartTicks - 1 }).WaitForExitAsync(TimeSpan.FromMilliseconds(30)));
        await Assert.ThrowsAsync<LauncherUpdateException>(() => identity.WaitForExitAsync(TimeSpan.FromMilliseconds(30)));
        using var f = new LauncherUpdaterTests.Fixture();
        using (LauncherUpdateGuards.ApplicationReadLease(f.AppRoot)) Assert.Throws<LauncherUpdateException>(() => LauncherUpdateGuards.ApplicationWriteLease(f.AppRoot));
        using (LauncherUpdateGuards.ApplicationWriteLease(f.AppRoot)) Assert.ThrowsAny<IOException>(() => LauncherUpdateGuards.ApplicationReadLease(f.AppRoot));
    }
    [Fact] public async Task ProfileAndServerGuardsDoNotTerminateActiveComponents()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync(); var p = ws.Profile(); await ws.Store.SaveProfileAsync(p);
        using (new ProfileUsage(ws.Paths).Acquire(p.Id)) await Assert.ThrowsAnyAsync<Exception>(() => LauncherUpdateGuards.ProfileLeases(ws.Paths, default));
        var directory = SafePath.Join(ws.Paths.Root, "servers/synthetic"); Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory, "active-server.json"), "{}");
        await Assert.ThrowsAsync<LauncherUpdateException>(() => LauncherUpdateGuards.ProfileLeases(ws.Paths, default));
        Assert.True(File.Exists(Path.Combine(directory, "active-server.json")));
    }
}
