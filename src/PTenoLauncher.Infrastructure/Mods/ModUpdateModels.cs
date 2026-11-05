using PTenoLauncher.Core;
namespace PTenoLauncher.Infrastructure.Mods;

public sealed class ModUpdateException(string code, string detail = "") : InvalidOperationException(code + ": " + detail)
{ public string Code { get; } = code; public string Detail { get; } = detail; }
public sealed record ModFileState(string Path, string Sha256, string Sha512, long Size);
public sealed record ModVersionPin(ModFileState File, CatalogModVersion? Provider);
public sealed record ModPinSet(Guid ProfileId, IReadOnlyList<ModVersionPin> Pins, int Schema = 1);
public sealed record ModSetState(GameProfile Profile, IReadOnlyList<ModFileState> Files, byte[]? Inventory, byte[]? Pins, byte[]? Epoch, string ContextHash);
public sealed record ModUpdateRow(ModFileState File, CatalogModVersion? Current, CatalogModVersion? Available, bool Pinned, string? Note);
public sealed record ModUpdateScan(string Fingerprint, IReadOnlyList<ModUpdateRow> Rows, IReadOnlyList<string> Findings, bool Cached, DateTimeOffset? MetadataAt);
public sealed record ModUpdateRequest(string Path, string? VersionId = null, bool Remove = false);
public sealed record ModUpdateChange(string Kind, ModFileState? Before, ModFileState? After, CatalogModVersion? Version);
public sealed record ModUpdatePlan(Guid Id, Guid ProfileId, DateTimeOffset At, string Kind, ModSetState Before, ModSetState After,
    IReadOnlyList<ModUpdateChange> Changes, IReadOnlyList<string> Blockers, IReadOnlyList<string> Warnings, Guid? Reverts = null)
{ public bool CanApply => Changes.Count > 0 && Blockers.Count == 0; }
public sealed record ModUpdateOperation(ModUpdatePlan Plan, string State, string Backup, string BackupSha256, int Schema = 1);
public sealed record ModUpdateHistory(Guid Id, DateTimeOffset At, string Kind, string State, string Backup)
{ public override string ToString() => At.ToLocalTime().ToString("g") + " · " + Kind + " · " + State; }
