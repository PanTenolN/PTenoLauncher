using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Modpacks;

public enum PackBasis { Left, Right }

public sealed record PackCopyRow(string Decision, ComparedPackFile? Basis, ComparedPackFile? Other, string? Verification);

public sealed class PackCopyPlan
{
    internal PackCopyPlan(MrpackPreview basis, MrpackPreview other, IReadOnlyList<PackCopyRow> rows,
        IReadOnlyList<MrpackProblem> blockers, IReadOnlyList<string> warnings)
    {
        Base = basis;
        Other = other;
        Rows = rows;
        Blockers = blockers;
        Warnings = warnings;
    }

    internal MrpackPreview Base { get; }
    internal MrpackPreview Other { get; }
    public string BaseArchive => Base.Archive;
    public string OtherArchive => Other.Archive;
    public string BaseSha256 => Base.ArchiveSha256;
    public string OtherSha256 => Other.ArchiveSha256;
    public string ProfileName => Base.ProfileName;
    public string Minecraft => Base.Minecraft;
    public ModLoader Loader => Base.Loader;
    public string? LoaderVersion => Base.LoaderVersion;
    public IReadOnlyList<PackCopyRow> Rows { get; }
    public IReadOnlyList<MrpackProblem> Blockers { get; }
    public IReadOnlyList<string> Warnings { get; }
    public bool CanCreate => Blockers.Count == 0;
}

public sealed partial class MrpackImporter
{
    internal const string ReconciledSourceKind = "reconciled-mrpack";

    public async Task<PackCopyPlan> PrepareCopyAsync(string left, string right, PackBasis basis, CancellationToken ct = default)
    {
        var (first, second) = basis switch
        {
            PackBasis.Left => (left, right),
            PackBasis.Right => (right, left),
            _ => throw new ArgumentOutOfRangeException(nameof(basis))
        };
        var (basePack, baseSource) = await PackComparison.InspectAsync(Reader, first, ct);
        var (otherPack, otherSource) = await PackComparison.InspectAsync(Reader, second, ct);
        const string copySuffix = " (copy)";
        var nameLength = Math.Min(80 - copySuffix.Length, baseSource.ProfileName.Length);
        if (char.IsHighSurrogate(baseSource.ProfileName[nameLength - 1])) nameLength--;
        baseSource = baseSource with { ProfileName = baseSource.ProfileName[..nameLength].TrimEnd() + copySuffix };
        var compare = PackComparison.Compare(basePack, otherPack, ct);
        var rows = new List<PackCopyRow>();
        var problems = new List<MrpackProblem>();
        var selected = new List<ComparedPackFile>();
        var inspections = new List<ModCartItem>();
        var inspector = new LocalModManager(paths, store);
        var content = new ProfileContent(paths);
        if ((await PendingAsync(ct)).Count > 0) problems.Add(new("PackRecovery", ""));

        foreach (var difference in compare.Files)
        {
            ct.ThrowIfCancellationRequested();
            var a = difference.Left;
            var b = difference.Right;
            var chosen = a ?? b!;
            var payload = chosen.Payload;
            var decision = a is null ? "PackCopyAdd" : difference.Kind;
            var verification = payload?.Download is not null ? "PackCopyDeclared" : null;
            if (a is not null && b is not null && Inconsistent(a, b))
            {
                problems.Add(new("PackCopyConflict", a.Path));
                rows.Add(new("PackCopyConflict", a, b, verification));
                continue;
            }
            if (!SupportedCopy(chosen) || a is null && !compare.SameTarget)
            {
                rows.Add(new(a is null && !compare.SameTarget ? "PackCopyOtherTarget" : "PackCopyManual", a, b, verification));
                continue;
            }
            if (a is not null && b is not null && a.Sha512 != b.Sha512 && difference.Kind != "PackCompareVersion")
                decision = "PackCopyKeepConflict";
            if (MrpackReader.RootJar(chosen.Path) && selected.Any(item => item.Sha512 == chosen.Sha512))
            {
                if (selected.Any(item => item.Sha512 == chosen.Sha512 && Inconsistent(item, chosen)))
                    problems.Add(new("PackCopyConflict", chosen.Path));
                rows.Add(new("PackCopyDuplicate", a, b, verification));
                continue;
            }
            if (selected.Any(item => item.Path.Equals(chosen.Path, StringComparison.OrdinalIgnoreCase) ||
                chosen.Project is not null && item.Project == chosen.Project))
            {
                problems.Add(new("PackCopyConflict", chosen.Path));
                rows.Add(new("PackCopyConflict", a, b, verification));
                continue;
            }
            selected.Add(chosen);
            rows.Add(new(decision, a, b, verification));
        }

        if (selected.Count > MrpackReader.MaxFiles || selected.Sum(f => f.Payload!.Size) > MrpackReader.MaxExpandedBytes ||
            selected.Count(f => MrpackReader.RootJar(f.Path)) > 128)
            problems.Add(new("PackBudget", ""));
        var warnings = new List<string>();
        if (problems.Count == 0)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (!WillCopy(row)) continue;
                var file = (row.Basis ?? row.Other)!;
                try
                {
                    if (file.Payload!.Download is not { } download)
                    {
                        await CheckIncludedPackAsync(row.Basis is not null ? baseSource : otherSource, file, content, ct);
                        rows[i] = row with { Verification = "PackCopyChecked" };
                        continue;
                    }
                    if (file.Project is null || file.VersionId is null ||
                        !download.Downloads.Any(url => PackExporter.OfficialFile(url, file.Project, file.VersionId, Path.GetFileName(file.Path))))
                        throw new MrpackException("PackOrigin", file.Path);
                    var official = download.Downloads.Where(url => PackExporter.OfficialFile(url, file.Project, file.VersionId, Path.GetFileName(file.Path))).ToArray();
                    await VerifyDownloadAsync(download with { Downloads = official }, null, ct);
                    rows[i] = row with { Verification = "PackCopyChecked" };
                    var cached = Reader.CachePath(download);
                    if (MrpackReader.RootJar(file.Path))
                    {
                        var jar = await inspector.InspectPayloadAsync(cached, Path.GetFileName(file.Path), ct);
                        if (jar.Environment == "server" || jar.Loader is null || string.IsNullOrEmpty(jar.Id))
                            problems.Add(new("PackCompatibility", file.Path));
                        inspections.Add(new(cached, jar));
                    }
                    else await content.ValidatePackAsync(cached, ContentKind(file.Path), ct);
                }
                catch (MrpackException error) { problems.Add(new(error.Code, file.Path)); }
                catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException or KeyNotFoundException or UnauthorizedAccessException or InvalidOperationException)
                { problems.Add(new("PackIntegrity", file.Path)); }
            }
            if (inspections.Count > 0)
            {
                var target = new GameProfile(Guid.NewGuid(), baseSource.ProfileName, baseSource.Minecraft, baseSource.Loader, 4096, null, DateTimeOffset.UtcNow);
                var review = await inspector.ReviewResultSetAsync(target, inspections, ct);
                problems.AddRange(review.Blockers.Select(issue => new MrpackProblem("PackCompatibility", issue)));
                warnings.AddRange(review.Warnings);
            }
        }
        await CheckArchiveAsync(baseSource, ct);
        await CheckArchiveAsync(otherSource, ct);
        var plan = new PackCopyPlan(baseSource, otherSource, rows.AsReadOnly(), problems.AsReadOnly(), warnings.AsReadOnly());
        if (plan.CanCreate)
        {
            try { _ = CopyIndex(plan); }
            catch (MrpackException error) { problems.Add(new(error.Code, error.Detail)); }
        }
        return plan;
    }

    private async Task CheckIncludedPackAsync(MrpackPreview source, ComparedPackFile file, ProfileContent content, CancellationToken ct)
    {
        await using var archive = OpenSource(source);
        if (archive.Length != source.ArchiveSize) throw new MrpackException("PackChanged", file.Path);
        MrpackReader.CheckContainer(source.Archive);
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.GetEntry(file.Payload!.Entry!) ?? throw new MrpackException("PackChanged", file.Path);
        var temp = SafePath.Join(paths.Cache, ".pteno-copy-review-" + Guid.NewGuid().ToString("N") + ".part");
        var owned = false;
        try
        {
            await using (var input = entry.Open())
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                owned = true;
                await CopyPayloadAsync(input, output, file.Path, file.Payload.Size, null, ct);
            }
            if (!await VerifiedDownloader.VerifyAsync(temp, file.Sha512, "SHA512", file.Payload.Size, ct))
                throw new MrpackException("PackChanged", file.Path);
            await content.ValidatePackAsync(temp, ContentKind(file.Path), ct);
        }
        finally
        {
            if (owned)
            {
                SafePath.RejectLinks(temp);
                File.Delete(temp);
            }
        }
    }

    private static ProfileContentKind ContentKind(string path) => path.StartsWith("resourcepacks/", StringComparison.Ordinal)
        ? ProfileContentKind.ResourcePacks : ProfileContentKind.Shaders;

    private static bool Inconsistent(ComparedPackFile a, ComparedPackFile b)
    {
        if (a.Sha512 == b.Sha512)
            return a.Project is not null && b.Project is not null && (a.Project != b.Project || a.VersionId != b.VersionId) ||
                a.Payload is { } x && b.Payload is { } y && (x.Size != y.Size ||
                    x.Download is { } dx && y.Download is { } dy && dx.Sha1 != dy.Sha1);
        return a.Project is not null && a.Project == b.Project && a.VersionId is not null && a.VersionId == b.VersionId &&
            Path.GetFileName(a.Path).Equals(Path.GetFileName(b.Path), StringComparison.OrdinalIgnoreCase);
    }

    private static bool SupportedCopy(ComparedPackFile file)
    {
        if (file.Payload is null || file.Payload.Download?.Client == "optional") return false;
        if (MrpackReader.RootJar(file.Path)) return file.Payload.Download is not null;
        return file.Path.Count(c => c == '/') == 1 && file.Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
            (file.Path.StartsWith("resourcepacks/", StringComparison.Ordinal) || file.Path.StartsWith("shaderpacks/", StringComparison.Ordinal));
    }

    private static bool WillCopy(PackCopyRow row) =>
        row.Decision is not ("PackCopyConflict" or "PackCopyDuplicate" or "PackCopyManual" or "PackCopyOtherTarget");

    public async Task<MrpackImportResult> CreateCopyAsync(PackCopyPlan plan, bool confirmed,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        if (!confirmed) throw new MrpackException("PackConsent");
        if (!plan.CanCreate) throw new MrpackException(plan.Blockers[0].Code, plan.Blockers[0].Detail);
        ct.ThrowIfCancellationRequested();
        using var lease = Lease();
        if ((await PendingAsync(ct)).Count != 0) throw new MrpackException("PackRecovery");
        await using var first = OpenSource(plan.Base);
        await using var second = OpenSource(plan.Other);
        await CheckSourcesAsync(ct);
        using var baseZip = new ZipArchive(first, ZipArchiveMode.Read, leaveOpen: true);
        using var otherZip = new ZipArchive(second, ZipArchiveMode.Read, leaveOpen: true);

        var selected = plan.Rows.Where(WillCopy).Select(row => (File: (row.Basis ?? row.Other)!, FromBase: row.Basis is not null)).ToArray();
        foreach (var (file, _) in selected)
            if (file.Payload!.Download is { } download && !await Reader.CachedAsync(download, ct))
                throw new MrpackException("PackCopyVerifyAgain", file.Path);

        var (index, version, fingerprint) = CopyIndex(plan);
        var fresh = plan.Base with
        {
            SourceKind = ReconciledSourceKind, Version = version, Fingerprint = fingerprint, Blockers = [], Optional = [],
            Files = selected.Select(item => item.File.Payload! with { Download = null }).ToArray()
        };
        var id = Guid.NewGuid();
        try
        {
            return await ImportPreparedAsync(fresh, progress, ct, verifyInstance: CheckSourcesAsync, instanceMetadata: index,
                copyPrepared: CopyAsync, profileId: id);
        }
        catch
        {
            if (File.Exists(Journal(id)) && (await ReadJournal(id, CancellationToken.None)).State == "Preparing")
                await DiscardPreparingAsync(id);
            throw;
        }

        async Task CheckSourcesAsync(CancellationToken token)
        {
            await CheckArchiveAsync(plan.Base, token, first);
            await CheckArchiveAsync(plan.Other, token, second);
        }
        async Task CopyAsync(string game, CancellationToken token)
        {
            foreach (var (file, fromBase) in selected)
            {
                var payload = file.Payload!;
                var target = SafePath.Join(game, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = payload.Download is { } d ? File.OpenRead(Reader.CachePath(d)) :
                    ((fromBase ? baseZip : otherZip).GetEntry(payload.Entry!) ?? throw new MrpackException("PackChanged")).Open();
                await CopyPayloadAsync(input, target, file.Path, payload.Size, progress, token);
                if (!await VerifiedDownloader.VerifyAsync(target, file.Sha512, "SHA512", payload.Size, token))
                    throw new MrpackException("PackIntegrity", file.Path);
            }
        }
    }

    private static (byte[] Bytes, string Version, string Fingerprint) CopyIndex(PackCopyPlan plan)
    {
        var dependencies = PackExportIndex.Dependencies(plan.Minecraft, plan.Loader, plan.LoaderVersion);
        var confirmation = new
        {
            basis = Path.GetFileName(plan.BaseArchive), plan.BaseSha256,
            other = Path.GetFileName(plan.OtherArchive), plan.OtherSha256, plan.ProfileName, dependencies, plan.Rows
        };
        var fingerprint = MrpackReader.Hash(JsonSerializer.SerializeToUtf8Bytes(confirmation, AtomicFile.JsonOptions));
        var version = "reconciled-" + fingerprint[..12];
        var index = JsonSerializer.SerializeToUtf8Bytes(new { plan.Base.Name, versionId = version, dependencies, fingerprint, confirmation }, AtomicFile.JsonOptions);
        if (index.Length > MrpackReader.MaxIndexBytes) throw new MrpackException("PackBudget");
        using var validation = MrpackReader.Json(index);
        return (index, version, fingerprint);
    }

    private async Task DiscardPreparingAsync(Guid id)
    {
        var journal = await ReadJournal(id, CancellationToken.None);
        Validate(journal);
        if (journal.Id != id || journal.State != "Preparing" || Directory.Exists(paths.Profile(id)) ||
            (await store.GetProfilesAsync(CancellationToken.None)).Any(profile => profile.Id == id))
            throw new MrpackException("PackConflict");
        var stage = Staging(id).Root;
        if (Directory.Exists(stage))
        {
            var remaining = new Stack<string>();
            remaining.Push(stage);
            while (remaining.TryPop(out var path))
            {
                SafePath.RejectLinks(path);
                if (Directory.Exists(path))
                    foreach (var child in Directory.EnumerateFileSystemEntries(path)) remaining.Push(child);
            }
            Directory.Delete(stage, recursive: true);
        }
        await AtomicFile.WriteJsonAsync(Journal(id), journal with { State = "Aborted" }, CancellationToken.None);
    }

    private static FileStream OpenSource(MrpackPreview source)
    {
        SafePath.RejectLinks(source.Archive);
        try { return new(source.Archive, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous); }
        catch (IOException error) { throw new MrpackException("PackChanged", Path.GetFileName(source.Archive), error); }
    }

    private static async Task CheckArchiveAsync(MrpackPreview source, CancellationToken ct, FileStream? opened = null)
    {
        await using var current = OpenSource(source);
        if (current.Length != source.ArchiveSize ||
            Convert.ToHexStringLower(await SHA256.HashDataAsync(current, ct)) != source.ArchiveSha256 ||
            opened is not null && (opened.Length != current.Length || !await MatchesOpenedAsync()))
            throw new MrpackException("PackChanged", Path.GetFileName(source.Archive));

        async Task<bool> MatchesOpenedAsync()
        {
            opened!.Position = 0;
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(opened, ct));
            opened.Position = 0;
            return hash == source.ArchiveSha256;
        }
    }
}
