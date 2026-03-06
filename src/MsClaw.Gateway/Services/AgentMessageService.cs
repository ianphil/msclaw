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
    private readonly ISessionMap sessionMap;
    private readonly IGatewayClient client;
    private readonly IGatewayHostedService hostedService;

    /// <summary>
    /// Initializes the service with the shared coordination, session, and hosting dependencies.
    /// </summary>
    public AgentMessageService(
        IConcurrencyGate concurrencyGate,
        ISessionMap sessionMap,
        IGatewayClient client,
        IGatewayHostedService hostedService)
    {
        this.concurrencyGate = concurrencyGate;
        this.sessionMap = sessionMap;
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
            await using var session = await GetOrCreateSessionAsync(callerKey, cancellationToken);
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

    /// <summary>
    /// Resolves the session for the caller, creating one when needed.
    /// </summary>
    private async Task<IGatewaySession> GetOrCreateSessionAsync(string callerKey, CancellationToken cancellationToken)
    {
        var existingSessionId = sessionMap.GetSessionId(callerKey);
        if (string.IsNullOrWhiteSpace(existingSessionId) is false)
        {
            return await client.ResumeSessionAsync(existingSessionId, cancellationToken: cancellationToken);
        }

        var session = await client.CreateSessionAsync(CreateSessionConfig(), cancellationToken);
        sessionMap.SetSessionId(callerKey, session.SessionId);

        return session;
    }

    /// <summary>
    /// Creates the session configuration for newly created gateway sessions.
    /// </summary>
    private SessionConfig CreateSessionConfig()
    {
        var sessionConfig = new SessionConfig();
        if (string.IsNullOrWhiteSpace(hostedService.SystemMessage))
        {
            return sessionConfig;
        }

        sessionConfig.SystemMessage = new SystemMessageConfig
        {
            Mode = SystemMessageMode.Append,
            Content = hostedService.SystemMessage
        };

        return sessionConfig;
    }
}
