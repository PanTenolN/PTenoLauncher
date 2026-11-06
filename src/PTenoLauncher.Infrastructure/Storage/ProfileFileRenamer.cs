using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Modpacks;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Network;

namespace PTenoLauncher.Infrastructure.Storage;

public sealed record ProfileFileRename(Guid Profile, string GameRoot, string From, string To, string Hash, long Size, bool Restore)
{
    public string Source => SafePath.Join(GameRoot, From);
    public string Target => SafePath.Join(GameRoot, To);
}

public sealed class ProfileFileRenamer(LauncherPaths paths, ILauncherStore store, LocalModManager mods, ProfileContent content)
{
    private sealed record Journal(Guid Profile, string From, string To, string Hash, long Size, InstalledModInventory? Before);
    private readonly ProfileUsage usage = new(paths);
    private readonly ModInventoryStore inventory = new(paths);
    internal static string PendingPath(LauncherPaths paths, Guid id) => SafePath.Join(paths.Root, $"locks/name-change-{id:N}.json");
    private string LastPath(Guid id) => SafePath.Join(paths.Profile(id), ".pteno-last-name-change.json");
    internal Action? FaultAfterMove { get; set; }

    public async Task<ProfileFileRename> PreviewAsync(Guid id, string? relative, CancellationToken ct = default)
    {
        using var lease = usage.Acquire(id);
        return await PlanAsync(id, relative, ct);
    }

    public async Task ApplyAsync(ProfileFileRename approved, CancellationToken ct = default)
    {
        using var lease = usage.Acquire(approved.Profile);
        var plan = await PlanAsync(approved.Profile, approved.Restore ? null : approved.From, ct);
        if (plan != approved) throw new IOException("ContentChanged");
        var before = await inventory.ReadAsync(plan.Profile, ct);
        if (before?.Files.All(file => !Same(file.RelativePath, plan.From)) == true) before = null;
        var journal = new Journal(plan.Profile, plan.From, plan.To, plan.Hash, plan.Size, before);
        Validate(plan.Profile, journal);
        var pending = PendingPath(paths, plan.Profile);
        await AtomicFile.WriteJsonAsync(pending, journal, ct);
        var moved = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            File.Move(plan.Source, plan.Target, false);
            moved = true;
            FaultAfterMove?.Invoke();
            if (!await MatchesAsync(plan.Target, journal)) throw new IOException("ContentChanged");
            if (before is not null) await AtomicFile.WriteJsonAsync(inventory.PathFor(plan.Profile), After(journal));
            File.Move(pending, LastPath(plan.Profile), true);
        }
        catch
        {
            if (moved) await RollbackAsync(journal);
            else File.Delete(pending);
            throw;
        }
    }

    public async Task RecoverAsync(Guid id, CancellationToken ct = default)
    {
        var pending = PendingPath(paths, id);
        if (!File.Exists(pending)) return;
        using var lease = usage.Acquire(id, allowNameRecovery: true);
        usage.CheckWorldLocks(id);
        if (await AtomicFile.ReadJsonAsync<Journal>(pending, ct) is not { } journal) return;
        Validate(id, journal);
        await RollbackAsync(journal);
    }

    private async Task<ProfileFileRename> PlanAsync(Guid id, string? relative, CancellationToken ct)
    {
        usage.CheckWorldLocks(id);
        if (!(await store.GetProfilesAsync(ct)).Any(p => p.Id == id)) throw new IOException("ContentMissing");
        var game = paths.Game(id);
        Journal? last = null;
        if (relative is null)
        {
            last = await AtomicFile.ReadJsonAsync<Journal>(LastPath(id), ct) ?? throw new IOException("ProfileNameNoUndo");
            Validate(id, last);
            relative = last.To;
        }
        var category = Category(relative);
        var source = SafePath.Join(game, relative);
        if (!File.Exists(source)) throw new IOException("ContentMissing");
        var size = new FileInfo(source).Length;
        if (size is <= 0 or > MrpackReader.MaxArchiveBytes) throw new IOException("ContentCopyLimit");
        var original = Path.GetFileNameWithoutExtension(source);
        if (last is null && !ReadableName.LooksTechnical(original)) throw new IOException("ProfileFolderNameAlready");
        var hash = await MrpackReader.HashFile(source, ct);
        await RejectBoundPathsAsync(id, relative, category, ct);
        string target;
        if (last is not null)
        {
            if (last.Hash != hash || last.Size != size) throw new IOException("ContentChanged");
            target = SafePath.Join(game, last.From);
            if (File.Exists(target) || Directory.Exists(target)) throw new IOException("ContentCollision");
        }
        else
        {
            await using var input = File.OpenRead(source);
            var sha512 = Convert.ToHexStringLower(await SHA512.HashDataAsync(input, ct));
            var stripped = ReadableName.StripDigest(original, hash, sha512) ?? throw new IOException("ProfileNameUnverified");
            string? title;
            if (category == "mods")
            {
                var mod = await mods.InspectAsync(source, ct);
                title = mod.Id is null ? null : mod.Id + (mod.Version is null ? "" : "-" + mod.Version);
            }
            else title = await content.ValidatePackAsync(source, category == "resourcepacks" ? ProfileContentKind.ResourcePacks : ProfileContentKind.Shaders, ct);
            if (string.IsNullOrWhiteSpace(stripped) && string.IsNullOrWhiteSpace(title)) throw new IOException("ProfileNameUnverified");
            target = ReadableName.Free(Path.GetDirectoryName(source)!, ReadableName.Stem(stripped, ReadableName.Stem(title ?? "Pack", "Pack")), Path.GetExtension(source));
        }
        return new(id, game, relative, Path.GetRelativePath(game, target).Replace('\\', '/'), hash, size, last is not null);
    }

    private async Task RejectBoundPathsAsync(Guid id, string relative, string category, CancellationToken ct)
    {
        var root = paths.Profile(id);
        var transactions = SafePath.Join(root, "transactions");
        var updates = SafePath.Join(root, "mod-updates");
        var pins = category == "mods" ? await AtomicFile.ReadJsonAsync<ModPinSet>(SafePath.Join(root, "mod-pins.json"), ct) : null;
        var install = await AtomicFile.ReadJsonAsync<InstallationReceipt>(paths.Receipt(id), ct);
        var imported = await AtomicFile.ReadJsonAsync<MrpackImportReceipt>(SafePath.Join(root, MrpackImporter.ReceiptName), ct);
        if (Directory.Exists(transactions) && Directory.EnumerateFileSystemEntries(transactions).Any() ||
            category == "mods" && Directory.Exists(updates) && Directory.EnumerateFileSystemEntries(updates).Any() ||
            pins?.Pins.Any(pin => Same(pin.File.Path, relative)) == true ||
            install?.Files.Any(file => Same(file.RelativePath, relative)) == true ||
            imported?.Files.Any(file => Same(file.RelativePath, relative)) == true ||
            (await store.GetArtifactsAsync(id, ct)).Any(artifact => Same(artifact.RelativePath, paths.ProfileRelative(id) + "/game/" + relative)))
            throw new IOException("ProfileNameReferenced");
        string[] configs = category switch
        {
            "resourcepacks" => ["options.txt"],
            "shaderpacks" => ["optionsshaders.txt", "config/iris.properties", "config/oculus.properties", "config/canvas.json5"],
            _ => []
        };
        var name = Path.GetFileName(relative);
        foreach (var item in configs)
        {
            var config = SafePath.Join(paths.Game(id), item);
            if (!File.Exists(config)) continue;
            var text = Encoding.UTF8.GetString(await AtomicFile.ReadBytesAsync(config, 1024 * 1024, ct));
            if (text.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                text.Contains(JsonSerializer.Serialize(name).Trim('"'), StringComparison.OrdinalIgnoreCase))
                throw new IOException("ProfileNameReferenced");
        }
    }

    private static bool Same(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);
    private static string Category(string relative)
    {
        var segments = relative.Split('/');
        if (segments.Length != 2 || segments[1].StartsWith(".pteno-", StringComparison.OrdinalIgnoreCase) ||
            !(segments[0] == "mods" && segments[1].EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
              segments[0] is "resourcepacks" or "shaderpacks" && segments[1].EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("ContentMissing");
        return segments[0];
    }

    private void Validate(Guid id, Journal journal)
    {
        if (journal.Profile != id || Category(journal.From) != Category(journal.To) || Same(journal.From, journal.To) ||
            journal.Size is <= 0 or > MrpackReader.MaxArchiveBytes || journal.Hash.Length != 64 || !journal.Hash.All(Uri.IsHexDigit))
            throw new IOException("ProfileNameRecovery");
        _ = SafePath.Join(paths.Game(id), journal.From);
        _ = SafePath.Join(paths.Game(id), journal.To);
        if (journal.Before is not { } before) return;
        inventory.Validate(id, before);
        inventory.Validate(id, After(journal));
        if (!before.Files.Any(file => Same(file.RelativePath, journal.From) &&
            Same(file.Sha256, journal.Hash) && file.Size == journal.Size)) throw new IOException("ContentChanged");
    }

    private static InstalledModInventory After(Journal journal) => journal.Before! with
    {
        Files = journal.Before!.Files.Select(file => Same(file.RelativePath, journal.From)
            ? file with { RelativePath = journal.To } : file).ToArray()
    };
    private static Task<bool> MatchesAsync(string file, Journal journal) =>
        VerifiedDownloader.VerifyAsync(file, journal.Hash, "SHA256", journal.Size);

    private async Task RollbackAsync(Journal journal)
    {
        var game = paths.Game(journal.Profile);
        var source = SafePath.Join(game, journal.From);
        var target = SafePath.Join(game, journal.To);
        var from = File.Exists(source);
        var to = File.Exists(target);
        if (from == to || !await MatchesAsync(from ? source : target, journal)) throw new IOException("ProfileNameRecovery");
        if (journal.Before is { } before)
        {
            static string Snapshot(InstalledModInventory? value) => JsonSerializer.Serialize(value, AtomicFile.JsonOptions);
            var current = Snapshot(await inventory.ReadAsync(journal.Profile));
            if (current != Snapshot(before) && current != Snapshot(After(journal))) throw new IOException("ProfileNameRecovery");
            if (current != Snapshot(before)) await AtomicFile.WriteJsonAsync(inventory.PathFor(journal.Profile), before);
        }
        if (to) File.Move(target, source, false);
        File.Delete(PendingPath(paths, journal.Profile));
    }
}
