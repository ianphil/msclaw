using System.Collections.Concurrent;
using MsClaw.Gateway.Hosting;

namespace MsClaw.Gateway.Services;

/// <summary>
/// Holds live gateway sessions keyed by caller for reuse across messages.
/// </summary>
public sealed class SessionPool : ISessionPool
{
    private readonly ConcurrentDictionary<string, IGatewaySession> sessions = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the pooled session for the caller, invoking the factory when no session is tracked.
    /// </summary>
    public async Task<IGatewaySession> GetOrCreateAsync(
        string callerKey,
        Func<CancellationToken, Task<IGatewaySession>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerKey);
        ArgumentNullException.ThrowIfNull(factory);

        if (sessions.TryGetValue(callerKey, out var existing))
        {
            return existing;
        }

        var session = await factory(cancellationToken);
        sessions[callerKey] = session;

        return session;
    }

    /// <summary>
    /// Returns the pooled session for the caller, or null when none is tracked.
    /// </summary>
    public IGatewaySession? TryGet(string callerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerKey);

        return sessions.TryGetValue(callerKey, out var session) ? session : null;
    }

    /// <summary>
    /// Disposes and removes the session for the caller. No-op when no session is tracked.
    /// </summary>
    public async Task RemoveAsync(string callerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerKey);

        if (sessions.TryRemove(callerKey, out var session))
        {
            await session.DisposeAsync();
        }
    }

    /// <summary>
    /// Lists all caller-to-session mappings currently held in the pool.
    /// </summary>
    public IReadOnlyList<(string CallerKey, string SessionId)> ListCallers()
    {
        return sessions
            .Select(static pair => (pair.Key, pair.Value.SessionId))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Disposes all tracked sessions and clears the pool.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var pair in sessions)
        {
            await pair.Value.DisposeAsync();
        }

        sessions.Clear();
    }
}
