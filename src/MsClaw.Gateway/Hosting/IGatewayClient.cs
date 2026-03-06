using GitHub.Copilot.SDK;

namespace MsClaw.Gateway.Hosting;

/// <summary>
/// Represents a testable client boundary over the Copilot SDK client.
/// </summary>
public interface IGatewayClient : IAsyncDisposable
{
    /// <summary>
    /// Starts the underlying Copilot SDK client.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new gateway session.
    /// </summary>
    Task<IGatewaySession> CreateSessionAsync(SessionConfig? config = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes an existing gateway session.
    /// </summary>
    Task<IGatewaySession> ResumeSessionAsync(string sessionId, ResumeSessionConfig? config = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the sessions tracked by the underlying client.
    /// </summary>
    Task<IReadOnlyList<SessionMetadata>> ListSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the specified session from the underlying client.
    /// </summary>
    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}
