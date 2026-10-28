using System.Security.Cryptography;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.AI;
using PTenoLauncher.Infrastructure.Settings;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class Foundation020Tests
{
    [Fact] public async Task ThemeAndAiPreferencesRoundTripAndDefaultToGraphiteWithAiOff()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync();
        var preferences = new PreferencesService(ws.Store);
        Assert.Equal(LauncherTheme.GraphiteDark, (await preferences.GetThemeAsync()).Theme);
        Assert.False(await preferences.GetAiEnabledAsync());
        var settings = new ThemePreferences(LauncherTheme.Custom, new("#101010", "#181818", "#222222", "#AAAAAA", "#EEEEEE", 9, false));
        await preferences.SetThemeAsync(settings);
        Assert.Equal(settings, await new PreferencesService(ws.Store).GetThemeAsync());
        await preferences.SetAiEnabledAsync(true); Assert.True(await preferences.GetAiEnabledAsync());
    }
    [Theory]
    [InlineData("red")][InlineData("#nothex")][InlineData("#123")][InlineData("")]
    public void InvalidThemeColorsAreRejected(string color) =>
        Assert.Throws<ArgumentException>(() => PreferencesValidation.Validate(new(LauncherTheme.Custom, new(Background: color))));
    [Fact] public async Task DisabledAiNeverCallsProviderAndDisablingCancelsRunningOperation()
    {
        var ai = new AiPolicy(); int calls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => ai.ExecuteAsync(_ => { calls++; return Task.FromResult(1); }));
        Assert.Equal(0, calls);
        ai.SetEnabled(true);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = ai.ExecuteAsync(async ct => { started.SetResult(); await Task.Delay(Timeout.Infinite, ct); return 1; });
        await started.Task; ai.SetEnabled(false);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.False(ai.Enabled);
    }
    [Fact] public async Task DeleteKeepsBackupsAndNeverTouchesOtherProfileSharedCacheOrJava()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync();
        var a = ws.Profile(); var b = ws.Profile() with { Name = "Other" };
        await ws.Store.SaveProfileAsync(a); await ws.Store.SaveProfileAsync(b);
        await File.WriteAllTextAsync(Path.Combine(ws.Paths.Game(a.Id), "saves", "fake-world"), "synthetic");
        await File.WriteAllTextAsync(Path.Combine(ws.Paths.Game(b.Id), "saves", "other"), "preserve");
        var backup = await new BackupManager(ws.Paths, ws.Store).CreateAsync(a);
        var shared = Path.Combine(ws.Paths.Cache, "shared.jar"); await File.WriteAllTextAsync(shared, "shared");
        var java = SafePath.Join(ws.Paths.Root, "runtimes/test.txt"); Directory.CreateDirectory(Path.GetDirectoryName(java)!); await File.WriteAllTextAsync(java, "java");
        var service = new ProfileDeletionService(ws.Paths, ws.Store, new(ws.Paths));
        var plan = await service.PreviewAsync(a.Id, false);
        Assert.True(plan.TotalBytes > 0); Assert.Contains(plan.Files, f => f.RelativePath.Contains("fake-world"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(plan, "wrong"));
        Assert.True(Directory.Exists(ws.Paths.Profile(a.Id)));
        var result = await service.DeleteAsync(plan, a.Name);
        Assert.False(result.CleanupPending); Assert.False(Directory.Exists(ws.Paths.Profile(a.Id)));
        Assert.True(File.Exists(backup)); Assert.True(File.Exists(shared)); Assert.True(File.Exists(java));
        Assert.Equal(b.Id, Assert.Single(await ws.Store.GetProfilesAsync()).Id);
        Assert.Empty(await ws.Store.GetHistoryAsync(a.Id));
    }
    [Fact] public async Task DeleteAllIncludesOnlyOwnedBackupsAndExplicitlySelectedTrackedExports()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync();
        var a = ws.Profile(); await ws.Store.SaveProfileAsync(a);
        var backup = await new BackupManager(ws.Paths, ws.Store).CreateAsync(a);
        var chosen = $"exports/{a.Id:N}/chosen.zip"; var kept = $"exports/{a.Id:N}/kept.zip";
        foreach (var relative in new[] { chosen, kept })
        {
            var file = SafePath.Join(ws.Paths.Root, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, "synthetic export"); await ws.Store.RegisterArtifactAsync(new(a.Id, relative, "export"));
        }
        var service = new ProfileDeletionService(ws.Paths, ws.Store, new(ws.Paths));
        var plan = await service.PreviewAsync(a.Id, true, [chosen]);
        await service.DeleteAsync(plan, a.Name);
        Assert.False(File.Exists(backup)); Assert.False(File.Exists(SafePath.Join(ws.Paths.Root, chosen)));
        Assert.True(File.Exists(SafePath.Join(ws.Paths.Root, kept)));
    }
    [Fact] public async Task DeleteRefusesAnActiveProfileAndChangesAfterPreview()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync(); var p = ws.Profile(); await ws.Store.SaveProfileAsync(p);
        var usage = new ProfileUsage(ws.Paths); var service = new ProfileDeletionService(ws.Paths, ws.Store, usage);
        using (usage.Acquire(p.Id)) await Assert.ThrowsAsync<InvalidOperationException>(() => service.PreviewAsync(p.Id, false));
        var plan = await service.PreviewAsync(p.Id, false);
        await File.WriteAllTextAsync(Path.Combine(ws.Paths.Game(p.Id), "new-file"), "changed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(plan, p.Name));
        Assert.True(Directory.Exists(ws.Paths.Profile(p.Id)));
    }
    [Fact] public async Task UnsafeArtifactCannotMakeDeletionRemoveSharedFiles()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync(); var p = ws.Profile(); await ws.Store.SaveProfileAsync(p);
        await ws.Store.RegisterArtifactAsync(new(p.Id, "cache/shared.jar", "export"));
        var service = new ProfileDeletionService(ws.Paths, ws.Store, new(ws.Paths));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.PreviewAsync(p.Id, true));
    }
    [Fact] public async Task InterruptedDeleteBeforeDatabaseCommitIsRecovered()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync(); var p = ws.Profile(); await ws.Store.SaveProfileAsync(p);
        var op = Guid.NewGuid(); var stage = SafePath.Join(ws.Paths.Root, $"trash/{op:N}"); Directory.CreateDirectory(stage);
        await AtomicFile.WriteJsonAsync(Path.Combine(stage, "deletion.json"), new {
            Operation = op, ProfileId = p.Id, Moves = new[] { new { Source = $"instances/{p.Id:N}", Destination = $"trash/{op:N}/item-0", Directory = true } }
        });
        Directory.Move(ws.Paths.Profile(p.Id), Path.Combine(stage, "item-0"));
        await new ProfileDeletionService(ws.Paths, ws.Store, new(ws.Paths)).RecoverAsync();
        Assert.True(Directory.Exists(ws.Paths.Profile(p.Id))); Assert.False(Directory.Exists(stage));
    }
}