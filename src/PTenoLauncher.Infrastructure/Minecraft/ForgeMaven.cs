using System.Text.RegularExpressions;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed record ForgeCoordinate(string Group, string Artifact, string Version, string? Classifier, string Extension)
{
    public string Path => $"{Group.Replace('.', '/')}/{Artifact}/{Version}/{Artifact}-{Version}" + (Classifier is null ? "" : "-" + Classifier) + "." + Extension;
    public static ForgeCoordinate Parse(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 400) throw new InvalidDataException("Invalid Maven coordinate.");
        var extension = value.Split('@'); var parts = extension[0].Split(':');
        if (extension.Length > 2 || parts.Length is < 3 or > 4 ||
            parts.Any(p => p.Length is 0 or > 128 || p is "." or ".." || !p.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '+')) ||
            parts[0].Split('.').Any(p => string.IsNullOrEmpty(p))) throw new InvalidDataException("Unsafe Maven coordinate.");
        var ext = extension.Length == 2 ? extension[1] : "jar";
        if (ext.Length is 0 or > 12 || !ext.All(char.IsAsciiLetterOrDigit)) throw new InvalidDataException("Invalid Maven extension.");
        return new(parts[0], parts[1], parts[2], parts.Length == 4 ? parts[3] : null, ext);
    }
}

internal static class ForgeMaven
{
    public static string Segment(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 || value is "." or ".." ||
            !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '+')) throw new InvalidDataException("Invalid loader version.");
        return value;
    }
    public static DownloadSource Source(ModLoader loader) => loader switch { ModLoader.Forge => DownloadSource.Forge, ModLoader.NeoForge => DownloadSource.NeoForge, _ => throw new ArgumentOutOfRangeException(nameof(loader)) };
    public static string Repository(ModLoader loader) => loader == ModLoader.Forge ? "https://maven.minecraftforge.net/" : loader == ModLoader.NeoForge ? "https://maven.neoforged.net/releases/" : throw new ArgumentOutOfRangeException(nameof(loader));
    public static ForgeCoordinate Installer(ModLoader loader, string version) => ForgeCoordinate.Parse((loader == ModLoader.Forge ? "net.minecraftforge:forge:" : loader == ModLoader.NeoForge ? "net.neoforged:neoforge:" : throw new ArgumentOutOfRangeException(nameof(loader))) + Segment(version) + ":installer");
    public static Uri Metadata(ModLoader loader) => new(Repository(loader) + (loader == ModLoader.Forge ? "net/minecraftforge/forge/" : "net/neoforged/neoforge/") + "maven-metadata.xml");
    public static void ValidateArtifactUri(Uri uri, ForgeCoordinate artifact, ModLoader loader)
    {
        TrustedSources.Validate(uri, Source(loader));
        var expected = uri.Host == "maven.neoforged.net" ? "/releases/" + artifact.Path : "/" + artifact.Path;
        if (uri.AbsolutePath != expected || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsoluteUri.Contains('%'))
            throw new InvalidDataException("Artifact URL does not match its Maven coordinate.");
    }
}
