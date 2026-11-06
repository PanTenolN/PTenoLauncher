using System.Net;

namespace PTenoLauncher.Infrastructure.Network;

public sealed class InternetConnectivity(HttpMessageHandler? transport = null) : IDisposable
{
    private static readonly Uri[] Targets = [new("https://www.microsoft.com/"), new("https://www.cloudflare.com/")];
    private readonly HttpClient client = new(transport ?? new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    }) { Timeout = TimeSpan.FromSeconds(3) };

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        var results = await Task.WhenAll(Targets.Select(target => ProbeAsync(target, ct)));
        return results.Any(static available => available);
    }

    private async Task<bool> ProbeAsync(Uri target, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, target);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            return response.StatusCode != HttpStatusCode.ProxyAuthenticationRequired;
        }
        catch (HttpRequestException) { return false; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
    }

    public void Dispose() => client.Dispose();
}
