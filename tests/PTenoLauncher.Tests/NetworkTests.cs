using System.Net;
using System.Security.Cryptography;
using System.Text;
using PTenoLauncher.Infrastructure.Network;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public int Requests { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    { Requests++; return Task.FromResult(respond(request)); }
}
public sealed class NetworkTests
{
    private static readonly Uri Allowed = new("https://libraries.minecraft.net/test.jar");
    [Fact] public async Task VerifiedDownloadsCommitAtomicallyAndAreReusedOffline()
    {
        using var ws = new TestWorkspace(); var data = Encoding.UTF8.GetBytes("verified artifact");
        using var handler = new StubHandler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
        using var http = new HttpClient(handler); var policy = new NetworkPolicy();
        var downloader = new VerifiedDownloader(http, policy); var target = Path.Combine(ws.Paths.Cache, "artifact.jar");
        var hash = Convert.ToHexString(SHA1.HashData(data));
        await downloader.DownloadAsync(Allowed, target, hash, data.Length);
        policy.Offline = true;
        await downloader.DownloadAsync(Allowed, target, hash, data.Length);
        Assert.Equal(1, handler.Requests); Assert.Equal(data, await File.ReadAllBytesAsync(target));
    }
    [Fact] public async Task HashMismatchKeepsPreviousFileAndRemovesPartial()
    {
        using var ws = new TestWorkspace(); var data = Encoding.UTF8.GetBytes("tampered");
        using var handler = new StubHandler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
        using var http = new HttpClient(handler); var downloader = new VerifiedDownloader(http, new());
        var target = Path.Combine(ws.Paths.Cache, "artifact.jar"); await File.WriteAllTextAsync(target, "old");
        await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(Allowed, target, new string('a', 40), data.Length));
        Assert.Equal("old", await File.ReadAllTextAsync(target)); Assert.Empty(Directory.EnumerateFiles(ws.Paths.Cache, "*.part"));
    }
    [Fact] public async Task OfflineFailureNeverCallsNetwork()
    {
        using var ws = new TestWorkspace();
        using var handler = new StubHandler(_ => throw new Exception("Must not be called"));
        using var http = new HttpClient(handler);
        var downloader = new VerifiedDownloader(http, new() { Offline = true });
        await Assert.ThrowsAsync<InvalidOperationException>(() => downloader.DownloadAsync(Allowed, Path.Combine(ws.Paths.Cache, "a"), new string('a', 40), 2));
        Assert.Equal(0, handler.Requests);
    }
    [Fact] public async Task RedirectAndOversizeResponseAreRejected()
    {
        using var ws = new TestWorkspace();
        using var handler = new StubHandler(_ => new(HttpStatusCode.Redirect) { Headers = { Location = new("https://evil.example/file") } });
        using var http = new HttpClient(handler); var downloader = new VerifiedDownloader(http, new());
        await Assert.ThrowsAsync<HttpRequestException>(() => downloader.GetBytesAsync(Allowed, 5));
        using var large = new HttpClient(new StubHandler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[6]) }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new VerifiedDownloader(large, new()).GetBytesAsync(Allowed, 5));
    }
    [Theory]
    [InlineData("http://libraries.minecraft.net/file")][InlineData("https://libraries.minecraft.net.evil.example/file")]
    [InlineData("https://localhost/file")][InlineData("https://127.0.0.1/file")][InlineData("https://libraries.minecraft.net:444/file")]
    [InlineData("https://user@libraries.minecraft.net/file")]
    public void UntrustedDownloadSourcesAreRejected(string url) => Assert.Throws<InvalidDataException>(() => NetworkPolicy.ValidateDownloadUri(new(url)));
}