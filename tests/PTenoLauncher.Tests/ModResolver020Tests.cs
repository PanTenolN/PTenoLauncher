using System.Security.Cryptography;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Mods;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;
public sealed class ModResolver020Tests
{
    [Theory]
    [InlineData("1.21.1", ">=1.20 <1.22", ConstraintMatch.Match)]
    [InlineData("1.19.4", ">=1.20 <1.22", ConstraintMatch.Mismatch)]
    [InlineData("1.21.1", "1.21.x", ConstraintMatch.Match)]
    [InlineData("1.21.1", "1.20.*", ConstraintMatch.Mismatch)]
    [InlineData("0.16.5", "^0.16", ConstraintMatch.Match)]
    [InlineData("0.17.0", "^0.16", ConstraintMatch.Mismatch)]
    [InlineData("1.5.0", "~1", ConstraintMatch.Match)]
    [InlineData("1.5.0", "~1.4.0", ConstraintMatch.Mismatch)]
    [InlineData("0.0.3", "^0", ConstraintMatch.Match)]
    [InlineData("1.21.1", "[1.20,1.22)", ConstraintMatch.Match)]
    [InlineData("1.22", "[1.20,1.22)", ConstraintMatch.Mismatch)]
    [InlineData("2.0", "1.0 || >=2.0", ConstraintMatch.Match)]
    [InlineData("1.0-beta", ">=1.0", ConstraintMatch.Unknown)]
    [InlineData("snapshot-a", "snapshot-a", ConstraintMatch.Match)]
    public void RangesAreDeterministicAndUnsupportedFormsRemainUnknown(string actual, string constraint, ConstraintMatch expected) =>
        Assert.Equal(expected, VersionConstraints.Match(actual, constraint));
    [Fact] public async Task FabricVersionMismatchBlocksLocalInstallWithoutAi()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync();
        var profile = ws.Profile(ModLoader.Fabric); await ws.Store.SaveProfileAsync(profile);
        var jar = Path.Combine(ws.Paths.Root, "example.jar");
        SecurityAndModsTests.CreateJar(jar, depends: """{"minecraft":">=1.22"}""");
        var mods = new LocalModManager(ws.Paths, ws.Store); var item = new ModCartItem(jar, await mods.InspectAsync(jar));
        Assert.Contains((await mods.ReviewAsync(profile, [item])).Blockers, b => b.Contains("1.22"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => mods.InstallAsync(profile, [item], true));
    }
    [Fact] public async Task PreparedModJournalRollsBackOnlyMatchingOwnedTargets()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync(); var p = ws.Profile(ModLoader.Fabric); await ws.Store.SaveProfileAsync(p);
        var id = Guid.NewGuid(); var relative = "mods/synthetic.jar";
        byte[] bytes = [1, 2, 3];
        await File.WriteAllBytesAsync(SafePath.Join(ws.Paths.Game(p.Id), relative), bytes);
        var journal = new ModInstallJournal(id, p.Id, "Prepared", [new(relative + ".pending-" + id.ToString("N"), relative, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length)]);
        var file = SafePath.Join(ws.Paths.Profile(p.Id), $"transactions/mods-{id:N}.json");
        await AtomicFile.WriteJsonAsync(file, journal);
        await new LocalModManager(ws.Paths, ws.Store).RecoverAsync(p);
        Assert.False(File.Exists(SafePath.Join(ws.Paths.Game(p.Id), relative))); Assert.False(File.Exists(file));
    }
    [Fact] public async Task RecoveryNeverDeletesAChangedTarget()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync(); var p = ws.Profile(ModLoader.Fabric); await ws.Store.SaveProfileAsync(p);
        var id = Guid.NewGuid(); var relative = "mods/synthetic.jar";
        await File.WriteAllTextAsync(SafePath.Join(ws.Paths.Game(p.Id), relative), "changed by user");
        var file = SafePath.Join(ws.Paths.Profile(p.Id), $"transactions/mods-{id:N}.json");
        await AtomicFile.WriteJsonAsync(file, new ModInstallJournal(id, p.Id, "Prepared", [new(relative + ".pending-" + id.ToString("N"), relative, new string('a',64), 3)]));
        await Assert.ThrowsAsync<InvalidDataException>(() => new LocalModManager(ws.Paths, ws.Store).RecoverAsync(p));
        Assert.Equal("changed by user", await File.ReadAllTextAsync(SafePath.Join(ws.Paths.Game(p.Id), relative)));
        Assert.True(File.Exists(file));
    }
}