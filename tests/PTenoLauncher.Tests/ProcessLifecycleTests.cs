using System.Diagnostics;
using System.Text;
using PTenoLauncher.Infrastructure.Diagnostics;
using Xunit;

namespace PTenoLauncher.Tests;

public sealed class ProcessLifecycleTests
{
    private static string PowerShell => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
    private static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    private static ProcessStartInfo Start(string script)
    {
        var info = new ProcessStartInfo(PowerShell);
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Encode(script) }) info.ArgumentList.Add(argument);
        return info;
    }
    private sealed class Sink(Action<string> receive) : IProgress<string> { public void Report(string value) => receive(value); }

    [Fact]
    public async Task RealProcessRecordsBothChannelsRedactsSecretsAndReturnsNonzeroExit()
    {
        using var ws = new TestWorkspace();
        var log = Path.Combine(ws.Paths.Root, "process.log"); var lines = new List<string>();
        var exit = await LiveProcessRunner.RunAsync(Start("[Console]::WriteLine('synthetic-secret-12345'); [Console]::Error.WriteLine('stderr-marker'); exit 7"),
            log, output: new Sink(lines.Add), secrets: ["synthetic-secret-12345"]);
        Assert.Equal(7, exit);
        var content = await File.ReadAllTextAsync(log);
        Assert.Contains("[out] [REDACTED]", content); Assert.Contains("[err] stderr-marker", content);
        Assert.Contains("Exit code: 7", content); Assert.DoesNotContain("synthetic-secret-12345", content);
        Assert.DoesNotContain(lines, line => line.Contains("synthetic-secret-12345", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancellationStopsRealParentAndChildAndFlushesExitRecord()
    {
        using var ws = new TestWorkspace(); using var stop = new CancellationTokenSource();
        var childReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executable = PowerShell.Replace("'", "''", StringComparison.Ordinal);
        var script = "$child = Start-Process -FilePath '" + executable + "' -ArgumentList '-NoProfile','-NonInteractive','-EncodedCommand','" +
            Encode("Start-Sleep -Seconds 30") + "' -WindowStyle Hidden -PassThru; [Console]::WriteLine('child=' + $child.Id); $child.WaitForExit()";
        int parentId = 0;
        var log = Path.Combine(ws.Paths.Root, "cancel.log");
        var running = LiveProcessRunner.RunAsync(Start(script), log, process => { parentId = process.Id; return Task.CompletedTask; },
            new Sink(line => { int i = line.IndexOf("child=", StringComparison.Ordinal); if (i >= 0) childReady.TrySetResult(int.Parse(line[(i + 6)..].Trim())); }), ct: stop.Token);
        Process? child = null;
        try
        {
            var childId = await childReady.Task.WaitAsync(TimeSpan.FromSeconds(15));
            child = Process.GetProcessById(childId); using var parent = Process.GetProcessById(parentId);
            Assert.False(parent.HasExited); Assert.False(child.HasExited);
            await stop.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(15)));
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(parent.HasExited); Assert.True(child.HasExited);
            var text = await File.ReadAllTextAsync(log);
            Assert.Contains("explicit cancellation request", text); Assert.Contains("Exit code:", text);
        }
        finally
        {
            await stop.CancelAsync();
            try { await running.WaitAsync(TimeSpan.FromSeconds(15)); } catch (OperationCanceledException) { }
            if (child is not null) { if (!child.HasExited) child.Kill(true); child.Dispose(); }
        }
    }

    [Fact]
    public async Task FailedConsoleConsumerTerminatesOwnedProcessInsteadOfHanging()
    {
        using var ws = new TestWorkspace(); int pid = 0;
        var running = LiveProcessRunner.RunAsync(Start("[Console]::WriteLine('consumer-probe'); Start-Sleep -Seconds 30"),
            Path.Combine(ws.Paths.Root, "sink-failure.log"), process => { pid = process.Id; return Task.CompletedTask; },
            new Sink(_ => throw new IOException("synthetic consumer failure")));
        await Assert.ThrowsAsync<IOException>(() => running.WaitAsync(TimeSpan.FromSeconds(15)));
        try { using var process = Process.GetProcessById(pid); Assert.True(process.HasExited); }
        catch (ArgumentException) { /* The terminated process has already left the process table. */ }
    }

    [Fact]
    public async Task AlreadyCanceledRequestCreatesNeitherProcessNorLog()
    {
        using var ws = new TestWorkspace(); using var stop = new CancellationTokenSource(); await stop.CancelAsync();
        var path = Path.Combine(ws.Paths.Root, "must-not-exist.log"); bool started = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LiveProcessRunner.RunAsync(Start("exit 0"), path,
            _ => { started = true; return Task.CompletedTask; }, ct: stop.Token));
        Assert.False(started); Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task DiagnosticFailureDoesNotLoseProcessExitOrLeakExceptionMessage()
    {
        using var ws = new TestWorkspace(); var path = Path.Combine(ws.Paths.Root, "diagnostics.log");
        Assert.Equal(0, await LiveProcessRunner.RunAsync(Start("exit 0"), path,
            _ => throw new IOException("private-unlogged-detail")));
        var text = await File.ReadAllTextAsync(path);
        Assert.Contains("Startup diagnostics: IOException", text); Assert.Contains("Exit code: 0", text);
        Assert.DoesNotContain("private-unlogged-detail", text);
    }

    private sealed class WaitingReader : TextReader
    {
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
    }

    [Fact]
    public async Task BoundedLineReaderCancelsAnIdlePipe()
    {
        using var reader = new WaitingReader(); using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        { await foreach (var _ in LiveProcessRunner.ReadBoundedLinesAsync(reader, ct: stop.Token)) { } });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task BoundedLineReaderRejectsInvalidLimits(int limit)
    {
        using var reader = new StringReader("data");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        { await foreach (var _ in LiveProcessRunner.ReadBoundedLinesAsync(reader, limit)) { } });
    }
}
