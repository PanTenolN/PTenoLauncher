using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
namespace PTenoLauncher.Infrastructure.Modpacks;

public sealed class MrpackException(string code, string detail = "", Exception? inner = null) : InvalidOperationException(code + ": " + detail, inner)
{ public string Code { get; } = code; public string Detail { get; } = detail; }
public sealed record MrpackProblem(string Code, string Detail);
public sealed record MrpackDownload(string Path, long Size, string Sha1, string Sha512, IReadOnlyList<Uri> Downloads, string Client, string Server, IReadOnlyList<string>? RejectedOrigins = null);
public sealed record MrpackFile(string Path, long Size, string? Entry, MrpackDownload? Download, string Source, string? Replaces = null);
public sealed record MrpackFilePreview(string Path, string Client, string Server, bool Selected, bool Optional, bool Cached, long Size, string Source);
public sealed record MrpackPreview(string Archive, long ArchiveSize, string ArchiveSha256, string Name, string Version, string Summary,
    string ProfileName, string Minecraft, ModLoader Loader, string? LoaderVersion, bool PinMods, IReadOnlyList<string> Optional,
    IReadOnlyList<MrpackFile> Files, IReadOnlyList<MrpackFilePreview> Entries, IReadOnlyList<MrpackProblem> Blockers, string Fingerprint)
{
    public string SourceKind { get; init; } = "local-mrpack";
    public string ContentRoot { get; init; } = "";
    public IReadOnlyList<string> Excluded { get; init; } = [];
    public IReadOnlyList<string> ManualFiles { get; init; } = [];
    public bool CanImport => Blockers.Count == 0;
    public long DownloadBytes => Files.Where(f => f.Download is not null).GroupBy(f => f.Download!.Sha512, StringComparer.Ordinal)
        .Sum(g => g.Any(f => Entries.Any(e => e.Path == f.Path && e.Cached)) ? 0 : g.First().Size);
}
public sealed record MrpackImportReceipt(int Schema, Guid Id, string Name, string Version, string ArchiveSha256, GameProfile Profile,
    string? LoaderVersion, IReadOnlyList<InstalledFile> Files, IReadOnlyList<string> Optional, bool PinMods, ModCartReview? Review)
{
    public string SourceKind { get; init; } = "local-mrpack";
    public string ArchiveFileName { get; init; } = "";
    public string IndexSha256 { get; init; } = "";
    public long IndexSize { get; init; }
    public string ContentRoot { get; init; } = "";
    public IReadOnlyList<string> Excluded { get; init; } = [];
}
public sealed record MrpackJournal(int Schema, Guid Id, string State, GameProfile Profile, IReadOnlyList<InstalledFile> Files);
public sealed record MrpackImportResult(GameProfile Profile, MrpackImportReceipt Receipt);
