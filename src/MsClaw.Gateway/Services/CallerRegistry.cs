using System.Collections.Concurrent;

namespace MsClaw.Gateway.Services;

/// <summary>
/// Stores per-caller concurrency gates for gateway request handling.
/// </summary>
public sealed class CallerRegistry : IConcurrencyGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.Ordinal);

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
    /// Releases the caller's gate if it is currently held. Safe to call when the gate may have already been released.
    /// </summary>
    public bool TryRelease(string callerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerKey);
        if (gates.TryGetValue(callerKey, out var gate) is false)
        {
            return false;
        }

        if (gate.CurrentCount > 0)
        {
            return false;
        }

        gate.Release();

        return true;
    }
}
