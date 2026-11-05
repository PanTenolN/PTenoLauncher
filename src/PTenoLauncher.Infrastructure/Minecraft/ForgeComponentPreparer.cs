using System.IO.Compression;
using System.Security.Cryptography;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed record ForgePreparedComponents(Guid ProfileId, DateTimeOffset At, ForgeInstallerArtifact Installer, ForgeInstallPlan Plan, IReadOnlyList<InstalledFile> Files);

/// <summary>Download and verify official installer inputs. No processors, Java or game are executed.</summary>
public sealed class ForgeComponentPreparer(LauncherPaths paths, ForgeCatalog catalog, VerifiedDownloader downloads, ILauncherStore store)
{
    public async Task<ForgePreparedComponents> PrepareAsync(GameProfile profile, string selectedVersion, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ProfileRules.Validate(profile); _ = ForgeMaven.Source(profile.Loader);
        using var lease = new ProfileUsage(paths).Acquire(profile.Id); new ProfileUsage(paths).CheckWorldLocks(profile.Id);
        return await PrepareUnderLeaseAsync(profile, selectedVersion, progress, ct);
    }
    internal async Task<ForgePreparedComponents> PrepareUnderLeaseAsync(GameProfile profile, string selectedVersion, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var installer = await catalog.GetInstallerAsync(profile.Loader, selectedVersion, progress, ct);
        var path = SafePath.Join(paths.Cache, installer.RelativePath);
        var plan = await ForgeInstallerMetadata.InspectAsync(path, SafePath.Join(paths.Cache, "forge/validation"), profile.Loader, selectedVersion, profile.MinecraftVersion, ct);
        var files = new List<InstalledFile> { new(installer.RelativePath, installer.Hash, installer.Algorithm, installer.Size) };
        using var archive = ZipFile.OpenRead(path);
        foreach (var library in plan.Libraries.Where(l => !l.Generated))
        {
            ct.ThrowIfCancellationRequested();
            var relative = $"forge/libraries/sha1/{library.Sha1}/{library.MavenPath}";
            var destination = SafePath.Join(paths.Cache, relative);
            if (!await VerifiedDownloader.VerifyAsync(destination, library.Sha1!, "SHA1", library.Size, ct))
            {
                if (library.Embedded)
                {
                    var entry = archive.GetEntry("maven/" + library.MavenPath) ?? throw new InvalidDataException("Embedded library disappeared.");
                    var bytes = await ForgeInstallerMetadata.ReadEntryAsync(entry, 256L * 1024 * 1024, ct);
                    if (bytes.LongLength != library.Size || !Convert.ToHexString(SHA1.HashData(bytes)).Equals(library.Sha1, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Embedded library checksum mismatch.");
                    await AtomicFile.WriteAsync(destination, bytes, ct);
                    progress?.Report(new(System.IO.Path.GetFileName(library.MavenPath), library.Size, library.Size));
                }
                else await downloads.DownloadAsync(library.DownloadUri!, destination, library.Sha1!, library.Size, progress: progress, ct: ct, source: ForgeMaven.Source(profile.Loader));
            }
            files.Add(new(relative, library.Sha1!, "SHA1", library.Size));
        }
        // Profile preparation pointer is committed only after every downloadable/embedded input is verified.
        // Generated outputs and processors remain incomplete; the real installation receipt is never replaced.
        var result = new ForgePreparedComponents(profile.Id, DateTimeOffset.UtcNow, installer, plan, files);
        ct.ThrowIfCancellationRequested();
        await AtomicFile.WriteJsonAsync(SafePath.Join(paths.Profile(profile.Id), "loader-components.json"), result, ct);
        await store.AddHistoryAsync(profile.Id, "loader.components-prepared", profile.Loader + " " + selectedVersion, ct);
        return result;
    }
}
