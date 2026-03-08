using System.Collections.Concurrent;
using MsClaw.Gateway.Hosting;

namespace MsClaw.Gateway.Services;

/// <summary>
/// Holds live gateway sessions keyed by caller for reuse across messages.
/// Sessions inactive beyond the configured timeout are reaped automatically.
/// </summary>
public sealed class SessionPool : ISessionPool
{
    private readonly ConcurrentDictionary<string, TrackedSession> sessions = new(StringComparer.Ordinal);
    private readonly TimeSpan sessionTimeout;
    private readonly Timer reapTimer;

    /// <summary>
    /// Creates a session pool with the specified inactivity timeout and reap interval.
    /// </summary>
    public SessionPool(TimeSpan? sessionTimeout = null, TimeSpan? reapInterval = null)
    {
        this.sessionTimeout = sessionTimeout ?? TimeSpan.FromMinutes(30);
        var interval = reapInterval ?? TimeSpan.FromMinutes(5);
        reapTimer = new Timer(_ => ReapExpiredSessions(), null, interval, interval);
    }

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

        if (sessions.TryGetValue(callerKey, out var tracked))
        {
            tracked.Touch();
            return tracked.Session;
        }

        var session = await factory(cancellationToken);
        sessions[callerKey] = new TrackedSession(session);

        return session;
    }

    /// <summary>
    /// Returns the pooled session for the caller, or null when none is tracked.
    /// </summary>
    public IGatewaySession? TryGet(string callerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerKey);

        if (sessions.TryGetValue(callerKey, out var tracked))
        {
            tracked.Touch();
            return tracked.Session;
        }

        return null;
    }

    /// <summary>
    /// Disposes and removes the session for the caller. No-op when no session is tracked.
    /// </summary>
    public async Task RemoveAsync(string callerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerKey);

        if (sessions.TryRemove(callerKey, out var tracked))
        {
            await tracked.Session.DisposeAsync();
        }
    }

    /// <summary>
    /// Lists all caller-to-session mappings currently held in the pool.
    /// </summary>
    public IReadOnlyList<(string CallerKey, string SessionId)> ListCallers()
    {
        return sessions
            .Select(static pair => (pair.Key, pair.Value.Session.SessionId))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Disposes all tracked sessions, stops the reap timer, and clears the pool.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await reapTimer.DisposeAsync();

        foreach (var pair in sessions)
        {
            await pair.Value.Session.DisposeAsync();
        }

        sessions.Clear();
    }

    private void ReapExpiredSessions()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in sessions)
        {
            if (now - pair.Value.LastAccessed > sessionTimeout
                && sessions.TryRemove(pair.Key, out var tracked))
            {
                tracked.Session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }

    private sealed class TrackedSession(IGatewaySession session)
    {
        public IGatewaySession Session { get; } = session;

        public DateTimeOffset LastAccessed { get; private set; } = DateTimeOffset.UtcNow;

        public void Touch() => LastAccessed = DateTimeOffset.UtcNow;
    }
}
