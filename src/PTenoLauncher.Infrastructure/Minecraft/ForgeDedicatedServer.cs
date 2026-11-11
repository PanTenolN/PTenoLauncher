using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Diagnostics;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

internal sealed class ForgeDedicatedServer(VerifiedDownloader downloads)
{
    internal const string Game = "1.21.1";
    internal const string Build = "52.1.0";
    internal const int JavaMajor = 21;
    internal const string MavenVersion = Game + "-" + Build;
    private const string ArtifactFolder = "libraries/net/minecraftforge/forge/" + MavenVersion + "/";
    private const string BundleFolder = "libraries/net/minecraft/server/" + Game + "/";
    private const string InstallerFile = ".pteno-forge-installer.jar";
    private const string InstallerHash = "F1B620F2879AD6A5BBE15DABA4D8F81EAB9F5E08004967604960B98996BDEBC4";
    private const int InstallerSize = 6377616;
    private const string ServerHash = "59353fb40c36d304f2035d51e7d6e6baa98dc05c";
    private const int ServerSize = 51627615;
    private const string MojangMappingsHash = "03f8985492bda0afc0898465341eb0acef35f570";
    private const string PreparedMappingsHash = "897129e2f8fd491d95b8c40216d7565abb08a4c3";
    private const string MappingFile = BundleFolder + "server-" + Game + "-mappings.tsrg";
    private const string BundleFile = BundleFolder + "server-" + Game + "-bundled.jar";
    private const string Shim = "forge-" + MavenVersion + "-shim.jar";
    private const string ShimHash = "f3469ffa6aa4b1af0554a6aeeb65ea743e229fa4";
    private const string WindowsArguments = ArtifactFolder + "win_args.txt";
    private const string Ipv6Option = "-Djava.net.preferIPv6Addresses=system";

    internal static bool Supports(string game, string? build) => game == Game && build == Build;
    internal static string? ProfileBuild(string? build) => build == MavenVersion ? Build : build;

    internal async Task PrepareAsync(string stage, JavaRuntime java, string logs, CancellationToken ct)
    {
        var installer = SafePath.Join(stage, InstallerFile);
        var uri = new Uri(ForgeMaven.Repository(ModLoader.Forge) + ForgeMaven.Installer(ModLoader.Forge, MavenVersion).Path);
        await downloads.DownloadAsync(uri, installer, InstallerHash, InstallerSize, "SHA256", ct: ct, source: DownloadSource.Forge);
        var plan = await ReadPlanAsync(stage, ct);
        using (var archive = ZipFile.OpenRead(installer))
        {
            foreach (var library in plan.Libraries.Where(library => !library.Generated))
            {
                var destination = SafePath.Join(stage, "libraries/" + library.MavenPath);
                if (!library.Embedded)
                {
                    await downloads.DownloadAsync(library.DownloadUri!, destination, library.Sha1!, library.Size,
                        ct: ct, source: DownloadSource.Forge);
                    continue;
                }
                var entry = archive.GetEntry("maven/" + library.MavenPath) ?? throw IntegrityError();
                var bytes = await ForgeInstallerMetadata.ReadEntryAsync(entry, 256L * 1024 * 1024, ct);
                if (bytes.LongLength != library.Size ||
                    !Convert.ToHexString(SHA1.HashData(bytes)).Equals(library.Sha1, StringComparison.OrdinalIgnoreCase))
                    throw IntegrityError();
                await AtomicFile.WriteAsync(destination, bytes, ct);
            }
        }
        await downloads.DownloadAsync(new($"https://piston-data.mojang.com/v1/objects/{ServerHash}/server.jar"),
            SafePath.Join(stage, BundleFile), ServerHash, ServerSize, ct: ct);
        await downloads.DownloadAsync(new($"https://piston-data.mojang.com/v1/objects/{MojangMappingsHash}/server.txt"),
            SafePath.Join(stage, "server-mappings.txt"), MojangMappingsHash, 7456063, ct: ct);
        // The official DOWNLOAD_MOJMAPS processor reuses the converted output when its declared hash matches.
        var script = "var M=Java.type('net.minecraftforge.srgutils.IMappingFile');\n" +
            "var F=Java.type('java.io.File');\n" +
            "var Format=Java.type('net.minecraftforge.srgutils.IMappingFile$Format');\n" +
            $"M.load(new F('server-mappings.txt')).write(new F('{MappingFile}').toPath(),Format.TSRG,false);\n";
        await AtomicFile.WriteAsync(SafePath.Join(stage, "sanitize-mappings.js"), Encoding.ASCII.GetBytes(script), ct);
        string[] converterLibraries =
        [
            "net.minecraftforge:srgutils:0.5.10", "org.openjdk.nashorn:nashorn-core:15.4",
            "org.ow2.asm:asm:9.7.1", "org.ow2.asm:asm-commons:9.7.1", "org.ow2.asm:asm-tree:9.7.1",
            "org.ow2.asm:asm-util:9.7.1", "org.ow2.asm:asm-analysis:9.7.1"
        ];
        var classpath = string.Join(Path.PathSeparator, converterLibraries.Select(coordinate =>
        {
            var library = plan.Libraries.SingleOrDefault(library => library.Coordinate == coordinate && !library.Generated) ?? throw IntegrityError();
            return "libraries/" + library.MavenPath;
        }));
        await RunAsync(java, stage, logs, "forge-mappings", ["-cp", classpath, "org.openjdk.nashorn.tools.Shell", "sanitize-mappings.js"], ct);
        await VerifySha1Async(stage, MappingFile, PreparedMappingsHash, ct);
        await RunAsync(java, stage, logs, "forge-install", ["-Djava.awt.headless=true", "-jar", InstallerFile, "--installServer", ".", "--offline"], ct);
        await VerifyAsync(stage, ct);
        File.Delete(SafePath.Join(stage, "sanitize-mappings.js"));
        File.Delete(SafePath.Join(stage, "server-mappings.txt"));
    }

    internal static async Task VerifyAsync(string folder, CancellationToken ct)
    {
        var plan = await ReadPlanAsync(folder, ct);
        var trusted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in plan.Libraries.Where(library => !library.Generated))
        {
            var relative = "libraries/" + library.MavenPath;
            if (!await VerifiedDownloader.VerifyAsync(SafePath.Join(folder, relative), library.Sha1!, "SHA1", library.Size, ct))
                throw IntegrityError();
            trusted.Add(relative);
        }

        var bundle = SafePath.Join(folder, BundleFile);
        if (!await VerifiedDownloader.VerifyAsync(bundle, ServerHash, "SHA1", ServerSize, ct)) throw IntegrityError();
        trusted.Add(BundleFile);
        using (var zip = ZipFile.OpenRead(bundle))
        {
            var manifest = await ForgeInstallerMetadata.ReadEntryAsync(zip.GetEntry("META-INF/libraries.list") ?? throw IntegrityError(), 64 * 1024, ct);
            foreach (var line in Encoding.UTF8.GetString(manifest).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var entry = line.Split('\t');
                if (entry.Length != 3 || entry[0].Length != 64 || !entry[0].All(Uri.IsHexDigit)) throw IntegrityError();
                var relative = "libraries/" + entry[2];
                var path = SafePath.Join(folder, relative);
                if (!File.Exists(path) || !await VerifiedDownloader.VerifyAsync(path, entry[0], "SHA256", new FileInfo(path).Length, ct))
                    throw IntegrityError();
                trusted.Add(relative);
            }
        }
        foreach (var (relative, hash) in new[]
        {
            (BundleFolder + "server-" + Game + "-unpacked.jar", "a8ae1f986e20cc91442b6b2b64d62bcd065618d7"),
            (BundleFolder + "server-" + Game + "-official.jar", "30b8201b59018b69989d857115b9be7dcbc62b7a"),
            (ArtifactFolder + "forge-" + MavenVersion + "-server.jar", "a58e275894025c9f600ef3b60eb0ad6caf4532c8"),
            (ArtifactFolder + Shim, ShimHash),
            (MappingFile, PreparedMappingsHash)
        })
        {
            await VerifySha1Async(folder, relative, hash, ct);
            trusted.Add(relative);
        }
        using var installer = ZipFile.OpenRead(SafePath.Join(folder, InstallerFile));
        var expected = await ForgeInstallerMetadata.ReadEntryAsync(installer.GetEntry("data/win_args.txt") ?? throw IntegrityError(), 1024, ct);
        var argumentPath = SafePath.Join(folder, WindowsArguments);
        if (!File.Exists(argumentPath)) throw IntegrityError();
        var actual = await AtomicFile.ReadBytesAsync(argumentPath, 1024, ct);
        if (!actual.AsSpan().SequenceEqual(expected) || !IsExpectedWindowsArguments(actual)) throw IntegrityError();
        if (!await VerifiedDownloader.VerifyAsync(SafePath.Join(folder, Shim), ShimHash, "SHA1", 10698, ct)) throw IntegrityError();
        foreach (var path in Directory.EnumerateFiles(SafePath.Join(folder, "libraries"), "*.jar",
                     new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
        {
            ct.ThrowIfCancellationRequested();
            if (!trusted.Contains(Path.GetRelativePath(folder, path).Replace('\\', '/'))) throw IntegrityError();
        }
    }

    internal static bool IsExpectedWindowsArguments(byte[] bytes) =>
        Encoding.ASCII.GetString(bytes).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) is [Ipv6Option, "-jar", Shim];

    internal static void AddLaunchArguments(ProcessStartInfo info)
    {
        info.ArgumentList.Add(Ipv6Option);
        info.ArgumentList.Add("-jar");
        info.ArgumentList.Add(Shim);
        info.ArgumentList.Add("nogui");
    }

    private static async Task<ForgeInstallPlan> ReadPlanAsync(string folder, CancellationToken ct)
    {
        var installer = SafePath.Join(folder, InstallerFile);
        if (!await VerifiedDownloader.VerifyAsync(installer, InstallerHash, "SHA256", InstallerSize, ct)) throw IntegrityError();
        return await ForgeInstallerMetadata.InspectAsync(installer, folder, ModLoader.Forge, MavenVersion, Game, ct);
    }

    private static async Task VerifySha1Async(string folder, string relative, string hash, CancellationToken ct)
    {
        var path = SafePath.Join(folder, relative);
        if (!File.Exists(path) || !await VerifiedDownloader.VerifyAsync(path, hash, "SHA1", new FileInfo(path).Length, ct))
            throw IntegrityError();
    }

    private static async Task RunAsync(JavaRuntime java, string stage, string logs, string name, string[] args, CancellationToken ct)
    {
        var info = new ProcessStartInfo(java.Path) { WorkingDirectory = stage };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        info.Environment.Clear();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        info.Environment["SystemRoot"] = windows;
        info.Environment["PATH"] = Path.Combine(windows, "System32");
        info.Environment["TEMP"] = info.Environment["TMP"] = stage;
        var log = SafePath.Join(logs, name + "-" + Guid.NewGuid().ToString("N") + ".log");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            if (await LiveProcessRunner.RunAsync(info, log, ct: timeout.Token) != 0)
                throw new InvalidOperationException("ServerForgePrepareFailed");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new InvalidOperationException("ServerForgePrepareFailed"); }
    }

    private static InvalidDataException IntegrityError() => new("ServerForgeIntegrity");
}
