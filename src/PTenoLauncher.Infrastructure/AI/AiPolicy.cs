namespace PTenoLauncher.Infrastructure.AI;

/// <summary>All future AI providers must enter this gate and use its cancellation token.
/// Core downloads, metadata resolution and existing translation files do not depend on this service.</summary>
public sealed class AiPolicy
{
    private readonly object sync = new();
    private bool enabled;
    private readonly HashSet<CancellationTokenSource> active = [];
    public bool Enabled { get { lock (sync) return enabled; } }
    public void SetEnabled(bool value)
    {
        lock (sync)
        {
            enabled = value;
            if (!value) foreach (var operation in active) operation.Cancel();
        }
    }
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        CancellationTokenSource linked;
        lock (sync)
        {
            if (!enabled) throw new InvalidOperationException("AI-функції вимкнено.");
            linked = CancellationTokenSource.CreateLinkedTokenSource(ct); active.Add(linked);
        }
        try { linked.Token.ThrowIfCancellationRequested(); return await operation(linked.Token); }
        finally { lock (sync) active.Remove(linked); linked.Dispose(); }
    }
}
public sealed class AiRequestGateHandler(AiPolicy policy, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        policy.ExecuteAsync(ct => base.SendAsync(request, ct), cancellationToken);
}