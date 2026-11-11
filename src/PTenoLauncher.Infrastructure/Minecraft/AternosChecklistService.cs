using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Diagnostics;
using PTenoLauncher.Infrastructure.Modpacks;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public enum AternosItemKind { ServerMod, ClientMod, Plugin, ResourcePack, Shader, UnclassifiedMod }
public enum AternosFormat { Txt, Csv, Json }

public sealed record AternosItem(AternosItemKind Kind, string File, string Name, string? Version, IReadOnlyList<string> Dependencies,
    bool NeedsReview, Uri? Project, [property: JsonIgnore] string? LocalFile, bool Installed = false, bool Included = true);

public sealed record AternosChecklist(string Source, string Minecraft, string Software, string? Build, IReadOnlyList<AternosItem> Items);

public sealed class AternosChecklistService(LauncherPaths paths, LocalModManager mods, ProfileContent content, ModrinthClient modrinth, VanillaServerManager servers)
{
    public const string PanelUrl = "https://aternos.org/go/";
    public const string CatalogUrl = "https://aternos.org/addons/";
    public const string ResourcePackGuide = "https://support.aternos.org/hc/en-us/articles/5013255309085-Adding-server-resource-packs-Minecraft-Java-Edition";

    public async Task<AternosChecklist> FromProfileAsync(GameProfile profile, CancellationToken ct = default)
    {
        var rows = new List<AternosItem>();
        var folder = SafePath.Join(paths.Game(profile.Id), "mods");
        foreach (var path in Files(folder, ".jar"))
        {
            ct.ThrowIfCancellationRequested();
            var file = Path.GetFileName(path);
            var name = Path.GetFileNameWithoutExtension(file);
            try
            {
                var mod = await mods.InspectPayloadAsync(path, file, ct, "aternos");
                var kind = mod.Loader != profile.Loader ? AternosItemKind.UnclassifiedMod : mod.Environment switch
                {
                    "client" => AternosItemKind.ClientMod,
                    "server" or "*" => AternosItemKind.ServerMod,
                    _ => AternosItemKind.UnclassifiedMod
                };
                var review = kind == AternosItemKind.UnclassifiedMod || mod.MetadataWarnings is { Count: > 0 };
                rows.Add(new(kind, file, mod.Name ?? name, mod.Version, mod.RequiredDependencies.ToArray(), review, null, path));
            }
            catch (Exception error) when (InstalledModReadiness.IsFileError(error))
            {
                rows.Add(new(AternosItemKind.UnclassifiedMod, file, name, null, [], true, null, null));
            }
        }

        await AddPacksAsync(ProfileContentKind.ResourcePacks, AternosItemKind.ResourcePack);
        await AddPacksAsync(ProfileContentKind.Shaders, AternosItemKind.Shader);
        var receipt = await AtomicFile.ReadJsonAsync<InstallationReceipt>(paths.Receipt(profile.Id), ct);
        var installedBuild = receipt?.Version == profile.MinecraftVersion && receipt.Loader == profile.Loader ? receipt.LoaderVersion : null;
        return new(profile.Name, profile.MinecraftVersion, profile.Loader.ToString(), profile.PreferredLoaderVersion ?? installedBuild, rows.Select(SafeRow).ToArray());

        async Task AddPacksAsync(ProfileContentKind type, AternosItemKind kind)
        {
            var packsFolder = content.Folder(profile.Id, type);
            foreach (var entry in await content.ListAsync(profile.Id, type, ct))
                rows.Add(new(kind, entry.FileName, entry.Name, null, [], entry.Directory, null,
                    entry.Directory ? null : SafePath.Join(packsFolder, entry.FileName)));
        }
    }

    public async Task<AternosChecklist> FromPluginServerAsync(LocalVanillaServer server, CancellationToken ct = default)
    {
        var folder = servers.PluginsFolder(server);
        var rows = new List<AternosItem>();
        foreach (var path in Files(folder, ".jar"))
        {
            ct.ThrowIfCancellationRequested();
            var file = Path.GetFileName(path);
            var name = Path.GetFileNameWithoutExtension(file);
            try
            {
                SafePath.RejectLinks(path);
                MrpackReader.CheckContainer(path, 50000);
                using var archive = ZipFile.OpenRead(path);
                var descriptor = archive.GetEntry("plugin.yml") ?? archive.GetEntry("paper-plugin.yml");
                if (descriptor is null) throw new InvalidDataException();
                var lines = Encoding.UTF8.GetString(await MrpackReader.EntryBytes(descriptor, 128 * 1024, ct)).Split('\n');
                string? Field(string key)
                {
                    var values = lines.Where(line => line.StartsWith(key + ":", StringComparison.Ordinal)).Take(2).ToArray();
                    return values.Length == 1 ? values[0][(key.Length + 1)..].Trim().Trim('\'', '"') : null;
                }
                var version = Field("version");
                var plugin = Field("name");
                var depends = Field("depend") ?? "";
                var dependencies = depends.StartsWith('[') && depends.EndsWith(']')
                    ? depends[1..^1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
                var review = version is null || plugin is null || depends.Length > 0 && dependencies.Length == 0 ||
                             lines.Any(line => line.StartsWith("dependencies:", StringComparison.Ordinal));
                rows.Add(new(AternosItemKind.Plugin, file, plugin ?? name, version, dependencies, review, null, path));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException)
            {
                rows.Add(new(AternosItemKind.Plugin, file, name, null, [], true, null, null));
            }
        }
        return new(server.Name, server.Version, server.Core.ToString(), server.Build, rows.Select(SafeRow).ToArray());
    }

    public async Task<AternosItem> MatchProjectAsync(AternosItem row, string minecraft, string software, CancellationToken ct = default)
    {
        if (row.LocalFile is not { } path) return row;
        var relative = Path.GetRelativePath(paths.Root, path).Replace('\\', '/');
        path = SafePath.Join(paths.Root, relative);
        string hash;
        long size;
        await using (var file = File.OpenRead(path))
        {
            size = file.Length;
            if (size is <= 0 or > 512L * 1024 * 1024) return row;
            hash = Convert.ToHexStringLower(await SHA512.HashDataAsync(file, ct));
        }
        var type = row.Kind switch
        {
            AternosItemKind.ResourcePack => "resourcepack", AternosItemKind.Shader => "shader",
            AternosItemKind.Plugin => "plugin", _ => "mod"
        };
        var version = await modrinth.FindAternosVersionAsync(hash, type, ct);
        if (version is null || version.File.Size != size || !version.GameVersions.Contains(minecraft, StringComparer.Ordinal) ||
            (type == "mod" && !version.Loaders.Contains(software, StringComparer.OrdinalIgnoreCase))) return row;
        var project = await modrinth.AternosProjectPageAsync(version.ProjectId, type, ct);
        if (project is null) return row;
        return SafeRow(row with
        {
            Version = version.Number,
            Project = project,
            Dependencies = row.Dependencies.Concat(version.Dependencies.Where(d => d.Kind == "required")
                .Select(d => d.ProjectId ?? d.VersionId ?? d.FileName).OfType<string>()).Distinct(StringComparer.Ordinal).ToArray()
        });
    }

    private static IEnumerable<string> Files(string folder, string extension)
    {
        SafePath.RejectLinks(folder);
        return Directory.Exists(folder)
            ? new DirectoryInfo(folder).EnumerateFiles().Where(file => (file.Attributes & FileAttributes.ReparsePoint) == 0 &&
                file.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase)).OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Take(1000).Select(file => file.FullName)
            : [];
    }

    public static byte[] Export(AternosChecklist checklist, AternosFormat format)
    {
        var rows = checklist.Items.Where(row => row.Included).Select(row => new
        {
            kind = row.Kind.ToString(), file = PublicText(row.File), name = PublicText(row.Name), version = PublicText(row.Version),
            dependencies = row.Dependencies.Take(256).Select(PublicText).Where(value => value.Length != 0).ToArray(),
            project = PublicProject(row.Project), review = row.NeedsReview || row.Project is null, row.Installed
        }).ToArray();
        var source = PublicText(checklist.Source);
        var minecraft = PublicText(checklist.Minecraft);
        var software = PublicText(checklist.Software);
        var build = PublicText(checklist.Build);
        if (format == AternosFormat.Json)
            return JsonSerializer.SerializeToUtf8Bytes(new
            { source, minecraft, software, build, aternosAvailability = "unverified", items = rows }, AtomicFile.JsonOptions);

        if (format == AternosFormat.Csv)
        {
            var csv = new StringBuilder("source,minecraft,software,build,category,file,name,version,dependencies,project,availability,manual_review,installed\r\n");
            foreach (var row in rows)
                csv.AppendJoin(',', new[] { source, minecraft, software, build, row.kind, row.file, row.name, row.version,
                    string.Join(" | ", row.dependencies), row.project, "unverified", row.review.ToString(), row.Installed.ToString() }.Select(CsvCell)).Append("\r\n");
            return Encoding.UTF8.GetBytes(csv.ToString());
        }

        var text = new StringBuilder().AppendJoin(" · ", source, minecraft, software, build).AppendLine()
            .AppendLine("Aternos availability: unverified");
        foreach (var row in rows)
        {
            text.Append(row.Installed ? "[x] " : "[ ] ").AppendJoin(" · ", row.kind, row.name, row.version, row.file);
            if (row.dependencies.Length != 0) text.Append(" · requires ").AppendJoin(", ", row.dependencies);
            if (row.project.Length != 0) text.Append(" · ").Append(row.project);
            if (row.review) text.Append(" · manual review");
            text.AppendLine();
        }
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private static AternosItem SafeRow(AternosItem item)
    {
        var file = PublicText(item.File);
        var name = PublicText(item.Name);
        var version = PublicText(item.Version);
        return item with { File = file, Name = name.Length > 0 ? name : file, Version = version.Length > 0 ? version : null,
            Dependencies = item.Dependencies.Take(256).Select(PublicText).Where(value => value.Length > 0).ToArray() };
    }

    private static string PublicText(string? value)
    {
        if (value is null) return "";
        var text = LauncherLog.Redact(value).Trim();
        if (text.Any(char.IsControl) || text.Contains('\\') || text.Contains('/') || text.Contains("file:", StringComparison.OrdinalIgnoreCase)) return "";
        return text.Length <= 255 ? text : text[..255];
    }

    private static string PublicProject(Uri? project) => project is { IsAbsoluteUri: true, Scheme: "https", Host: "modrinth.com",
        IsDefaultPort: true, UserInfo.Length: 0, Query.Length: 0, Fragment.Length: 0 } &&
        project.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries) is [var type, var id] &&
        type is "mod" or "plugin" or "resourcepack" or "shader" && id.Length is >= 1 and <= 128 &&
        id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_') ? project.AbsoluteUri : "";

    private static string CsvCell(string value) =>
        "\"" + (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' ? "'" : "") + value.Replace("\"", "\"\"") + "\"";
}
