using System.Text.Json;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class BackupAndRulesTests
{
    [Fact] public async Task BackupVerifiesAndRestoresIntoNewProfileWithoutChangingOriginal()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync();
        var profile = ws.Profile(); await ws.Store.SaveProfileAsync(profile);
        var world = Path.Combine(ws.Paths.Game(profile.Id), "saves", "precious.dat");
        await File.WriteAllTextAsync(world, "my world");
        var manager = new BackupManager(ws.Paths, ws.Store);
        var backup = await manager.CreateAsync(profile); await manager.VerifyAsync(backup);
        await File.WriteAllTextAsync(world, "newer state");
        var restored = await manager.RestoreAsNewAsync(backup);
        Assert.NotEqual(profile.Id, restored.Id);
        Assert.Equal("my world", await File.ReadAllTextAsync(Path.Combine(ws.Paths.Game(restored.Id), "saves", "precious.dat")));
        Assert.Equal("newer state", await File.ReadAllTextAsync(world));
        Assert.False(File.Exists(ws.Paths.Receipt(restored.Id)));
    }
    [Fact] public void MinecraftRulesExcludeOtherPlatformsAndUnsupportedFeatures()
    {
        using var linux = JsonDocument.Parse("""{"rules":[{"action":"allow","os":{"name":"linux"}}]}""");
        using var windows = JsonDocument.Parse("""{"rules":[{"action":"allow","os":{"name":"windows"}}]}""");
        using var feature = JsonDocument.Parse("""{"rules":[{"action":"allow","features":{"is_demo_user":true}}]}""");
        Assert.False(MinecraftRules.Allowed(linux.RootElement)); Assert.True(MinecraftRules.Allowed(windows.RootElement));
        Assert.False(MinecraftRules.Allowed(feature.RootElement));
    }
    [Fact] public void RuleArgumentsRemainSeparateWithoutShellInterpretation()
    {
        using var json = JsonDocument.Parse("""["--username","two words",{"rules":[{"action":"allow","os":{"name":"linux"}}],"value":["bad"]}]""");
        Assert.Equal(new[] { "--username", "two words" }, MinecraftRules.Arguments(json.RootElement));
    }
}