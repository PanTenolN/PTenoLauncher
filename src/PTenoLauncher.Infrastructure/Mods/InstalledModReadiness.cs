using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.Mods;

public sealed record InstalledModReport(IReadOnlyList<ReadinessIssue> Issues, int VerifiedFiles);

/// <summary>Bounded, read-only client-side inspection. Never uses a network service or runs JAR code.</summary>
public sealed class InstalledModReadiness(LauncherPaths paths, LocalModManager inspector)
{
    public async Task<InstalledModReport> CheckAsync(GameProfile profile, InstallationReceipt? receipt, CancellationToken ct = default)
    {
        if (profile.Loader == ModLoader.Vanilla) return new([], 0); // Vanilla does not load this directory.
        var issues = new List<ReadinessIssue>(); var mods = new List<ModInspection>(); int verified = 0;
        void Add(string code, string text) => issues.Add(new(code, text));
        try
        {
            var transactions = SafePath.Join(paths.Profile(profile.Id), "transactions");
            if (Directory.Exists(transactions) && Directory.EnumerateFiles(transactions, "mods-*.json").Any())
                Add("mods.pending", "Є незавершене встановлення модів. Виконайте відновлення перед запуском.");
            var inventory = await new ModInventoryStore(paths).ReadAsync(profile.Id, ct);
            var known = (inventory?.Files ?? []).ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
            var directory = SafePath.Join(paths.Game(profile.Id), "mods");
            var files = Directory.Exists(directory) ? Directory.EnumerateFileSystemEntries(directory).Take(8193).ToArray() : [];
            if (files.Length > 8192) { Add("mods.limit.unknown", "Перевищено ліміт перевірки каталогу модів."); return new(issues, 0); }
            var jars = files.Where(f => Path.GetExtension(f).Equals(".jar", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (jars.Length > ModInventoryStore.MaximumFiles) { Add("mods.limit.unknown", "Перевищено ліміт 2048 модів."); return new(issues, 0); }
            if (files.Any(f => Path.GetFileName(f).Contains(".pending-", StringComparison.Ordinal)))
                Add("mods.pending", "Знайдено незавершені файли модів. Потрібна перевірка журналу встановлення.");
            foreach (var entry in files)
            {
                SafePath.RejectLinks(entry);
                if (Directory.Exists(entry)) Add("mods.layout.unknown", "Вкладені каталоги mods не перевіряються: " + Path.GetFileName(entry));
            }
            foreach (var file in jars)
            {
                ct.ThrowIfCancellationRequested();
                var relative = "mods/" + Path.GetFileName(file);
                try
                {
                    var mod = await inspector.InspectAsync(file, ct);
                    mods.Add(mod);
                    if (!known.TryGetValue(relative, out var expected))
                        Add("mods.integrity.unknown", mod.FileName + ": немає збереженого контрольного хешу. Перегляньте та підтвердьте локальні файли.");
                    else if (expected.Size != mod.Size || !expected.Sha256.Equals(mod.Sha256, StringComparison.OrdinalIgnoreCase))
                        Add("mods.file.corrupt", mod.FileName + ": файл змінився після підтвердженого встановлення.");
                    else verified++;
                    if (mod.Loader is not null && mod.Loader != profile.Loader) Add("mods.loader.mismatch", mod.FileName + ": потрібен " + mod.Loader + ", обрано " + profile.Loader + ".");
                    if (mod.Loader != profile.Loader || mod.Id is null || string.IsNullOrWhiteSpace(mod.Version))
                        Add("mods.metadata.unsupported", mod.FileName + ": повна перевірка метаданих цього формату ще недоступна.");
                    foreach (var warning in mod.MetadataWarnings ?? []) Add("mods.metadata.unknown", mod.FileName + ": " + warning);
                    if (mod.Environment is not ("*" or "client" or "server"))
                        Add("mods.environment.unknown", mod.FileName + ": невідоме середовище завантаження.");
                }
                catch (Exception ex) when (IsFileError(ex)) { Add("mods.file.corrupt", Path.GetFileName(file) + ": JAR або його метадані не пройшли перевірку."); }
            }
            var present = jars.Select(f => "mods/" + Path.GetFileName(f)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var missing in known.Keys.Where(k => !present.Contains(k)))
                Add("mods.file.missing", "Відсутній встановлений мод: " + missing);
            CheckDependencies(profile, receipt, mods, issues);
        }
        catch (Exception ex) when (IsFileError(ex)) { Add("mods.inventory.corrupt", "Не вдалося перевірити каталог або список контрольних хешів модів. Файли залишено без змін."); }
        return new(issues.Distinct().ToArray(), verified);
    }
    internal static bool IsFileError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException;
    private static void CheckDependencies(GameProfile profile, InstallationReceipt? receipt, List<ModInspection> all, List<ReadinessIssue> issues)
    {
        if (profile.Loader is ModLoader.Forge or ModLoader.NeoForge)
        {
            issues.AddRange(ForgeCompatibility.Check(profile, receipt, all).Issues);
            return;
        }
        if (profile.Loader == ModLoader.Quilt)
        {
            issues.AddRange(QuiltCompatibility.Check(profile, receipt, all));
            return;
        }
        var versions = new Dictionary<string, string?>(StringComparer.Ordinal) { ["minecraft"] = profile.MinecraftVersion, ["java"] = receipt?.JavaMajor.ToString() };
        if (profile.Loader == ModLoader.Fabric) versions["fabricloader"] = receipt is {Loader: ModLoader.Fabric} ? receipt.LoaderVersion : null;
        var active = all.Where(m => m.Loader == ModLoader.Fabric && m.Environment is "*" or "client").ToArray();
        var roots = new HashSet<string>(versions.Keys, StringComparer.Ordinal);
        void Supply(string id, string? version, string file, bool root)
        {
            if (root && !roots.Add(id)) issues.Add(new("mods.duplicate", file + ": повторюваний Mod ID " + id));
            if (versions.TryGetValue(id, out var prior) && prior != version)
            {
                versions[id] = null;
                issues.Add(new("mods.selection.unknown", id + ": вибір між кількома версіями залежить від Mod Loader."));
            }
            else if (!versions.ContainsKey(id)) versions[id] = version;
        }
        foreach (var mod in active)
        {
            if (mod.Id is not null) Supply(mod.Id, mod.Version, mod.FileName, true);
            foreach (var provided in mod.ProvidedMods ?? []) Supply(provided.Id, provided.Version, mod.FileName, false);
        }
        foreach (var mod in active)
        {
            foreach (var id in mod.RequiredDependencies.Where(id => !versions.ContainsKey(id)))
                issues.Add(new("mods.dependency.missing", mod.FileName + ": відсутня обов'язкова залежність " + id));
            foreach (var requirement in mod.Requirements ?? [])
            {
                if (!versions.TryGetValue(requirement.Id, out var actual)) continue;
                var match = VersionConstraints.Or(requirement.Alternatives.Select(c => VersionConstraints.MatchFabric(actual, c)));
                if (requirement.Kind == "required" && match == ConstraintMatch.Mismatch)
                    issues.Add(new("mods.dependency.version", mod.FileName + ": " + requirement.Id + " " + actual + " не відповідає " + string.Join(" || ", requirement.Alternatives)));
                else if (requirement.Kind == "incompatible" && match == ConstraintMatch.Match)
                    issues.Add(new("mods.conflict", mod.FileName + ": конфлікт із " + requirement.Id + " " + actual));
                else if (requirement.Kind is "required" or "incompatible" && match == ConstraintMatch.Unknown)
                    issues.Add(new("mods.constraint.unknown", mod.FileName + ": сумісність із " + requirement.Id + " не підтверджена."));
            }
        }
    }
}
