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
        if (receipt is not null && receipt.Version == profile.MinecraftVersion) return receipt.JavaMajor;
        var catalog = await versions.GetCachedAsync(ct) ?? throw new InvalidOperationException("Спочатку оновіть каталог або встановіть Minecraft.");
        var entry = catalog.Versions.FirstOrDefault(v => v.Id == profile.MinecraftVersion) ?? throw new InvalidOperationException("Версії немає в кеші каталогу.");
        using var manifest = await versions.GetVersionAsync(entry, ct);
        return manifest.RootElement.TryGetProperty("javaVersion", out var j) ? j.GetProperty("majorVersion").GetInt32() : 8;
    }
    public async Task<JavaRuntime> EnsureAsync(GameProfile profile, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            int major = await RequiredMajorAsync(profile, ct);
            foreach (var candidate in new[] { profile.JavaPath }.Concat(java.Discover()).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try { if (!await VerifyManagedPathAsync(paths, candidate!, ct)) continue; var runtime = await java.InspectAsync(candidate!, ct); if (runtime.Major == major && runtime.Is64Bit && IsX64Executable(candidate!)) return runtime; }
                catch (Exception ex) when (ex is not OperationCanceledException) { /* Try another explicitly discovered runtime. */ }
            }
            var runtimeRoot = SafePath.Join(paths.Root, "runtimes"); Directory.CreateDirectory(runtimeRoot);
            foreach (var directory in Directory.EnumerateDirectories(runtimeRoot, $"temurin-{major}-*"))
            {
                var managed = await AtomicFile.ReadJsonAsync<ManagedJavaReceipt>(SafePath.Join(directory, "runtime.json"), ct);
                if (managed is null || managed.Major != major) continue;
                if (await VerifyManagedAsync(directory, managed, ct))
                    return await java.InspectAsync(SafePath.Join(directory, managed.JavaRelativePath), ct);
            }
            var metadataPath = SafePath.Join(paths.Cache, $"runtime-metadata/{major}.json");
            byte[] metadataBytes;
            if (File.Exists(metadataPath)) metadataBytes = await File.ReadAllBytesAsync(metadataPath, ct);
            else { network.RequireOnline(); metadataBytes = await downloader.GetBytesAsync(new($"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse"), 4 * 1024 * 1024, ct, DownloadSource.Temurin); await AtomicFile.WriteAsync(metadataPath, metadataBytes, ct); }
            if (metadataBytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("Runtime metadata too large.");
            using var metadata = JsonDocument.Parse(metadataBytes);
            var asset = metadata.RootElement.EnumerateArray().FirstOrDefault(e =>
                e.GetProperty("binary").GetProperty("package").GetProperty("name").GetString()!.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (asset.ValueKind == JsonValueKind.Undefined) throw new NotSupportedException($"Temurin Java {major} x64 ZIP не знайдено.");
            var package = asset.GetProperty("binary").GetProperty("package");
            var hash = package.GetProperty("checksum").GetString()!;
            var uri = new Uri(package.GetProperty("link").GetString()!);
            if (uri.Host != "github.com" || !uri.AbsolutePath.StartsWith($"/adoptium/temurin{major}-binaries/releases/download/", StringComparison.Ordinal))
                throw new InvalidDataException("Непідтверджене джерело Java.");
            var archive = SafePath.Join(paths.Cache, $"runtime-downloads/{hash}.zip");
            await downloader.DownloadAsync(uri, archive, hash, package.GetProperty("size").GetInt64(), "SHA256", progress, ct, DownloadSource.Temurin);
            var stage = SafePath.Join(runtimeRoot, "staging-" + Guid.NewGuid().ToString("N"));
            try
            {
                await Security.SafeArchive.ExtractAsync(archive, stage, ct);
                var executables = Directory.EnumerateFiles(stage, "java.exe", SearchOption.AllDirectories).ToArray();
                if (executables.Length != 1 || !IsX64Executable(executables[0])) throw new InvalidDataException("Пакет Java не містить одного перевіреного x64 java.exe.");
                var inspected = await java.InspectAsync(executables[0], ct);
                if (inspected.Major != major || !inspected.Is64Bit) throw new InvalidDataException("Версія/архітектура Java не відповідає запиту.");
                var entries = new List<InstalledFile>();
                foreach (var file in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
                {
                    SafePath.RejectLinks(file);
                    await using var stream = File.OpenRead(file);
                    entries.Add(new(Path.GetRelativePath(stage, file).Replace('\\', '/'), Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)), "SHA256", stream.Length));
                }
                var receipt = new ManagedJavaReceipt(major, asset.GetProperty("release_name").GetString()!, hash,
                    Path.GetRelativePath(stage, executables[0]).Replace('\\', '/'), entries);
                await AtomicFile.WriteJsonAsync(SafePath.Join(stage, "runtime.json"), receipt, ct);
                var destination = SafePath.Join(runtimeRoot, $"temurin-{major}-{hash[..12]}");
                if (Directory.Exists(destination)) destination += "-" + Guid.NewGuid().ToString("N")[..8];
                Directory.Move(stage, destination);
                return new(SafePath.Join(destination, receipt.JavaRelativePath), major, true);
            }
            finally { if (Directory.Exists(stage)) { SafePath.RejectLinks(stage); Directory.Delete(stage, true); } }
        }
        finally { gate.Release(); }
    }
    public static async Task<bool> VerifyManagedAsync(string root, ManagedJavaReceipt receipt, CancellationToken ct = default)
    {
        if (receipt.Files is null || receipt.Files.Count == 0) return false;
        foreach (var file in receipt.Files)
            if (file.Algorithm != "SHA256" || !await VerifiedDownloader.VerifyAsync(SafePath.Join(root, file.RelativePath), file.Hash, file.Algorithm, file.Size, ct)) return false;
        return IsX64Executable(SafePath.Join(root, receipt.JavaRelativePath));
    }
    public static async Task<bool> VerifyManagedPathAsync(LauncherPaths paths, string javaPath, CancellationToken ct = default)
    {
        var runtimes = Path.GetFullPath(Path.Combine(paths.Root, "runtimes")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(javaPath).StartsWith(runtimes, StringComparison.OrdinalIgnoreCase)) return true;
        for (var directory = Path.GetDirectoryName(Path.GetFullPath(javaPath)); directory is not null && directory.StartsWith(runtimes, StringComparison.OrdinalIgnoreCase); directory = Path.GetDirectoryName(directory))
        {
            var receipt = await AtomicFile.ReadJsonAsync<ManagedJavaReceipt>(SafePath.Join(directory, "runtime.json"), ct);
            if (receipt is not null) return await VerifyManagedAsync(directory, receipt, ct);
        }
        return false;
    }
    public static bool IsX64Executable(string file)
    {
        using var stream = File.OpenRead(file); using var reader = new BinaryReader(stream);
        if (stream.Length < 64 || reader.ReadUInt16() != 0x5A4D) return false;
        stream.Position = 0x3c; int pe = reader.ReadInt32();
        if (pe < 0 || pe > stream.Length - 6) return false;
        stream.Position = pe; return reader.ReadUInt32() == 0x00004550 && reader.ReadUInt16() == 0x8664;
    }
}