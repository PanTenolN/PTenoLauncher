using System.Diagnostics;
using PTenoLauncher.Infrastructure.Diagnostics;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed class VanillaServerSession
{
    private readonly SemaphoreSlim inputGate = new(1, 1);
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private StreamWriter? input;
    internal LocalVanillaServer Server { get; }
    internal SemaphoreSlim PlayerGate { get; } = new(1, 1);
    public Task<bool> Ready => ready.Task;
    public Task<int> Completion { get; }

    internal VanillaServerSession(LocalVanillaServer server, ProcessStartInfo info, string log, string activeFile, IDisposable lease, IProgress<string>? output)
    {
        Server = server;
        Completion = RunAsync(info, log, activeFile, lease, output);
    }

    private async Task<int> RunAsync(ProcessStartInfo info, string log, string activeFile, IDisposable lease, IProgress<string>? output)
    {
        using (lease)
        {
            var launched = false;
            try
            {
                info.RedirectStandardInput = true;
                return await LiveProcessRunner.RunAsync(info, log, async process =>
                {
                    launched = true;
                    await AtomicFile.WriteJsonAsync(activeFile,
                        new ActiveGameRecord(process.Id, process.StartTime.ToUniversalTime().Ticks, info.FileName));
                    input = process.StandardInput;
                    ready.TrySetResult(true);
                }, output, requireStartupCallback: true);
            }
            finally
            {
                ready.TrySetResult(false);
                await inputGate.WaitAsync();
                try
                {
                    input = null;
                    if (!launched)
                    {
                        SafePath.RejectLinks(activeFile);
                        File.Delete(activeFile);
                    }
                }
                finally { inputGate.Release(); }
            }
        }
    }

    public async Task SendAsync(string command, CancellationToken ct = default)
    {
        if (command.Any(character => char.IsControl(character) || character is '\u2028' or '\u2029'))
            throw new ArgumentException("ServerInvalidCommand");
        command = command.Trim();
        if (command.StartsWith('/')) command = command[1..].TrimStart();
        if (command.Length is 0 or > 500) throw new ArgumentException("ServerInvalidCommand");
        await inputGate.WaitAsync(ct);
        try
        {
            if (input is not { } stream) throw new InvalidOperationException("ServerNotRunning");
            await stream.WriteLineAsync(command.AsMemory(), ct);
            await stream.FlushAsync(ct);
        }
        finally { inputGate.Release(); }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!Completion.IsCompleted) await SendAsync("stop", ct);
        await Completion.WaitAsync(TimeSpan.FromSeconds(45), ct);
    }
}
