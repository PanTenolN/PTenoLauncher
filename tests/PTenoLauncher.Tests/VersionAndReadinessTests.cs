using System.Net;
using System.Security.Cryptography;
using System.Text;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Authentication;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class VersionAndReadinessTests
{
    [Fact] public async Task MojangCatalogAndVerifiedManifestRemainUsableOffline()
    {
        using var ws = new TestWorkspace();
        var version = Encoding.UTF8.GetBytes("""{"id":"1.21.1","mainClass":"net.minecraft.client.main.Main"}""");
        var hash = Convert.ToHexString(SHA1.HashData(version)).ToLowerInvariant();
        var catalog = Encoding.UTF8.GetBytes($$"""
            {"latest":{"release":"1.21.1"},"versions":[{"id":"1.21.1","type":"release","url":"https://piston-meta.mojang.com/test.json","sha1":"{{hash}}","releaseTime":"2024-08-08T00:00:00Z"}]}
            """);
        using var handler = new StubHandler(r => new(HttpStatusCode.OK) { Content = new ByteArrayContent(r.RequestUri == VersionManager.ManifestUri ? catalog : version) });
        using var http = new HttpClient(handler); var policy = new NetworkPolicy();
        var manager = new VersionManager(ws.Paths, new(http, policy));
        var online = await manager.RefreshAsync();
        using (var manifest = await manager.GetVersionAsync(online.Versions[0])) Assert.Equal("1.21.1", manifest.RootElement.GetProperty("id").GetString());
        policy.Offline = true;
        var offline = await manager.GetCachedAsync();
        using var cachedManifest = await manager.GetVersionAsync(offline!.Versions[0]);
        Assert.Equal(2, handler.Requests);
        Assert.Equal("1.21.1", cachedManifest.RootElement.GetProperty("id").GetString());
    }
    [Fact] public async Task ReadinessNeverClaimsUninstalledOrUnauthenticatedProfileIsReady()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync();
        var profile = ws.Profile(); await ws.Store.SaveProfileAsync(profile);
        using var handler = new StubHandler(_ => throw new Exception("No network expected")); using var http = new HttpClient(handler);
        var auth = new MicrosoftAuthentication(new(ws.Paths), http, new() { Offline = true });
        var report = await new OfflineReadiness(ws.Paths, new(), auth).CheckAsync(profile);
        Assert.False(report.Ready);
        Assert.Contains(report.Issues, i => i.Code == "game.missing");
        Assert.Contains(report.Issues, i => i.Code == "java.missing");
        Assert.Contains(report.Issues, i => i.Code == "account.missing");
        Assert.Equal(0, handler.Requests);
    }
    [Fact] public void ReceiptCannotReferToUnverifiedClasspathFiles()
    {
        using var ws = new TestWorkspace();
        var receipt = new InstallationReceipt("1.21.1", 21, "net.minecraft.client.main.Main", DateTimeOffset.UtcNow,
            [new("assets/indexes/17.json", new string('a',40), "SHA1", 1)], ["unverified.jar"], [], [], [], "17");
        Assert.Throws<InvalidDataException>(() => ReceiptValidator.Validate(ws.Paths, receipt));
    }
    [Fact] public async Task CorruptReceiptIsReportedInsteadOfAllowingLaunch()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync(); var profile = ws.Profile(); await ws.Store.SaveProfileAsync(profile);
        await File.WriteAllTextAsync(ws.Paths.Receipt(profile.Id), "{}");
        using var http = new HttpClient(new StubHandler(_ => throw new Exception()));
        var auth = new MicrosoftAuthentication(new(ws.Paths), http, new() { Offline = true });
        var report = await new OfflineReadiness(ws.Paths, new(), auth).CheckAsync(profile);
        Assert.Contains(report.Issues, i => i.Code == "manifest.corrupt");
        Assert.False(report.Ready);
    }
}