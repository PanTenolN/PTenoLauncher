namespace PTenoLauncher.Infrastructure.Network;

public enum DownloadSource { Minecraft, Modrinth, Temurin, MinecraftSkin, Fabric, Quilt, Forge, NeoForge, LauncherUpdate, Mrpack }
public sealed class UntrustedMrpackSourceException(string host) : IOException("Unsupported modpack download origin: " + host)
{ public string Host { get; } = host; }
public static class TrustedSources
{
    public static void Validate(Uri uri, DownloadSource source)
    {
        if (source == DownloadSource.Mrpack) { ValidateMrpack(uri, initial: false); return; }
        if (source == DownloadSource.Minecraft) { NetworkPolicy.ValidateDownloadUri(uri); return; }
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0)
            throw new InvalidDataException("Джерело повинно використовувати HTTPS без сторонніх облікових даних.");
        bool allowed = source switch
        {
            DownloadSource.LauncherUpdate => uri.Fragment.Length == 0 &&
                (uri.Host == "github.com" && uri.Query.Length == 0 && (uri.AbsolutePath.StartsWith("/PanTenolN/PTenoLauncher/releases/download/", StringComparison.Ordinal) || uri.AbsolutePath == "/PanTenolN/PTenoLauncher/releases/latest/download/launcher-updates.json") ||
                 uri.Host == "release-assets.githubusercontent.com"),
            DownloadSource.Forge => uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
                (uri.Host is "maven.minecraftforge.net" or "libraries.minecraft.net" ||
                 uri.Host == "files.minecraftforge.net" && uri.AbsolutePath == "/net/minecraftforge/forge/promotions_slim.json"),
            DownloadSource.NeoForge => uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
                (uri.Host == "maven.neoforged.net" && uri.AbsolutePath.StartsWith("/releases/", StringComparison.Ordinal) ||
                 uri.Host is "maven.minecraftforge.net" or "libraries.minecraft.net"),
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
    public static void ValidateMrpack(Uri uri, bool initial)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            !(uri.Host is "cdn.modrinth.com" or "github.com" or "raw.githubusercontent.com" or "gitlab.com" ||
              !initial && (uri.Host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com")))
            throw new UntrustedMrpackSourceException(uri.IsAbsoluteUri ? uri.Host : "relative URI");
    }
}
