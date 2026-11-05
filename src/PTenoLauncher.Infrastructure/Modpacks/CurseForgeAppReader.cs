using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Modpacks;

// Strict subset of the local minecraftinstance.json shape consumed by Modrinth App's CurseForge importer.
public sealed class CurseForgeAppReader(LauncherPaths paths, ILauncherStore store)
{
    internal const string SourceKind = "local-curseforge-app";
    internal const string Origin = "curseforge-app-minecraftinstance";

    public async Task<InstancePackPreview> PreviewAsync(string directory, InstancePackRequest request, CancellationToken ct = default)
    {
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var name = request.ProfileName.Trim();
        var minecraft = "";
        var loader = ModLoader.Vanilla;
        string? version = null;
        var metadataHash = MrpackReader.Hash([]);
        var blockers = new List<MrpackProblem>();
        var components = new List<InstanceComponent>();
        var payload = new LocalInstancePayload([], [], []);
        ModCartReview? review = null;

        try
        {
            if (directory.StartsWith("\\\\", StringComparison.Ordinal)) throw new MrpackException("InstanceExternal");
            var bytes = await AtomicFile.ReadBytesAsync(SafePath.Join(directory, "minecraftinstance.json"), 1024 * 1024, ct);
            using var document = MrpackReader.Json(bytes);
            var root = document.RootElement;
            minecraft = ReadText(root, "gameVersion");
            if (!InstancePackReader.Exact(minecraft)) throw new MrpackException("CurseForgeAppUnsupported");
            if (name.Length == 0) name = ReadText(root, "name", required: false);
            if (name.Length == 0) name = Path.GetFileName(directory);
            if (name.Length is < 1 or > 80 || name.Any(char.IsControl)) throw new MrpackException("ZipRequirements", "profile");
            components.Add(new("net.minecraft", minecraft));
            if (root.TryGetProperty("baseModLoader", out var baseLoader) && baseLoader.ValueKind != JsonValueKind.Null)
            {
                var exact = ReadText(baseLoader, "name");
                if (exact.StartsWith("forge-", StringComparison.Ordinal) && Version.TryParse(exact[6..], out _))
                {
                    loader = ModLoader.Forge;
                    version = minecraft + "-" + exact[6..];
                    components.Add(new("net.minecraftforge", version));
                }
                else if (exact.StartsWith("fabric-", StringComparison.Ordinal) && exact.EndsWith("-" + minecraft, StringComparison.Ordinal) && exact.Length > minecraft.Length + 8)
                {
                    version = exact[7..^(minecraft.Length + 1)];
                    if (!InstancePackReader.Exact(version)) throw new MrpackException("CurseForgeAppUnsupported");
                    loader = ModLoader.Fabric;
                    components.Add(new("net.fabricmc.fabric-loader", version));
                }
                else throw new MrpackException("CurseForgeAppUnsupported");
            }
            metadataHash = MrpackReader.Hash(bytes);
            payload = await InstancePackReader.ReadLocalPayloadAsync(paths, directory, ct);
            if (payload.Content.Count == 0) blockers.Add(new("ZipContent", ""));
            else review = await new InstancePackReader(paths, store).ReviewSourceAsync(directory, payload.Content, name, minecraft, loader, ct);
        }
        catch (MrpackException e) { blockers.Add(new(e.Code, e.Detail)); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        { blockers.Add(new("CurseForgeAppUnsupported", "")); }

        if (review is not null) blockers.AddRange(review.Blockers.Select(b => new MrpackProblem("PackCompatibility", b)));
        var effective = request with { ProfileName = name };
        var sourceHash = MrpackReader.Hash(JsonSerializer.SerializeToUtf8Bytes(new { metadataHash, payload.Content, payload.Excluded }, AtomicFile.JsonOptions));
        var fingerprint = MrpackReader.Hash(JsonSerializer.SerializeToUtf8Bytes(new { directory, effective, sourceHash }, AtomicFile.JsonOptions));
        var plan = new MrpackPreview(directory, payload.Content.Sum(f => f.Size), sourceHash, name, "minecraftinstance-json", "", name,
            minecraft, loader, version, request.PinMods, [],
            payload.Content.Select(f => new MrpackFile(f.Path, f.Size, f.SourcePath, null, f.Path.Split('/')[0])).ToArray(),
            payload.Entries, blockers, fingerprint) { SourceKind = SourceKind, ContentRoot = "content", Excluded = payload.Excluded };
        return new(effective, plan, payload.Content, components, ["InstanceExclusions", "InstanceDefaults"], review, metadataHash);
    }

    private static string ReadText(JsonElement value, string key, bool required = true)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new MrpackException("CurseForgeAppUnsupported");
        if (!value.TryGetProperty(key, out var field) || field.ValueKind == JsonValueKind.Null)
        {
            if (required) throw new MrpackException("CurseForgeAppUnsupported");
            return "";
        }
        if (field.ValueKind != JsonValueKind.String || field.GetString() is not { Length: <= 200 } text || text.Any(char.IsControl))
            throw new MrpackException("CurseForgeAppUnsupported");
        return text;
    }
}
