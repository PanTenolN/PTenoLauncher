using System.Diagnostics;
using System.Text.Json;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Authentication;
using PTenoLauncher.Infrastructure.Diagnostics;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Security;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;

namespace PTenoLauncher.Tests;
public sealed class Core020Tests
{
    [Fact] public async Task LegacyArgumentsPreserveTokensAndQuotedValues()
    {
        Assert.Equal(new[] { "--username", "two words", "--version", "1.12.2" },
            LegacyArguments.Split("--username \"two words\" --version 1.12.2"));
        Assert.Throws<InvalidDataException>(() => LegacyArguments.Split("\"unterminated"));
        using var reader = new StringReader(new string('x', 20000) + "\nnormal\n");
        var lines = new List<string>();
        await foreach (var line in LiveProcessRunner.ReadBoundedLinesAsync(reader)) lines.Add(line);
        Assert.Equal(2, lines.Count); Assert.True(lines[0].Length < 8300); Assert.EndsWith("[truncated]", lines[0]);
        Assert.Equal("normal", lines[1]);
    }
    [Fact] public void JavaArchitectureCheckRejectsOtherMachineTypesAndMalformedFiles()
    {
        using var ws = new TestWorkspace(); var path = Path.Combine(ws.Paths.Root, "java.exe");
        byte[] data = new byte[256]; data[0] = 0x4d; data[1] = 0x5a; data[0x3c] = 0x80;
        data[0x80] = 0x50; data[0x81] = 0x45; data[0x84] = 0x64; data[0x85] = 0x86;
        File.WriteAllBytes(path, data); Assert.True(JavaRuntimeManager.IsX64Executable(path));
        data[0x84] = 0x4c; data[0x85] = 0x01; File.WriteAllBytes(path, data); Assert.False(JavaRuntimeManager.IsX64Executable(path));
        File.WriteAllBytes(path, [1, 2]); Assert.False(JavaRuntimeManager.IsX64Executable(path));
    }
    [Fact] public async Task MultiAccountSelectionAndTargetedLogoutPreserveOtherAccount()
    {
        using var ws = new TestWorkspace(); var vault = new SecretVault(ws.Paths);
        var a = new MinecraftSession(new(new string('a',32), "First", DateTimeOffset.UtcNow), "secret-first", DateTimeOffset.UtcNow.AddHours(1));
        var b = new MinecraftSession(new(new string('b',32), "Second", DateTimeOffset.UtcNow), "secret-second", DateTimeOffset.UtcNow.AddHours(1));
        // Synthetic protected credentials for persistence tests only. No game/auth API is invoked.
        vault.Write("minecraft-accounts", JsonSerializer.SerializeToUtf8Bytes(new
        {
            ActiveUuid = a.Account.Uuid, Accounts = new[] { new { MicrosoftId = "ms-a", Session = a }, new { MicrosoftId = "ms-b", Session = b } }
        }, AtomicFile.JsonOptions));
        using var http = new HttpClient(new StubHandler(_ => throw new Exception("No network")));
        var auth = new MicrosoftAuthentication(vault, http, new() { Offline = true });
        Assert.Equal("First", auth.GetCachedSession()!.Account.Name);
        await auth.SelectAccountAsync(b.Account.Uuid); Assert.Equal("Second", auth.GetCachedSession()!.Account.Name);
        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.SelectAccountAsync(new string('c',32)));
        await auth.SignOutAsync(); Assert.Equal("First", auth.GetCachedSession()!.Account.Name);
        Assert.Single(auth.GetAccounts());
        await auth.SignOutAllAsync(); Assert.Null(auth.GetCachedSession()); Assert.Empty(auth.GetAccounts());
        Assert.DoesNotContain("secret-first", a.ToString());
    }
    [Fact] public async Task AStillRunningRecordedProcessBlocksDeletionEvenWithoutWorldSessionLock()
    {
        using var ws = new TestWorkspace(); await ws.Store.InitializeAsync(); var p = ws.Profile(); await ws.Store.SaveProfileAsync(p);
        using var current = Process.GetCurrentProcess();
        await AtomicFile.WriteJsonAsync(Path.Combine(ws.Paths.Profile(p.Id), "active-game.json"),
            new ActiveGameRecord(current.Id, current.StartTime.ToUniversalTime().Ticks, "synthetic-test"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ProfileDeletionService(ws.Paths, ws.Store, new(ws.Paths)).PreviewAsync(p.Id, false));
    }
}