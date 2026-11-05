using Microsoft.Data.Sqlite;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Modpacks;

public sealed record ModrinthAppChoice(string Id, string Name, string Minecraft, string Loader, string? LoaderVersion)
{
    public override string ToString() => $"{Name} · {Minecraft} · {Loader} {LoaderVersion}".TrimEnd();
}

public sealed record ModrinthAppRequest(string InstanceId = "", string ProfileName = "", bool PinMods = false, string? ProfilesDirectory = null);
public sealed record ModrinthAppPreview(string Database, ModrinthAppRequest Request, IReadOnlyList<ModrinthAppChoice> Choices, InstancePackPreview Snapshot)
{
    public MrpackPreview Plan => Snapshot.Plan;
}

// The app stores instance identity separately from the applied content set. Read only these two tables.
public sealed class ModrinthAppReader(LauncherPaths paths, ILauncherStore store)
{
    internal const string SourceKind = "local-modrinth-app";
    internal const string Origin = "modrinth-app-applied-content-set";
    private sealed record SourceRow(ModrinthAppChoice Choice, string Folder, string ContentSetId);

    public async Task<ModrinthAppPreview> PreviewAsync(string database, ModrinthAppRequest request, CancellationToken ct = default)
    {
        database = Path.GetFullPath(database);
        var blockers = new List<MrpackProblem>();
        IReadOnlyList<SourceRow> rows = [];
        try { rows = await Task.Run(() => ReadRows(database, ct), ct); }
        catch (MrpackException e) { blockers.Add(new(e.Code, e.Detail)); }
        catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        { blockers.Add(new("ModrinthAppUnsupported", "")); }

        var selected = rows.SingleOrDefault(row => row.Choice.Id == request.InstanceId);
        if (selected is null && blockers.Count == 0) blockers.Add(new("ModrinthAppChoose", ""));
        var choice = selected?.Choice;
        var name = string.IsNullOrWhiteSpace(request.ProfileName) ? choice?.Name ?? "Modrinth App" : request.ProfileName.Trim();
        var minecraft = choice?.Minecraft ?? "";
        var loader = choice?.Loader switch
        {
            "vanilla" => ModLoader.Vanilla, "fabric" => ModLoader.Fabric, "quilt" => ModLoader.Quilt,
            "forge" => ModLoader.Forge, "neoforge" => ModLoader.NeoForge, _ => (ModLoader?)null
        };
        var version = choice?.LoaderVersion;
        if (loader == ModLoader.Forge && Version.TryParse(version, out _)) version = minecraft + "-" + version;
        if (selected is not null && (!InstancePackReader.Exact(minecraft) || loader is null ||
            (loader == ModLoader.Vanilla ? version is not null : version is null || !InstancePackReader.Exact(version)) ||
            loader == ModLoader.Forge && (!version!.StartsWith(minecraft + "-", StringComparison.Ordinal) ||
                !InstancePackReader.Exact(version[(minecraft.Length + 1)..]))))
            blockers.Add(new("ModrinthAppUnsupported", ""));
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl)) blockers.Add(new("ZipRequirements", "profile"));

        var directory = "";
        var content = new List<InstanceContentFile>();
        var excluded = new List<string>();
        var entries = new List<MrpackFilePreview>();
        var components = new List<InstanceComponent>();
        var metadataHash = Hash(selected);
        if (selected is not null && blockers.Count == 0)
        {
            try
            {
                var profiles = request.ProfilesDirectory ?? SafePath.Join(Path.GetDirectoryName(database)!, "profiles");
                directory = ResolveContent(profiles, selected.Folder);
                var payload = await InstancePackReader.ReadLocalPayloadAsync(paths, directory, ct);
                content = payload.Content;
                excluded = payload.Excluded;
                entries = payload.Entries;
            }
            catch (MrpackException e) { blockers.Add(new(e.Code, e.Detail)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
            { blockers.Add(new("ModrinthAppContent", "")); }

            if (content.Count == 0) blockers.Add(new("ZipContent", ""));
            components.Add(new("net.minecraft", minecraft));
            if (version is not null) components.Add(new(loader switch
            {
                ModLoader.Fabric => "net.fabricmc.fabric-loader", ModLoader.Quilt => "org.quiltmc.quilt-loader",
                ModLoader.Forge => "net.minecraftforge", _ => "net.neoforged"
            }, version));
        }

        PTenoLauncher.Infrastructure.Mods.ModCartReview? review = null;
        if (blockers.Count == 0)
        {
            try { review = await new InstancePackReader(paths, store).ReviewSourceAsync(directory, content, name, minecraft, loader!.Value, ct); }
            catch (MrpackException e) { blockers.Add(new(e.Code, e.Detail)); }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
            { blockers.Add(new("PackCompatibility", "")); }
        }
        if (review is not null) blockers.AddRange(review.Blockers.Select(b => new MrpackProblem("PackCompatibility", b)));
        var sourceHash = Hash(new { metadataHash, content, excluded });
        var fingerprint = Hash(new { database, directory, request, sourceHash });
        var plan = new MrpackPreview(directory, content.Sum(f => f.Size), sourceHash, name, "app-db-content-set", "", name,
            minecraft, loader ?? ModLoader.Vanilla, version, request.PinMods, [],
            content.Select(f => new MrpackFile(f.Path, f.Size, f.SourcePath, null, f.Path.Split('/')[0])).ToArray(), entries, blockers, fingerprint)
        { SourceKind = SourceKind, ContentRoot = "content", Excluded = excluded };
        return new(database, request, rows.Select(r => r.Choice).ToArray(),
            new(new(name, request.PinMods), plan, content, components, ["InstanceExclusions", "InstanceDefaults"], review, metadataHash));
    }

    private static string Hash<T>(T value) => MrpackReader.Hash(JsonSerializer.SerializeToUtf8Bytes(value, AtomicFile.JsonOptions));

    private string ResolveContent(string profiles, string folder)
    {
        if (folder.Length is < 1 or > 240 || folder.Contains('/') || profiles.StartsWith("\\\\", StringComparison.Ordinal))
            throw new MrpackException("ModrinthAppContent");
        var directory = SafePath.Join(profiles, folder);
        if (!Directory.Exists(directory)) throw new MrpackException("ModrinthAppContent");
        return directory;
    }

    private static IReadOnlyList<SourceRow> ReadRows(string file, CancellationToken ct)
    {
        if (Path.GetFileName(file) != "app.db" || file.StartsWith("\\\\", StringComparison.Ordinal)) throw new MrpackException("ModrinthAppUnsupported");
        SafePath.RejectLinks(file);
        using var snapshot = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (snapshot.Length is < 100 or > 128L * 1024 * 1024) throw new MrpackException("ModrinthAppUnsupported");
        RequireCheckpointed(file);
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = new Uri(file).AbsoluteUri + "?immutable=1", Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 2
        }.ToString());
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "PRAGMA trusted_schema=OFF; PRAGMA query_only=ON;";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT count(*) FROM pragma_table_list WHERE schema='main' AND type='table' AND name IN ('instances','instance_content_sets')";
        if (Convert.ToInt32(command.ExecuteScalar()) != 2) throw new MrpackException("ModrinthAppUnsupported");
        command.CommandText = """
            SELECT i.id,i.path,i.name,cs.id,cs.game_version,cs.loader,cs.loader_version
            FROM main.instances i
            LEFT JOIN main.instance_content_sets cs ON cs.id=i.applied_content_set_id AND cs.instance_id=i.id
            ORDER BY i.name,i.id LIMIT 257
            """;
        using var reader = command.ExecuteReader();
        var rows = new List<SourceRow>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            var id = ReadField(reader, 0, 64);
            if (rows.Count == 256 || !ValidId(id) || !ids.Add(id)) throw new MrpackException("ModrinthAppUnsupported");
            var choice = new ModrinthAppChoice(id, ReadField(reader, 2, 80), ReadField(reader, 4, 100), ReadField(reader, 5, 32),
                reader.IsDBNull(6) ? null : ReadField(reader, 6, 100));
            rows.Add(new(choice, ReadField(reader, 1, 240), ReadField(reader, 3, 64)));
        }
        RequireCheckpointed(file);
        return rows;
    }

    private static string ReadField(SqliteDataReader reader, int ordinal, int limit)
    {
        if (reader.IsDBNull(ordinal)) return "";
        if (reader.GetValue(ordinal) is not string text || text.Length > limit || text.Any(char.IsControl))
            throw new MrpackException("ModrinthAppUnsupported");
        return text;
    }

    internal static bool ValidId(string? id) => id is { Length: > 0 and <= 64 } &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static void RequireCheckpointed(string file)
    {
        foreach (var suffix in new[] { "-wal", "-journal", "-shm" })
        {
            var sidecar = file + suffix;
            SafePath.RejectLinks(sidecar);
            if (File.Exists(sidecar) && new FileInfo(sidecar).Length > 0) throw new MrpackException("ModrinthAppClose");
        }
    }
}
