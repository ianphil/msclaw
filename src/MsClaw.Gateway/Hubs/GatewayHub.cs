using System.Runtime.CompilerServices;
using GitHub.Copilot.SDK;
using Microsoft.AspNetCore.SignalR;
using MsClaw.Gateway.Hosting;
using MsClaw.Gateway.Services;

namespace MsClaw.Gateway.Hubs;

/// <summary>
/// Routes SignalR gateway operations to the underlying message and session services.
/// </summary>
public sealed class GatewayHub(AgentMessageService messageService, IGatewayClient client, ISessionMap sessionMap) : Hub<IGatewayHubClient>
{
    /// <summary>
    /// Sends a prompt for the connected caller and streams the resulting session events.
    /// </summary>
    public IAsyncEnumerable<SessionEvent> SendMessage(
        string prompt,
        CancellationToken cancellationToken)
    {
        return messageService.SendAsync(Context.ConnectionId, prompt, cancellationToken);
    }

    /// <summary>
    /// Creates a new session for the connected caller and tracks the mapping.
    /// </summary>
    public async Task<string> CreateSession(CancellationToken cancellationToken)
    {
        await using var session = await client.CreateSessionAsync(cancellationToken: cancellationToken);
        sessionMap.SetSessionId(Context.ConnectionId, session.SessionId);

        return session.SessionId;
    }

    /// <summary>
    /// Lists all sessions known to the gateway client.
    /// </summary>
    public Task<IReadOnlyList<SessionMetadata>> ListSessions(CancellationToken cancellationToken)
    {
        return client.ListSessionsAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the current caller's session history.
    /// </summary>
    public async Task<IReadOnlyList<SessionEvent>> GetHistory(CancellationToken cancellationToken)
    {
        var sessionId = GetCallerSessionId();
        await using var session = await client.ResumeSessionAsync(sessionId, cancellationToken: cancellationToken);

        return await session.GetMessagesAsync(cancellationToken);
    }

    /// <summary>
    /// Aborts the active response for the current caller.
    /// </summary>
    public async Task AbortResponse(CancellationToken cancellationToken)
    {
        var sessionId = GetCallerSessionId();
        await using var session = await client.ResumeSessionAsync(sessionId, cancellationToken: cancellationToken);
        await session.AbortAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves the tracked session identifier for the current caller.
    /// </summary>
    private string GetCallerSessionId()
    {
        var sessionId = sessionMap.GetSessionId(Context.ConnectionId);
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException($"No session is tracked for caller '{Context.ConnectionId}'.");
        }

        return sessionId;
    }
}
