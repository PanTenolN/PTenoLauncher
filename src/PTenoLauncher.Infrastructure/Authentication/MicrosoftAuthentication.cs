using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Identity.Client;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Security;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Authentication;

/// <summary>MSAL public client + Xbox/XSTS + Minecraft entitlement/profile verification.
/// Never accepts passwords, shared launcher client IDs or unverified offline identities.</summary>
public sealed partial class MicrosoftAuthentication(SecretVault vault, HttpClient http, NetworkPolicy network)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    // Xbox's protocol is case-sensitive. JsonContent's default Web options would
    // turn Properties/RpsTicket into properties/rpsTicket and cause HTTP 400.
    private static readonly JsonSerializerOptions ProtocolJson = new() { PropertyNamingPolicy = null };
    private IPublicClientApplication? app;
    public void Configure(string clientId)
    {
        if (!Guid.TryParse(clientId, out var id) || id == Guid.Empty)
            throw new ArgumentException("Потрібен власний Application (client) ID зареєстрованого public client.");
        app = PublicClientApplicationBuilder.Create(clientId).WithAuthority(AzureCloudInstance.AzurePublic, "consumers")
            .WithRedirectUri("http://localhost").Build();
        app.UserTokenCache.SetBeforeAccess(args =>
        {
            var data = vault.Read("msal");
            if (data is not null) args.TokenCache.DeserializeMsalV3(data);
        });
        app.UserTokenCache.SetAfterAccess(args =>
        {
            if (args.HasStateChanged) vault.Write("msal", args.TokenCache.SerializeMsalV3());
        });
    }
    public MinecraftSession? GetCachedSession()
    {
        var state = LoadAccounts();
        return state.Accounts.FirstOrDefault(a => a.Session.Account.Uuid == state.ActiveUuid)?.Session;
    }
    public async Task<MinecraftSession> SignInAsync(bool interactive, CancellationToken ct = default)
    {
        network.RequireOnline();
        if (app is null) throw new InvalidOperationException("Спочатку збережіть власний Microsoft Client ID у налаштуваннях.");
        await gate.WaitAsync(ct);
        try
        {
            var scopes = new[] { "XboxLive.signin" };
            var accounts = (await app.GetAccountsAsync()).ToArray();
            AuthenticationResult result;
            if (interactive)
                result = await app.AcquireTokenInteractive(scopes).WithUseEmbeddedWebView(false).WithPrompt(Prompt.SelectAccount).ExecuteAsync(ct);
            else
            {
                var state = LoadAccounts();
                var activeId = state.Accounts.FirstOrDefault(a => a.Session.Account.Uuid == state.ActiveUuid)?.MicrosoftId;
                var account = accounts.FirstOrDefault(a => a.HomeAccountId.Identifier == activeId);
                if (account is null && string.IsNullOrEmpty(activeId) && state.Accounts.Count == 1 && accounts.Length == 1) account = accounts[0];
                if (account is null) throw new MsalUiRequiredException("no_account", "Потрібен офіційний вхід для вибраного акаунта.");
                result = await app.AcquireTokenSilent(scopes, account).ExecuteAsync(ct);
            }
            using var xbox = await PostAsync("https://user.auth.xboxlive.com/user/authenticate", new
            {
                Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + result.AccessToken },
                RelyingParty = "http://auth.xboxlive.com", TokenType = "JWT"
            }, ct);
            using var xsts = await PostAsync("https://xsts.auth.xboxlive.com/xsts/authorize", new
            {
                Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xbox.RootElement.GetProperty("Token").GetString() } },
                RelyingParty = "rp://api.minecraftservices.com/", TokenType = "JWT"
            }, ct);
            var userHash = xsts.RootElement.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString();
            using var login = await PostAsync("https://api.minecraftservices.com/authentication/login_with_xbox",
                new { identityToken = $"XBL3.0 x={userHash};{xsts.RootElement.GetProperty("Token").GetString()}" }, ct);
            var token = login.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidDataException("Немає Minecraft access token.");
            using var entitlements = await GetAuthorizedAsync("https://api.minecraftservices.com/entitlements/mcstore", token, ct);
            if (!entitlements.RootElement.GetProperty("items").EnumerateArray().Any(e =>
                e.GetProperty("name").GetString() is "game_minecraft" or "product_minecraft"))
                throw new InvalidOperationException("Minecraft Java Edition не підтверджено для цього акаунта. Перевірте ліцензію або підписку офіційно.");
            using var profile = await GetAuthorizedAsync("https://api.minecraftservices.com/minecraft/profile", token, ct);
            var uuid = profile.RootElement.GetProperty("id").GetString()!;
            var name = profile.RootElement.GetProperty("name").GetString()!;
            if (!Guid.TryParseExact(uuid, "N", out _) || string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException("Некоректний Minecraft-профіль.");
            string? skinUrl = null;
            if (profile.RootElement.TryGetProperty("skins", out var skins))
                foreach (var skin in skins.EnumerateArray())
                    if (skin.TryGetProperty("state", out var skinState) && skinState.GetString() == "ACTIVE" && skin.TryGetProperty("url", out var url) && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var skinUri) && skinUri.Host == "textures.minecraft.net")
                        skinUrl = new UriBuilder(skinUri) { Scheme = "https", Port = -1 }.Uri.ToString();
            var session = new MinecraftSession(new(uuid, name, DateTimeOffset.UtcNow, skinUrl), token,
                DateTimeOffset.UtcNow.AddSeconds(login.RootElement.GetProperty("expires_in").GetInt32()));
            SaveSession(result.Account.HomeAccountId.Identifier, session);
            return session;
        }
        finally { gate.Release(); }
    }
    public async Task SignOutAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var state = LoadAccounts(); var selected = state.Accounts.FirstOrDefault(a => a.Session.Account.Uuid == state.ActiveUuid);
            if (app is not null && selected is not null && !string.IsNullOrEmpty(selected.MicrosoftId))
                foreach (var account in await app.GetAccountsAsync()) if (account.HomeAccountId.Identifier == selected.MicrosoftId) await app.RemoveAsync(account);
            var remaining = state.Accounts.Where(a => a.Session.Account.Uuid != state.ActiveUuid).ToArray();
            SaveAccounts(new(remaining.FirstOrDefault()?.Session.Account.Uuid, remaining));
        }
        finally { gate.Release(); }
    }
    internal async Task<JsonDocument> PostAsync(string uri, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body, options: ProtocolJson) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (request.RequestUri!.Host is "user.auth.xboxlive.com" or "xsts.auth.xboxlive.com")
            request.Headers.Add("x-xbl-contract-version", "1");
        return await SendAsync(request, ct);
    }
    private async Task<JsonDocument> GetAuthorizedAsync(string uri, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await SendAsync(request, ct);
    }
    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new AuthenticationServiceException(request.RequestUri!.Host, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream(); var bytes = new byte[8192]; int n;
        while ((n = await stream.ReadAsync(bytes, ct)) > 0)
        {
            if (buffer.Length + n > 1024 * 1024) throw new InvalidDataException("Відповідь авторизації завелика.");
            await buffer.WriteAsync(bytes.AsMemory(0, n), ct);
        }
        return JsonDocument.Parse(buffer.ToArray());
    }
}
