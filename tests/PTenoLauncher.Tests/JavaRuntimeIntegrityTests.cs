using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Authentication;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Network;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class JavaRuntimeIntegrityTests
{
    private static byte[] Pe(ushort machine = 0x8664)
    {
        var bytes = new byte[256]; bytes[0] = 0x4d; bytes[1] = 0x5a; bytes[0x3c] = 0x80;
        bytes[0x80] = 0x50; bytes[0x81] = 0x45; bytes[0x84] = (byte)machine; bytes[0x85] = (byte)(machine >> 8); return bytes;
    }
    private static async Task<(string Root, string Java, ManagedJavaReceipt Receipt)> Fixture(TestWorkspace ws, bool secondJava = false)
    {
        var hash = new string('a', 64); var root = Path.Combine(ws.Paths.Root, "runtimes", "temurin-21-" + hash[..12]);
        Directory.CreateDirectory(Path.Combine(root, "bin")); var java = Path.Combine(root, "bin", "java.exe");
        await File.WriteAllBytesAsync(java, Pe()); await File.WriteAllBytesAsync(Path.Combine(root, "bin", "jvm.dll"), [1, 2, 3]);
        if (secondJava) { Directory.CreateDirectory(Path.Combine(root, "other")); await File.WriteAllBytesAsync(Path.Combine(root, "other", "java.exe"), Pe()); }
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(path =>
            new InstalledFile(Path.GetRelativePath(root, path).Replace('\\', '/'), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), "SHA256", new FileInfo(path).Length)).ToArray();
        var receipt = new ManagedJavaReceipt(21, "jdk-21-test", hash, "bin/java.exe", files);
        await AtomicFile.WriteJsonAsync(Path.Combine(root, "runtime.json"), receipt); return (root, java, receipt);
    }
    [Fact] public async Task RequestedManagedExecutableMustBeTheExactTrackedJavaPath()
    {
        using var ws = new TestWorkspace(); var f = await Fixture(ws, true);
        Assert.True(await JavaRuntimeManager.VerifyManagedAsync(f.Root, f.Receipt));
        Assert.True(await JavaRuntimeManager.VerifyManagedPathAsync(ws.Paths, f.Java));
        Assert.False(await JavaRuntimeManager.VerifyManagedPathAsync(ws.Paths, Path.Combine(f.Root, "other", "java.exe")));
    }
    [Theory]
    [InlineData("duplicate")][InlineData("missing-java")][InlineData("untracked")][InlineData("bad-hash")]
    [InlineData("negative-size")][InlineData("algorithm")][InlineData("traversal")][InlineData("self-receipt")]
    [InlineData("bad-archive")][InlineData("bad-major")][InlineData("null-files")][InlineData("null-entry")]
    [InlineData("wrong-executable")][InlineData("absolute-java")]
    public async Task MalformedOrIncompleteManagedInventoryIsRejected(string change)
    {
        using var ws = new TestWorkspace(); var f = await Fixture(ws); var r = f.Receipt; var files = r.Files.ToArray();
        r = change switch {
            "duplicate" => r with { Files = [.. files, files[0]] },
            "missing-java" => r with { Files = files.Where(x => x.RelativePath != r.JavaRelativePath).ToArray() },
            "bad-hash" => r with { Files = [files[0] with { Hash = "invalid" }, files[1]] },
            "negative-size" => r with { Files = [files[0] with { Size = -1 }, files[1]] },
            "algorithm" => r with { Files = [files[0] with { Algorithm = "SHA1" }, files[1]] },
            "traversal" => r with { Files = [files[0] with { RelativePath = "../outside" }, files[1]] },
            "self-receipt" => r with { Files = [.. files, files[0] with { RelativePath = "runtime.json" }] },
            "bad-archive" => r with { ArchiveSha256 = "../untrusted" },
            "bad-major" => r with { Major = 0 }, "null-files" => r with { Files = null! },
            "null-entry" => r with { Files = [null!] }, "wrong-executable" => r with { JavaRelativePath = "bin/jvm.dll" },
            "absolute-java" => r with { JavaRelativePath = f.Java }, _ => r };
        if (change == "untracked") await File.WriteAllBytesAsync(Path.Combine(f.Root, "bin", "untracked.dll"), [4, 5, 6]);
        Assert.False(await JavaRuntimeManager.VerifyManagedAsync(f.Root, r));
    }
    [Fact] public async Task FolderIdentityAndReceiptCorruptionFailClosed()
    {
        using var ws = new TestWorkspace(); var f = await Fixture(ws);
        await AtomicFile.WriteJsonAsync(Path.Combine(f.Root, "runtime.json"), f.Receipt with { ArchiveSha256 = new string('b', 64) });
        Assert.False(await JavaRuntimeManager.VerifyManagedPathAsync(ws.Paths, f.Java));
        await File.WriteAllTextAsync(Path.Combine(f.Root, "runtime.json"), "{broken");
        Assert.False(await JavaRuntimeManager.VerifyManagedPathAsync(ws.Paths, f.Java));
    }
    [Fact] public async Task CorruptManagedRuntimeIsNotExecutedByOfflineReadiness()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync(); var f = await Fixture(ws);
        await File.WriteAllBytesAsync(f.Java, [1, 2]); var profile = ws.Profile() with { JavaPath = f.Java };
        await ws.Store.SaveProfileAsync(profile);
        using var handler = new StubHandler(_ => throw new Exception("No network")); using var http = new HttpClient(handler);
        var auth = new MicrosoftAuthentication(new(ws.Paths), http, new() { Offline = true });
        var report = await new OfflineReadiness(ws.Paths, new(), auth).CheckAsync(profile);
        Assert.Contains(report.Issues, i => i.Code == "java.corrupt");
        Assert.DoesNotContain(report.Issues, i => i.Code == "java.invalid"); // No attempt to start the invalid PE.
        Assert.Equal(0, handler.Requests);
    }
    [Theory][InlineData((ushort)0xAA64)][InlineData((ushort)0x014c)]
    public async Task NonX64MachineIsRejectedBeforeProcessStart(ushort machine)
    {
        using var ws = new TestWorkspace(); var path = Path.Combine(ws.Paths.Root, "java.exe"); await File.WriteAllBytesAsync(path, Pe(machine));
        Assert.False(JavaRuntimeManager.IsX64Executable(path));
        await Assert.ThrowsAsync<ArgumentException>(() => new JavaManager().InspectAsync(path));
        Assert.False(JavaRuntimeManager.IsX64Executable(path + ".missing"));
    }
    [Fact] public async Task ManagedOnlyOfflineFailureDoesNotUseNetworkOrDeleteCorruptFiles()
    {
        using var ws = new TestWorkspace(); var f = await Fixture(ws); var path = Path.Combine(f.Root, "runtime.json"); await File.WriteAllTextAsync(path, "broken");
        using var handler = new StubHandler(_ => throw new Exception("No network")); using var http = new HttpClient(handler);
        var policy = new NetworkPolicy { Offline = true }; var downloader = new VerifiedDownloader(http, policy);
        var manager = new JavaRuntimeManager(ws.Paths, new(), new(ws.Paths, downloader), downloader, policy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.EnsureManagedAsync(21));
        Assert.Equal(0, handler.Requests); Assert.Equal("broken", await File.ReadAllTextAsync(path));
    }
    [Fact] public async Task CanceledValidationDoesNotBecomeAFalseSuccess()
    {
        using var ws = new TestWorkspace(); var f = await Fixture(ws); using var stop = new CancellationTokenSource(); await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => JavaRuntimeManager.VerifyManagedAsync(f.Root, f.Receipt, stop.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new JavaManager().InspectAsync(f.Java, stop.Token));
    }
    [Theory][InlineData(8)][InlineData(17)][InlineData(21)][InlineData(25)]
    public async Task MinecraftManifestJavaRequirementIsCachedAndHistoricalDefaultIsEight(int major)
    {
        using var ws = new TestWorkspace(); var profile = ws.Profile();
        var manifest = major == 8 ? Encoding.UTF8.GetBytes("{\"id\":\"1.21.1\"}") :
            JsonSerializer.SerializeToUtf8Bytes(new { id = "1.21.1", javaVersion = new { majorVersion = major } });
        var hash = Convert.ToHexString(SHA1.HashData(manifest));
        var catalog = Encoding.UTF8.GetBytes($$"""{"latest":{"release":"1.21.1"},"versions":[{"id":"1.21.1","type":"release","url":"https://piston-meta.mojang.com/java-test.json","sha1":"{{hash}}","releaseTime":"2024-08-08T00:00:00Z"}]}""");
        using var handler = new StubHandler(request => new(HttpStatusCode.OK) { Content = new ByteArrayContent(request.RequestUri == VersionManager.ManifestUri ? catalog : manifest) });
        using var http = new HttpClient(handler); var policy = new NetworkPolicy(); var downloader = new VerifiedDownloader(http, policy); var versions = new VersionManager(ws.Paths, downloader);
        var manager = new JavaRuntimeManager(ws.Paths, new(), versions, downloader, policy);
        Assert.Equal(major, await manager.RequiredMajorAsync(profile)); policy.Offline = true;
        Assert.Equal(major, await manager.RequiredMajorAsync(profile)); Assert.Equal(2, handler.Requests);
    }

    private static JsonNode Metadata()
        => JsonNode.Parse("""[{"vendor":"eclipse","version":{"major":21},"release_name":"jdk-21-test","binary":{"architecture":"x64","os":"windows","image_type":"jre","jvm_impl":"hotspot","project":"jdk","package":{"name":"java-test.zip","checksum":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size":1024,"link":"https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21-test/java-test.zip"}}}]""")!;
    [Fact] public void ExactOfficialMetadataIsAccepted()
    {
        var package = TemurinPackage.Parse(JsonSerializer.SerializeToUtf8Bytes(Metadata()), 21);
        Assert.Equal(1024, package.Size); Assert.Equal("github.com", package.DownloadUri.Host); Assert.Equal(64, package.Sha256.Length);
    }
    [Theory]
    [InlineData("host",false)][InlineData("http",false)][InlineData("userinfo",false)][InlineData("port",false)]
    [InlineData("query",false)][InlineData("fragment",false)][InlineData("hash",false)][InlineData("size",false)]
    [InlineData("filename",false)][InlineData("major",true)][InlineData("vendor",true)][InlineData("architecture",true)]
    [InlineData("os",true)][InlineData("image_type",true)][InlineData("jvm_impl",true)][InlineData("project",true)]
    public void JavaMetadataCannotRedirectToWrongPlatformVersionOrOrigin(string change, bool unmatched)
    {
        var root = Metadata(); var asset = root[0]!; var binary = asset["binary"]!; var package = binary["package"]!;
        switch (change)
        {
            case "major": asset["version"]!["major"] = 17; break;
            case "vendor": asset["vendor"] = "other"; break;
            case "architecture": binary[change] = "aarch64"; break;
            case "os": binary[change] = "linux"; break;
            case "image_type": binary[change] = "jdk"; break;
            case "jvm_impl": binary[change] = "other"; break;
            case "project": binary[change] = "valhalla"; break;
            case "hash": package["checksum"] = "../bad"; break;
            case "size": package[change] = -1; break;
            case "filename": package["name"] = "../java-test.zip"; break;
            default:
                var link = package["link"]!.GetValue<string>();
                package["link"] = change switch {
                    "host" => link.Replace("github.com", "github.com.evil.test"), "http" => link.Replace("https:", "http:"),
                    "userinfo" => link.Replace("github.com", "user@github.com"), "port" => link.Replace("github.com", "github.com:444"),
                    "query" => link + "?redirect=bad", "fragment" => link + "#bad", _ => throw new Exception("Unknown fixture") }; break;
        }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(root);
        if (unmatched) Assert.Throws<NotSupportedException>(() => TemurinPackage.Parse(bytes, 21));
        else Assert.Throws<InvalidDataException>(() => TemurinPackage.Parse(bytes, 21));
    }
}
