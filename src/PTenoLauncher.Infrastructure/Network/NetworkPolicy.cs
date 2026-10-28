namespace PTenoLauncher.Infrastructure.Network;

public sealed class NetworkPolicy
{
    public bool Offline { get; set; }
    public void RequireOnline()
    {
        if (Offline) throw new InvalidOperationException("Увімкнено офлайн-режим. Для цієї дії потрібен інтернет.");
    }
    public static void ValidateDownloadUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) ||
            !(uri.Host is "piston-meta.mojang.com" or "launchermeta.mojang.com" or "piston-data.mojang.com"
                or "launcher.mojang.com" or "libraries.minecraft.net" or "resources.download.minecraft.net"))
            throw new InvalidDataException("Завантаження з цього джерела не дозволене.");
    }
    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PTenoLauncher/0.2.0");
        return client;
    }
}