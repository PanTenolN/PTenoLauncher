using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.Modpacks;

public static class MrpackRequirements
{
    public static async Task<MrpackImportReceipt?> ReadAsync(LauncherPaths paths, GameProfile profile, CancellationToken ct = default)
    {
        var file = SafePath.Join(paths.Profile(profile.Id), MrpackImporter.ReceiptName); if (!File.Exists(file)) return null;
        using var document = MrpackReader.Json(await AtomicFile.ReadBytesAsync(file, MrpackReader.MaxIndexBytes, ct));
        var r = document.Deserialize<MrpackImportReceipt>(AtomicFile.JsonOptions) ?? throw new MrpackException("PackInvalid");
        if (r.Schema != 1 || r.Id != profile.Id || r.Profile is null || r.Profile.Id != r.Id || r.Files is null || r.Files.Count > MrpackReader.MaxFiles ||
            r.ArchiveSha256 is null || r.ArchiveSha256.Length != 64 || !r.ArchiveSha256.All(Uri.IsHexDigit) ||
            r.Optional is null || r.Optional.Count > MrpackReader.MaxFiles || r.LoaderVersion?.Length > 100 ||
            r.SourceKind is not ("local-mrpack" or "local-zip" or "local-prism-multimc" or "local-minecraft-launcher" or ModrinthAppReader.SourceKind or CurseForgeAppReader.SourceKind or MrpackImporter.ReconciledSourceKind) || string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 200 || string.IsNullOrWhiteSpace(r.Version) || r.Version.Length > 200 ||
            r.IndexSha256 is null || r.IndexSha256.Length != 64 || !r.IndexSha256.All(Uri.IsHexDigit) || r.IndexSize is <= 0 or > MrpackReader.MaxIndexBytes ||
            r.Profile.Loader == ModLoader.Vanilla && r.LoaderVersion is not null || r.Profile.Loader != ModLoader.Vanilla && string.IsNullOrWhiteSpace(r.LoaderVersion) ||
            r.LoaderVersion is not null && r.LoaderVersion.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_' and not '+') ||
            r.Files.Any(f => f is null || f.Algorithm != "SHA256" || f.Hash is null || f.Hash.Length != 64 || !f.Hash.All(Uri.IsHexDigit) || f.Size is < 0 or > MrpackReader.MaxArchiveBytes) ||
            r.Files.Select(f => f.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != r.Files.Count) throw new MrpackException("PackInvalid");
        if (r.Files.Sum(f => f.Size) > MrpackReader.MaxExpandedBytes || r.Optional.Distinct(StringComparer.Ordinal).Count() != r.Optional.Count) throw new MrpackException("PackInvalid");
        var indexName = MrpackImporter.IndexName(r.SourceKind);
        if (!await Network.VerifiedDownloader.VerifyAsync(SafePath.Join(paths.Profile(profile.Id), indexName), r.IndexSha256, "SHA256", r.IndexSize, ct)) throw new MrpackException("PackIntegrity");
        ProfileRules.Validate(r.Profile); MrpackReader.PathsRule(r.Files.Select(f => (f.RelativePath, false)));
        using var index = MrpackReader.Json(await AtomicFile.ReadBytesAsync(SafePath.Join(paths.Profile(profile.Id), indexName), MrpackReader.MaxIndexBytes, ct));
        if (r.SourceKind is "local-prism-multimc" or "local-minecraft-launcher" or ModrinthAppReader.SourceKind or CurseForgeAppReader.SourceKind)
        {
            bool prism = r.SourceKind == "local-prism-multimc";
            var origin = r.SourceKind switch
            {
                "local-minecraft-launcher" => "minecraft-launcher-profile",
                ModrinthAppReader.SourceKind => ModrinthAppReader.Origin,
                CurseForgeAppReader.SourceKind => CurseForgeAppReader.Origin,
                _ => "prism-multimc-components-v1"
            };
            var target = index.Deserialize<InstanceImportTarget>(AtomicFile.JsonOptions) ?? throw new MrpackException("PackInvalid");
            if (target.Schema != 1 || target.Origin != origin || target.Minecraft != r.Profile.MinecraftVersion || target.Loader != r.Profile.Loader || target.LoaderVersion != r.LoaderVersion || target.ContentRoot != r.ContentRoot ||
                r.SourceKind == ModrinthAppReader.SourceKind && !ModrinthAppReader.ValidId(target.SourceProfileId) ||
                target.SourceMetadataSha256 is null || target.SourceMetadataSha256.Length != 64 || !target.SourceMetadataSha256.All(Uri.IsHexDigit) || target.Components is null || target.Components.Count is < 1 or > 2 ||
                target.Components.Any(c => c is null || c.Uid is null || c.Version is null || !InstancePackReader.Exact(c.Version)) || target.Components.Select(c => c.Uid).Distinct(StringComparer.OrdinalIgnoreCase).Count() != target.Components.Count ||
                !target.Components.Any(c => c.Uid == "net.minecraft" && c.Version == target.Minecraft)) throw new MrpackException("PackInvalid");
            var loaderUid = target.Loader switch { ModLoader.Fabric => "net.fabricmc.fabric-loader", ModLoader.Quilt => "org.quiltmc.quilt-loader", ModLoader.Forge => "net.minecraftforge", ModLoader.NeoForge => "net.neoforged", _ => null };
            if (target.Components.Count != (loaderUid is null ? 1 : 2) || loaderUid is not null && !target.Components.Any(c => c.Uid == loaderUid && c.Version == target.LoaderVersion)) throw new MrpackException("PackInvalid");
            if (r.ContentRoot is null || (!prism ? r.ContentRoot != "content" : r.ContentRoot.Split('/').Length > 2 || r.ContentRoot.Split('/')[^1] is not ("minecraft" or ".minecraft")) || r.Excluded is null || r.Excluded.Count > MrpackReader.MaxEntries) throw new MrpackException("PackInvalid");
            MrpackReader.PathRule(r.ContentRoot, true); MrpackReader.PathsRule(r.Excluded.Select(p => (p, false)), true);
            return r;
        }
        if (r.SourceKind == "local-zip")
        {
            var target = index.Deserialize<ZipImportTarget>(AtomicFile.JsonOptions) ?? throw new MrpackException("PackInvalid");
            if (target.Schema != 1 || target.Origin != "explicit-user-selection" || target.Minecraft != r.Profile.MinecraftVersion || target.Loader != r.Profile.Loader || target.LoaderVersion != r.LoaderVersion || target.ContentRoot != r.ContentRoot ||
                r.ContentRoot is null || r.ContentRoot.Length > 240 || r.ContentRoot.Contains('/') || r.Excluded is null || r.Excluded.Count > MrpackReader.MaxEntries) throw new MrpackException("PackInvalid");
            if (r.ContentRoot.Length > 0) MrpackReader.PathRule(r.ContentRoot, true);
            MrpackReader.PathsRule(r.Excluded.Select(p => (p, false)), true);
            return r;
        }
        var dependencies = index.RootElement.GetProperty("dependencies");
        var loaderKey = r.Profile.Loader switch { ModLoader.Fabric => "fabric-loader", ModLoader.Quilt => "quilt-loader", ModLoader.Forge => "forge", ModLoader.NeoForge => "neoforge", _ => null };
        if (dependencies.GetProperty("minecraft").GetString() != r.Profile.MinecraftVersion || dependencies.EnumerateObject().Count() != (loaderKey is null ? 1 : 2) ||
            loaderKey is not null && dependencies.GetProperty(loaderKey).GetString() != r.LoaderVersion ||
            index.RootElement.GetProperty("name").GetString() != r.Name || index.RootElement.GetProperty("versionId").GetString() != r.Version) throw new MrpackException("PackInvalid");
        return r;
    }
    public static async Task<string?> RequestedAsync(LauncherPaths paths, GameProfile profile, string? chosen = null, CancellationToken ct = default)
    {
        chosen ??= profile.PreferredLoaderVersion;
        var receipt = await ReadAsync(paths, profile, ct); if (receipt is null) return chosen;
        if (receipt.Profile.MinecraftVersion != profile.MinecraftVersion || receipt.Profile.Loader != profile.Loader ||
            chosen is not null && chosen != receipt.LoaderVersion) throw new MrpackException("PackTarget", receipt.LoaderVersion ?? receipt.Profile.MinecraftVersion);
        return receipt.LoaderVersion ?? chosen;
    }
    public static async Task<IReadOnlyList<ReadinessIssue>> CheckAsync(LauncherPaths paths, GameProfile profile, InstallationReceipt? installation, CancellationToken ct)
    {
        try
        {
            var r = await ReadAsync(paths, profile, ct); if (r is null) return [];
            var problems = new List<ReadinessIssue>();
            if (r.Profile.MinecraftVersion != profile.MinecraftVersion || r.Profile.Loader != profile.Loader ||
                installation is null || installation.Version != r.Profile.MinecraftVersion || installation.Loader != r.Profile.Loader || installation.LoaderVersion != r.LoaderVersion)
                problems.Add(new("modpack.target.mismatch", "PackTarget"));
            var journal = await AtomicFile.ReadJsonAsync<MrpackJournal>(SafePath.Join(paths.Root, "modpack-imports/" + r.Id.ToString("N") + "/journal.json"), ct);
            if (journal is null || journal.Id != r.Id || journal.State != "Committed") problems.Add(new("modpack.pending", "PackRecovery"));
            return problems;
        }
        catch (Exception e) when (e is not OperationCanceledException) { return [new("modpack.metadata.unknown", "PackInvalid")]; }
    }
}
