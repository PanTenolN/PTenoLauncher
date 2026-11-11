using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed record ServerModFile(string Source, string Target, string Sha256, long Size, string Title, bool RequiresReview = false);
public sealed record ServerModNotice(string Title, ServerModDisposition Disposition);
public sealed record ServerModInstallPlan(LocalVanillaServer Server, string Fingerprint, IReadOnlyList<ServerModFile> Files)
{
    public IReadOnlyList<ServerModNotice> Excluded { get; init; } = [];
}

public sealed partial class VanillaServerManager
{
    private const string PendingModsFile = ".mods-install.json";
    private sealed record PendingServerMods(string Stage, IReadOnlyList<InstalledFile> Files);

    public async Task<ServerModInstallPlan> PreviewLocalModAsync(LocalVanillaServer server, string path, CancellationToken ct = default)
    {
        var folder = DirectoryFor(server.Id);
        using var lease = Acquire(folder);
        var snapshot = await InspectServerModsAsync(server, folder, ct);
        return await PlanServerModsAsync(server, folder, snapshot, [(Path.GetFullPath(path), Path.GetFileName(path), Path.GetFileName(path))], ct);
    }

    public async Task<ServerModInstallPlan> PreviewModrinthModAsync(LocalVanillaServer server, ModrinthClient catalog, string project,
        string version, CancellationToken ct = default)
    {
        var folder = DirectoryFor(server.Id);
        using var lease = Acquire(folder);
        var snapshot = await InspectServerModsAsync(server, folder, ct);
        var profile = ServerModProfile(server);
        if (server.Core == ServerCore.Forge && await catalog.GetProjectAsync(project, ct) is { SupportsServer: false } client)
            return new(server, ModFingerprint(snapshot), []) { Excluded = [new(client.Title, ServerModDisposition.Client)] };
        var installed = new List<InstalledCatalogMod>();
        foreach (var mod in snapshot)
        {
            await using var file = File.OpenRead(SafePath.Join(folder, "mods/" + mod.FileName));
            var hash = Convert.ToHexStringLower(await SHA512.HashDataAsync(file, ct));
            if (await catalog.FindBySha512Async(hash, ct) is { } known && known.File.Size == mod.Size) installed.Add(new(known));
        }
        var resolution = await new RemoteDependencyResolver(catalog).ResolveAsync(profile, [new(project, version)], installed,
            ct, dedicatedServer: true);
        if (!resolution.CanInstall) throw new InvalidOperationException(string.Join(Environment.NewLine, resolution.Blockers));
        var files = new List<(string Source, string Name, string Title)>();
        foreach (var entry in RemoteModInstaller.DistinctArtifacts(resolution.Entries.Where(entry => !entry.AlreadyInstalled)))
        {
            var path = await catalog.DownloadContentAsync(entry.Version, null, ct);
            files.Add((path, entry.Version.File.Name, entry.Title + " " + entry.Version.Number));
        }
        return await PlanServerModsAsync(server, folder, snapshot, files, ct);
    }

    public async Task ApplyServerModsAsync(ServerModInstallPlan plan, CancellationToken ct = default, bool reviewedForgeMods = false)
    {
        if (plan.Files.Count is < 1 or > 128) throw new InvalidDataException("ServerModInvalid");
        var folder = DirectoryFor(plan.Server.Id);
        using var lease = Acquire(folder);
        var installed = await InspectServerModsAsync(plan.Server, folder, ct);
        var current = await PlanServerModsAsync(plan.Server, folder, installed,
            plan.Files.Select(file => (file.Source, Path.GetFileName(file.Target), file.Title)), ct);
        if (current.Fingerprint != plan.Fingerprint || !current.Files.SequenceEqual(plan.Files))
            throw new IOException("ContentChanged");
        if (!reviewedForgeMods && current.Files.Any(file => file.RequiresReview))
            throw new InvalidOperationException("ServerModReview");
        var stageName = ".installing-mods-" + Guid.NewGuid().ToString("N");
        var stage = SafePath.Join(folder, stageName);
        var pending = SafePath.Join(folder, PendingModsFile);
        var journal = new PendingServerMods(stageName, plan.Files.Select(file =>
            new InstalledFile(Path.GetFileName(file.Target), file.Sha256, "SHA256", file.Size)).ToArray());
        if (Directory.Exists(stage) || File.Exists(stage)) throw new IOException("ContentChanged");
        Directory.CreateDirectory(stage);
        try
        {
            foreach (var file in plan.Files)
            {
                var name = Path.GetFileName(file.Target);
                var target = SafePath.Join(stage, name);
                await ProfileContent.CopyItemAsync(new(name, name, false), file.Source, target, ct);
                if (!await VerifiedDownloader.VerifyAsync(target, file.Sha256, "SHA256", file.Size, ct))
                    throw new IOException("ContentChanged");
            }
            await AtomicFile.WriteJsonAsync(pending, journal, ct);
            Directory.CreateDirectory(SafePath.Join(folder, "mods"));
            foreach (var file in plan.Files)
            {
                ct.ThrowIfCancellationRequested();
                SafePath.RejectLinks(file.Target);
                File.Move(SafePath.Join(stage, Path.GetFileName(file.Target)), file.Target, false);
            }
            File.Delete(pending);
        }
        catch
        {
            await RecoverServerModsAsync(folder, CancellationToken.None);
            throw;
        }
        finally
        {
            if (!File.Exists(pending) && Directory.Exists(stage))
            {
                RejectServerLinks(stage, CancellationToken.None);
                Directory.Delete(stage, recursive: true);
            }
        }
    }

    private async Task<List<ModInspection>> InspectServerModsAsync(LocalVanillaServer server, string folder, CancellationToken ct)
    {
        if (!server.CanInstallMods) throw new InvalidOperationException("ServerBuildUnavailable");
        await CheckIdentityAsync(server, folder, ct);
        await CheckProcessAsync(folder, ct);
        await RecoverServerModsAsync(folder, ct);
        var directory = SafePath.Join(folder, "mods");
        var installed = new List<ModInspection>();
        var inspector = new LocalModManager(paths, store ?? new SqliteLauncherStore(paths));
        if (!Directory.Exists(directory)) return installed;
        foreach (var path in Directory.EnumerateFiles(directory, "*.jar").Order(StringComparer.OrdinalIgnoreCase))
        {
            if (installed.Count == 2048) throw new InvalidDataException("ServerModInvalid");
            var mod = await InspectAsync(inspector, path, Path.GetFileName(path), ct);
            if (ClassifyServerMod(server.Core, mod) != ServerModDisposition.Include)
                throw new InvalidDataException("ServerModInvalid" + Environment.NewLine + mod.FileName);
            installed.Add(mod);
        }
        return installed;
    }

    private async Task<ServerModInstallPlan> PlanServerModsAsync(LocalVanillaServer server, string folder, List<ModInspection> installed,
        IEnumerable<(string Source, string Name, string Title)> proposed, CancellationToken ct)
    {
        var directory = SafePath.Join(folder, "mods");
        var inspector = new LocalModManager(paths, store ?? new SqliteLauncherStore(paths));
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var additions = new List<ModInspection>();
        var planned = new List<ServerModFile>();
        var excluded = new List<ServerModNotice>();
        var forge = server.Core == ServerCore.Forge;
        var knownHashes = installed.Select(file => file.Sha256).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, name, title) in proposed)
        {
            ModInspection inspection;
            try { inspection = await InspectAsync(inspector, source, name, ct); }
            catch (Exception error) when (forge && InstalledModReadiness.IsFileError(error))
            {
                excluded.Add(new(title, ServerModDisposition.Review));
                continue;
            }
            var disposition = ClassifyServerMod(server.Core, inspection);
            if (disposition != ServerModDisposition.Include)
            {
                if (!forge) throw new InvalidDataException("ServerModInvalid" + Environment.NewLine + name);
                excluded.Add(new(title, disposition));
                continue;
            }
            if (!knownHashes.Add(inspection.Sha256)) continue;
            var stem = ReadableName.Stem(Path.GetFileNameWithoutExtension(name), forge ? "Forge mod" : "Fabric mod");
            var target = ReadableName.Free(directory, stem, ".jar", reserved);
            additions.Add(inspection);
            planned.Add(new(source, target, inspection.Sha256, inspection.Size, title, RequiresReview: forge));
        }
        // An excluded catalog artifact may be a required dependency. Never publish a partial selection.
        if (excluded.Count > 0) return new(server, ModFingerprint(installed), []) { Excluded = excluded.AsReadOnly() };
        var profile = ServerModProfile(server);
        var blockers = forge
            ? ForgeCompatibility.Check(profile, ForgeServerReceipt(server.Version, server.Build!), [.. installed, .. additions], dedicatedServer: true).Issues
            : InstalledModReadiness.CheckFabricServer(profile, server.Build!, 21, [.. installed, .. additions]);
        if (blockers.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, blockers.Select(issue => issue.Message)));
        return new(server, ModFingerprint(installed), planned.AsReadOnly());
    }

    private static async Task<ModInspection> InspectAsync(LocalModManager inspector, string path, string name, CancellationToken ct)
    {
        if (!Path.IsPathFullyQualified(path) || !name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ServerModInvalid");
        SafePath.RejectLinks(path);
        return await inspector.InspectPayloadAsync(path, name, ct, "server");
    }

    private static ServerModDisposition ClassifyServerMod(ServerCore core, ModInspection mod)
    {
        if (core == ServerCore.Forge)
        {
            if (mod.Loader != ModLoader.Forge || mod.ForgeMetadata is not { } metadata) return ServerModDisposition.Review;
            if (metadata.ClientSideOnly) return ServerModDisposition.Client;
            return mod.MetadataWarnings is { Count: 0 } && mod.Nested is { Candidates.Count: 0, Issues.Count: 0, Library: false }
                ? ServerModDisposition.Include : ServerModDisposition.Review;
        }
        return mod.Loader == ModLoader.Fabric && mod.Environment is "*" or "server" && mod.Id is not null &&
            mod.Version is not null && mod.MetadataWarnings is { Count: 0 } ? ServerModDisposition.Include : ServerModDisposition.Review;
    }

    private static string ModFingerprint(IEnumerable<ModInspection> mods) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(mods.Select(mod => new { mod.FileName, mod.Sha256, mod.Size }))));

    private static GameProfile ServerModProfile(LocalVanillaServer server) =>
        new(server.Id, server.Name, server.Version, server.Core == ServerCore.Forge ? ModLoader.Forge : ModLoader.Fabric,
            server.MemoryMb, null, DateTimeOffset.UnixEpoch);

    private static InstallationReceipt ForgeServerReceipt(string version, string build) =>
        new(version, 21, "", DateTimeOffset.UnixEpoch, [], [], [], [], [], "", ModLoader.Forge, build);

    private static async Task RecoverServerModsAsync(string folder, CancellationToken ct)
    {
        var pending = SafePath.Join(folder, PendingModsFile);
        if (!File.Exists(pending)) return;
        var journal = await AtomicFile.ReadJsonAsync<PendingServerMods>(pending, ct) ?? throw new IOException("ContentChanged");
        const string prefix = ".installing-mods-";
        if (!journal.Stage.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(journal.Stage[prefix.Length..], "N", out _) ||
            journal.Files is not { Count: >= 1 and <= 128 }) throw new IOException("ContentChanged");
        var stage = SafePath.Join(folder, journal.Stage);
        if (!Directory.Exists(stage)) throw new IOException("ContentChanged");
        RejectServerLinks(stage, ct);
        var rollback = new List<string>();
        foreach (var file in journal.Files)
        {
            if (file.RelativePath != Path.GetFileName(file.RelativePath) || !file.RelativePath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                throw new IOException("ContentChanged");
            var source = SafePath.Join(stage, file.RelativePath);
            var target = SafePath.Join(folder, "mods/" + file.RelativePath);
            if (File.Exists(source) || !File.Exists(target)) continue;
            if (!await VerifiedDownloader.VerifyAsync(target, file.Hash, "SHA256", file.Size, ct))
                throw new IOException("ContentChanged");
            rollback.Add(target);
        }
        foreach (var target in rollback) File.Delete(target);
        File.Delete(pending);
        Directory.Delete(stage, recursive: true);
    }
}
