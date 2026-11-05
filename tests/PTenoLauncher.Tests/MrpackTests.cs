using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Modpacks;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;
namespace PTenoLauncher.Tests;

public sealed class MrpackFixture : IDisposable
{
    public TestWorkspace Workspace { get; } = new();
    public NetworkPolicy Network { get; } = new();
    public MrpackImporter Importer { get; }
    public int Requests;
    public byte[] Payload = Encoding.UTF8.GetBytes("Synthetic config payload");
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Response;
    private readonly HttpClient client;
    public MrpackFixture()
    {
        client = new(new Handler(this)); Importer = new(Workspace.Paths, Workspace.Store, new(client, Network), Network);
    }
    private sealed class Handler(MrpackFixture fixture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            fixture.Requests++; Assert.False(fixture.Network.Offline);
            return fixture.Response?.Invoke(request, ct) ?? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fixture.Payload) });
        }
    }
    public Dictionary<string, object?> File(string path = "config/data.txt", string client = "required", string server = "required", string? url = null, byte[]? bytes = null)
    {
        bytes ??= Payload;
        return new() { ["path"] = path, ["fileSize"] = bytes.Length, ["hashes"] = new Dictionary<string, object?> { ["sha1"] = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant(), ["sha512"] = Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant() }, ["env"] = new { client, server }, ["downloads"] = new[] { url ?? "https://cdn.modrinth.com/data/test/synthetic.bin" } };
    }
    public static Dictionary<string, object?> Index(IEnumerable<object>? files = null, string? loader = null, string version = "0.16.10")
    {
        var deps = new Dictionary<string, string> { ["minecraft"] = "1.21.1" }; if (loader is not null) deps[loader] = version;
        return new() { ["formatVersion"] = 1, ["game"] = "minecraft", ["versionId"] = "synthetic-pack-v1", ["name"] = "Synthetic pack", ["files"] = files?.ToArray() ?? [], ["dependencies"] = deps };
    }
    public string Pack(Dictionary<string, object?>? index = null, params (string Path, byte[] Bytes)[] overrides) => RawPack(JsonSerializer.SerializeToUtf8Bytes(index ?? Index()), overrides);
    public string RawPack(byte[] index, params (string Path, byte[] Bytes)[] overrides)
    {
        var path = SafePath.Join(Workspace.Paths.Root, Guid.NewGuid().ToString("N") + ".mrpack");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var stream = zip.CreateEntry("modrinth.index.json").Open()) stream.Write(index);
        foreach (var (name, bytes) in overrides) { using var stream = zip.CreateEntry(name, CompressionLevel.Fastest).Open(); stream.Write(bytes); }
        return path;
    }
    public async Task Seed(MrpackDownload d)
    { var path = Importer.Reader.CachePath(d); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await System.IO.File.WriteAllBytesAsync(path, Payload); }
    public static byte[] Jar(string id = "demo", string? dependency = null, string environment = "client")
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            using var stream = zip.CreateEntry("fabric.mod.json").Open();
            var depends = new Dictionary<string, string> { ["minecraft"] = "1.21.1" }; if (dependency is not null) depends[dependency] = "*";
            JsonSerializer.Serialize(stream, new { schemaVersion = 1, id, name = id, version = "1.0.0", environment, depends });
        }
        return bytes.ToArray();
    }
    public void Dispose() { client.Dispose(); Workspace.Dispose(); }
}
public sealed class MrpackTests
{
    [Fact] public async Task MinimalPackIsPublishedOnlyAsNewContentProfileNotInstalledGame()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync();
        var preview = await f.Importer.Reader.PreviewAsync(f.Pack()); Assert.True(preview.CanImport); Assert.Equal(0, f.Requests);
        var result = await f.Importer.ImportAsync(preview, true); Assert.Equal(result.Profile, Assert.Single(await f.Workspace.Store.GetProfilesAsync()));
        Assert.False(System.IO.File.Exists(f.Workspace.Paths.Receipt(result.Profile.Id))); Assert.Equal(0, f.Requests);
        var receipt = await MrpackRequirements.ReadAsync(f.Workspace.Paths, result.Profile); Assert.Equal("synthetic-pack-v1", receipt!.Version);
        Assert.Contains(await MrpackRequirements.CheckAsync(f.Workspace.Paths, result.Profile, null, default), i => i.Code == "modpack.target.mismatch");
    }
    [Fact] public async Task ClientEnvironmentOptionalSelectionAndOverrideLayeringFollowManifestRules()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync();
        var files = new[] { f.File("config/required.txt", server: "unsupported"), f.File("config/optional.txt", "optional"), f.File("config/server.txt", "unsupported", "required"), f.File("config/layer.txt") };
        var pack = f.Pack(MrpackFixture.Index(files), ("overrides/config/layer.txt", Encoding.UTF8.GetBytes("common")), ("client-overrides/config/layer.txt", Encoding.UTF8.GetBytes("client")), ("server-overrides/config/layer.txt", Encoding.UTF8.GetBytes("server")), ("server-overrides/server-only.txt", [9]));
        var preview = await f.Importer.Reader.PreviewAsync(pack); Assert.False(preview.Entries.Single(x => x.Path == "config/optional.txt").Selected);
        Assert.DoesNotContain(preview.Files, x => x.Path == "config/server.txt"); Assert.Equal("client-overrides", preview.Files.Single(x => x.Path == "config/layer.txt").Source);
        var chosen = await f.Importer.Reader.PreviewAsync(pack, optional: ["config/optional.txt"]);
        Assert.Equal(f.Payload.Length, chosen.DownloadBytes); // One content-addressed payload, even at two target paths.
        var result = await f.Importer.ImportAsync(chosen, true); var game = f.Workspace.Paths.Game(result.Profile.Id);
        Assert.Equal("client", await System.IO.File.ReadAllTextAsync(SafePath.Join(game, "config/layer.txt"))); Assert.True(System.IO.File.Exists(SafePath.Join(game, "config/optional.txt")));
        Assert.False(System.IO.File.Exists(SafePath.Join(game, "config/server.txt"))); Assert.False(System.IO.File.Exists(SafePath.Join(game, "server-only.txt"))); Assert.Equal(1, f.Requests); // Identical hashes reuse one verified payload.
    }
    [Theory][InlineData("../outside")][InlineData("C:/Windows/x")][InlineData("config/file:ads")][InlineData("config/CON.txt")][InlineData("config/a./b")][InlineData("config\\x")][InlineData("/absolute")][InlineData("secrets/../accounts.json")][InlineData("launcher_accounts.json")]
    public async Task UnsafeTargetsAreRejectedWithoutWrites(string path)
    {
        using var f = new MrpackFixture(); var pack = f.Pack(MrpackFixture.Index([f.File(path)]));
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.Reader.PreviewAsync(pack)); Assert.Equal(0, f.Requests); Assert.Empty(Directory.GetDirectories(Path.Combine(f.Workspace.Paths.Root, "instances")));
    }
    [Theory][InlineData("config/A.txt", "config/a.txt")][InlineData("config", "config/a.txt")][InlineData("config/a", "Config/b")]
    public async Task CaseAndFileDirectoryCollisionsAreRejected(string a, string b)
    {
        using var f = new MrpackFixture(); var pack = f.Pack(MrpackFixture.Index([f.File(a), f.File(b)])); await Assert.ThrowsAsync<MrpackException>(() => f.Importer.Reader.PreviewAsync(pack));
    }
    [Fact] public async Task ArchiveLinksAndDuplicateMetadataAreRejected()
    {
        using var f = new MrpackFixture(); var path = f.Pack();
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update)) { zip.CreateEntry("overrides/link").ExternalAttributes = unchecked((int)0xA1FF0000); }
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.Reader.PreviewAsync(path));
        var duplicate = f.RawPack(Encoding.UTF8.GetBytes("{\"formatVersion\":1,\"FormatVersion\":1}")); await Assert.ThrowsAsync<MrpackException>(() => f.Importer.Reader.PreviewAsync(duplicate));
    }
    [Theory][InlineData("formatVersion", "2")][InlineData("game", "\"other\"")][InlineData("files", "null")][InlineData("dependencies", "null")][InlineData("name", "null")]
    public async Task InvalidIndexFieldsFailClosed(string key, string json)
    {
        using var f = new MrpackFixture(); var index = MrpackFixture.Index(); index[key] = JsonSerializer.Deserialize<JsonElement>(json);
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.Reader.PreviewAsync(f.Pack(index)));
    }
    [Fact] public async Task UnknownDependenciesAndMultipleLoadersAreExplicitBlockers()
    {
        using var f = new MrpackFixture(); var index = MrpackFixture.Index(loader: "fabric-loader"); var dependencies = (Dictionary<string, string>)index["dependencies"]!;
        dependencies["forge"] = "52.0.1"; dependencies["future-loader"] = "1.0.0";
        var preview = await f.Importer.Reader.PreviewAsync(f.Pack(index)); Assert.False(preview.CanImport); Assert.Equal(2, preview.Blockers.Count); Assert.Equal(0, f.Requests);
    }
    [Fact] public async Task UnsupportedOriginIsAFileBlockerAndDoesNotMakeARequest()
    {
        using var f = new MrpackFixture(); var preview = await f.Importer.Reader.PreviewAsync(f.Pack(MrpackFixture.Index([f.File(url: "https://localhost/private")] )));
        Assert.Contains(preview.Blockers, x => x.Code == "PackOrigin" && x.Detail.Contains("config/data.txt")); await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ImportAsync(preview, true)); Assert.Equal(0, f.Requests);
    }
    [Fact] public async Task ConsentAndChangedArchivePreventAnyDownloadOrProfilePublication()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var path = f.Pack(MrpackFixture.Index([f.File()])); var p = await f.Importer.Reader.PreviewAsync(path);
        Assert.Equal("PackConsent", (await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ImportAsync(p, false))).Code);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update)) { using var s = zip.CreateEntry("overrides/new.txt").Open(); s.WriteByte(1); }
        Assert.Equal("PackChanged", (await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ImportAsync(p, true))).Code); Assert.Empty(await f.Workspace.Store.GetProfilesAsync()); Assert.Equal(0, f.Requests);
    }
    [Fact] public async Task OfflineNeedsBothVerifiedHashesAndACompleteCache()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var path = f.Pack(MrpackFixture.Index([f.File()]));
        f.Network.Offline = true; var cold = await f.Importer.Reader.PreviewAsync(path); Assert.Contains(cold.Blockers, x => x.Code == "PackOfflineMissing");
        await f.Seed(cold.Files[0].Download!); var warm = await f.Importer.Reader.PreviewAsync(path); Assert.True(warm.CanImport);
        var result = await f.Importer.ImportAsync(warm, true); Assert.Equal(f.Payload, await System.IO.File.ReadAllBytesAsync(SafePath.Join(f.Workspace.Paths.Game(result.Profile.Id), "config/data.txt"))); Assert.Equal(0, f.Requests);
    }
    [Theory][InlineData("size")][InlineData("sha1")][InlineData("sha512")]
    public async Task DownloadIntegrityFailureNeverPublishesProfile(string failure)
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var file = f.File();
        if (failure == "size") file["fileSize"] = f.Payload.Length + 1;
        else ((Dictionary<string, object?>)file["hashes"]!)[failure] = new string('0', failure == "sha1" ? 40 : 128);
        var preview = await f.Importer.Reader.PreviewAsync(f.Pack(MrpackFixture.Index([file])));
        Assert.Equal("PackIntegrity", (await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ImportAsync(preview, true))).Code); Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
    }
    [Fact] public async Task UnavailableDownloadAndUntrustedRedirectDoNotPublishAnything()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var p = await f.Importer.Reader.PreviewAsync(f.Pack(MrpackFixture.Index([f.File()])));
        f.Response = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        Assert.Equal("PackDownload", (await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ImportAsync(p, true))).Code); Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
        await f.Importer.RecoverAsync(true); f.Response = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new("https://attacker.invalid/payload") } });
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ImportAsync(p, true)); Assert.Empty(await f.Workspace.Store.GetProfilesAsync()); Assert.Equal(2, f.Requests);
    }
    [Theory][InlineData("preparing", 0)][InlineData("ready", 1)][InlineData("moved", 1)][InlineData("before-database", 1)][InlineData("database", 1)]
    public async Task RecoveryCoordinatesFilesAndSqliteAndPreservesOtherProfiles(string point, int imported)
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var other = f.Workspace.Profile(); await f.Workspace.Store.SaveProfileAsync(other);
        var sentinel = SafePath.Join(f.Workspace.Paths.Game(other.Id), "saves/synthetic.txt"); await System.IO.File.WriteAllTextAsync(sentinel, "PRESERVE"); await f.Workspace.Store.SetSettingAsync("synthetic.setting", "PRESERVE");
        var p = await f.Importer.Reader.PreviewAsync(f.Pack(null, ("overrides/config/value.txt", [1, 2, 3])));
        f.Importer.Fault = s => { if (s == point) throw new IOException("Isolated fault"); };
        await Assert.ThrowsAsync<IOException>(() => f.Importer.ImportAsync(p, true)); f.Importer.Fault = null;
        Assert.Single(await f.Importer.PendingAsync()); f.Network.Offline = true; await f.Importer.RecoverAsync(true);
        Assert.Empty(await f.Importer.PendingAsync()); Assert.Equal(1 + imported, (await f.Workspace.Store.GetProfilesAsync()).Count);
        Assert.Equal("PRESERVE", await System.IO.File.ReadAllTextAsync(sentinel)); Assert.Equal("PRESERVE", await f.Workspace.Store.GetSettingAsync("synthetic.setting")); Assert.Equal(0, f.Requests);
        await f.Importer.RecoverAsync(true); Assert.Equal(1 + imported, (await f.Workspace.Store.GetProfilesAsync()).Count);
    }
    [Fact] public async Task CancellationAtReadyLeavesRecoverableVerifiedContent()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); using var cancel = new CancellationTokenSource();
        var p = await f.Importer.Reader.PreviewAsync(f.Pack(null, ("overrides/config/a", [1]))); f.Importer.Fault = s => { if (s == "ready") cancel.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Importer.ImportAsync(p, true, ct: cancel.Token)); Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
        f.Importer.Fault = null; await f.Importer.RecoverAsync(true); Assert.Single(await f.Workspace.Store.GetProfilesAsync());
    }
    [Fact] public async Task RecoveryPreservesForeignOrChangedFilesInsteadOfOverwriting()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var p = await f.Importer.Reader.PreviewAsync(f.Pack(null, ("overrides/config/a", [1])));
        f.Importer.Fault = s => { if (s == "ready") throw new IOException(); }; await Assert.ThrowsAsync<IOException>(() => f.Importer.ImportAsync(p, true)); f.Importer.Fault = null;
        var id = Assert.Single(await f.Importer.PendingAsync()); var directory = f.Workspace.Paths.Profile(id); Directory.CreateDirectory(directory); var file = SafePath.Join(directory, "foreign.txt"); await System.IO.File.WriteAllTextAsync(file, "PRESERVE");
        Assert.Equal("PackConflict", (await Assert.ThrowsAsync<MrpackException>(() => f.Importer.RecoverAsync(true))).Code); Assert.Equal("PRESERVE", await System.IO.File.ReadAllTextAsync(file)); Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
    }
    [Theory][InlineData("fabric-loader", ModLoader.Fabric)][InlineData("quilt-loader", ModLoader.Quilt)][InlineData("forge", ModLoader.Forge)][InlineData("neoforge", ModLoader.NeoForge)]
    public async Task ExactLoaderRequirementIsPersistedAndNeverSilentlyReplaced(string key, ModLoader loader)
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var p = await f.Importer.Reader.PreviewAsync(f.Pack(MrpackFixture.Index(loader: key, version: "1.2.3")));
        var imported = await f.Importer.ImportAsync(p, true); Assert.Equal(loader, imported.Profile.Loader);
        Assert.Equal("1.2.3", await MrpackRequirements.RequestedAsync(f.Workspace.Paths, imported.Profile));
        Assert.Equal("PackTarget", (await Assert.ThrowsAsync<MrpackException>(() => MrpackRequirements.RequestedAsync(f.Workspace.Paths, imported.Profile, "9.9.9"))).Code);
        await Assert.ThrowsAsync<MrpackException>(() => MrpackRequirements.RequestedAsync(f.Workspace.Paths, imported.Profile with { MinecraftVersion = "1.20.1" })); Assert.False(System.IO.File.Exists(f.Workspace.Paths.Receipt(imported.Profile.Id)));
    }
    [Fact] public async Task ActualJarsAreStaticallyCheckedAndLocalPinsNeverInventProviderIdentity()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var jar = MrpackFixture.Jar();
        var p = await f.Importer.Reader.PreviewAsync(f.Pack(MrpackFixture.Index(loader: "fabric-loader"), ("overrides/mods/demo.jar", jar)), pinMods: true);
        var result = await f.Importer.ImportAsync(p, true); Assert.NotNull(result.Receipt.Review); Assert.NotEmpty(result.Receipt.Review.Warnings);
        var inventory = await new ModInventoryStore(f.Workspace.Paths).ReadAsync(result.Profile.Id); Assert.Equal("mods/demo.jar", Assert.Single(inventory!.Files).RelativePath);
        var pins = await AtomicFile.ReadJsonAsync<ModPinSet>(SafePath.Join(f.Workspace.Paths.Profile(result.Profile.Id), "mod-pins.json")); Assert.Null(Assert.Single(pins!.Pins).Provider);
    }
    [Fact] public async Task BoundsRejectDeepOversizedAndZip64Indexes()
    {
        using var f = new MrpackFixture();
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.Reader.PreviewAsync(f.RawPack(new byte[MrpackReader.MaxIndexBytes + 1])));
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.Reader.PreviewAsync(f.RawPack(Encoding.UTF8.GetBytes(new string('[', 40) + "0" + new string(']', 40)))));
        var path = f.Pack(); var bytes = await System.IO.File.ReadAllBytesAsync(path);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - 12), 65535); await System.IO.File.WriteAllBytesAsync(path, bytes);
        Assert.Equal("PackUnsupported", (await Assert.ThrowsAsync<MrpackException>(() => f.Importer.Reader.PreviewAsync(path))).Code);
    }
    [Fact] public async Task DamagedCacheIsNotAcceptedOfflineAndBothHashesRemainRequired()
    {
        using var f = new MrpackFixture(); var p = await f.Importer.Reader.PreviewAsync(f.Pack(MrpackFixture.Index([f.File()]))); await f.Seed(p.Files[0].Download!);
        await System.IO.File.WriteAllBytesAsync(f.Importer.Reader.CachePath(p.Files[0].Download!), new byte[f.Payload.Length]); f.Network.Offline = true;
        Assert.Contains((await f.Importer.Reader.PreviewAsync(p.Archive)).Blockers, x => x.Code == "PackOfflineMissing"); Assert.Equal(0, f.Requests);
        var malformed = f.File(); ((Dictionary<string, object?>)malformed["hashes"]!).Remove("sha1");
        await Assert.ThrowsAsync<MrpackException>(() => f.Importer.Reader.PreviewAsync(f.Pack(MrpackFixture.Index([malformed]))));
    }
    [Fact] public async Task ScriptsAreCopiedAsDataAndExactProvenanceDoesNotInventProviderIdentity()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync();
        var p = await f.Importer.Reader.PreviewAsync(f.Pack(null, ("overrides/run.cmd", Encoding.UTF8.GetBytes("@echo off\r\necho EXECUTED>execution-marker.txt\r\n"))));
        var result = await f.Importer.ImportAsync(p, true); var r = await MrpackRequirements.ReadAsync(f.Workspace.Paths, result.Profile);
        Assert.Equal("local-mrpack", r!.SourceKind); Assert.Equal(Path.GetFileName(p.Archive), r.ArchiveFileName); Assert.Equal(p.ArchiveSha256, r.ArchiveSha256);
        Assert.Equal("run.cmd", Assert.Single(r.Files).RelativePath); Assert.False(System.IO.File.Exists(SafePath.Join(f.Workspace.Paths.Game(result.Profile.Id), "execution-marker.txt")));
    }
    [Fact] public async Task AllowedRedirectIsValidatedAndDownloadsOnlyAfterConsent()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync();
        var p = await f.Importer.Reader.PreviewAsync(f.Pack(MrpackFixture.Index([f.File(url: "https://github.com/example/pack/releases/download/v1/data")] ))); Assert.Equal(0, f.Requests);
        f.Response = (request, _) => Task.FromResult(request.RequestUri!.Host == "github.com"
            ? new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new("https://release-assets.githubusercontent.com/synthetic/data") } }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(f.Payload) });
        await f.Importer.ImportAsync(p, true); Assert.Equal(2, f.Requests);
    }
    [Fact] public async Task CorruptMissingLoaderVersionCannotSelectLatest()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync();
        var result = await f.Importer.ImportAsync(await f.Importer.Reader.PreviewAsync(f.Pack(MrpackFixture.Index(loader: "fabric-loader"))), true);
        await AtomicFile.WriteJsonAsync(SafePath.Join(f.Workspace.Paths.Profile(result.Profile.Id), MrpackImporter.ReceiptName), result.Receipt with { LoaderVersion = null });
        Assert.Equal("PackInvalid", (await Assert.ThrowsAsync<MrpackException>(() => MrpackRequirements.RequestedAsync(f.Workspace.Paths, result.Profile))).Code);
    }
    [Fact] public async Task UserChangesToMovedContentPreventRecoveryAndRemainIntact()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync();
        var preview = await f.Importer.Reader.PreviewAsync(f.Pack(null, ("overrides/config/user.txt", [1])));
        f.Importer.Fault = stage => { if (stage == "moved") throw new IOException(); }; await Assert.ThrowsAsync<IOException>(() => f.Importer.ImportAsync(preview, true)); f.Importer.Fault = null;
        var id = Assert.Single(await f.Importer.PendingAsync()); var file = SafePath.Join(f.Workspace.Paths.Game(id), "config/user.txt"); await System.IO.File.WriteAllTextAsync(file, "NEWER USER DATA");
        Assert.Equal("PackConflict", (await Assert.ThrowsAsync<MrpackException>(() => f.Importer.RecoverAsync(true))).Code); Assert.Equal("NEWER USER DATA", await System.IO.File.ReadAllTextAsync(file)); Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
    }
    [Fact] public async Task ExplicitServerOnlyContentCannotBeIntroducedThroughClientOverrides()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var jar = MrpackFixture.Jar(environment: "server");
        var index = MrpackFixture.Index([f.File("mods/server.jar", "unsupported", "required", bytes: jar)], "fabric-loader");
        var conflict = await f.Importer.Reader.PreviewAsync(f.Pack(index, ("client-overrides/mods/server.jar", jar)));
        Assert.Contains(conflict.Blockers, p => p.Code == "PackUnsupported"); Assert.Equal(0, f.Requests);
        var nested = await f.Importer.Reader.PreviewAsync(f.Pack(MrpackFixture.Index(loader: "fabric-loader"), ("overrides/mods/server.jar", jar)));
        Assert.Equal("PackCompatibility", (await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ImportAsync(nested, true))).Code); Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
    }
    [Fact] public async Task DefiniteActualJarDependencyConflictBlocksPublication()
    {
        using var f = new MrpackFixture(); await f.Workspace.Store.InitializeAsync(); var p = await f.Importer.Reader.PreviewAsync(f.Pack(MrpackFixture.Index(loader: "fabric-loader"), ("overrides/mods/demo.jar", MrpackFixture.Jar(dependency: "missing-required"))));
        Assert.Equal("PackCompatibility", (await Assert.ThrowsAsync<MrpackException>(() => f.Importer.ImportAsync(p, true))).Code); Assert.Empty(await f.Workspace.Store.GetProfilesAsync());
    }
}
