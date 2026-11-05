using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Security;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Mods;

public sealed record ModCartItem(string SourcePath, ModInspection Inspection, string? Reason = null, string? CatalogTitle = null);
public enum ModBlockerKind { Definite, MissingDependency, DependencyVersion, DependencyConflict }
public sealed record ModBlocker(ModBlockerKind Kind, string Message, ModIdentityConflict? Conflict = null);
public sealed record ModCartReview(IReadOnlyList<string> Blockers, IReadOnlyList<string> Warnings, IReadOnlyList<ModBlocker> Details, NativeModSelection? NativeSelection = null);

public sealed partial class LocalModManager(LauncherPaths paths, ILauncherStore store)
{
    public async Task<ModInspection> InspectAsync(string file, CancellationToken ct = default)
    {
        SafePath.RejectLinks(file);
        var info = new FileInfo(file);
        if (!info.Extension.Equals(".jar", StringComparison.OrdinalIgnoreCase) || info.Length > 512L * 1024 * 1024)
            throw new InvalidDataException("Оберіть JAR до 512 MB.");
        await using var stream = File.OpenRead(file);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, true);
        SafeArchive.Validate(zip, paths.Cache);
        return await InspectArchiveAsync(zip, info.Name, hash, info.Length, new NativeBudget(), 0, null, ct);
    }
    private async Task<ModInspection> InspectArchiveAsync(ZipArchive zip, string fileName, string hash, long size,
        NativeBudget budget, int depth, ModLoader? nativeContext, CancellationToken ct)
    {
        string? id = null, name = null, version = null; ModLoader? loader = null; string environment = "unknown";
        var metadataWarnings = new List<string>();
        var dependencies = new List<string>(); var requirements = new List<ModRequirement>();
        var quilt = zip.GetEntry("quilt.mod.json");
        var fabric = zip.GetEntry("fabric.mod.json"); // Preserve existing Fabric handling for dual-format JARs.
        QuiltInspection? quiltInfo = null;
        ForgeModMetadata? forgeInfo = null;
        if (fabric is not null)
        {
            if (fabric.Length > 1024 * 1024) throw new InvalidDataException("Завеликий metadata-файл.");
            using var json = JsonDocument.Parse(await ReadBoundedEntryAsync(fabric, 1024 * 1024, ct));
            var root = json.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var schema) || !schema.TryGetInt32(out var schemaNumber) || schemaNumber != 1)
                metadataWarnings.Add("Непідтримувана або історична схема fabric.mod.json.");
            id = RequiredFabricText(root, "id"); version = RequiredFabricText(root, "version");
            name = root.TryGetProperty("name", out var n) ? n.GetString() : id;
            environment = ReadFabricEnvironment(root);
            loader = ModLoader.Fabric;
            foreach (var kind in new[] { ("depends", "required"), ("recommends", "optional"), ("breaks", "incompatible") })
                if (root.TryGetProperty(kind.Item1, out var group))
                    foreach (var requirement in group.EnumerateObject())
                    {
                        var alternatives = requirement.Value.ValueKind == JsonValueKind.String ? new[] { requirement.Value.GetString()! } :
                            requirement.Value.ValueKind == JsonValueKind.Array ? requirement.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray() : Array.Empty<string>();
                        requirements.Add(new(requirement.Name, alternatives, kind.Item2));
                        if (kind.Item2 == "required") dependencies.Add(requirement.Name);
                    }
        }
        else if (quilt is not null)
        {
            quiltInfo = await ReadQuiltAsync(zip, metadataWarnings, ct);
            id = quiltInfo.Id; name = quiltInfo.Name; version = quiltInfo.Version; environment = quiltInfo.Environment;
            loader = ModLoader.Quilt;
            dependencies.AddRange(quiltInfo.Metadata.Dependencies.Where(d => d.Id is not null && !d.Optional && d.Unless is null).Select(d => d.Id!));
        }
        else if (zip.GetEntry("META-INF/neoforge.mods.toml") is { } neoEntry)
        { loader = ModLoader.NeoForge; forgeInfo = await ReadForgeAsync(zip, neoEntry, loader.Value, metadataWarnings, ct); }
        else if (zip.GetEntry("META-INF/mods.toml") is { } forgeEntry)
        { loader = ModLoader.Forge; forgeInfo = await ReadForgeAsync(zip, forgeEntry, loader.Value, metadataWarnings, ct); }
        var provided = new List<ProvidedMod>();
        if (forgeInfo is not null)
        {
            var first = forgeInfo.Mods[0]; id = first.Id; name = first.Name; version = first.Version; environment = "*";
            provided.AddRange(forgeInfo.Mods.Skip(1).Where(m => m.Version is not null).Select(m => new ProvidedMod(m.Id, m.Version!)));
            dependencies.AddRange(forgeInfo.Mods.SelectMany(m => m.Dependencies).Where(d => d.Kind == "required" && d.Side is "CLIENT" or "BOTH").Select(d => d.Id));
        }
        if (quiltInfo is not null) provided.AddRange(quiltInfo.Provided);
        FabricModMetadata? fabricMetadata = null;
        if (fabric is not null && nativeContext is null) fabricMetadata = await ReadEmbeddedFabricAsync(zip, 0, new EmbeddedBudget(), provided, metadataWarnings, ct);
        else if (fabric is not null) metadataWarnings.Add("Nested Fabric metadata requires a separate native-loader adapter; cross-loader assumptions are disabled.");
        dependencies.AddRange(requirements.Where(r => r.Kind == "required").Select(r => r.Id));
        var context = loader is ModLoader.Quilt or ModLoader.Forge or ModLoader.NeoForge ? loader : nativeContext;
        var nested = context is null ? null : await ReadNativeNestedAsync(zip, context.Value, budget, depth, ct);
        return new(fileName, hash, size, "Unknown Source", id, name, version, loader, dependencies.Distinct().ToArray(), environment, requirements, provided, metadataWarnings, quiltInfo?.Metadata, forgeInfo, nested, fabricMetadata);
    }
    public async Task<ModCartReview> ReviewAsync(GameProfile profile, IReadOnlyList<ModCartItem> items, CancellationToken ct = default)
        => await ReviewCoreAsync(profile, items, true, ct);
    internal Task<ModCartReview> ReviewResultSetAsync(GameProfile profile, IReadOnlyList<ModCartItem> items, CancellationToken ct)
        => ReviewCoreAsync(profile, items, false, ct);
    private async Task<ModCartReview> ReviewCoreAsync(GameProfile profile, IReadOnlyList<ModCartItem> items, bool includeInstalled, CancellationToken ct)
    {
        var blockers = new List<ModBlocker>(); var warnings = new List<string>();
        void Block(ModBlockerKind kind, string message) => blockers.Add(new(kind, message));
        if (profile.Loader == ModLoader.Vanilla) Block(ModBlockerKind.Definite, "Vanilla не завантажує моди. Створіть профіль із потрібним Mod Loader.");
        var knownIds = new HashSet<string>(StringComparer.Ordinal) { "minecraft", "java" };
        knownIds.Add(profile.Loader switch { ModLoader.Fabric => "fabricloader", ModLoader.Quilt => "quilt_loader", ModLoader.Forge => "forge", ModLoader.NeoForge => "neoforge", _ => "" });
        var existingHashes = new HashSet<string>(StringComparer.Ordinal);
        var knownVersions = new Dictionary<string, string?>(StringComparer.Ordinal) { ["minecraft"] = profile.MinecraftVersion };
        var receipt = await AtomicFile.ReadJsonAsync<InstallationReceipt>(paths.Receipt(profile.Id), ct);
        if (receipt is not null) knownVersions["java"] = receipt.JavaMajor.ToString();
        if (receipt is { Loader: ModLoader.Fabric, LoaderVersion: not null } && profile.Loader == ModLoader.Fabric) knownVersions["fabricloader"] = receipt.LoaderVersion;
        bool StableBuiltIn(string id) => id == "minecraft" ||
            (id is "java" or "fabricloader") && receipt is not null && receipt.Version == profile.MinecraftVersion && receipt.Loader == profile.Loader;
        void AddProvided(IEnumerable<ProvidedMod> mods)
        {
            foreach (var supplied in mods)
            {
                knownIds.Add(supplied.Id);
                if (knownVersions.TryGetValue(supplied.Id, out var prior) && prior != supplied.Version)
                { knownVersions[supplied.Id] = null; warnings.Add($"{supplied.Id}: кілька вкладених версій; вибір завантажувача не підтверджено."); }
                else knownVersions[supplied.Id] = supplied.Version;
            }
        }
        var installed = new List<ModCartItem>();
        foreach (var file in includeInstalled ? Directory.EnumerateFiles(Path.Combine(paths.Game(profile.Id), "mods"), "*.jar") : [])
        {
            var mod = await InspectAsync(file, ct);
            existingHashes.Add(mod.Sha256);
            installed.Add(new(file, mod, "Уже встановлено у профілі"));
        }
        foreach (var item in items)
            if (!existingHashes.Add(item.Inspection.Sha256)) Block(ModBlockerKind.Definite, $"Дублікат JAR: {item.Inspection.FileName}");

        var all = installed.Concat(items).ToArray();
        var rootOwners = new Dictionary<string, ModCartItem>(StringComparer.Ordinal);
        foreach (var item in all)
        {
            var mod = item.Inspection;
            if (profile.Loader == ModLoader.Fabric && mod.Loader == ModLoader.Fabric && mod.Environment == "server") continue;
            var ids = (mod.FabricMetadata?.Aliases.Select(p => p.Id) ?? []).Prepend(mod.Id).OfType<string>().Distinct();
            foreach (var id in ids)
            {
                if (rootOwners.TryGetValue(id, out var other))
                {
                    var conflict = new ModIdentityConflict(id, other, item);
                    blockers.Add(new(ModBlockerKind.Definite, conflict.Technical, conflict));
                }
                else if (knownIds.Contains(id)) Block(ModBlockerKind.Definite, $"Дублікат Mod ID: {id}");
                else
                {
                    rootOwners.Add(id, item);
                    knownIds.Add(id);
                    knownVersions[id] = mod.Version;
                }
            }
        }
        var rootIds = knownIds.ToHashSet(StringComparer.Ordinal);
        foreach (var item in all)
        {
            var mod = item.Inspection;
            if (profile.Loader == ModLoader.Fabric && mod.Loader == ModLoader.Fabric && mod.Environment == "server") continue;
            AddProvided(mod.FabricMetadata is not null
                ? EffectiveFabricChildren(mod, rootIds).SelectMany(m => m.Aliases.Append(new(m.Id, m.Version)))
                : mod.ProvidedMods ?? []);
        }
        foreach (var item in items)
        {
            var mod = item.Inspection;
            if (mod.Loader is not null && mod.Loader != profile.Loader) Block(ModBlockerKind.Definite, $"{mod.FileName}: потрібен {mod.Loader}.");
            foreach (var warning in mod.MetadataWarnings ?? []) warnings.Add(mod.FileName + ": " + warning);
            var requirements = (mod.Requirements ?? []).Concat(EffectiveFabricChildren(mod, rootIds).SelectMany(m => m.Requirements)).ToArray();
            var required = mod.RequiredDependencies.Concat(requirements.Where(r => r.Kind == "required").Select(r => r.Id)).Distinct();
            foreach (var dependency in required.Where(d => mod.Loader is not (ModLoader.Quilt or ModLoader.Forge or ModLoader.NeoForge) && !knownIds.Contains(d)))
                Block(ModBlockerKind.MissingDependency, $"{mod.FileName}: відсутня обов'язкова залежність {dependency}.");
            if (mod.SourceStatus == "Unknown Source") warnings.Add($"{mod.FileName}: Unknown Source; антивірусна перевірка не виконана.");
            foreach (var requirement in requirements)
            {
                if (!knownIds.Contains(requirement.Id)) { if (requirement.Kind == "optional") warnings.Add($"{mod.FileName}: необов'язкова залежність {requirement.Id}"); continue; }
                knownVersions.TryGetValue(requirement.Id, out var actual);
                var match = VersionConstraints.Or(requirement.Alternatives.Select(c => profile.Loader == ModLoader.Fabric ? VersionConstraints.MatchFabric(actual, c) : VersionConstraints.Match(actual, c)));
                if (requirement.Kind == "incompatible" && match == ConstraintMatch.Match) Block(StableBuiltIn(requirement.Id) ? ModBlockerKind.Definite : ModBlockerKind.DependencyConflict, $"{mod.FileName}: конфлікт із {requirement.Id} {actual}.");
                else if (requirement.Kind == "required" && match == ConstraintMatch.Mismatch) Block(requirement.Id is "minecraft" or "java" or "fabricloader" ? ModBlockerKind.Definite : ModBlockerKind.DependencyVersion, $"{mod.FileName}: {requirement.Id} {actual} не відповідає {string.Join(" || ", requirement.Alternatives)}.");
                else if (match == ConstraintMatch.Unknown) warnings.Add($"{mod.FileName}: сумісність {requirement.Id} не підтверджена.");
            }
            if (mod.QuiltMetadata is null && mod.ForgeMetadata is null && (mod.Requirements is null || mod.Requirements.Count == 0)) warnings.Add($"{mod.FileName}: повна сумісність версій не підтверджена.");
            if (mod.Id is null) warnings.Add($"{mod.FileName}: metadata не підтримується або не визначено.");
        }
        NativeModSelection? native = null;
        if (profile.Loader is ModLoader.Quilt or ModLoader.Forge or ModLoader.NeoForge)
        {
            var result = native = await NativeNestedResolver.AnalyzeProfileAsync(paths, profile, receipt, all.Select(item => item.Inspection).ToArray(), ct);
            warnings.AddRange(result.Warnings);
            foreach (var issue in result.Issues)
                if (issue.Code.Contains("unknown", StringComparison.Ordinal)) warnings.Add(issue.Message);
                else Block(issue.Code switch
                {
                    "mods.dependency.missing" => ModBlockerKind.MissingDependency,
                    "mods.dependency.version" => ModBlockerKind.DependencyVersion,
                    "mods.conflict" => ModBlockerKind.DependencyConflict,
                    "mods.nested.unsatisfied" => ModBlockerKind.DependencyConflict,
                    _ => ModBlockerKind.Definite
                }, issue.Message);
            if (result.Confirmed && result.Artifacts.Count > 0)
                warnings.Add("Native nested selection (static metadata only): " + string.Join(", ", result.Artifacts.Select(a => a.Identity + " " + a.Version + (a.Library ? " [library]" : " [mod]"))));
        }
        var details = blockers.Distinct().ToArray();
        return new(details.Select(b => b.Message).Distinct().ToArray(), warnings.Distinct().ToArray(), details, native);
    }
    public async Task InstallAsync(GameProfile profile, IReadOnlyList<ModCartItem> items, bool warningAccepted, CancellationToken ct = default)
    {
        using var profileLease = new ProfileUsage(paths).Acquire(profile.Id);
        new ProfileUsage(paths).CheckWorldLocks(profile.Id);
        await InstallUnderLeaseAsync(profile, items, warningAccepted, ct);
    }
    internal async Task InstallUnderLeaseAsync(GameProfile profile, IReadOnlyList<ModCartItem> items, bool warningAccepted, CancellationToken ct = default, bool allowMissingDependencies = false)
    {
        if (items.Count == 0) throw new InvalidOperationException("Кошик порожній.");
        if (!warningAccepted) throw new InvalidOperationException("Потрібне свідоме підтвердження попередження Unknown Source.");
        await RecoverUnderLeaseAsync(profile, ct);
        await new ModInventoryStore(paths).ReadAsync(profile.Id, ct);
        var review = await ReviewAsync(profile, items, ct);
        var blockers = allowMissingDependencies
            ? review.Details.Where(b => b.Kind != ModBlockerKind.MissingDependency).Select(b => b.Message).ToArray()
            : review.Blockers.ToArray();
        var conflicts = review.Details.Select(b => b.Conflict).OfType<ModIdentityConflict>().ToArray();
        if (conflicts.Length > 0) throw new ModIdentityConflictException(conflicts);
        if (blockers.Length > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, blockers));
        var staged = new List<(string Temp, string Target)>();
        var game = paths.Game(profile.Id);
        var reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var transaction = Guid.NewGuid(); var journalEntries = new List<ModJournalEntry>();
        var journalPath = SafePath.Join(paths.Profile(profile.Id), $"transactions/mods-{transaction:N}.json");
        await AtomicFile.WriteJsonAsync(journalPath, new ModInstallJournal(transaction, profile.Id, "Prepared", journalEntries), ct);
        try
        {
            foreach (var item in items)
            {
                var mod = await InspectAsync(item.SourcePath, ct);
                if (mod.Sha256 != item.Inspection.Sha256) throw new InvalidDataException("JAR змінився після додавання у кошик.");
                var cached = SafePath.Join(paths.Cache, $"mods/sha256/{mod.Sha256}.jar");
                if (!File.Exists(cached))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
                    var part = cached + "." + Guid.NewGuid().ToString("N") + ".part";
                    try
                    {
                        File.Copy(item.SourcePath, part, false);
                        var verified = await PTenoLauncher.Infrastructure.Network.VerifiedDownloader.VerifyAsync(part, mod.Sha256, "SHA256", mod.Size, ct);
                        if (!verified) throw new InvalidDataException("JAR змінився під час копіювання.");
                        File.Move(part, cached, true);
                    }
                    finally { if (File.Exists(part)) File.Delete(part); }
                }
                if (!await PTenoLauncher.Infrastructure.Network.VerifiedDownloader.VerifyAsync(cached, mod.Sha256, "SHA256", mod.Size, ct)) throw new InvalidDataException("Пошкоджений кеш JAR.");
                var target = SafePath.Join(game, ChooseInstallPath(game, mod.FileName, mod.Sha256, reservedNames));
                var temp = target + ".pending-" + transaction.ToString("N");
                var origin = item.Inspection.SourceStatus switch
                {
                    "Modrinth" => "modrinth",
                    "Local file / Modrinth SHA512" => "local-modrinth",
                    _ => "local"
                };
                journalEntries.Add(new(Path.GetRelativePath(game, temp).Replace('\\', '/'), Path.GetRelativePath(game, target).Replace('\\', '/'),
                    mod.Sha256, mod.Size, origin));
                await AtomicFile.WriteJsonAsync(journalPath, new ModInstallJournal(transaction, profile.Id, "Prepared", journalEntries), ct);
                File.Copy(cached, temp, false); staged.Add((temp, target));
            }
            ct.ThrowIfCancellationRequested();
            foreach (var (temp, target) in staged) File.Move(temp, target, false);
            await AtomicFile.WriteJsonAsync(journalPath, new ModInstallJournal(transaction, profile.Id, "Committed", journalEntries), CancellationToken.None);
            await new ModInventoryStore(paths).RecordCommittedAsync(profile.Id, journalEntries.Select(e => new InstalledModFile(e.Target, e.Sha256, e.Size, e.Origin)).ToArray(), CancellationToken.None);
            await store.AddHistoryAsync(profile.Id, "mods.installed", string.Join(", ", items.Select(i => i.Inspection.FileName)), CancellationToken.None);
            File.Delete(journalPath);
        }
        catch { if (File.Exists(journalPath)) await RecoverJournalAsync(profile, journalPath, CancellationToken.None); throw; }
        finally { foreach (var (temp, _) in staged) if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static string ChooseInstallPath(string game, string fileName, string hash, HashSet<string> reserved)
    {
        if (fileName != Path.GetFileName(fileName) || !fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid JAR filename.");
        _ = SafePath.Join(game, "mods/" + fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (stem.Length > 180)
        {
            var length = char.IsHighSurrogate(stem[179]) ? 179 : 180;
            stem = stem[..length].TrimEnd(' ', '.');
        }
        var extension = Path.GetExtension(fileName);
        for (var index = 0; index < 2048; index++)
        {
            var suffix = index == 0 ? "" : index == 1 ? "-" + hash[..8] : $"-{hash[..8]}-{index}";
            var relative = "mods/" + stem + suffix + extension;
            var target = SafePath.Join(game, relative);
            if (!File.Exists(target) && !Directory.Exists(target) && reserved.Add(relative)) return relative;
        }
        throw new IOException("No free name for the JAR.");
    }
}
