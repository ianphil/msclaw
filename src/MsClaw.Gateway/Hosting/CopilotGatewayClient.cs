using GitHub.Copilot.SDK;

namespace MsClaw.Gateway.Hosting;

/// <summary>
/// Adapts the Copilot SDK client to the gateway client boundary.
/// </summary>
public sealed class CopilotGatewayClient : IGatewayClient
{
    private readonly Func<CancellationToken, Task> startAsync;
    private readonly Func<SessionConfig?, CancellationToken, Task<IGatewaySession>> createSessionAsync;
    private readonly Func<string, ResumeSessionConfig?, CancellationToken, Task<IGatewaySession>> resumeSessionAsync;
    private readonly Func<CancellationToken, Task<IReadOnlyList<SessionMetadata>>> listSessionsAsync;
    private readonly Func<string, CancellationToken, Task> deleteSessionAsync;
    private readonly Func<ValueTask> disposeAsync;

    /// <summary>
    /// Initializes the gateway client wrapper around a Copilot SDK client.
    /// </summary>
    public CopilotGatewayClient(CopilotClient client)
        : this(
            startAsync: client.StartAsync,
            createSessionAsync: async (config, cancellationToken) => new CopilotGatewaySession(
                await client.CreateSessionAsync(config ?? new SessionConfig(), cancellationToken)),
            resumeSessionAsync: async (sessionId, config, cancellationToken) => new CopilotGatewaySession(
                await client.ResumeSessionAsync(sessionId, config ?? new ResumeSessionConfig(), cancellationToken)),
            listSessionsAsync: async cancellationToken => await client.ListSessionsAsync(new SessionListFilter(), cancellationToken),
            deleteSessionAsync: client.DeleteSessionAsync,
            disposeAsync: client.DisposeAsync)
    {
        ArgumentNullException.ThrowIfNull(client);
    }

    /// <summary>
    /// Initializes the gateway client wrapper with delegates for targeted unit testing.
    /// </summary>
    public CopilotGatewayClient(
        Func<CancellationToken, Task> startAsync,
        Func<SessionConfig?, CancellationToken, Task<IGatewaySession>> createSessionAsync,
        Func<string, ResumeSessionConfig?, CancellationToken, Task<IGatewaySession>> resumeSessionAsync,
        Func<CancellationToken, Task<IReadOnlyList<SessionMetadata>>> listSessionsAsync,
        Func<string, CancellationToken, Task> deleteSessionAsync,
        Func<ValueTask> disposeAsync)
    {
        this.startAsync = startAsync;
        this.createSessionAsync = createSessionAsync;
        this.resumeSessionAsync = resumeSessionAsync;
        this.listSessionsAsync = listSessionsAsync;
        this.deleteSessionAsync = deleteSessionAsync;
        this.disposeAsync = disposeAsync;
    }

    /// <summary>
    /// Starts the wrapped Copilot SDK client.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return startAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a session through the wrapped Copilot SDK client.
    /// </summary>
    public Task<IGatewaySession> CreateSessionAsync(SessionConfig? config = null, CancellationToken cancellationToken = default)
    {
        return createSessionAsync(config, cancellationToken);
    }

    /// <summary>
    /// Resumes a session through the wrapped Copilot SDK client.
    /// </summary>
    public Task<IGatewaySession> ResumeSessionAsync(string sessionId, ResumeSessionConfig? config = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return resumeSessionAsync(sessionId, config, cancellationToken);
    }

    /// <summary>
    /// Lists sessions known to the wrapped Copilot SDK client.
    /// </summary>
    public Task<IReadOnlyList<SessionMetadata>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        return listSessionsAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes a session through the wrapped Copilot SDK client.
    /// </summary>
    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return deleteSessionAsync(sessionId, cancellationToken);
    }

    /// <summary>
    /// Disposes the wrapped Copilot SDK client asynchronously.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        return disposeAsync();
    }
}
