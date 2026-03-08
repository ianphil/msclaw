using GitHub.Copilot.SDK;

namespace MsClaw.Gateway.Hosting;

/// <summary>
/// Delegates session operations to the client managed by the gateway hosted service.
/// </summary>
public sealed class GatewayClientProxy(GatewayHostedService hostedService) : IGatewayClient
{
    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<IGatewaySession> CreateSessionAsync(SessionConfig? config = null, CancellationToken cancellationToken = default)
        => hostedService.GetClientOrThrow().CreateSessionAsync(config, cancellationToken);

    /// <inheritdoc/>
    public Task<IGatewaySession> ResumeSessionAsync(string sessionId, ResumeSessionConfig? config = null, CancellationToken cancellationToken = default)
        => hostedService.GetClientOrThrow().ResumeSessionAsync(sessionId, config, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<SessionMetadata>> ListSessionsAsync(CancellationToken cancellationToken = default)
        => hostedService.GetClientOrThrow().ListSessionsAsync(cancellationToken);

    /// <inheritdoc/>
    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        => hostedService.GetClientOrThrow().DeleteSessionAsync(sessionId, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
        => hostedService.DisposeAsync();
}
