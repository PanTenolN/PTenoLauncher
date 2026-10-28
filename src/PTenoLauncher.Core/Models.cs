namespace PTenoLauncher.Core;

public enum ModLoader { Vanilla, Fabric, Forge, NeoForge, Quilt }

public sealed record GameProfile(Guid Id, string Name, string MinecraftVersion, ModLoader Loader,
    int MemoryMb, string? JavaPath, DateTimeOffset CreatedAt, bool Archived = false)
{
    public string Summary => $"{MinecraftVersion} · {Loader} · {MemoryMb / 1024.0:0.#} GB";
    public override string ToString() => Name;
}
public sealed record VersionEntry(string Id, string Type, Uri Url, string Sha1, DateTimeOffset ReleasedAt)
{
    public override string ToString() => $"{Id} ({Type})";
}
public sealed record VersionCatalog(DateTimeOffset FetchedAt, string LatestRelease, IReadOnlyList<VersionEntry> Versions, string? LatestSnapshot = null);
public sealed record ReadinessIssue(string Code, string Message);
public sealed record ReadinessReport(IReadOnlyList<ReadinessIssue> Issues, int VerifiedFiles, int VerifiedMods = 0)
{
    public bool Ready => Issues.Count == 0;
    public OfflineReadinessState State => Ready ? OfflineReadinessState.Ready : Issues.Any(i => i.Code.Contains("corrupt") || i.Code == "file.invalid") ? OfflineReadinessState.Error : Issues.Any(i => i.Code.Contains("unsupported") || i.Code.Contains("unknown")) ? OfflineReadinessState.Unknown : OfflineReadinessState.NeedsPreparation;
    public string Summary => Ready ? $"Готово до офлайн-гри: {VerifiedFiles} файлів Minecraft і {VerifiedMods} модів перевірено." : string.Join(Environment.NewLine, Issues.Select(i => "• " + i.Message));
}
public sealed record DownloadProgress(string Name, long Received, long? Total);
public sealed record MinecraftAccount(string Uuid, string Name, DateTimeOffset VerifiedAt, string? SkinUrl = null);
public sealed record MinecraftSession(MinecraftAccount Account, string AccessToken, DateTimeOffset ExpiresAt)
{
    public override string ToString() => $"MinecraftSession({Account.Name}, expires {ExpiresAt:O}, token [REDACTED])";
}
public sealed record InstalledFile(string RelativePath, string Hash, string Algorithm, long Size);
public sealed record InstallationReceipt(string Version, int JavaMajor, string MainClass, DateTimeOffset InstalledAt,
    IReadOnlyList<InstalledFile> Files, IReadOnlyList<string> Classpath, IReadOnlyList<string> NativeArchives,
    IReadOnlyList<string> JvmArguments, IReadOnlyList<string> GameArguments, string AssetIndex,
    ModLoader Loader = ModLoader.Vanilla, string? LoaderVersion = null);
public sealed record ModInspection(string FileName, string Sha256, long Size, string SourceStatus, string? Id,
    string? Name, string? Version, ModLoader? Loader, IReadOnlyList<string> RequiredDependencies, string Environment, IReadOnlyList<ModRequirement>? Requirements = null, IReadOnlyList<ProvidedMod>? ProvidedMods = null, IReadOnlyList<string>? MetadataWarnings = null, QuiltModMetadata? QuiltMetadata = null, ForgeModMetadata? ForgeMetadata = null);
public sealed record ProvidedMod(string Id, string Version, string? Group = null);
public sealed record HistoryEntry(DateTimeOffset At, string Action, string Details);

public interface ILauncherStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task RegisterArtifactAsync(OwnedArtifact artifact, CancellationToken ct = default);
    Task<IReadOnlyList<OwnedArtifact>> GetArtifactsAsync(Guid profileId, CancellationToken ct = default);
    Task DeleteProfileMetadataAsync(Guid profileId, CancellationToken ct = default);
    Task<IReadOnlyList<GameProfile>> GetProfilesAsync(CancellationToken ct = default);
    Task SaveProfileAsync(GameProfile profile, CancellationToken ct = default);
    Task<string?> GetSettingAsync(string key, CancellationToken ct = default);
    Task SetSettingAsync(string key, string value, CancellationToken ct = default);
    Task AddHistoryAsync(Guid? profileId, string action, string details, CancellationToken ct = default);
    Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync(Guid? profileId, CancellationToken ct = default);
}
