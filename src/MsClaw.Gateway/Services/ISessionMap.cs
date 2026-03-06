namespace MsClaw.Gateway.Services;

/// <summary>
/// Maps caller keys to session identifiers for continued conversations.
/// </summary>
public interface ISessionMap
{
    /// <summary>
    /// Gets the session identifier for the specified caller key.
    /// </summary>
    string? GetSessionId(string callerKey);

    /// <summary>
    /// Stores the session identifier for the specified caller key.
    /// </summary>
    void SetSessionId(string callerKey, string sessionId);

    /// <summary>
    /// Lists all caller-to-session mappings currently tracked by the registry.
    /// </summary>
    IReadOnlyList<(string CallerKey, string SessionId)> ListCallers();
}
