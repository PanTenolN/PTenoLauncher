using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Modpacks;

internal static class PackExportIndex
{
    internal sealed record Entry(string Path, string Action, long Size, string Sha512, string? ProjectId,
        string? VersionId, string? Version, string? License);
    private sealed record ExportMetadata(int Schema, Entry[] Files);
    internal static IReadOnlyList<Entry> ReadFiles(JsonElement root) =>
        root.TryGetProperty("ptenoExport", out var extension) && extension.Deserialize<ExportMetadata>(AtomicFile.JsonOptions) is { Schema: 1 } metadata
            ? metadata.Files : [];

    internal static bool Disclosed(PackExportItem item) => item.Action != PackExportAction.Skip ||
        item.Reason is "ExportUnknownSource" or "ExportUnknownLicense" or "ExportUnusableSource";

    internal static Dictionary<string, string> Dependencies(string minecraft, ModLoader target, string? loaderVersion)
    {
        var dependencies = new Dictionary<string, string> { ["minecraft"] = minecraft };
        var loader = target switch
        {
            ModLoader.Fabric => "fabric-loader", ModLoader.Quilt => "quilt-loader",
            ModLoader.Forge => "forge", ModLoader.NeoForge => "neoforge", _ => null
        };
        if (loader is not null) dependencies.Add(loader, loaderVersion!);
        return dependencies;
    }

    internal static byte[] Build(PackExportPlan plan)
    {
        var dependencies = Dependencies(plan.Minecraft, plan.Loader, plan.LoaderVersion);
        var links = plan.Items.Where(i => i.Action == PackExportAction.Link).Select(i => new
        {
            path = i.Path, fileSize = i.Size, hashes = new { sha1 = i.Sha1, sha512 = i.Sha512 },
            env = new { client = "required", server = "unsupported" },
            downloads = new[] { i.Url!.AbsoluteUri }
        }).ToArray();
        var metadata = plan.Items.Where(Disclosed)
            .Select(i => new Entry(i.Path, i.Action.ToString(), i.Size, i.Sha512, i.ProjectId, i.VersionId, i.Version, i.License)).ToArray();
        var id = MrpackReader.Hash(JsonSerializer.SerializeToUtf8Bytes(new { plan.Name, dependencies, links, metadata }, AtomicFile.JsonOptions));
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            formatVersion = 1, game = "minecraft", versionId = "pteno-" + id[..12], name = plan.Name,
            files = links, dependencies, ptenoExport = new ExportMetadata(1, metadata)
        }, AtomicFile.JsonOptions);
    }

    internal static async Task<IReadOnlyList<string>> ValidateAsync(JsonElement root, ZipArchive zip,
        IReadOnlyList<MrpackDownload> downloads, CancellationToken ct)
    {
        if (!root.TryGetProperty("ptenoExport", out var extension)) return [];
        var metadata = extension.Deserialize<ExportMetadata>(AtomicFile.JsonOptions);
        if (metadata is not { Schema: 1, Files.Length: <= 129 }) throw MrpackReader.Error("PackUnsupported");
        var embedded = zip.Entries.Where(e => e.FullName != "modrinth.index.json" && !e.FullName.EndsWith('/'))
            .ToDictionary(e => e.FullName, StringComparer.Ordinal);
        var linked = downloads.ToDictionary(d => d.Path, StringComparer.Ordinal);
        var manual = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in metadata.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (file is null || file.Path is null || file.Sha512 is not { Length: 128 } || !file.Sha512.All(Uri.IsHexDigit) ||
                file.Size is <= 0 or > MrpackReader.MaxArchiveBytes || !seen.Add(file.Path) ||
                !(MrpackReader.RootJar(file.Path) && ZipPackReader.Eligible(file.Path) || file.Path == PackExporter.GraphicsPath && file.Action == "Include"))
                throw MrpackReader.Error("PackInvalid");
            switch (file.Action)
            {
                case "Skip":
                    manual.Add(file.Path);
                    break;
                case "Link":
                    if (file.ProjectId is null || file.VersionId is null || !linked.Remove(file.Path, out var link) ||
                        link.Size != file.Size || link.Sha512 != file.Sha512 || link.Downloads.Count != 1 || link.Client != "required" ||
                        !PackExporter.OfficialFile(link.Downloads[0], file.ProjectId, file.VersionId, Path.GetFileName(file.Path)))
                        throw MrpackReader.Error("PackIntegrity", file.Path);
                    break;
                case "Include":
                    if (!embedded.Remove("overrides/" + file.Path, out var entry) || entry.Length != file.Size)
                        throw MrpackReader.Error("PackIntegrity", file.Path);
                    await using (var input = entry.Open())
                        if (Convert.ToHexStringLower(await SHA512.HashDataAsync(input, ct)) != file.Sha512)
                            throw MrpackReader.Error("PackIntegrity", file.Path);
                    break;
                default:
                    throw MrpackReader.Error("PackUnsupported");
            }
        }
        if (embedded.Count != 0 || linked.Count != 0) throw MrpackReader.Error("PackIntegrity");
        return manual;
    }
}
