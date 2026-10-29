using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed record ManagedJavaReceipt(int Major, string Release, string ArchiveSha256, string JavaRelativePath, IReadOnlyList<InstalledFile> Files);
public sealed class JavaRuntimeManager(LauncherPaths paths, JavaManager java, VersionManager versions, VerifiedDownloader downloader, NetworkPolicy network)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<int> RequiredMajorAsync(GameProfile profile, CancellationToken ct = default)
    {
        var receipt = await AtomicFile.ReadJsonAsync<InstallationReceipt>(paths.Receipt(profile.Id), ct);
        if (receipt is not null && receipt.Version == profile.MinecraftVersion)
        {
            ReceiptValidator.Validate(paths, receipt);
            return receipt.JavaMajor;
        }
        var catalog = await versions.GetCachedAsync(ct);
        if ((catalog is null || !catalog.Versions.Any(v => v.Id == profile.MinecraftVersion)) && !network.Offline)
            catalog = await versions.RefreshAsync(ct);
        var entry = catalog?.Versions.FirstOrDefault(v => v.Id == profile.MinecraftVersion)
            ?? throw new InvalidOperationException("Minecraft version metadata is not cached. Refresh the catalog while online.");
        using var manifest = await versions.GetVersionAsync(entry, ct);
        var major = manifest.RootElement.TryGetProperty("javaVersion", out var j) ? j.GetProperty("majorVersion").GetInt32() : 8;
        if (major is < 8 or > 100) throw new InvalidDataException("Unsupported Java major in Minecraft metadata.");
        return major;
    }
    public async Task<JavaRuntime> EnsureAsync(GameProfile profile, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
        => await EnsureCoreAsync(await RequiredMajorAsync(profile, ct), profile.JavaPath, true, progress, ct);

    // Explicit managed-only option: no reliance on system Java or PATH, also used by opt-in runtime integration checks.
    public Task<JavaRuntime> EnsureManagedAsync(int major, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
        => EnsureCoreAsync(major, null, false, progress, ct);

    private async Task<JavaRuntime> EnsureCoreAsync(int major, string? preferred, bool discoverSystem, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        if (major is < 8 or > 100) throw new ArgumentOutOfRangeException(nameof(major));
        await gate.WaitAsync(ct);
        try
        {
            if (discoverSystem)
                foreach (var candidate in new[] { preferred }.Concat(java.Discover()).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
                    try
                    {
                        if (!await VerifyManagedPathAsync(paths, candidate!, ct)) continue;
                        var runtime = await java.InspectAsync(candidate!, ct);
                        if (runtime.Major == major && runtime.Is64Bit) return runtime;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { /* Inspect the next explicitly discovered candidate. */ }
            var runtimeRoot = SafePath.Join(paths.Root, "runtimes"); Directory.CreateDirectory(runtimeRoot);
            foreach (var directory in Directory.EnumerateDirectories(runtimeRoot, $"temurin-{major}-*"))
                try
                {
                    SafePath.RejectLinks(directory);
                    var managed = await AtomicFile.ReadJsonAsync<ManagedJavaReceipt>(SafePath.Join(directory, "runtime.json"), ct);
                    if (managed is null || managed.Major != major || !ManagedJavaValidation.OwnedFolder(directory, managed) ||
                        !await VerifyManagedAsync(directory, managed, ct)) continue;
                    var runtime = await java.InspectAsync(SafePath.Join(directory, managed.JavaRelativePath), ct);
                    if (runtime.Major == major && runtime.Is64Bit) return runtime;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or TimeoutException)
                { /* Keep corrupt/incomplete directories intact; repair into a new verified directory. */ }
            var package = await GetPackageAsync(major, ct);
            var archive = SafePath.Join(paths.Cache, $"runtime-downloads/{package.Sha256}.zip");
            await downloader.DownloadAsync(package.DownloadUri, archive, package.Sha256, package.Size, "SHA256", progress, ct, DownloadSource.Temurin);
            var stage = SafePath.Join(runtimeRoot, "staging-" + Guid.NewGuid().ToString("N"));
            try
            {
                await Security.SafeArchive.ExtractAsync(archive, stage, ct);
                var executables = Directory.EnumerateFiles(stage, "java.exe", SearchOption.AllDirectories).ToArray();
                if (executables.Length != 1 || !IsX64Executable(executables[0])) throw new InvalidDataException("Java archive must contain one Windows x64 java.exe.");
                var inspected = await java.InspectAsync(executables[0], ct);
                if (inspected.Major != major || !inspected.Is64Bit) throw new InvalidDataException("Java version/architecture does not match the request.");
                var entries = new List<InstalledFile>();
                foreach (var file in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested(); SafePath.RejectLinks(file);
                    await using var stream = File.OpenRead(file);
                    entries.Add(new(Path.GetRelativePath(stage, file).Replace('\\', '/'), Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)), "SHA256", stream.Length));
                }
                var receipt = new ManagedJavaReceipt(major, package.Release, package.Sha256, Path.GetRelativePath(stage, executables[0]).Replace('\\', '/'), entries);
                await AtomicFile.WriteJsonAsync(SafePath.Join(stage, "runtime.json"), receipt, ct);
                if (!await VerifyManagedAsync(stage, receipt, ct)) throw new InvalidDataException("Extracted Java inventory is invalid.");
                var destination = SafePath.Join(runtimeRoot, $"temurin-{major}-{package.Sha256[..12]}");
                if (Directory.Exists(destination)) destination += "-" + Guid.NewGuid().ToString("N")[..8];
                Directory.Move(stage, destination);
                return new(SafePath.Join(destination, receipt.JavaRelativePath), major, true);
            }
            finally { if (Directory.Exists(stage)) { SafePath.RejectLinks(stage); Directory.Delete(stage, true); } }
        }
        finally { gate.Release(); }
    }
    private async Task<TemurinPackage> GetPackageAsync(int major, CancellationToken ct)
    {
        var path = SafePath.Join(paths.Cache, $"runtime-metadata/{major}.json");
        if (File.Exists(path))
            try
            {
                if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Java metadata is too large.");
                return TemurinPackage.Parse(await File.ReadAllBytesAsync(path, ct), major);
            }
            catch (Exception ex) when (!network.Offline && ex is IOException or InvalidDataException or NotSupportedException)
            { /* One explicit metadata refresh; never retry indefinitely or discard user files. */ }
        network.RequireOnline();
        var bytes = await downloader.GetBytesAsync(new($"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse"), 4 * 1024 * 1024, ct, DownloadSource.Temurin);
        var package = TemurinPackage.Parse(bytes, major);
        await AtomicFile.WriteAsync(path, bytes, ct);
        return package;
    }
    public static Task<bool> VerifyManagedAsync(string root, ManagedJavaReceipt receipt, CancellationToken ct = default)
        => receipt is null ? Task.FromResult(false) : ManagedJavaValidation.VerifyAsync(root, receipt, ct);

    public static async Task<bool> VerifyManagedPathAsync(LauncherPaths paths, string javaPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var runtimes = Path.GetFullPath(SafePath.Join(paths.Root, "runtimes")) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(javaPath);
            if (!full.StartsWith(runtimes, StringComparison.OrdinalIgnoreCase)) return true;
            var relative = Path.GetRelativePath(runtimes, full);
            var first = relative.Split(Path.DirectorySeparatorChar)[0];
            var directory = SafePath.Join(runtimes, first);
            var receipt = await AtomicFile.ReadJsonAsync<ManagedJavaReceipt>(SafePath.Join(directory, "runtime.json"), ct);
            return receipt is not null && ManagedJavaValidation.OwnedFolder(directory, receipt) &&
                full.Equals(SafePath.Join(directory, receipt.JavaRelativePath), StringComparison.OrdinalIgnoreCase) &&
                await VerifyManagedAsync(directory, receipt, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or NotSupportedException)
        { return false; }
    }
    public static bool IsX64Executable(string file)
    {
        try
        {
            SafePath.RejectLinks(file);
            using var stream = File.OpenRead(file); using var reader = new BinaryReader(stream);
            if (stream.Length < 64 || reader.ReadUInt16() != 0x5A4D) return false;
            stream.Position = 0x3c; int pe = reader.ReadInt32();
            if (pe < 64 || pe > stream.Length - 6) return false;
            stream.Position = pe; return reader.ReadUInt32() == 0x00004550 && reader.ReadUInt16() == 0x8664;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
}
