using GitHub.Copilot.SDK;

namespace MsClaw.Gateway.Hosting;

/// <summary>
/// Adapts a Copilot SDK session to the gateway session boundary.
/// </summary>
public sealed class CopilotGatewaySession(CopilotSession session) : IGatewaySession
{
    /// <summary>
    /// Gets the unique identifier for the wrapped Copilot session.
    /// </summary>
    public string SessionId => session.SessionId;

    /// <summary>
    /// Subscribes to wrapped session events using the gateway session contract.
    /// </summary>
    public IDisposable On(Action<SessionEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return session.On(evt => handler(evt));
    }

    /// <summary>
    /// Sends a message through the wrapped Copilot session.
    /// </summary>
    public async Task SendAsync(MessageOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        _ = await session.SendAsync(options, cancellationToken);
    }

    /// <summary>
    /// Aborts the active response on the wrapped Copilot session.
    /// </summary>
    public Task AbortAsync(CancellationToken cancellationToken = default)
    {
        return session.AbortAsync(cancellationToken);
    }

    /// <summary>
    /// Retrieves recorded session events from the wrapped Copilot session.
    /// </summary>
    public Task<IReadOnlyList<SessionEvent>> GetMessagesAsync(CancellationToken cancellationToken = default)
    {
        return session.GetMessagesAsync(cancellationToken);
    }

    /// <summary>
    /// Disposes the wrapped Copilot session asynchronously.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        return session.DisposeAsync();
    }
}
