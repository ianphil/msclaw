using MsClaw.Gateway.Hosting;

namespace MsClaw.Gateway.Services;

/// <summary>
/// Manages live gateway sessions keyed by caller, replacing per-message create/resume/dispose.
/// </summary>
public interface ISessionPool : IAsyncDisposable
{
    /// <summary>
    /// Returns the pooled session for the caller, creating one via the factory when none exists.
    /// </summary>
    Task<IGatewaySession> GetOrCreateAsync(string callerKey, Func<CancellationToken, Task<IGatewaySession>> factory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the pooled session for the caller without creating one, or null when none is tracked.
    /// </summary>
    IGatewaySession? TryGet(string callerKey);

    /// <summary>
    /// Disposes and removes the session for the caller. No-op when no session is tracked.
    /// </summary>
    Task RemoveAsync(string callerKey);

    /// <summary>
    /// Lists all caller-to-session mappings currently held in the pool.
    /// </summary>
    IReadOnlyList<(string CallerKey, string SessionId)> ListCallers();
}
