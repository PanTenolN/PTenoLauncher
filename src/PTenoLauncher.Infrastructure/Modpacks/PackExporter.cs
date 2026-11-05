using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Security;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Modpacks;

public enum PackExportAction { Include, Link, Skip }
public sealed record PackExportItem(string Source, string Path, PackExportAction Action, string Reason, long Size = 0,
    string Sha512 = "", string Sha1 = "", string? ProjectId = null, string? VersionId = null, string? Version = null,
    string? License = null, Uri? Url = null, string? SourceSha512 = null);
public sealed record PackExportPlan(Guid ProfileId, string ProfileName, string Name, string Minecraft, ModLoader Loader,
    string? LoaderVersion, bool ShareGraphics, IReadOnlyList<PackExportItem> Items, string Fingerprint);

public sealed class PackExporter(LauncherPaths paths, ILauncherStore store, ModrinthClient catalog)
{
    public const string GraphicsPath = "config/sodium-options.json";
    private const int MaxGraphicsBytes = 64 * 1024;
    private const int MaxMods = 128;
    private static readonly string[] GraphicsOptions = ["animate_only_visible_textures", "use_entity_culling", "use_fog_occlusion"];

    public async Task<PackExportPlan> PreviewAsync(Guid id, string name, bool shareGraphics = false, CancellationToken ct = default)
    {
        using var lease = new ProfileUsage(paths).Acquire(id);
        return await PreviewUnderLeaseAsync(id, name, shareGraphics, ct);
    }

    private async Task<PackExportPlan> PreviewUnderLeaseAsync(Guid id, string name, bool shareGraphics, CancellationToken ct)
    {
        var profile = (await store.GetProfilesAsync(ct)).SingleOrDefault(p => p.Id == id) ?? throw new MrpackException("ExportChanged");
        name = name.Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl)) throw new MrpackException("ExportName");
        var loaderVersion = await MrpackRequirements.RequestedAsync(paths, profile, ct: ct);
        var receipt = await AtomicFile.ReadJsonAsync<InstallationReceipt>(paths.Receipt(id), ct);
        if (receipt is not null && receipt.Version == profile.MinecraftVersion && receipt.Loader == profile.Loader)
        {
            if (loaderVersion is not null && receipt.LoaderVersion != loaderVersion) throw new MrpackException("ExportTarget");
            loaderVersion ??= receipt.LoaderVersion;
        }
        if (profile.Loader != ModLoader.Vanilla && (loaderVersion is not { Length: > 0 and <= 100 } ||
            !loaderVersion.Any(char.IsAsciiDigit) || loaderVersion.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-' or '+'))))
            throw new MrpackException("ExportTarget");

        var game = paths.Game(id);
        var modRoot = SafePath.Join(game, "mods");
        var mods = Directory.Exists(modRoot) ? Directory.EnumerateFiles(modRoot).Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)).Take(MaxMods + 1).Order(StringComparer.Ordinal).ToArray() : [];
        if (mods.Length > MaxMods) throw new MrpackException("PackBudget");
        var items = new List<PackExportItem>();
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        long scannedBytes = 0;
        foreach (var mod in mods)
        {
            ct.ThrowIfCancellationRequested();
            var relative = "mods/" + Path.GetFileName(mod);
            if (!MrpackReader.RootJar(relative)) { items.Add(new(relative, relative, PackExportAction.Skip, "ExportDisabled")); continue; }
            if (!ZipPackReader.Eligible(relative) || (File.GetAttributes(mod) & FileAttributes.ReparsePoint) != 0)
            { items.Add(new(relative, relative, PackExportAction.Skip, "ExportPrivate")); continue; }
            SafePath.RejectLinks(mod);
            await using var input = File.OpenRead(mod);
            if (input.Length is <= 0 or > MrpackReader.MaxArchiveBytes || (scannedBytes += input.Length) > MrpackReader.MaxExpandedBytes)
                throw new MrpackException("PackBudget");
            var (sha512, sha1) = await HashAsync(input, input.Length, null, ct);
            var item = new PackExportItem(relative, relative, PackExportAction.Skip, "ExportUnknownSource", input.Length, sha512, sha1);
            if (!hashes.Add(sha512)) item = item with { Reason = "ExportDuplicate" };
            else if (profile.Loader != ModLoader.Vanilla) item = await IdentifyAsync(profile, item, input, ct);
            items.Add(item);
        }

        try
        {
            var graphics = SafePath.Join(game, GraphicsPath);
            if (File.Exists(graphics))
            {
                var item = new PackExportItem(GraphicsPath, GraphicsPath, PackExportAction.Skip, shareGraphics ? "ExportPrivate" : "ExportGraphicsOff");
                if (shareGraphics && await ReadGraphicsAsync(graphics, ct) is { } config)
                    item = item with { Action = PackExportAction.Include, Reason = "ExportGraphicsChecked", Size = config.Bytes.Length,
                        Sha512 = Hex(SHA512.HashData(config.Bytes)), SourceSha512 = config.SourceHash };
                items.Add(item);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { items.Add(new(GraphicsPath, GraphicsPath, PackExportAction.Skip, "ExportPrivate")); }

        var disclosed = items.Where(PackExportIndex.Disclosed).Select(i => i.Path).ToArray();
        MrpackReader.PathsRule(disclosed.Select(path => (path, false)));
        if (disclosed.Distinct(StringComparer.OrdinalIgnoreCase).Count() != disclosed.Length) throw new MrpackException("PackPath");
        var plan = new PackExportPlan(id, profile.Name, name, profile.MinecraftVersion, profile.Loader, loaderVersion, shareGraphics, items.AsReadOnly(), "");
        return plan with { Fingerprint = MrpackReader.Hash(JsonSerializer.SerializeToUtf8Bytes(plan, AtomicFile.JsonOptions)) };
    }

    private async Task<PackExportItem> IdentifyAsync(GameProfile profile, PackExportItem item, FileStream input, CancellationToken ct)
    {
        try
        {
            var version = await catalog.FindBySha512Async(item.Sha512, ct);
            if (version is null || version.File.Sha512 != item.Sha512 || version.File.Size != item.Size) return item;
            var path = "mods/" + version.File.Name;
            if (!ZipPackReader.Eligible(path) || !OfficialFile(version.File.Url, version.ProjectId, version.Id, version.File.Name) ||
                version.Number is not { Length: > 0 and <= 100 } || version.Number.Any(char.IsControl) ||
                !version.GameVersions.Contains(profile.MinecraftVersion, StringComparer.Ordinal) ||
                !version.Loaders.Contains(ModrinthClient.LoaderName(profile.Loader), StringComparer.Ordinal))
                return item with { Reason = "ExportUnusableSource" };
            var project = await catalog.GetProjectAsync(version.ProjectId, ct);
            if (project.Id != version.ProjectId || !project.SupportsClient) return item with { Reason = "ExportUnusableSource" };
            if (!KnownLicense(project.LicenseId)) return item with { Reason = "ExportUnknownLicense" };
            var embed = project.LicenseId == "CC0-1.0" && await HasReleaseLicenseAsync(input, ct);
            return item with { Path = path, Action = embed ? PackExportAction.Include : PackExportAction.Link,
                Reason = embed ? "ExportCc0" : "ExportCatalogLink", Url = version.File.Url, License = project.LicenseId,
                ProjectId = project.Id, VersionId = version.Id, Version = version.Number };
        }
        catch (Exception e) when (!ct.IsCancellationRequested && e is IOException or InvalidDataException or HttpRequestException or
            JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or TaskCanceledException)
        {
            return item;
        }
    }

    internal static bool OfficialFile(Uri url, string project, string version, string name) =>
        url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort && url.Host == "cdn.modrinth.com" &&
        url.UserInfo.Length == 0 && url.Query.Length == 0 && url.Fragment.Length == 0 &&
        Uri.UnescapeDataString(url.AbsolutePath) == $"/data/{project}/versions/{version}/{name}";

    private static bool KnownLicense(string? license) => license is { Length: > 0 and <= 100 } && !string.IsNullOrWhiteSpace(license) &&
        !license.Contains("unknown", StringComparison.OrdinalIgnoreCase) && !license.Equals("NOASSERTION", StringComparison.OrdinalIgnoreCase) &&
        !license.Equals("NONE", StringComparison.OrdinalIgnoreCase) && license.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '+' or ' ' or '(' or ')');

    private async Task<bool> HasReleaseLicenseAsync(FileStream input, CancellationToken ct)
    {
        MrpackReader.CheckContainer(input.Name);
        input.Position = 0;
        using var jar = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        SafeArchive.Validate(jar, paths.Cache, maxEntries: MrpackReader.MaxEntries);
        var license = jar.Entries.Where(e => Path.GetFileNameWithoutExtension(e.Name).Equals("LICENSE", StringComparison.OrdinalIgnoreCase)
            || e.Name.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase) || e.Name.StartsWith("COPYING", StringComparison.OrdinalIgnoreCase)
            || e.Name.StartsWith("NOTICE", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (license.Length != 1 || license[0].FullName != license[0].Name || license[0].Length > 32 * 1024 ||
            jar.Entries.Any(e => e.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))) return false;
        var text = Encoding.UTF8.GetString(await MrpackReader.EntryBytes(license[0], 32 * 1024, ct)).Trim();
        return text == "SPDX-License-Identifier: CC0-1.0" ||
            text.Contains("CC0 1.0 Universal", StringComparison.Ordinal) &&
            text.Contains("creativecommons.org/publicdomain/zero/1.0", StringComparison.Ordinal) &&
            text.Contains("Public License Fallback", StringComparison.Ordinal);
    }

    private static async Task<(byte[] Bytes, string SourceHash)?> ReadGraphicsAsync(string path, CancellationToken ct)
    {
        SafePath.RejectLinks(path);
        await using var stream = File.OpenRead(path);
        if (stream.Length > MaxGraphicsBytes) return null;
        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, ct);
        try
        {
            using var doc = MrpackReader.Json(bytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("performance", out var source) ||
                source.ValueKind != JsonValueKind.Object) return null;
            var values = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var option in GraphicsOptions)
                if (source.TryGetProperty(option, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    values.Add(option, value.GetBoolean());
            return values.Count == 0 ? null : (JsonSerializer.SerializeToUtf8Bytes(new { performance = values }, AtomicFile.JsonOptions), Hex(SHA512.HashData(bytes)));
        }
        catch (Exception e) when (e is JsonException or DecoderFallbackException or MrpackException) { return null; }
    }

    public async Task ExportAsync(PackExportPlan preview, string destination, bool confirmed, CancellationToken ct = default,
        IProgress<DownloadProgress>? progress = null)
    {
        if (!confirmed) throw new MrpackException("ExportConsent");
        using var lease = new ProfileUsage(paths).Acquire(preview.ProfileId);
        var plan = await PreviewUnderLeaseAsync(preview.ProfileId, preview.Name, preview.ShareGraphics, ct);
        if (plan.Fingerprint != preview.Fingerprint) throw new MrpackException("ExportChanged");
        destination = Path.GetFullPath(destination);
        var parent = Path.GetDirectoryName(destination)!;
        var relative = Path.GetRelativePath(paths.Root, destination);
        if (!(Path.IsPathRooted(relative) || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) ||
            !(destination.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase) || destination.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ||
            SafePath.Join(parent, Path.GetFileName(destination)) != destination)
            throw new MrpackException("ExportDestination");
        if (File.Exists(destination) || Directory.Exists(destination)) throw new MrpackException("ExportExists");
        var pending = SafePath.Join(parent, ".pteno-export-" + Guid.NewGuid().ToString("N") + ".part");
        var owned = false;
        try
        {
            var index = PackExportIndex.Build(plan);
            if (index.Length > MrpackReader.MaxIndexBytes ||
                plan.Items.Where(i => i.Action == PackExportAction.Include).Sum(i => i.Size) + index.Length > MrpackReader.MaxArchiveBytes)
                throw new MrpackException("PackBudget");
            await using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                owned = true;
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    await using (var output = archive.CreateEntry("modrinth.index.json").Open()) await output.WriteAsync(index, ct);
                    progress?.Report(new("modrinth.index.json", index.Length, index.Length));
                    ct.ThrowIfCancellationRequested();
                    foreach (var item in plan.Items.Where(i => i.Action == PackExportAction.Include))
                    {
                        var source = SafePath.Join(paths.Game(plan.ProfileId), item.Source);
                        await using var output = archive.CreateEntry("overrides/" + item.Path, CompressionLevel.NoCompression).Open();
                        try
                        {
                            if (item.Path == GraphicsPath)
                            {
                                var config = await ReadGraphicsAsync(source, ct);
                                if (config is not { } value || value.SourceHash != item.SourceSha512) throw new MrpackException("ExportChanged");
                                await output.WriteAsync(value.Bytes, ct);
                            }
                            else
                            {
                                await using var input = File.OpenRead(source);
                                if (input.Length != item.Size || (await HashAsync(input, item.Size, output, ct)).Sha512 != item.Sha512)
                                    throw new MrpackException("ExportChanged");
                            }
                        }
                        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
                        { throw new MrpackException("ExportChanged", inner: e); }
                        progress?.Report(new(item.Path, item.Size, item.Size));
                        ct.ThrowIfCancellationRequested();
                    }
                }
                if (stream.Length > MrpackReader.MaxArchiveBytes) throw new MrpackException("PackBudget");
                await stream.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }
            var latest = await PreviewUnderLeaseAsync(preview.ProfileId, preview.Name, preview.ShareGraphics, ct);
            if (latest.Fingerprint != preview.Fingerprint) throw new MrpackException("ExportChanged");
            ct.ThrowIfCancellationRequested();
            SafePath.RejectLinks(pending);
            SafePath.RejectLinks(destination);
            File.Move(pending, destination, overwrite: false);
            owned = false;
        }
        finally
        {
            if (owned) { SafePath.RejectLinks(pending); File.Delete(pending); }
        }
    }

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);
    private static async Task<(string Sha512, string Sha1)> HashAsync(Stream source, long size, Stream? copy, CancellationToken ct)
    {
        using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            if ((total += read) > size) throw new MrpackException("ExportChanged");
            sha512.AppendData(buffer, 0, read);
            sha1.AppendData(buffer, 0, read);
            if (copy is not null) await copy.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (total != size) throw new MrpackException("ExportChanged");
        return (Hex(sha512.GetHashAndReset()), Hex(sha1.GetHashAndReset()));
    }
}
