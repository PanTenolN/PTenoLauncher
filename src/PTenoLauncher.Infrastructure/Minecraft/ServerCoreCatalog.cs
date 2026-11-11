using System.Globalization;
using System.Net;
using System.Text.Json;
using PTenoLauncher.Infrastructure.Network;

namespace PTenoLauncher.Infrastructure.Minecraft;

public enum ServerCore { Vanilla, Fabric, Forge, Quilt, NeoForge, Paper, Purpur, Pumpkin }

internal sealed record ServerArtifact(Uri Uri, string Sha256, long Size, DownloadSource Source);

internal sealed class ServerCoreCatalog(VerifiedDownloader downloads)
{
    internal const string FabricGame = "1.21.1";
    internal const string FabricLoader = "0.20.1";
    private const string Paper = "https://fill.papermc.io/v3/projects/paper";
    private const string Purpur = "https://api.purpurmc.org/v2/purpur";
    private const string PurpurBuild = "2329";

    internal static bool SupportsPlugins(ServerCore core) => core is ServerCore.Paper or ServerCore.Purpur;

    internal static string? PinnedGame(ServerCore core) => core switch
    {
        ServerCore.Fabric => FabricGame,
        ServerCore.Forge => ForgeDedicatedServer.Game,
        _ => null
    };

    internal async Task<IReadOnlySet<string>> VersionsAsync(ServerCore core, CancellationToken ct)
    {
        if (PinnedGame(core) is { } game) return new HashSet<string>(StringComparer.Ordinal) { game };
        if (!SupportsPlugins(core)) return new HashSet<string>(StringComparer.Ordinal);
        using var doc = await GetAsync(core == ServerCore.Paper ? Paper : Purpur, Source(core), ct);
        var versions = doc.RootElement.GetProperty("versions");
        var entries = core == ServerCore.Paper
            ? versions.EnumerateObject().SelectMany(group => group.Value.EnumerateArray())
            : versions.EnumerateArray().AsEnumerable();
        return entries.Select(entry => Segment(entry.GetString())).ToHashSet(StringComparer.Ordinal);
    }

    internal async Task<IReadOnlyList<string>> BuildsAsync(ServerCore core, string game, CancellationToken ct)
    {
        game = Segment(game);
        if (core == ServerCore.Fabric) return game == FabricGame ? [FabricLoader] : [];
        if (core == ServerCore.Forge) return ForgeDedicatedServer.Supports(game, ForgeDedicatedServer.Build) ? [ForgeDedicatedServer.Build] : [];
        if (!SupportsPlugins(core)) return [];
        try
        {
            using var doc = await GetAsync(core == ServerCore.Paper ? $"{Paper}/versions/{game}/builds" : $"{Purpur}/{game}", Source(core), ct);
            if (core == ServerCore.Paper)
                return doc.RootElement.EnumerateArray().Where(entry => entry.GetProperty("channel").GetString() == "STABLE" &&
                    entry.GetProperty("downloads").TryGetProperty("server:default", out _))
                    .Select(entry => entry.GetProperty("id").GetInt32()).Where(id => id > 0).Distinct().OrderDescending()
                    .Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
            if (doc.RootElement.GetProperty("project").GetString() != "purpur" ||
                doc.RootElement.GetProperty("version").GetString() != game) throw new InvalidDataException("ServerBuildUnavailable");
            return game == FabricGame && doc.RootElement.GetProperty("builds").GetProperty("all").EnumerateArray()
                .Any(entry => entry.GetString() == PurpurBuild) ? [PurpurBuild] : [];
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound) { return []; }
    }

    internal async Task<ServerArtifact> ResolveAsync(ServerCore core, string game, string? build, CancellationToken ct)
    {
        game = Segment(game);
        build = Segment(build);
        if (core == ServerCore.Fabric && game == FabricGame && build == FabricLoader)
            return new(new($"https://meta.fabricmc.net/v2/versions/loader/{game}/{build}/1.1.2/server/jar"),
                "7638DC69EF2CECB27D1CFF1A1B0A6E2C3CED7ECE0000D584039A3F4E1386123C", 181840, DownloadSource.Fabric);
        if (core == ServerCore.Purpur && game == FabricGame && build == PurpurBuild)
        {
            using var doc = await GetAsync($"{Purpur}/{game}/{build}", DownloadSource.Purpur, ct);
            var root = doc.RootElement;
            // Purpur publishes MD5 only. Keep a SHA-256 pin for the independently downloaded, matching official build.
            if (root.GetProperty("project").GetString() != "purpur" || root.GetProperty("version").GetString() != game ||
                root.GetProperty("build").GetString() != build || root.GetProperty("result").GetString() != "SUCCESS" ||
                !string.Equals(root.GetProperty("md5").GetString(), "dc0026462cac76c869335feb43388a76", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("ServerBuildUnavailable");
            return new(new($"{Purpur}/{game}/{build}/download"),
                "30403CF54F981F16E1403F172645E82D3E4A59AD6C9F1D8E98DF99EDB1F8AE4C", 53071332, DownloadSource.Purpur);
        }
        if (core != ServerCore.Paper || !int.TryParse(build, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new InvalidDataException("ServerBuildUnavailable");
        using var paper = await GetAsync($"{Paper}/versions/{game}/builds/{build}", DownloadSource.Paper, ct);
        var info = paper.RootElement;
        if (info.GetProperty("id").GetInt32() != id || info.GetProperty("channel").GetString() != "STABLE")
            throw new InvalidDataException("ServerBuildUnavailable");
        var jar = info.GetProperty("downloads").GetProperty("server:default");
        var name = jar.GetProperty("name").GetString();
        var sha = jar.GetProperty("checksums").GetProperty("sha256").GetString();
        var size = jar.GetProperty("size").GetInt64();
        if (sha is not { Length: 64 } || !sha.All(Uri.IsHexDigit) || size is <= 0 or > 256L * 1024 * 1024 ||
            name != $"paper-{game}-{build}.jar" || !Uri.TryCreate(jar.GetProperty("url").GetString(), UriKind.Absolute, out var uri) ||
            uri.Host != "fill-data.papermc.io" || uri.AbsolutePath != $"/v1/objects/{sha.ToLowerInvariant()}/{name}")
            throw new InvalidDataException("ServerBuildUnavailable");
        TrustedSources.Validate(uri, DownloadSource.Paper);
        return new(uri, sha, size, DownloadSource.Paper);
    }

    internal static int JavaMajor(ServerCore core, string game, int mojang)
    {
        if (core != ServerCore.Paper) return mojang;
        if (!Version.TryParse(game, out var version)) throw new InvalidDataException("ServerBuildUnavailable");
        var paper = version.Major >= 26 ? 25 : version.Minor >= 20 ? 21 : version.Minor >= 17 ? 17 :
            version.Minor == 16 && version.Build >= 5 ? 16 : version.Minor >= 12 ? 11 : 8;
        return Math.Max(paper, mojang);
    }

    private async Task<JsonDocument> GetAsync(string url, DownloadSource source, CancellationToken ct) =>
        JsonDocument.Parse(await downloads.GetBytesAsync(new(url), 8 * 1024 * 1024, ct, source));

    private static DownloadSource Source(ServerCore core) => core == ServerCore.Paper ? DownloadSource.Paper : DownloadSource.Purpur;

    private static string Segment(string? value) =>
        value is { Length: > 0 and <= 100 } && value is not ("." or "..") && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_')
            ? value : throw new InvalidDataException("ServerBuildUnavailable");
}
