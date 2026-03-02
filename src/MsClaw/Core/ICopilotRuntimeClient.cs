namespace MsClaw.Core;

public interface ICopilotRuntimeClient : IAsyncDisposable
{
    /// <summary>
    /// Creates a new SDK session. Returns the session ID.
    /// </summary>
    Task<string> CreateSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a single message to an existing session. Returns the assistant's response.
    /// The SDK maintains conversation history internally.
    /// </summary>
    Task<string> SendMessageAsync(
        string sessionId,
        string message,
        CancellationToken cancellationToken = default);
}
