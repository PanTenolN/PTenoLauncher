using System.Net;
using System.Text.Json;
using PTenoLauncher.Infrastructure.Authentication;
using PTenoLauncher.Infrastructure.Diagnostics;
using PTenoLauncher.Infrastructure.Security;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class AuthenticationHttpTests
{
    [Fact]
    public async Task XboxUserRequestKeepsExactProtocolPropertyNamesAndContractHeader()
    {
        using var ws = new TestWorkspace();
        using var handler = new InspectRequest(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = body.RootElement;
            Assert.True(root.TryGetProperty("Properties", out var properties));
            Assert.Equal("RPS", properties.GetProperty("AuthMethod").GetString());
            Assert.Equal("user.auth.xboxlive.com", properties.GetProperty("SiteName").GetString());
            Assert.Equal("d=synthetic-microsoft-token", properties.GetProperty("RpsTicket").GetString());
            Assert.Equal("http://auth.xboxlive.com", root.GetProperty("RelyingParty").GetString());
            Assert.Equal("JWT", root.GetProperty("TokenType").GetString());
            Assert.False(root.TryGetProperty("properties", out _));
            CheckXboxHeaders(request);
        });
        using var http = new HttpClient(handler);
        var auth = new MicrosoftAuthentication(new SecretVault(ws.Paths), http, new());
        using var result = await auth.PostAsync("https://user.auth.xboxlive.com/user/authenticate", new
        {
            Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=synthetic-microsoft-token" },
            RelyingParty = "http://auth.xboxlive.com", TokenType = "JWT"
        }, CancellationToken.None);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task XstsRequestKeepsExactProtocolPropertyNamesAndContractHeader()
    {
        using var ws = new TestWorkspace();
        using var handler = new InspectRequest(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = body.RootElement;
            Assert.True(root.TryGetProperty("Properties", out var properties));
            Assert.Equal("RETAIL", properties.GetProperty("SandboxId").GetString());
            Assert.Equal("synthetic-xbox-token", properties.GetProperty("UserTokens")[0].GetString());
            Assert.Equal("rp://api.minecraftservices.com/", root.GetProperty("RelyingParty").GetString());
            Assert.Equal("JWT", root.GetProperty("TokenType").GetString());
            CheckXboxHeaders(request);
        });
        using var http = new HttpClient(handler);
        var auth = new MicrosoftAuthentication(new SecretVault(ws.Paths), http, new());
        using var result = await auth.PostAsync("https://xsts.auth.xboxlive.com/xsts/authorize", new
        {
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { "synthetic-xbox-token" } },
            RelyingParty = "rp://api.minecraftservices.com/", TokenType = "JWT"
        }, CancellationToken.None);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task MinecraftIdentityTokenKeepsItsOwnCaseWithoutXboxContractHeader()
    {
        using var ws = new TestWorkspace();
        using var handler = new InspectRequest(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("synthetic-identity-token", body.RootElement.GetProperty("identityToken").GetString());
            Assert.False(body.RootElement.TryGetProperty("IdentityToken", out _));
            Assert.False(request.Headers.Contains("x-xbl-contract-version"));
            Assert.Equal("application/json", request.Content.Headers.ContentType!.MediaType);
        });
        using var http = new HttpClient(handler);
        var auth = new MicrosoftAuthentication(new SecretVault(ws.Paths), http, new());
        using var result = await auth.PostAsync("https://api.minecraftservices.com/authentication/login_with_xbox",
            new { identityToken = "synthetic-identity-token" }, CancellationToken.None);
        Assert.False(http.DefaultRequestHeaders.Contains("x-xbl-contract-version"));
    }

    [Theory]
    [InlineData("https://user.auth.xboxlive.com/user/authenticate", HttpStatusCode.BadRequest)]
    [InlineData("https://xsts.auth.xboxlive.com/xsts/authorize", HttpStatusCode.Unauthorized)]
    public async Task ServiceFailureLogsOnlyHostAndHttpCodeWithoutRequestOrResponseSecrets(string endpoint, HttpStatusCode status)
    {
        using var ws = new TestWorkspace();
        using var http = new HttpClient(new StubHandler(_ => new(status)
        {
            Content = new StringContent("{\"Token\":\"synthetic-response-secret\",\"Message\":\"synthetic-private-detail\"}")
        }));
        var auth = new MicrosoftAuthentication(new SecretVault(ws.Paths), http, new());
        var failure = await Assert.ThrowsAsync<AuthenticationServiceException>(() => auth.PostAsync(endpoint,
            new { Properties = new { RpsTicket = "synthetic-request-secret" } }, CancellationToken.None));
        Assert.Equal(new Uri(endpoint).Host, failure.Host);
        Assert.Equal(status, failure.StatusCode);
        Assert.Contains(((int)status).ToString(), failure.Message);
        new LauncherLog(ws.Paths).Error("auth.test", failure);
        var log = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(ws.Paths.Logs, "launcher-*.log")));
        Assert.Contains("host=" + new Uri(endpoint).Host, log);
        Assert.Contains("http=" + (int)status, log);
        Assert.DoesNotContain("synthetic-", log);
        Assert.DoesNotContain("synthetic-", failure.Message);
    }

    private static void CheckXboxHeaders(HttpRequestMessage request)
    {
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https", request.RequestUri!.Scheme);
        Assert.Equal("1", Assert.Single(request.Headers.GetValues("x-xbl-contract-version")));
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Contains(request.Headers.Accept, h => h.MediaType == "application/json");
    }

    private sealed class InspectRequest(Func<HttpRequestMessage, Task> inspect) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            await inspect(request);
            return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }
}
