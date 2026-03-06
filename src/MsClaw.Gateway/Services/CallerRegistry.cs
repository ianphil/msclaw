using System.Collections.Concurrent;

namespace MsClaw.Gateway.Services;

/// <summary>
/// Stores per-caller coordination primitives for gateway request handling.
/// </summary>
public sealed class CallerRegistry : IConcurrencyGate, ISessionMap
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> sessions = new(StringComparer.Ordinal);

    /// <summary>
    /// Attempts to acquire the caller's gate without blocking the current thread.
    /// </summary>
    public bool TryAcquire(string callerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerKey);
        var gate = gates.GetOrAdd(callerKey, static _ => new SemaphoreSlim(1, 1));

        return gate.Wait(0);
    }

    /// <summary>
    /// Releases the caller's gate after a completed or aborted run.
    /// </summary>
    public void Release(string callerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerKey);
        if (gates.TryGetValue(callerKey, out var gate) is false)
        {
            throw new InvalidOperationException($"No concurrency gate exists for caller '{callerKey}'.");
        }

        if (gate.CurrentCount > 0)
        {
            throw new InvalidOperationException($"Caller '{callerKey}' does not hold an acquired concurrency gate.");
        }

        gate.Release();
    }

    /// <summary>
    /// Gets the tracked session identifier for the specified caller key.
    /// </summary>
    public string? GetSessionId(string callerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerKey);

        return sessions.TryGetValue(callerKey, out var sessionId) ? sessionId : null;
    }

    /// <summary>
    /// Stores or replaces the tracked session identifier for the specified caller key.
    /// </summary>
    public void SetSessionId(string callerKey, string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        sessions[callerKey] = sessionId;
    }

    /// <summary>
    /// Lists the tracked caller-to-session mappings.
    /// </summary>
    public IReadOnlyList<(string CallerKey, string SessionId)> ListCallers()
    {
        return sessions
            .Select(static pair => (pair.Key, pair.Value))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToArray();
    }
}
