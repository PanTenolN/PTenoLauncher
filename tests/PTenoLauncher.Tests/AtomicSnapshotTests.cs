using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PTenoLauncher.Infrastructure.Storage;
using Xunit;
namespace PTenoLauncher.Tests;

public sealed class AtomicSnapshotTests
{
    private sealed class NonPumpingContext : SynchronizationContext
    { public override void Post(SendOrPostCallback callback, object? state) { /* Models a UI thread synchronously using an infrastructure API. */ } }
    [Fact] public void SnapshotReadDoesNotRequireTheCallersUiContext()
    {
        using var ws = new TestWorkspace(); var path = SafePath.Join(ws.Paths.Root, "ui.json"); AtomicFile.Write(path, Encoding.UTF8.GetBytes("{\"value\":1}"));
        Exception? error = null; int observed = 0;
        var thread = new Thread(() => {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingContext());
            try { observed = AtomicFile.ReadJsonAsync<Snapshot>(path).GetAwaiter().GetResult()!.Value; }
            catch (Exception e) { error = e; }
        }) { IsBackground = true };
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "Infrastructure snapshot read captured a blocked UI context.");
        Assert.Null(error); Assert.Equal(1, observed);
    }
    [Fact] public void LegacyWindowsReaderReproducesJournalSharingViolation()
    {
        if (!OperatingSystem.IsWindows()) return; // This is specifically the Win32 sharing contract.
        using var ws = new TestWorkspace(); var path = SafePath.Join(ws.Paths.Root, "helper-status.json"); AtomicFile.Write(path, [1]);
        using (var legacy = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failure = Record.Exception(() => AtomicFile.Write(path, [2]));
            Assert.True(failure is IOException or UnauthorizedAccessException);
            Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(path));
        }
        AtomicFile.Write(path, [2]); Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(path));
    }
    [JsonConverter(typeof(SnapshotConverter))]
    public sealed record Snapshot(int Value);
    public sealed class SnapshotConverter : JsonConverter<Snapshot>
    {
        public static readonly AsyncLocal<Action?> DuringRead = new();
        public override Snapshot Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            DuringRead.Value?.Invoke(); // Interpretation must not retain a handle that prevents replacing the metadata pathname.
            return new(document.RootElement.GetProperty("value").GetInt32());
        }
        public override void Write(Utf8JsonWriter writer, Snapshot value, JsonSerializerOptions options) => throw new NotSupportedException();
    }
    [Fact] public async Task OpenJsonSnapshotDoesNotBlockAtomicJournalReplacement()
    {
        using var ws = new TestWorkspace(); var path = SafePath.Join(ws.Paths.Root, "helper-status.json");
        AtomicFile.Write(path, Encoding.UTF8.GetBytes("{\"value\":1}"));
        SnapshotConverter.DuringRead.Value = () => AtomicFile.Write(path, Encoding.UTF8.GetBytes("{\"value\":2}"));
        try { Assert.Equal(1, (await AtomicFile.ReadJsonAsync<Snapshot>(path))!.Value); }
        finally { SnapshotConverter.DuringRead.Value = null; }
        Assert.Equal(2, (await AtomicFile.ReadJsonAsync<Snapshot>(path))!.Value);
    }
    [Fact] public async Task WaitingForMetadataGateIsCancelableAndDoesNotWrite()
    {
        using var ws = new TestWorkspace(); var path = SafePath.Join(ws.Paths.Root, "transaction.json");
        AtomicFile.Write(path, Encoding.UTF8.GetBytes("{\"value\":1}"));
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var holder = Task.Run(() => { using var gate = new Mutex(false, AtomicFile.GateName(path)); gate.WaitOne(); entered.Set(); try { release.Wait(TimeSpan.FromSeconds(10)); } finally { gate.ReleaseMutex(); } });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        try { using var cancel = new CancellationTokenSource(50); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AtomicFile.ReadBytesAsync(path, 100, cancel.Token)); }
        finally { release.Set(); await holder; }
        Assert.Equal(1, (await AtomicFile.ReadJsonAsync<Snapshot>(path))!.Value);
    }
    [Fact] public async Task BoundedMetadataSnapshotRejectsOversizeAndCancellation()
    {
        using var ws = new TestWorkspace(); var path = SafePath.Join(ws.Paths.Root, "transaction.json"); AtomicFile.Write(path, [1, 2, 3]);
        Assert.Equal(new byte[] { 1, 2, 3 }, await AtomicFile.ReadBytesAsync(path, 3));
        await Assert.ThrowsAsync<InvalidDataException>(() => AtomicFile.ReadBytesAsync(path, 2));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AtomicFile.ReadBytesAsync(path, 3, cancel.Token));
    }
}
