namespace MsClaw.Gateway.Services;

/// <summary>
/// Enforces one active run at a time for each caller key.
/// </summary>
public interface IConcurrencyGate
{
    /// <summary>
    /// Attempts to acquire the caller's slot without waiting.
    /// </summary>
    bool TryAcquire(string callerKey);

    /// <summary>
    /// Releases the caller's previously acquired slot.
    /// </summary>
    void Release(string callerKey);
}
