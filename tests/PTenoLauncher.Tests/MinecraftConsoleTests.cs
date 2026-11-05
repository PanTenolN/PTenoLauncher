using System.Collections.Specialized;
using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using PTenoLauncher.Desktop;
using PTenoLauncher.Desktop.ViewModels;
using PTenoLauncher.Infrastructure.Diagnostics;
using PTenoLauncher.Testing;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class MinecraftConsoleTests
{
    [AvaloniaFact]
    public async Task OwnedProcessKeepsColoredFiltersAndPlainRedactedTranscriptDuringBursts()
    {
        using var ws = new TestWorkspace();
        using var owned = new OwnedTestRoot("console-burst");
        using var services = new LauncherServices(ws.Paths);
        await services.Store.InitializeAsync();
        var profile = ws.Profile();
        await services.Store.SaveProfileAsync(profile);
        var vm = new MainViewModel(services);
        await vm.InitializeAsync();
        vm.OpenProfileWorkspace(profile.Id);
        vm.ChooseWorkspaceSection(ProfileSection.Console);
        vm.ConsoleAutoScroll = false;
        var window = new MainWindow { DataContext = vm };
        window.Show();
        var script = """
            [Console]::WriteLine(([char]27) + '[31m[10:00:00] [main/INFO]: ready' + ([char]27) + '[0m');
            [Console]::WriteLine('[10:00:01] [main/WARN]: warn marker');
            [Console]::WriteLine('[10:00:02] [main/ERROR]: secret-for-console-555');
            [Console]::WriteLine('java.lang.IllegalStateException: failure');
            [Console]::WriteLine('    at a.b.Main.run(Main.java:10)');
            [Console]::WriteLine('unfamiliar WARN syntax');
            [Console]::WriteLine('[10:00:03] [main/DEBUG]: debug marker');
            [Console]::WriteLine('<log4j:Event logger="fixture" timestamp="0" level="ERROR" thread="Worker">');
            [Console]::WriteLine('<log4j:Message><![CDATA[full XML error');
            [Console]::WriteLine('  second message line secret-for-console-555]]></log4j:Message>');
            [Console]::WriteLine('<log4j:Throwable><![CDATA[java.lang.Error: XML failure');
            [Console]::WriteLine('    at a.b.Main.xml(Main.java:42)]]></log4j:Throwable>');
            [Console]::WriteLine('</log4j:Event>');
            foreach ($n in 1..4250) { [Console]::WriteLine("[10:00:04] [main/INFO]: burst $n") }
            """;
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var info = new ProcessStartInfo(shell) { WorkingDirectory = owned.Root };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            info.ArgumentList.Add(arg);
        var log = Path.Combine(owned.Root, "console.log");
        try
        {
            Assert.True(vm.ConsoleColored);
            var resets = 0;
            vm.VisibleConsoleRows.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };
            var exit = await vm.RunProfileConsoleAsync(profile.Id, (output, started, logOpened, ct) =>
                LiveProcessRunner.RunAsync(info, log, process =>
                {
                    owned.RegisterProcess(process);
                    logOpened(log);
                    started();
                    return Task.CompletedTask;
                }, output, ["secret-for-console-555"], ct, formatMinecraftLog: true), CancellationToken.None);
            Assert.Equal(0, exit);
            for (var i = 0; vm.VisibleConsoleRows.Count < 4000 && i < 500; i++) await Task.Delay(10);
            Assert.Equal(4000, vm.VisibleConsoleRows.Count);
            Assert.InRange(resets, 0, 1);
            Assert.Equal(ConsoleLevel.Normal, vm.VisibleConsoleRows[0].Level);
            Assert.Equal(ConsoleLevel.Warning, vm.VisibleConsoleRows[1].Level);
            Assert.All(vm.VisibleConsoleRows.Skip(2).Take(3), row => Assert.Equal(ConsoleLevel.Error, row.Level));
            Assert.Equal(ConsoleLevel.Normal, vm.VisibleConsoleRows[5].Level);
            Assert.Equal(ConsoleLevel.Verbose, vm.VisibleConsoleRows[6].Level);
            Assert.Contains("[00:00:00.000] [Worker/ERROR]: full XML error", vm.VisibleConsoleRows[7].Text);
            Assert.Contains("  second message line [REDACTED]", vm.VisibleConsoleRows[8].Text);
            Assert.Contains("    at a.b.Main.xml(Main.java:42)", vm.VisibleConsoleRows[10].Text);
            Assert.All(vm.VisibleConsoleRows.Skip(7).Take(4), row => Assert.Equal(ConsoleLevel.Error, row.Level));
            window.FindControl<ComboBox>("ProfileConsoleFilter")!.SelectedIndex = 1;
            await vm.ShowConsoleHistoryAsync(0);
            Assert.Equal(8, vm.VisibleConsoleRows.Count);
            vm.ConsoleFilterIndex = 2;
            Assert.Equal(7, vm.VisibleConsoleRows.Count);
            vm.ConsoleQuery = "failure";
            Assert.Equal(2, vm.VisibleConsoleLines.Count);
            vm.ConsoleQuery = null;
            Assert.Contains("[REDACTED]", string.Join("\n", vm.VisibleConsoleLines));
            vm.ClearProfileConsole();
            Assert.Empty(vm.VisibleConsoleRows);
            var transcript = Assert.IsType<string>(await vm.ReadFullConsoleAsync());
            Assert.Contains("burst 4250", transcript);
            Assert.True(transcript.IndexOf("full XML error", StringComparison.Ordinal) <
                transcript.IndexOf("burst 1\r", StringComparison.Ordinal));
            Assert.DoesNotContain("<log4j:Event", transcript, StringComparison.Ordinal);
            Assert.Contains("[REDACTED]", transcript);
            Assert.DoesNotContain("secret-for-console-555", transcript);
            Assert.DoesNotContain("\u001b", transcript, StringComparison.Ordinal);
            owned.Preserve("console.log");
        }
        finally { window.DataContext = null; window.Close(); }
    }
}
