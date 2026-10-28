namespace PTenoLauncher.Infrastructure.Network;

public enum DownloadSource { Minecraft, Modrinth, Temurin, MinecraftSkin, Fabric, Quilt }
public static class TrustedSources
{
    public static void Validate(Uri uri, DownloadSource source)
    {
        if (source == DownloadSource.Minecraft) { NetworkPolicy.ValidateDownloadUri(uri); return; }
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0)
            throw new InvalidDataException("Джерело повинно використовувати HTTPS без сторонніх облікових даних.");
        bool allowed = source switch
        {
            DownloadSource.Quilt => uri.Host == "meta.quiltmc.org" && uri.AbsolutePath.StartsWith("/v3/",StringComparison.Ordinal) ||
                uri.Host == "maven.quiltmc.org" && uri.AbsolutePath.StartsWith("/repository/release/",StringComparison.Ordinal) || uri.Host == "maven.fabricmc.net",
            DownloadSource.Fabric => uri.Host is "meta.fabricmc.net" or "maven.fabricmc.net",
            DownloadSource.Modrinth => uri.Host is "api.modrinth.com" or "cdn.modrinth.com",
            DownloadSource.MinecraftSkin => uri.Host == "textures.minecraft.net" && uri.AbsolutePath.StartsWith("/texture/", StringComparison.Ordinal),
            DownloadSource.Temurin => uri.Host == "api.adoptium.net" ||
                uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/adoptium/temurin", StringComparison.Ordinal) && uri.AbsolutePath.Contains("-binaries/releases/download/", StringComparison.Ordinal) ||
                uri.Host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com",
            _ => false
        };
        if (!allowed) throw new InvalidDataException("Джерело завантаження не дозволене.");
    }
}