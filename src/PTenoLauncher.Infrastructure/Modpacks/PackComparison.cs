using System.IO.Compression;
using System.Security.Cryptography;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Modpacks;

public sealed record ComparedPackFile(string Path, string Sha512, string Source, string? Project, string? VersionId, string? Version);
public sealed record ComparedPack(string Name, string Minecraft, ModLoader Loader, string? LoaderVersion, IReadOnlyList<ComparedPackFile> Files);
public sealed record PackDifference(string Kind, ComparedPackFile? Left, ComparedPackFile? Right);
public sealed record PackComparisonResult(ComparedPack Left, ComparedPack Right, IReadOnlyList<PackDifference> Files)
{
    public bool SameTarget => Left.Minecraft == Right.Minecraft && Left.Loader == Right.Loader && Left.LoaderVersion == Right.LoaderVersion;
}

public sealed class PackComparison(LauncherPaths paths)
{
    // The import reader validates archive paths, size budgets, and declared digests. It does not download anything.
    private readonly MrpackReader reader = new(paths, new NetworkPolicy());

    public async Task<PackComparisonResult> CompareAsync(string leftFile, string rightFile, CancellationToken ct = default)
    {
        var left = await InspectAsync(leftFile, ct);
        var right = await InspectAsync(rightFile, ct);
        var available = right.Files.ToList();
        var differences = new List<PackDifference>();
        foreach (var a in left.Files)
        {
            ct.ThrowIfCancellationRequested();
            var byProject = a.Project is null ? [] : available.Where(b => b.Project == a.Project).Take(2).ToArray();
            var byHash = available.Where(b => b.Sha512 == a.Sha512).Take(2).ToArray();
            var b = byProject.Length == 1 ? byProject[0] :
                available.FirstOrDefault(b => b.Path.Equals(a.Path, StringComparison.OrdinalIgnoreCase)) ??
                (byHash.Length == 1 ? byHash[0] : null);
            if (b is not null) available.Remove(b);
            var kind = b is null ? "PackCompareLeftOnly" :
                a.VersionId is not null && b.VersionId is not null && a.VersionId != b.VersionId ? "PackCompareVersion" :
                a.Sha512 != b.Sha512 ? "PackCompareHash" :
                a.Source != b.Source || a.Path != b.Path ? "PackCompareLayout" : "PackCompareSame";
            differences.Add(new(kind, a, b));
        }
        differences.AddRange(available.Select(b => new PackDifference("PackCompareRightOnly", null, b)));
        return new(left, right, differences);
    }

    private async Task<ComparedPack> InspectAsync(string file, CancellationToken ct)
    {
        file = Path.GetFullPath(file);
        SafePath.RejectLinks(file);
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        if (stream.Length > MrpackReader.MaxArchiveBytes) throw new MrpackException("PackBudget");
        MrpackReader.CheckContainer(file);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var index = zip.GetEntry("modrinth.index.json") ?? throw new MrpackException("PackUnsupported");
        using var doc = MrpackReader.Json(await MrpackReader.EntryBytes(index, MrpackReader.MaxIndexBytes, ct));
        var root = doc.RootElement;
        var optional = root.ValueKind == System.Text.Json.JsonValueKind.Object &&
            root.TryGetProperty("files", out var list) && list.ValueKind == System.Text.Json.JsonValueKind.Array
            ? list.EnumerateArray().Where(item => item.ValueKind == System.Text.Json.JsonValueKind.Object &&
                item.TryGetProperty("env", out var env) && env.ValueKind == System.Text.Json.JsonValueKind.Object &&
                env.TryGetProperty("client", out var client) && client.ValueKind == System.Text.Json.JsonValueKind.String &&
                client.GetString() == "optional" && item.TryGetProperty("path", out var path) &&
                path.ValueKind == System.Text.Json.JsonValueKind.String).Select(item => item.GetProperty("path").GetString()!).ToArray() : [];
        var pack = await reader.PreviewAsync(file, optional: optional, ct: ct);
        if (!pack.CanImport) throw new MrpackException(pack.Blockers[0].Code, pack.Blockers[0].Detail);
        var metadata = PackExportIndex.ReadFiles(root).ToDictionary(item => item.Path, StringComparer.Ordinal);
        var files = new List<ComparedPackFile>();
        foreach (var item in pack.Files.Where(item => ZipPackReader.Eligible(item.Path)))
        {
            ct.ThrowIfCancellationRequested();
            metadata.TryGetValue(item.Path, out var descriptor);
            var hash = item.Download?.Sha512;
            if (hash is null)
            {
                var entry = zip.GetEntry(item.Entry!) ?? throw new MrpackException("PackIntegrity", item.Path);
                await using var input = entry.Open();
                hash = Convert.ToHexStringLower(await SHA512.HashDataAsync(input, ct));
            }
            var project = descriptor?.ProjectId;
            var version = descriptor?.VersionId;
            if (project is null && item.Download?.Downloads.FirstOrDefault() is { Host: "cdn.modrinth.com" } url)
            {
                var parts = Uri.UnescapeDataString(url.AbsolutePath).Split('/');
                if (parts.Length == 6 && PackExporter.OfficialFile(url, parts[2], parts[4], Path.GetFileName(item.Path)))
                    (project, version) = (parts[2], parts[4]);
            }
            files.Add(new(item.Path, hash, item.Download is null ? "PackCompareIncluded" : "PackCompareLinked", project, version, descriptor?.Version));
        }
        files.AddRange(metadata.Values.Where(item => item.Action == "Skip").Select(item =>
            new ComparedPackFile(item.Path, item.Sha512, "PackCompareManual", item.ProjectId, item.VersionId, item.Version)));
        return new(pack.Name, pack.Minecraft, pack.Loader, pack.LoaderVersion, files.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
