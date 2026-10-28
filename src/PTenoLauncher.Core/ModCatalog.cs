namespace PTenoLauncher.Core;

public sealed record CatalogProject(string Id, string Slug, string Title, bool SupportsClient);
public sealed record CatalogSearchHit(string Id, string Slug, string Title, string Description)
{
    public override string ToString() => Title;
}
public sealed record CatalogDependency(string? ProjectId, string? VersionId, string Kind, string? FileName = null);
public sealed record CatalogModFile(string Name, Uri Url, long Size, string Sha512);
public sealed record CatalogModVersion(string Id, string ProjectId, string Number, string Name,
    DateTimeOffset Published, string Channel, IReadOnlyList<string> GameVersions, IReadOnlyList<string> Loaders,
    IReadOnlyList<CatalogDependency> Dependencies, CatalogModFile File)
{
    public override string ToString() => Number + " (" + Channel + ")";
}
public sealed record RemoteModRequest(string ProjectId, string? VersionId = null, string? Reason = null);
public sealed record InstalledCatalogMod(CatalogModVersion Version);
public sealed record ModPlanEntry(CatalogModVersion Version, string Title, string Reason, bool AlreadyInstalled, bool FromLocalFile = false)
{
    public string Details => $"{Version.Number} · {(FromLocalFile ? "Локальний JAR / Modrinth SHA512" : "Modrinth")} · Minecraft {string.Join(", ", Version.GameVersions)} · {string.Join(", ", Version.Loaders)}";
    public string Download => AlreadyInstalled ? "Уже встановлено" : FromLocalFile ? "Локальний файл; без завантаження" : $"{Version.File.Size / 1048576.0:0.##} MB";
    public string Dependencies => string.Join(", ", Version.Dependencies.Select(d => $"{d.Kind}: {d.ProjectId ?? d.VersionId ?? d.FileName ?? "невідомо"}"));
}
public sealed record OptionalModDependency(string Parent, CatalogDependency Dependency, string? ParentProjectId = null, string? ParentVersionId = null)
{
    public OptionalDependencyKey Key => new(ParentVersionId ?? "", Dependency.ProjectId, Dependency.VersionId, Dependency.FileName);
    public bool CanSelect => ParentProjectId is not null && ParentVersionId is not null && (Dependency.ProjectId is not null || Dependency.VersionId is not null);
}
public sealed record OptionalDependencyKey(string ParentVersionId, string? ProjectId, string? VersionId, string? FileName = null);
public sealed record DependencyResolution(IReadOnlyList<ModPlanEntry> Entries,
    IReadOnlyList<OptionalModDependency> Optional, IReadOnlyList<string> Blockers, IReadOnlyList<string> Warnings, IReadOnlyList<OptionalDependencyKey>? SelectedOptional = null)
{
    public bool CanInstall => Blockers.Count == 0 && Entries.Count > 0;
}
public sealed record RemoteInstallPlan(Guid ProfileId, string MinecraftVersion, ModLoader Loader,
    string InstalledFingerprint, DependencyResolution Resolution, bool CachedMetadata);
public interface IModCatalog
{
    Task<CatalogProject> GetProjectAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<CatalogModVersion>> GetVersionsAsync(string projectId, string minecraft, ModLoader loader, CancellationToken ct = default);
    Task<CatalogModVersion> GetVersionAsync(string id, CancellationToken ct = default);
}
