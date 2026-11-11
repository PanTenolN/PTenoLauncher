namespace PTenoLauncher.Infrastructure.Network;

internal sealed class NetworkOfflineException(string message) : InvalidOperationException(message);

public sealed class NetworkPolicy
{
    private const string OfflineMessage = "Увімкнено офлайн-режим. Для цієї дії потрібен інтернет.";
    private volatile bool offline;
    public bool Offline
    {
        get => offline;
        set
        {
            if (offline == value) return;
            offline = value;
            if (value) WentOffline?.Invoke();
        }
    }
    internal event Action? WentOffline;
    public void RequireOnline()
    {
        if (Offline) throw new InvalidOperationException(OfflineMessage);
    }
    internal void RequireOnlineForLaunchAuthentication()
    {
        if (Offline) throw new NetworkOfflineException(OfflineMessage);
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PTenoLauncher/" + Updates.LauncherBuild.Current.Version);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("(https://github.com/PanTenolN/PTenoLauncher)");
        return client;
    }
}
