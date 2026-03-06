using System.Runtime.CompilerServices;
using GitHub.Copilot.SDK;
using MsClaw.Gateway.Hosting;

namespace MsClaw.Gateway.Services;

/// <summary>
/// Coordinates gateway message delivery across concurrency, sessions, and streaming.
/// </summary>
public sealed class AgentMessageService
{
    private readonly IConcurrencyGate concurrencyGate;
    private readonly ISessionPool sessionPool;
    private readonly IGatewayClient client;
    private readonly IGatewayHostedService hostedService;

    /// <summary>
    /// Initializes the service with the shared coordination, session pool, and hosting dependencies.
    /// </summary>
    public AgentMessageService(
        IConcurrencyGate concurrencyGate,
        ISessionPool sessionPool,
        IGatewayClient client,
        IGatewayHostedService hostedService)
    {
        this.concurrencyGate = concurrencyGate;
        this.sessionPool = sessionPool;
        this.client = client;
        this.hostedService = hostedService;
    }

    /// <summary>
    /// Sends a prompt for the specified caller and streams the resulting SDK events.
    /// </summary>
    public async IAsyncEnumerable<SessionEvent> SendAsync(
        string callerKey,
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        if (concurrencyGate.TryAcquire(callerKey) is false)
        {
            throw new InvalidOperationException($"Caller '{callerKey}' already has an active run.");
        }

        try
        {
            var session = await sessionPool.GetOrCreateAsync(callerKey, async ct =>
            {
                var sessionConfig = new SessionConfig { Streaming = true };
                if (string.IsNullOrWhiteSpace(hostedService.SystemMessage) is false)
                {
                    sessionConfig.SystemMessage = new SystemMessageConfig
                    {
                        Mode = SystemMessageMode.Append,
                        Content = hostedService.SystemMessage
                    };
                }

                return await client.CreateSessionAsync(sessionConfig, ct);
            }, cancellationToken);

            var (subscription, events) = SessionEventBridge.Bridge(session, cancellationToken);
            try
            {
                await session.SendAsync(new MessageOptions { Prompt = prompt }, cancellationToken);
                await foreach (var sessionEvent in events.WithCancellation(cancellationToken))
                {
                    yield return sessionEvent;
                }
            }
            finally
            {
                subscription.Dispose();
            }
        }
        finally
        {
            concurrencyGate.Release(callerKey);
        }
    }
}
