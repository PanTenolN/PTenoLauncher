using System.IO.Compression;
using System.Text.Json;
using PTenoLauncher.Infrastructure.Modpacks;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class PackComparisonTests
{
    [Fact]
    public async Task ComparisonShowsExactVersionsAndDigestsWithoutChangingEitherArchive()
    {
        using var ws = new TestWorkspace();
        var first = WritePack(ws, "first", "1.21.1", "0.16.10", "File1234", "left bytes", "left.jar");
        var second = WritePack(ws, "second", "1.21.1", "0.16.11", "File1235", "right bytes", "right.jar");
        var firstOriginal = await File.ReadAllBytesAsync(first);
        var secondOriginal = await File.ReadAllBytesAsync(second);
        var compared = await new PackComparison(ws.Paths).CompareAsync(first, second);
        Assert.False(compared.SameTarget);
        Assert.Equal("0.16.10", compared.Left.LoaderVersion);
        Assert.Equal("0.16.11", compared.Right.LoaderVersion);
        var version = Assert.Single(compared.Files, row => row.Kind == "PackCompareVersion");
        Assert.Equal("File1234", version.Left?.VersionId);
        Assert.Equal("File1235", version.Right?.VersionId);
        Assert.Contains(compared.Files, row => row.Kind == "PackCompareHash" && row.Left?.Path == "mods/shared.jar");
        Assert.Contains(compared.Files, row => row.Kind == "PackCompareLeftOnly");
        Assert.Contains(compared.Files, row => row.Kind == "PackCompareRightOnly");
        Assert.Equal(firstOriginal, await File.ReadAllBytesAsync(first));
        Assert.Equal(secondOriginal, await File.ReadAllBytesAsync(second));
        Assert.Empty(Directory.EnumerateFileSystemEntries(ws.Paths.Cache));
        Assert.Empty(Directory.EnumerateFileSystemEntries(SafePath.Join(ws.Paths.Root, "instances")));
    }

    private static string WritePack(TestWorkspace ws, string name, string minecraft, string loader, string version, string bytes, string exclusive)
    {
        var file = SafePath.Join(ws.Paths.Root, name + ".mrpack");
        using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
        var manifest = new
        {
            formatVersion = 1, game = "minecraft", name, versionId = "1",
            dependencies = new Dictionary<string, string> { ["minecraft"] = minecraft, ["fabric-loader"] = loader },
            files = new[] { new { path = "mods/test.jar", fileSize = 20,
                hashes = new { sha1 = new string('a', 40), sha512 = new string('b', 128) },
                downloads = new[] { $"https://cdn.modrinth.com/data/Test1234/versions/{version}/test.jar" } } }
        };
        Write("modrinth.index.json", JsonSerializer.Serialize(manifest));
        Write("overrides/mods/shared.jar", bytes);
        Write("overrides/mods/" + exclusive, exclusive);
        return file;

        void Write(string entry, string text)
        {
            using var output = new StreamWriter(zip.CreateEntry(entry).Open());
            output.Write(text);
        }
    }
}
