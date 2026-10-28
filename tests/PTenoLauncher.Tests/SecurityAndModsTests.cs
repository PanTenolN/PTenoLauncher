using System.IO.Compression;
using System.Text;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Authentication;
using PTenoLauncher.Infrastructure.Diagnostics;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Security;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class SecurityAndModsTests
{
    internal static void CreateJar(string path, string id = "example", string depends = "{}")
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var text = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open());
        text.Write($$"""{"schemaVersion":1,"id":"{{id}}","version":"1.0.0","name":"Example","depends":{{depends}}}""");
    }
    [Fact] public void DpapiDoesNotWritePlaintextAndRejectsTampering()
    {
        using var ws = new TestWorkspace(); var vault = new SecretVault(ws.Paths);
        vault.Write("test", Encoding.UTF8.GetBytes("private-token-value"));
        var file = Path.Combine(ws.Paths.Secrets, "test.dpapi");
        var data = File.ReadAllBytes(file); Assert.DoesNotContain("private-token-value", Encoding.UTF8.GetString(data));
        Assert.Equal("private-token-value", Encoding.UTF8.GetString(vault.Read("test")!));
        data[^1] ^= 1; File.WriteAllBytes(file, data);
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => vault.Read("test"));
    }
    [Fact] public async Task SignInWithoutOwnClientIdFailsBeforeAnyRequest()
    {
        using var ws = new TestWorkspace();
        using var handler = new StubHandler(_ => throw new Exception("Unexpected network"));
        using var http = new HttpClient(handler);
        var auth = new MicrosoftAuthentication(new(ws.Paths), http, new());
        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.SignInAsync(true));
        Assert.Equal(0, handler.Requests); Assert.Null(auth.GetCachedSession());
    }
    [Fact] public void DiagnosticsRedactKnownSecrets()
    {
        var redacted = LauncherLog.Redact("access_token=abc refresh_token=def password=\"private value\"");
        Assert.DoesNotContain("abc", redacted); Assert.DoesNotContain("def", redacted); Assert.DoesNotContain("private value", redacted);
    }
    [Theory]
    [InlineData("../escape.txt")][InlineData("C:/escape.txt")][InlineData("mods\\evil.jar")]
    public void MaliciousArchiveEntriesAreRejected(string name)
    {
        using var ws = new TestWorkspace(); using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true)) zip.CreateEntry(name);
        memory.Position = 0; using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        Assert.Throws<InvalidDataException>(() => SafeArchive.Validate(archive, ws.Paths.Cache));
    }
    [Fact] public void ExpandedSizeAndSymbolicLinksAreRejected()
    {
        using var ws = new TestWorkspace(); using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        { using var writer = zip.CreateEntry("large").Open(); writer.Write(new byte[100]); }
        memory.Position = 0; using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        Assert.Throws<InvalidDataException>(() => SafeArchive.Validate(archive, ws.Paths.Cache, maxExpanded: 50));
        using var second = new MemoryStream();
        using (var zip = new ZipArchive(second, ZipArchiveMode.Create, true)) zip.CreateEntry("link").ExternalAttributes = unchecked((int)0xA1FF0000);
        second.Position = 0; using var links = new ZipArchive(second, ZipArchiveMode.Read);
        Assert.Throws<InvalidDataException>(() => SafeArchive.Validate(links, ws.Paths.Cache));
    }
    [Fact] public async Task FabricImportRequiresDependencyAndUnknownSourceConfirmation()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync();
        var p = ws.Profile(ModLoader.Fabric); await ws.Store.SaveProfileAsync(p);
        var file = Path.Combine(ws.Paths.Root, "example.jar"); CreateJar(file, depends: """{"missing_library":"*"}""");
        var mods = new LocalModManager(ws.Paths, ws.Store); var inspected = await mods.InspectAsync(file);
        Assert.Equal("example", inspected.Id); Assert.Equal("Unknown Source", inspected.SourceStatus);
        ModCartItem[] cart = [new(file, inspected)];
        Assert.Contains((await mods.ReviewAsync(p, cart)).Blockers, s => s.Contains("missing_library"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => mods.InstallAsync(p, cart, false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => mods.InstallAsync(p, cart, true));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(ws.Paths.Game(p.Id), "mods")));
    }
    [Fact] public async Task ApprovedJarIsCopiedAndDeduplicatedInCache()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync();
        var p = ws.Profile(ModLoader.Fabric); await ws.Store.SaveProfileAsync(p);
        var file = Path.Combine(ws.Paths.Root, "example.jar"); CreateJar(file);
        var mods = new LocalModManager(ws.Paths, ws.Store); var inspection = await mods.InspectAsync(file);
        await mods.InstallAsync(p, [new(file, inspection)], true);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(ws.Paths.Game(p.Id), "mods")));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(ws.Paths.Cache, "mods", "sha256")));
        Assert.Contains((await mods.ReviewAsync(p, [new(file, inspection)])).Blockers, s => s.Contains("Дублікат"));
    }
}