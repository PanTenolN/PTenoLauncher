using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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

    [Theory]
    [InlineData("http://fill.papermc.io/v3/projects/paper", DownloadSource.Paper)]
    [InlineData("https://fill.papermc.io/v3/projects/paper-fork", DownloadSource.Paper)]
    [InlineData("https://api.purpurmc.org/v2/purpur?secret=abc", DownloadSource.Purpur)]
    [InlineData("https://api.minecraftservices.com/minecraft/profile/lookup/name/Player?token=abc", DownloadSource.MinecraftProfile)]
    public void ServerCatalogAndProfileRejectNoncanonicalSources(string url, DownloadSource source) =>
        Assert.Throws<InvalidDataException>(() => TrustedSources.Validate(new(url), source));

    [Fact]
    public async Task DefaultHttpTransportRejectsAnUntrustedCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var handshake = AcceptAsync();
        using var client = NetworkPolicy.CreateHttpClient();
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAsync($"https://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/", timeout.Token));
        Assert.Equal(HttpRequestError.SecureConnectionError, error.HttpRequestError);
        await handshake;

        async Task AcceptAsync()
        {
            using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var tls = new SslStream(connection.GetStream());
            try { await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, timeout.Token); }
            catch (Exception error) when (error is AuthenticationException or IOException) { }
        }
    }
}
