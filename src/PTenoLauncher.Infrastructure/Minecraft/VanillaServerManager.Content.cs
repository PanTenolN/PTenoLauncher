using System.IO.Compression;
using System.Security.Cryptography;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Security;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public enum ServerModDisposition { Include, Client, Review }
public sealed record ServerProfileMod(string File, string? Version, string? Sha256, long Size, ServerModDisposition Disposition, string? Reason,
    bool CopyAfterReview = false);
public sealed record ServerProfilePlan(GameProfile Profile, ServerCore Core, string? Build, string? World, WorldVersion? WorldVersion,
    string? LevelHash, IReadOnlyList<ServerProfileMod> Mods, IReadOnlyList<string> Blockers, IReadOnlyList<string>? ForgeCopyBlockers = null);
public sealed record ServerPluginPlan(LocalVanillaServer Server, string Source, string Target, string Sha256, long Size);

public sealed partial class VanillaServerManager
{
    public IReadOnlyList<string> WorldsFor(Guid profileId)
    {
        var saves = SafePath.Join(paths.Game(profileId), "saves");
        return Directory.Exists(saves)
            ? Directory.EnumerateDirectories(saves).Select(folder => SafePath.Join(folder, "level.dat"))
                .Where(File.Exists).Select(file => Path.GetFileName(Path.GetDirectoryName(file))!).Order().ToArray()
            : [];
    }

    public async Task<ServerProfilePlan> PreviewProfileAsync(GameProfile profile, string? world, CancellationToken ct = default)
    {
        var usage = new ProfileUsage(paths);
        using var lease = usage.Acquire(profile.Id);
        usage.CheckWorldLocks(profile.Id);
        return await PreviewProfileUnderLeaseAsync(profile, world, ct);
    }

    private async Task<ServerProfilePlan> PreviewProfileUnderLeaseAsync(GameProfile profile, string? world, CancellationToken ct)
    {
        var profiles = store ?? throw new InvalidOperationException("ServerProfileUnavailable");
        if (!(await profiles.GetProfilesAsync(ct)).Contains(profile) || profile.Archived)
            throw new InvalidOperationException("ServerProfileUnavailable");
        var core = profile.Loader switch
        {
            ModLoader.Vanilla => ServerCore.Vanilla,
            ModLoader.Fabric => ServerCore.Fabric,
            ModLoader.Forge => ServerCore.Forge,
            _ => throw new InvalidOperationException("ServerBuildUnavailable")
        };
        var entry = await FindVersionAsync(profile.MinecraftVersion, ct);
        var receipt = await AtomicFile.ReadJsonAsync<InstallationReceipt>(paths.Receipt(profile.Id), ct);
        var installedBuild = receipt?.Version == profile.MinecraftVersion && receipt.Loader == profile.Loader ? receipt.LoaderVersion : null;
        var build = core == ServerCore.Vanilla ? null : profile.PreferredLoaderVersion ?? installedBuild;
        if (core == ServerCore.Forge)
        {
            build = ForgeDedicatedServer.ProfileBuild(build);
            if (installedBuild is not null && ForgeDedicatedServer.ProfileBuild(installedBuild) != build)
                throw new InvalidOperationException("ServerProfileVersionChanged");
            if (!ForgeDedicatedServer.Supports(entry.Id, build)) throw new InvalidOperationException("ServerBuildUnavailable");
        }
        else if (core != ServerCore.Vanilla)
        {
            if (installedBuild is not null && installedBuild != build) throw new InvalidOperationException("ServerProfileVersionChanged");
            _ = await cores.ResolveAsync(core, entry.Id, build, ct);
        }
        var mods = new List<ServerProfileMod>();
        var blockers = new List<string>();
        IReadOnlyList<string>? forgeCopyBlockers = null;
        if (core == ServerCore.Forge)
            forgeCopyBlockers = await InspectForgeProfileAsync(profile, build!, profiles, mods, ct);
        else if (core == ServerCore.Fabric)
        {
            var inspection = new LocalModManager(paths, profiles);
            var folder = SafePath.Join(paths.Game(profile.Id), "mods");
            var accepted = new List<ModInspection>();
            if (Directory.Exists(folder))
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*.jar").Order(StringComparer.OrdinalIgnoreCase))
                {
                    ct.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(file);
                    try
                    {
                        var mod = await inspection.InspectPayloadAsync(file, name, ct, "server");
                        var kind = mod.Loader == ModLoader.Fabric && mod.Environment == "client" ? ServerModDisposition.Client :
                            mod.Loader == ModLoader.Fabric && mod.Environment is "*" or "server" && mod.MetadataWarnings is { Count: 0 }
                                ? ServerModDisposition.Include : ServerModDisposition.Review;
                        var reason = kind == ServerModDisposition.Review ? string.Join(" · ",
                            new[] { mod.Loader?.ToString() ?? "?", mod.Environment }.Concat(mod.MetadataWarnings ?? [])) : null;
                        mods.Add(new(name, mod.Version, mod.Sha256, mod.Size, kind, reason));
                        if (kind == ServerModDisposition.Include) accepted.Add(mod);
                        if (mod.Loader is not null && mod.Loader != ModLoader.Fabric)
                            blockers.Add(name + " · " + mod.Loader + " → Fabric");
                    }
                    catch (Exception error) when (InstalledModReadiness.IsFileError(error))
                    {
                        mods.Add(new(name, null, null, 0, ServerModDisposition.Review, error.Message));
                        blockers.Add(name + " · " + error.Message);
                    }
                }
            }
            using var metadata = await versions.GetVersionAsync(entry, ct);
            var major = metadata.RootElement.TryGetProperty("javaVersion", out var java) ? java.GetProperty("majorVersion").GetInt32() : 8;
            blockers.AddRange(InstalledModReadiness.CheckFabricServer(profile, build!, major, accepted).Select(issue => issue.Message));
        }
        WorldVersion? worldVersion = null;
        string? levelHash = null;
        if (world is not null)
        {
            await using var level = File.OpenRead(SafePath.Join(WorldSource(profile.Id, world), "level.dat"));
            if (level.Length > 64L * 1024 * 1024) throw new InvalidDataException("WorldArchiveInvalid");
            levelHash = Convert.ToHexString(await SHA256.HashDataAsync(level, ct));
            level.Position = 0;
            worldVersion = WorldVersionReader.Read(level);
            if (worldVersion.Name is { } savedVersion && savedVersion != profile.MinecraftVersion)
                blockers.Add("ServerWorldVersionMismatch");
        }
        return new(profile, core, build, world, worldVersion, levelHash, mods.AsReadOnly(), blockers.AsReadOnly(), forgeCopyBlockers);
    }

    private async Task<IReadOnlyList<string>> InspectForgeProfileAsync(GameProfile profile, string build, ILauncherStore profiles,
        List<ServerProfileMod> rows, CancellationToken ct)
    {
        var inspector = new LocalModManager(paths, profiles);
        var folder = SafePath.Join(paths.Game(profile.Id), "mods");
        var candidates = new List<ModInspection>();
        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.jar").Order(StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file);
                try
                {
                    var mod = await inspector.InspectPayloadAsync(file, name, ct, "server");
                    var metadata = mod.ForgeMetadata;
                    var disposition = ClassifyServerMod(ServerCore.Forge, mod);
                    var copy = disposition == ServerModDisposition.Include;
                    var notes = new[] { mod.Loader?.ToString() ?? "?" }.Concat(mod.MetadataWarnings ?? []);
                    if (metadata is not null)
                        notes = notes.Concat(metadata.Mods.SelectMany(m => m.Dependencies)
                            .Where(d => d.Side is "SERVER" or "BOTH")
                            .Select(d => $"{d.Id} {d.Range} [{d.Kind}, {d.Side}]").Distinct(StringComparer.Ordinal));
                    rows.Add(new(name, mod.Version, mod.Sha256, mod.Size,
                        disposition == ServerModDisposition.Client ? disposition : ServerModDisposition.Review, string.Join(" · ", notes), copy));
                    if (copy) candidates.Add(mod);
                }
                catch (Exception error) when (InstalledModReadiness.IsFileError(error))
                {
                    rows.Add(new(name, null, null, 0, ServerModDisposition.Review, error.Message));
                }
            }
        }
        var receipt = ForgeServerReceipt(profile.MinecraftVersion, build);
        return ForgeCompatibility.Check(profile, receipt, candidates, dedicatedServer: true).Issues.Select(issue => issue.Message).ToArray();
    }

    public async Task<LocalVanillaServer> CreateFromProfileAsync(ServerProfilePlan plan, string name, int port, int memoryMb,
        CancellationToken ct = default, bool privateServer = false, bool copyReviewedForgeMods = false)
    {
        var usage = new ProfileUsage(paths);
        using var lease = usage.Acquire(plan.Profile.Id);
        usage.CheckWorldLocks(plan.Profile.Id);
        var current = await PreviewProfileUnderLeaseAsync(plan.Profile, plan.World, ct);
        if (current != (plan with { Mods = current.Mods, Blockers = current.Blockers, ForgeCopyBlockers = current.ForgeCopyBlockers }) ||
            !current.Mods.SequenceEqual(plan.Mods) || !current.Blockers.SequenceEqual(plan.Blockers) ||
            !(current.ForgeCopyBlockers ?? []).SequenceEqual(plan.ForgeCopyBlockers ?? []))
            throw new InvalidOperationException("ContentChanged");
        if (current.Blockers.Count != 0) throw new InvalidOperationException("ServerProfileBlocked");
        if (copyReviewedForgeMods && (current.Core != ServerCore.Forge || current.ForgeCopyBlockers is not { Count: 0 }))
            throw new InvalidOperationException("ServerProfileBlocked");
        return await CreateCoreAsync(name, current.Profile.MinecraftVersion, port, memoryMb, current.Core, current.Build, async (stage, token) =>
        {
            var mods = SafePath.Join(stage, "mods");
            foreach (var mod in current.Mods.Where(mod => mod.Disposition == ServerModDisposition.Include ||
                copyReviewedForgeMods && mod.CopyAfterReview))
            {
                var source = SafePath.Join(paths.Game(current.Profile.Id), "mods/" + mod.File);
                var target = SafePath.Join(mods, mod.File);
                await ProfileContent.CopyItemAsync(new(mod.File, mod.File, false), source, target, token);
                if (!await VerifiedDownloader.VerifyAsync(target, mod.Sha256!, "SHA256", mod.Size, token))
                    throw new InvalidOperationException("ContentChanged");
            }
            if (current.World is { } world)
            {
                var source = WorldSource(current.Profile.Id, world);
                var lockPath = SafePath.Join(source, "session.lock");
                using var worldLock = File.Exists(lockPath) ? new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.None) : null;
                var target = SafePath.Join(stage, "world");
                await ProfileContent.CopyItemAsync(new(world, world, true), source, target, token);
                await using var level = File.OpenRead(SafePath.Join(target, "level.dat"));
                if (Convert.ToHexString(await SHA256.HashDataAsync(level, token)) != current.LevelHash)
                    throw new InvalidOperationException("ContentChanged");
            }
        }, privateServer, ct);
    }

    private string WorldSource(Guid profile, string world)
    {
        if (world != Path.GetFileName(world) || world.Contains('/')) throw new InvalidDataException("WorldArchiveInvalid");
        return SafePath.Join(paths.Game(profile), "saves/" + world);
    }

    public string PluginsFolder(LocalVanillaServer server) => server.HasPlugins
        ? SafePath.Join(DirectoryFor(server.Id), "plugins") : throw new InvalidOperationException("ServerPluginInvalid");

    public async Task<ServerPluginPlan> PreviewPluginAsync(LocalVanillaServer server, string source, CancellationToken ct = default)
    {
        var folder = PluginsFolder(server);
        source = Path.GetFullPath(source);
        SafePath.RejectLinks(source);
        if (!source.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("ServerPluginInvalid");
        var stem = ReadableName.Stem(Path.GetFileNameWithoutExtension(source), "Plugin", 100);
        await using var input = File.OpenRead(source);
        if (input.Length is <= 0 or > 256L * 1024 * 1024) throw new InvalidDataException("ServerPluginInvalid");
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        SafeArchive.Validate(archive, folder);
        if (archive.GetEntry("plugin.yml") is null && archive.GetEntry("paper-plugin.yml") is null)
            throw new InvalidDataException("ServerPluginInvalid");
        input.Position = 0;
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
        return new(server, source, ReadableName.Free(folder, stem, ".jar"), digest, input.Length);
    }

    public async Task AddPluginAsync(ServerPluginPlan plan, CancellationToken ct = default)
    {
        var folder = DirectoryFor(plan.Server.Id);
        using var lease = Acquire(folder);
        await CheckIdentityAsync(plan.Server, folder, ct);
        await CheckProcessAsync(folder, ct);
        if (await PreviewPluginAsync(plan.Server, plan.Source, ct) != plan) throw new InvalidOperationException("ContentChanged");
        // Verify the staged bytes before publishing under the confirmed name.
        var staged = SafePath.Join(PluginsFolder(plan.Server), ".plugin-" + Guid.NewGuid().ToString("N") + ".part");
        try
        {
            await ProfileContent.CopyItemAsync(new(Path.GetFileName(plan.Source), Path.GetFileName(plan.Source), false), plan.Source, staged, ct);
            if (!await VerifiedDownloader.VerifyAsync(staged, plan.Sha256, "SHA256", plan.Size, ct))
                throw new InvalidOperationException("ContentChanged");
            ct.ThrowIfCancellationRequested();
            SafePath.RejectLinks(plan.Target);
            File.Move(staged, plan.Target);
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }
}
