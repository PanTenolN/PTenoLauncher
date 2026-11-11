using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed record LocalVanillaServer(Guid Id, string Name, string Version, int Port)
{
    public ServerCore Core { get; init; }
    public string? Build { get; init; }
    public int MemoryMb { get; init; } = 2048;
    public bool HasPlugins => ServerCoreCatalog.SupportsPlugins(Core);
    public bool HasMods => Core is ServerCore.Fabric or ServerCore.Forge or ServerCore.Quilt or ServerCore.NeoForge;
    public bool CanInstallMods => Core == ServerCore.Fabric && Version == ServerCoreCatalog.FabricGame && Build == ServerCoreCatalog.FabricLoader ||
        Core == ServerCore.Forge && ForgeDedicatedServer.Supports(Version, Build);
    public string Address => "localhost:" + Port.ToString(CultureInfo.InvariantCulture);
    public override string ToString() => $"{Name} · {Version} · {Core}{(Build is null ? "" : " " + Build)} · {Address}";
}

public sealed partial class VanillaServerManager
{
    public const string EulaUrl = "https://www.minecraft.net/en-us/eula";
    private const string EulaMarker = "# PTenoLauncher Minecraft EULA";
    private const string SharedEulaMarker = EulaMarker + " (all local servers, v1)";
    private readonly LauncherPaths paths;
    private readonly VersionManager versions;
    private readonly VerifiedDownloader downloader;
    private readonly ServerCoreCatalog cores;
    private readonly ILauncherStore? store;
    private readonly Func<int, IProgress<DownloadProgress>?, CancellationToken, Task<JavaRuntime>> resolveJava;
    private readonly Func<LocalVanillaServer, string, JavaRuntime, ProcessStartInfo> startInfo;

    public VanillaServerManager(LauncherPaths paths, VersionManager versions, VerifiedDownloader downloader, JavaRuntimeManager runtimes,
        ILauncherStore? store = null)
        : this(paths, versions, downloader, runtimes.EnsureManagedAsync, CreateStartInfo, store) { }

    internal VanillaServerManager(LauncherPaths paths, VersionManager versions, VerifiedDownloader downloader,
        Func<int, IProgress<DownloadProgress>?, CancellationToken, Task<JavaRuntime>> resolveJava,
        Func<LocalVanillaServer, string, JavaRuntime, ProcessStartInfo> startInfo, ILauncherStore? store = null)
    {
        this.paths = paths;
        this.versions = versions;
        this.downloader = downloader;
        cores = new(downloader);
        this.store = store;
        this.resolveJava = resolveJava;
        this.startInfo = startInfo;
    }

    private string Root => SafePath.Join(paths.Root, "servers");
    private string SharedEulaPath => SafePath.Join(Root, ".pteno-eula.txt");
    private static string Identity(Guid id) => $".pteno-server-{id:N}";

    public string DirectoryFor(Guid id)
    {
        var legacy = SafePath.Join(Root, id.ToString("N"));
        if (!Directory.Exists(Root)) return legacy;
        var matches = new DirectoryInfo(Root).EnumerateDirectories().Where(folder =>
            (folder.Attributes & FileAttributes.ReparsePoint) == 0 &&
            !IsStaging(folder.Name) &&
            (folder.Name.Equals(id.ToString("N"), StringComparison.OrdinalIgnoreCase) || HasIdentity(folder.FullName, id))).Take(2).ToArray();
        if (matches.Length > 1) throw new IOException("ServerInvalid");
        return matches.SingleOrDefault()?.FullName ?? legacy;
    }

    private static bool HasIdentity(string directory, Guid id)
    {
        var marker = SafePath.Join(directory, Identity(id));
        return File.Exists(marker) && new FileInfo(marker).Length == 0;
    }

    private static bool IsStaging(string name) => name.StartsWith(".creating-", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith(".deleting-", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<VersionEntry>> VersionsAsync(bool refresh = false, CancellationToken ct = default)
    {
        var catalog = refresh ? await versions.RefreshAsync(ct) : await versions.GetCachedAsync(ct);
        return catalog?.Versions.Where(entry => entry.Type == "release").OrderByDescending(entry => entry.ReleasedAt).ToArray() ?? [];
    }

    public async Task<IReadOnlyList<VersionEntry>> VersionsAsync(ServerCore core, bool refresh = false,
        CancellationToken ct = default, bool refreshIfEmpty = false)
    {
        var pinned = ServerCoreCatalog.PinnedGame(core);
        if (pinned is null && core != ServerCore.Vanilla && !ServerCoreCatalog.SupportsPlugins(core)) return [];
        IReadOnlyList<VersionEntry> cached = [];
        try
        {
            cached = await VersionsAsync(ct: ct);
            foreach (var entry in cached)
            {
                NetworkPolicy.ValidateDownloadUri(entry.Url);
                if (entry.Sha1 is not { Length: 40 } || !entry.Sha1.All(Uri.IsHexDigit))
                    throw new InvalidDataException("Invalid cached Minecraft manifest hash.");
            }
            if (cached.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count() != cached.Count)
                throw new InvalidDataException("Duplicate cached Minecraft versions.");
        }
        catch (Exception error) when ((refresh || refreshIfEmpty) && error is IOException or InvalidDataException or JsonException)
        {
            cached = [];
        }

        var hasUsableCache = pinned is null ? cached.Count > 0 : cached.Any(entry => entry.Id == pinned);
        var available = cached;
        if (refresh || refreshIfEmpty && !hasUsableCache)
        {
            try { available = await VersionsAsync(refresh: true, ct: ct); }
            catch (Exception error) when (!ct.IsCancellationRequested && hasUsableCache &&
                error is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException or InvalidOperationException)
            { available = cached; }
        }
        if (core == ServerCore.Vanilla || available.Count == 0) return available;
        var supported = await cores.VersionsAsync(core, ct);
        return available.Where(entry => supported.Contains(entry.Id)).ToArray();
    }

    public Task<IReadOnlyList<string>> BuildsAsync(ServerCore core, string version, CancellationToken ct = default) =>
        cores.BuildsAsync(core, version, ct);

    public Task<IReadOnlyList<LocalVanillaServer>> ListAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<LocalVanillaServer>>(async () =>
        {
            try
            {
                if ((File.GetAttributes(Root) & FileAttributes.Directory) == 0) throw new IOException("ServerInvalid");
            }
            catch (FileNotFoundException) { return []; }
            catch (DirectoryNotFoundException) { return []; }
            var servers = new List<LocalVanillaServer>();
            foreach (var directory in new DirectoryInfo(Root).EnumerateDirectories())
            {
                ct.ThrowIfCancellationRequested();
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0 || IsStaging(directory.Name)) continue;
                var markers = directory.EnumerateFiles(".pteno-server-*").Take(2).ToArray();
                if (markers.Length == 0 && !Guid.TryParseExact(directory.Name, "N", out _)) continue;
                var server = await AtomicFile.ReadJsonAsync<LocalVanillaServer>(SafePath.Join(directory.FullName, "server.json"), ct)
                    ?? throw new IOException("ServerInvalid");
                Validate(server, server.Id);
                if (!(markers.Length == 1 && HasIdentity(directory.FullName, server.Id) ||
                    markers.Length == 0 && directory.Name.Equals(server.Id.ToString("N"), StringComparison.OrdinalIgnoreCase)) ||
                    servers.Any(existing => existing.Id == server.Id))
                    throw new IOException("ServerInvalid");
                servers.Add(server);
            }
            return servers.OrderBy(server => server.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }, ct);

    public async Task<bool> IsInUseAsync(LocalVanillaServer server, CancellationToken ct = default)
    {
        var folder = DirectoryFor(server.Id);
        try
        {
            using var lease = Acquire(folder);
            await CheckIdentityAsync(server, folder, ct);
            await CheckProcessAsync(folder, ct);
            return false;
        }
        catch (InvalidOperationException error) when (error.Message == "ServerAlreadyRunning") { return true; }
    }

    public Task<LocalVanillaServer> CreateAsync(string name, string version, int port, CancellationToken ct = default) =>
        CreateAsync(name, version, port, 2048, ServerCore.Vanilla, null, ct);

    public Task<LocalVanillaServer> CreateAsync(string name, string version, int port, int memoryMb, ServerCore core,
        string? build, CancellationToken ct = default, bool privateServer = false) => CreateCoreAsync(name, version, port, memoryMb, core, build, null, privateServer, ct);

    private async Task<LocalVanillaServer> CreateCoreAsync(string name, string version, int port, int memoryMb, ServerCore core,
        string? build, Func<string, CancellationToken, Task>? populate, bool privateServer, CancellationToken ct)
    {
        var server = new LocalVanillaServer(Guid.NewGuid(), name.Trim(), version, port) { Core = core, Build = build, MemoryMb = memoryMb };
        Validate(server, server.Id);
        _ = await FindVersionAsync(version, ct);
        if (core == ServerCore.Forge)
        {
            if (!ForgeDedicatedServer.Supports(version, build)) throw new InvalidOperationException("ServerBuildUnavailable");
        }
        else if (core != ServerCore.Vanilla) _ = await cores.ResolveAsync(core, version, build, ct);
        var stage = SafePath.Join(Root, ".creating-" + server.Id.ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            if (core == ServerCore.Forge)
            {
                var java = await resolveJava(ForgeDedicatedServer.JavaMajor, null, ct);
                if (!java.Is64Bit || java.Major != ForgeDedicatedServer.JavaMajor) throw new InvalidDataException("ServerJavaMismatch");
                await new ForgeDedicatedServer(downloader).PrepareAsync(stage, java, paths.Logs, ct);
            }
            var properties = RequiredProperties(server);
            if (privateServer)
            {
                properties["white-list"] = "true";
                properties["enforce-whitelist"] = "true";
            }
            await AtomicFile.WriteAsync(SafePath.Join(stage, "server.properties"),
                Encoding.ASCII.GetBytes(string.Join('\n', properties.Select(pair => pair.Key + "=" + pair.Value)) + "\n"), ct);
            await AtomicFile.WriteJsonAsync(SafePath.Join(stage, "server.json"), server, ct);
            await AtomicFile.WriteAsync(SafePath.Join(stage, Identity(server.Id)), [], ct);
            if (server.HasPlugins) Directory.CreateDirectory(SafePath.Join(stage, "plugins"));
            if (populate is not null) await populate(stage, ct);
            ct.ThrowIfCancellationRequested();
            SafePath.RejectLinks(stage);
            var stem = ReadableName.Stem(server.Name, "Server", 64);
            if (Guid.TryParseExact(stem, "N", out _)) stem += " server";
            for (var attempt = 0; ; attempt++)
            {
                var final = ReadableName.Free(Root, stem);
                try { Directory.Move(stage, final); break; }
                catch (IOException) when (attempt < 7 && (Directory.Exists(final) || File.Exists(final))) { }
            }
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                RejectServerLinks(stage, CancellationToken.None);
                Directory.Delete(stage, recursive: true);
            }
        }
        return server;
    }

    public async Task AcceptEulaAsync(LocalVanillaServer server, CancellationToken ct = default)
    {
        var folder = DirectoryFor(server.Id);
        using var lease = Acquire(folder);
        await CheckIdentityAsync(server, folder, ct);
        await CheckProcessAsync(folder, ct);
        await WriteEulaAsync(SafePath.Join(folder, "eula.txt"), EulaMarker, DateTimeOffset.UtcNow, ct);
    }

    public async Task<bool> HasSharedEulaAsync(CancellationToken ct = default) =>
        await ReadEulaAsync(SharedEulaPath, SharedEulaMarker, ct) is not null;

    public Task AcceptSharedEulaAsync(CancellationToken ct = default) =>
        WriteEulaAsync(SharedEulaPath, SharedEulaMarker, DateTimeOffset.UtcNow, ct);

    private static Task WriteEulaAsync(string path, string marker, DateTimeOffset accepted, CancellationToken ct, bool overwrite = true) =>
        AtomicFile.WriteAsync(path, Encoding.ASCII.GetBytes($"{marker}\n# {EulaUrl}\n# {accepted:O}\neula=true\n"), ct, overwrite);

    private static async Task<DateTimeOffset?> ReadEulaAsync(string path, string marker, CancellationToken ct)
    {
        byte[] bytes;
        try { bytes = await AtomicFile.ReadBytesAsync(path, 4096, ct); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException or InvalidDataException) { return null; }
        var lines = Encoding.ASCII.GetString(bytes).Split('\n', StringSplitOptions.TrimEntries);
        return lines.Length == 5 && lines[0] == marker && lines[1] == "# " + EulaUrl &&
            lines[2].StartsWith("# ", StringComparison.Ordinal) &&
            DateTimeOffset.TryParseExact(lines[2][2..], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var accepted) &&
            lines[3] == "eula=true" && lines[4].Length == 0 ? accepted : null;
    }

    private async Task EnsureEulaAsync(string folder, CancellationToken ct)
    {
        var path = SafePath.Join(folder, "eula.txt");
        if (await ReadEulaAsync(path, EulaMarker, ct) is not null) return;
        if (await ReadEulaAsync(SharedEulaPath, SharedEulaMarker, ct) is not { } accepted)
            throw new InvalidOperationException("ServerEulaNeeded");
        if (File.Exists(path)) throw new InvalidOperationException("ServerEulaFileInvalid");
        await WriteEulaAsync(path, EulaMarker, accepted, ct, overwrite: false);
    }

    public async Task<VanillaServerSession> StartAsync(LocalVanillaServer server, IProgress<DownloadProgress>? progress = null,
        IProgress<string>? output = null, CancellationToken ct = default)
    {
        var folder = DirectoryFor(server.Id);
        var lease = Acquire(folder);
        try
        {
            await CheckIdentityAsync(server, folder, ct);
            await CheckProcessAsync(folder, ct);
            CheckPendingSettings(folder);
            await EnsureEulaAsync(folder, ct);
            await CheckPropertiesAsync(folder, server, ct);
            RejectServerLinks(folder, ct);
            CheckPort(server.Port);
            if (server.CanInstallMods) await RecoverServerModsAsync(folder, ct);

            int major;
            if (server.Core == ServerCore.Forge)
            {
                if (!ForgeDedicatedServer.Supports(server.Version, server.Build))
                    throw new InvalidOperationException("ServerBuildUnavailable");
                await ForgeDedicatedServer.VerifyAsync(folder, ct);
                major = ForgeDedicatedServer.JavaMajor;
            }
            else
                major = await PrepareDownloadableCoreAsync(server, folder, progress, ct);

            var runtime = await resolveJava(major, progress, ct);
            if (!runtime.Is64Bit || runtime.Major != major) throw new InvalidDataException("ServerJavaMismatch");
            var info = startInfo(server, folder, runtime);
            var log = ReadableName.Free(SafePath.Join(folder, "launcher-logs"), ReadableName.Stem(server.Name, "Server", 64) +
                $" — {DateTimeOffset.UtcNow:yyyy-MM-dd HH-mm-ss}", ".log");
            // Artifact and Java preparation may take time. Recheck protected settings before starting Java.
            await CheckPropertiesAsync(folder, server, ct);
            await CheckProcessAsync(folder, ct);
            CheckPort(server.Port);
            await EnableWhitelistEnforcementAsync(folder, server, ct);
            var activeFile = SafePath.Join(folder, "active-server.json");
            await AtomicFile.WriteJsonAsync(activeFile, new ActiveGameRecord(0, 0, info.FileName), ct);
            return new VanillaServerSession(server, info, log, activeFile, lease, output);
        }
        catch { lease.Dispose(); throw; }
    }

    private async Task<int> PrepareDownloadableCoreAsync(LocalVanillaServer server, string folder,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var entry = await FindVersionAsync(server.Version, ct);
        using var metadata = await versions.GetVersionAsync(entry, ct);
        var version = metadata.RootElement;
        if (!version.TryGetProperty("downloads", out var files) || !files.TryGetProperty("server", out var artifact))
            throw new InvalidDataException("ServerNoOfficialJar");
        var hash = artifact.GetProperty("sha1").GetString();
        var size = artifact.GetProperty("size").GetInt64();
        if (hash is null || hash.Length != 40 || !hash.All(Uri.IsHexDigit) || size is <= 0 or > 256L * 1024 * 1024 ||
            !Uri.TryCreate(artifact.GetProperty("url").GetString(), UriKind.Absolute, out var uri) ||
            uri.Host is not ("piston-data.mojang.com" or "launcher.mojang.com") ||
            uri.AbsolutePath != $"/v1/objects/{hash.ToLowerInvariant()}/server.jar" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("ServerNoOfficialJar");
        NetworkPolicy.ValidateDownloadUri(uri);
        var major = version.TryGetProperty("javaVersion", out var java) ? java.GetProperty("majorVersion").GetInt32() : 8;
        if (major is < 8 or > 100) throw new InvalidDataException("ServerJavaMismatch");
        if (server.Core is ServerCore.Vanilla or ServerCore.Fabric)
            await downloader.DownloadAsync(uri, SafePath.Join(folder, "server.jar"), hash, size, progress: progress, ct: ct);
        if (server.Core != ServerCore.Vanilla)
        {
            var core = await cores.ResolveAsync(server.Core, server.Version, server.Build, ct);
            var coreFile = SafePath.Join(folder, CoreFile(server));
            await downloader.DownloadAsync(core.Uri, coreFile, core.Sha256, core.Size,
                "SHA256", progress, ct, core.Source);
            if (server.HasPlugins)
                await PreparePaperclipAsync(server, folder, coreFile, uri, hash, size, progress, ct);
        }
        return ServerCoreCatalog.JavaMajor(server.Core, server.Version, major);
    }

    private async Task<VersionEntry> FindVersionAsync(string version, CancellationToken ct) =>
        (await VersionsAsync(ct: ct)).SingleOrDefault(entry => entry.Id == version)
            ?? throw new InvalidOperationException("ServerVersionUnavailable");

    private static void Validate(LocalVanillaServer server, Guid id)
    {
        if (id == Guid.Empty || server.Id != id || server.Name is not { Length: > 0 and <= 80 } || server.Name.Any(char.IsControl) ||
            server.Version is not { Length: > 0 and <= 100 } || server.Version.Any(char.IsControl) ||
            server.Port is < 1024 or > 65535 || server.MemoryMb is < 512 or > 65536 || !Enum.IsDefined(server.Core) ||
            (server.Core == ServerCore.Vanilla ? server.Build is not null : string.IsNullOrWhiteSpace(server.Build)))
            throw new InvalidDataException("ServerInvalid");
    }

    private sealed class ServerLease(FileStream gate, FileStream legacy) : IDisposable
    {
        // Windows cannot move a directory containing the open legacy lock.
        internal void ReleaseFolderHandle() => legacy.Dispose();

        public void Dispose()
        {
            legacy.Dispose();
            gate.Dispose();
        }
    }

    private static ServerLease Acquire(string folder)
    {
        if (!Directory.Exists(folder)) throw new InvalidOperationException("ServerInvalid");
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(folder).ToUpperInvariant())));
        var lockPath = SafePath.Join(Path.GetDirectoryName(folder)!, ".lease-" + id);
        FileStream? gate = null;
        try
        {
            gate = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
            var legacy = new FileStream(SafePath.Join(folder, "launcher.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new ServerLease(gate, legacy);
        }
        catch (IOException error)
        {
            gate?.Dispose();
            throw new InvalidOperationException("ServerAlreadyRunning", error);
        }
        catch { gate?.Dispose(); throw; }
    }

    private static async Task CheckIdentityAsync(LocalVanillaServer expected, string folder, CancellationToken ct)
    {
        Validate(expected, expected.Id);
        if (await AtomicFile.ReadJsonAsync<LocalVanillaServer>(SafePath.Join(folder, "server.json"), ct) != expected)
            throw new InvalidOperationException("ServerInvalid");
    }

    private static async Task CheckProcessAsync(string folder, CancellationToken ct)
    {
        var active = await AtomicFile.ReadJsonAsync<ActiveGameRecord>(SafePath.Join(folder, "active-server.json"), ct);
        if (active is not null && (active.Pid <= 0 || active.StartedTicks <= 0 || ActiveGameRecord.IsRunning(active)))
            throw new InvalidOperationException("ServerAlreadyRunning");
        var worldLock = SafePath.Join(folder, "world/session.lock");
        if (!File.Exists(worldLock)) return;
        try { using var probe = new FileStream(worldLock, FileMode.Open, FileAccess.Read, FileShare.None); }
        catch (IOException error) { throw new InvalidOperationException("ServerAlreadyRunning", error); }
    }

    private static Dictionary<string, string> RequiredProperties(LocalVanillaServer server) => new(StringComparer.Ordinal)
    {
        ["online-mode"] = "true", ["server-ip"] = "127.0.0.1",
        ["server-port"] = server.Port.ToString(CultureInfo.InvariantCulture), ["level-name"] = "world",
        ["enable-query"] = "false", ["enable-rcon"] = "false", ["enforce-secure-profile"] = "true",
        ["enable-jmx-monitoring"] = "false", ["management-server-enabled"] = "false"
    };

    private static Task<byte[]> ReadPropertiesBytesAsync(string folder, CancellationToken ct) =>
        AtomicFile.ReadBytesAsync(SafePath.Join(folder, "server.properties"), 256 * 1024, ct);

    private static async Task CheckPropertiesAsync(string folder, LocalVanillaServer server, CancellationToken ct)
    {
        _ = ParseProperties(await ReadPropertiesBytesAsync(folder, ct), server);
        await CheckProxyConfigurationAsync(folder, server, ct);
    }

    private static Dictionary<string, string> ParseProperties(byte[] bytes, LocalVanillaServer server)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in Encoding.Latin1.GetString(bytes).Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimStart(' ', '\t', '\f');
            if (line.Length == 0 || line[0] is '#' or '!') continue;
            var split = line.IndexOf('=');
            var last = line.Length - 1;
            while (last >= 0 && line[last] == '\\') last--;
            if (split < 1 || line.Contains('\r') || (line.Length - 1 - last) % 2 != 0 ||
                !line[..split].All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '.' or '_') ||
                !properties.TryAdd(line[..split], line[(split + 1)..]))
                throw new InvalidDataException("ServerPropertiesChanged");
        }
        if (RequiredProperties(server).Any(pair => properties.GetValueOrDefault(pair.Key) != pair.Value))
            throw new InvalidDataException("ServerPropertiesChanged");
        return properties;
    }

    private static void RejectServerLinks(string folder, CancellationToken ct)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new(folder));
        while (pending.TryPop(out var directory))
        {
            SafePath.RejectLinks(directory.FullName);
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                ct.ThrowIfCancellationRequested();
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("ServerPropertiesChanged");
                if (entry is DirectoryInfo child) pending.Push(child);
            }
        }
    }

    private static void CheckPort(int port)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
        try { socket.Bind(new IPEndPoint(IPAddress.Loopback, port)); }
        catch (SocketException error) when (error.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
        { throw new InvalidOperationException("ServerPortBusy", error); }
    }

    private async Task PreparePaperclipAsync(LocalVanillaServer server, string folder, string coreFile, Uri mojangUri, string mojangHash,
        long size, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(coreFile);
        var context = archive.GetEntry("META-INF/download-context");
        if (context is null || context.Length is <= 0 or > 4096) throw new InvalidDataException("ServerNoOfficialJar");
        using var stream = context.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var fields = (await reader.ReadToEndAsync(ct)).TrimEnd('\r', '\n').Split('\t');
        if (fields.Length != 3 || fields[0].Length != 64 || !fields[0].All(Uri.IsHexDigit) ||
            !Uri.TryCreate(fields[1], UriKind.Absolute, out var source) || source.AbsoluteUri != mojangUri.AbsoluteUri ||
            fields[2] != $"mojang_{server.Version}.jar")
            throw new InvalidDataException("ServerNoOfficialJar");
        var original = SafePath.Join(folder, "cache/" + fields[2]);
        await downloader.DownloadAsync(mojangUri, original, fields[0], size, "SHA256", progress, ct);
        if (!await VerifiedDownloader.VerifyAsync(original, mojangHash, "SHA1", size, ct))
            throw new InvalidDataException("ServerNoOfficialJar");
    }

    private static string CoreFile(LocalVanillaServer server) => server.Core switch
    {
        ServerCore.Vanilla => "server.jar",
        ServerCore.Fabric => "fabric-server-launcher.jar",
        ServerCore.Paper => "paper-server.jar",
        ServerCore.Purpur => "purpur-server.jar",
        _ => throw new InvalidOperationException("ServerBuildUnavailable")
    };

    internal static ProcessStartInfo CreateStartInfo(LocalVanillaServer server, string folder, JavaRuntime runtime)
    {
        var info = new ProcessStartInfo(runtime.Path)
        {
            WorkingDirectory = folder,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var option in new[] { "JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS", "JDK_JAVA_OPTIONS" }) info.Environment.Remove(option);
        info.ArgumentList.Add($"-Xmx{server.MemoryMb.ToString(CultureInfo.InvariantCulture)}M");
        if (server.Core == ServerCore.Forge)
        {
            ForgeDedicatedServer.AddLaunchArguments(info);
            return info;
        }
        if (server.Core == ServerCore.Fabric)
            info.ArgumentList.Add("-Dfabric.installer.server.gameJar=" + SafePath.Join(folder, "server.jar"));
        foreach (var argument in new[] { "-jar", CoreFile(server), "nogui" })
            info.ArgumentList.Add(argument);
        return info;
    }
}
