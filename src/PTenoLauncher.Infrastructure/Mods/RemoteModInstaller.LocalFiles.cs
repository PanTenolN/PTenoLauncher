using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
namespace PTenoLauncher.Infrastructure.Mods;

public sealed record LocalModSelection(ModCartItem Item, string Sha512, CatalogModVersion? Identity);
public sealed record LocalDependencyPlan(RemoteInstallPlan Remote, IReadOnlyList<LocalModSelection> LocalFiles,
    IReadOnlyList<string> Blockers, IReadOnlyList<string> Warnings, bool ManualOnly = false,
    IReadOnlyList<ModIdentityConflict>? Conflicts = null, IReadOnlyList<ModRequirement>? MissingRequirements = null)
{
    public bool CanInstall => Blockers.Count == 0 && Remote.Resolution.Blockers.Count == 0 &&
        (LocalFiles.Count > 0 || Remote.Resolution.Entries.Count > 0);
    public bool RequiresUnknownAcceptance => LocalFiles.Any(f => f.Identity is null);
    public IEnumerable<ModPlanEntry> Downloads => RemoteModInstaller.DistinctArtifacts(Remote.Resolution.Entries.Where(e => !e.AlreadyInstalled && !e.FromLocalFile));
    public long DownloadBytes => Downloads.Sum(e => e.Version.File.Size);
}

public sealed partial class RemoteModInstaller
{
    private sealed class SeededCatalog(ModrinthClient source, IReadOnlyList<CatalogModVersion> identities) : IModCatalog
    {
        public Task<CatalogProject> GetProjectAsync(string id, CancellationToken ct = default) => source.GetProjectAsync(id, ct);
        public Task<IReadOnlyList<CatalogModVersion>> GetVersionsAsync(string id, string game, ModLoader loader, CancellationToken ct = default) => source.GetVersionsAsync(id, game, loader, ct);
        public Task<CatalogModVersion> GetVersionAsync(string id, CancellationToken ct = default)
        {
            var exact = identities.FirstOrDefault(v => v.Id == id);
            return exact is not null ? Task.FromResult(exact) : source.GetVersionAsync(id, ct);
        }
    }
    private async Task<LocalModSelection> InspectSelectionAsync(ModCartItem item, CancellationToken ct)
    {
        SafePath.RejectLinks(item.SourcePath);
        // Keep the input read-locked across both hashes and static inspection on Windows.
        await using var input = File.OpenRead(item.SourcePath);
        var inspected = await local.InspectAsync(item.SourcePath, ct);
        if (!inspected.Sha256.Equals(item.Inspection.Sha256, StringComparison.OrdinalIgnoreCase) ||
            inspected.Size != item.Inspection.Size || inspected.FileName != item.Inspection.FileName)
            throw new InvalidDataException("Локальний JAR змінився після додавання у кошик: " + item.Inspection.FileName);
        var hash = Convert.ToHexString(await SHA512.HashDataAsync(input, ct)).ToLowerInvariant();
        return new(new(item.SourcePath, inspected, item.Reason), hash, null);
    }
    public async Task<LocalDependencyPlan> PreviewLocalAsync(GameProfile profile, IReadOnlyList<ModCartItem> files,
        IReadOnlyList<RemoteModRequest>? requests = null, CancellationToken ct = default, IReadOnlyList<OptionalDependencyKey>? optionalSelections = null,
        bool manualOnly = false, bool automaticOptional = false)
    {
        requests ??= [];
        if (manualOnly && requests.Count != 0) throw new InvalidOperationException("Manual import cannot include catalog requests.");
        if (files.Count + requests.Count is < 1 or > 128) throw new InvalidOperationException("Оберіть від 1 до 128 локальних файлів або проєктів.");
        if (profile.Loader == ModLoader.Vanilla) throw new InvalidOperationException("Для модів потрібен Mod Loader.");
        using var lease = new ProfileUsage(paths).Acquire(profile.Id);
        new ProfileUsage(paths).CheckWorldLocks(profile.Id);
        var transactions = SafePath.Join(paths.Profile(profile.Id), "transactions");
        if (Directory.Exists(transactions) && Directory.EnumerateFiles(transactions, "mods-*.json").Any())
            throw new InvalidOperationException("Спочатку відновіть незавершене встановлення модів.");
        catalog.BeginOperation();
        var snapshot = await ScanAsync(profile, !manualOnly, ct);
        var selected = new List<LocalModSelection>(); var blockers = new List<string>(); var warnings = snapshot.Warnings.ToList();
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var selection = await InspectSelectionAsync(file, ct);
            if (!hashes.Add(selection.Sha512)) { blockers.Add("Повторюваний локальний JAR: " + file.Inspection.FileName); continue; }
            CatalogModVersion? identity = null;
            try { if (!manualOnly) identity = await catalog.FindBySha512Async(selection.Sha512, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is InvalidDataException or HttpRequestException or JsonException or TaskCanceledException)
            { warnings.Add(file.Inspection.FileName + ": ідентифікація за SHA512 недоступна; використано лише локальні метадані."); }
            if (identity is not null && (identity.File.Size != selection.Item.Inspection.Size || !identity.File.Sha512.Equals(selection.Sha512, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Метадані Modrinth не відповідають локальному JAR.");
            if (identity is null) warnings.Add(file.Inspection.FileName + ": Unknown Source; залежності не вгадуються за назвою або Mod ID.");
            selected.Add(selection with { Identity = identity });
        }
        foreach (var duplicate in selected.Where(s => s.Identity is not null).GroupBy(s => s.Identity!.ProjectId).Where(g => g.Count() > 1))
            blockers.Add("Кілька локальних файлів одного Modrinth-проєкту: " + duplicate.Key);
        var identities = selected.Where(s => s.Identity is not null).Select(s => s.Identity!).ToArray();
        var roots = requests.Concat(identities.Select(v => new RemoteModRequest(v.ProjectId, v.Id))).Distinct().ToArray();
        var resolution = roots.Length == 0 ? new DependencyResolution([], [], [], []) :
            await new RemoteDependencyResolver(new SeededCatalog(catalog, identities)).ResolveWithOptionalAsync(
                profile, roots, snapshot.Known, optionalSelections ?? [], ct, automatic: automaticOptional);
        resolution = WithInstalledArtifacts(resolution, snapshot);
        var entries = resolution.Entries.Select(e => e with { FromLocalFile = hashes.Contains(e.Version.File.Sha512) }).ToArray();
        var localSizes = selected.ToDictionary(s => s.Sha512, s => s.Item.Inspection.Size, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.Where(e => e.FromLocalFile))
            if (entry.Version.File.Size != localSizes[entry.Version.File.Sha512])
                blockers.Add("Розмір локального JAR не відповідає метаданим Modrinth: " + entry.Version.File.Name);
        foreach (var selection in selected.Where(s => s.Identity is not null))
        {
            var present = snapshot.Known.FirstOrDefault(i => i.Version.ProjectId == selection.Identity!.ProjectId);
            if (present is not null && present.Version.Id == selection.Identity!.Id && present.Version.File.Sha512 != selection.Sha512)
                blockers.Add(selection.Item.Inspection.FileName + ": уже встановлено інший файл тієї самої версії; автоматична заміна заборонена.");
        }
        var pending = selected.Where(s => !entries.Any(e => e.AlreadyInstalled && e.Version.File.Sha512 == s.Sha512))
            .Select(s => s.Item with { Inspection = s.Item.Inspection with { SourceStatus = s.Identity is null ? "Unknown Source" : "Local file / Modrinth SHA512" } }).ToArray();
        var review = await local.ReviewAsync(profile, pending, ct);
        if (!manualOnly && profile.Loader == ModLoader.Fabric && selected.Any(s => s.Identity is null && s.Item.Inspection.FabricMetadata is not null) &&
            review.Details.Any(b => b.Requirement is not null) && !review.Details.Any(b => b.Kind == ModBlockerKind.Definite))
        {
            (resolution, review) = await ResolveFabricProvidersAsync(profile, pending, snapshot, resolution, roots,
                new(catalog, identities), optionalSelections ?? [], automaticOptional, hashes, blockers, ct);
            entries = resolution.Entries.Select(e => e with { FromLocalFile = hashes.Contains(e.Version.File.Sha512) }).ToArray();
            blockers.AddRange(review.Blockers);
        }
        else if (entries.Any(e => !e.AlreadyInstalled && !e.FromLocalFile))
        {
            warnings.Add("Після завантаження залежностей у кеш усі фактичні JAR перевіряються разом до зміни профілю. Каталог не містить усіх внутрішніх Mod ID.");
            blockers.AddRange(review.Details.Where(b => b.Kind == ModBlockerKind.Definite).Select(b => b.Message));
            warnings.AddRange(review.Details.Where(b => b.Kind != ModBlockerKind.Definite).Select(b => "Попередня перевірка локальних файлів: " + b.Message));
        }
        else if (manualOnly)
        {
            blockers.AddRange(review.Details.Where(b => b.Kind != ModBlockerKind.MissingDependency).Select(b => b.Message));
            warnings.AddRange(review.Details.Where(b => b.Kind == ModBlockerKind.MissingDependency).Select(b => b.Message));
        }
        else blockers.AddRange(review.Blockers);
        warnings.AddRange(review.Warnings);
        resolution = resolution with { Entries = entries };
        var remote = new RemoteInstallPlan(profile.Id, profile.MinecraftVersion, profile.Loader, snapshot.Fingerprint, resolution, catalog.UsedCache);
        return new(remote, selected, blockers.Distinct().ToArray(), warnings.Distinct().ToArray(), manualOnly,
            review.Details.Select(b => b.Conflict).OfType<ModIdentityConflict>().ToArray(),
            review.Details.Select(b => b.Requirement).OfType<ModRequirement>()
                .DistinctBy(r => (r.Id, string.Join(" || ", r.Alternatives))).ToArray());
    }
    public async Task InstallLocalAsync(GameProfile profile, LocalDependencyPlan plan, bool confirmed, bool unknownAccepted,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        if (!confirmed) throw new InvalidOperationException("Підтвердьте повний кошик локальних модів і залежностей.");
        if (plan.RequiresUnknownAcceptance && !unknownAccepted) throw new InvalidOperationException("Підтвердьте внутрішнє попередження про Unknown Source.");
        if (!plan.CanInstall || plan.Remote.ProfileId != profile.Id || plan.Remote.MinecraftVersion != profile.MinecraftVersion || plan.Remote.Loader != profile.Loader ||
            (plan.ManualOnly && plan.Remote.Resolution.Entries.Count != 0))
            throw new InvalidOperationException("План несумісний із профілем або містить помилки.");
        using var lease = new ProfileUsage(paths).Acquire(profile.Id);
        new ProfileUsage(paths).CheckWorldLocks(profile.Id);
        await local.RecoverUnderLeaseAsync(profile, ct);
        if ((await ScanAsync(profile, false, ct)).Fingerprint != plan.Remote.InstalledFingerprint)
            throw new InvalidOperationException("Моди профілю змінилися. Перерахуйте спільний кошик.");
        var cart = new List<ModCartItem>();
        foreach (var file in plan.LocalFiles)
        {
            var current = await InspectSelectionAsync(file.Item, ct);
            if (current.Sha512 != file.Sha512) throw new InvalidDataException("Локальний файл змінився після розрахунку залежностей.");
            if (!plan.Remote.Resolution.Entries.Any(e => e.AlreadyInstalled && e.Version.File.Sha512 == file.Sha512))
                cart.Add(current.Item with { Inspection = current.Item.Inspection with { SourceStatus = file.Identity is null ? "Unknown Source" : "Local file / Modrinth SHA512" } });
        }
        foreach (var entry in plan.Downloads)
        {
            cart.Add(await PrepareCatalogJarAsync(entry.Version, entry.Reason, entry.Title, progress, ct));
        }
        if ((await ScanAsync(profile, false, ct)).Fingerprint != plan.Remote.InstalledFingerprint)
            throw new InvalidOperationException("Встановлені моди змінилися під час завантаження. Повторіть розрахунок.");
        // Recheck original inputs after downloads, then one real-JAR review and one durable transaction.
        foreach (var file in plan.LocalFiles)
            if ((await InspectSelectionAsync(file.Item, ct)).Sha512 != file.Sha512) throw new InvalidDataException("Локальний JAR змінився під час завантаження.");
        if (cart.Count > 0)
        {
            progress?.Report(new("", 0, null, Installing: true));
            await local.InstallUnderLeaseAsync(profile, cart, true, ct, allowMissingDependencies: plan.ManualOnly);
        }
        await store.AddHistoryAsync(profile.Id, "mods.combined", "local=" + plan.LocalFiles.Count + "; catalog=" + plan.Remote.Resolution.Entries.Count, CancellationToken.None);
    }
}
