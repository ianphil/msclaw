using GitHub.Copilot.SDK;

namespace MsClaw.Gateway.Hosting;

/// <summary>
/// Represents a testable session boundary over the Copilot SDK session.
/// </summary>
public interface IGatewaySession : IAsyncDisposable
{
    /// <summary>
    /// Gets the unique identifier for the session.
    /// </summary>
    string SessionId { get; }

    /// <summary>
    /// Subscribes to session events emitted by the underlying SDK session.
    /// </summary>
    IDisposable On(Action<SessionEvent> handler);

    /// <summary>
    /// Sends a message to the session.
    /// </summary>
    Task SendAsync(MessageOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aborts the active response for the session.
    /// </summary>
    Task AbortAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the recorded messages for the session.
    /// </summary>
    Task<IReadOnlyList<SessionEvent>> GetMessagesAsync(CancellationToken cancellationToken = default);
}
