using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http.Connections.Features;
using GitHub.Copilot.SDK;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MsClaw.Gateway.Hosting;
using MsClaw.Gateway.Services;

namespace MsClaw.Gateway.Hubs;

/// <summary>
/// Routes SignalR gateway operations to the underlying message and session services.
/// </summary>
[Authorize]
public sealed class GatewayHub(
    AgentMessageService messageService,
    ISessionPool sessionPool,
    ILogger<GatewayHub>? logger = null) : Hub<IGatewayHubClient>
{
    private readonly ILogger<GatewayHub> logger = logger ?? NullLogger<GatewayHub>.Instance;

    /// <summary>
    /// Logs the negotiated SignalR transport for connection diagnostics.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var transport = Context.Features.Get<IHttpTransportFeature>()?.TransportType.ToString() ?? "unknown";
        logger.LogInformation("Gateway connection {ConnectionId} using transport {Transport}", Context.ConnectionId, transport);
        await base.OnConnectedAsync();
    }

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
    /// Lists all sessions known to the session pool.
    /// </summary>
    public Task<IReadOnlyList<(string CallerKey, string SessionId)>> ListSessions(CancellationToken cancellationToken)
    {
        return Task.FromResult(sessionPool.ListCallers());
    }

    /// <summary>
    /// Gets the current caller's session history.
    /// </summary>
    public async Task<IReadOnlyList<SessionEvent>> GetHistory(CancellationToken cancellationToken)
    {
        var session = GetCallerSession();

        return await session.GetMessagesAsync(cancellationToken);
    }

    /// <summary>
    /// Aborts the active response for the current caller.
    /// </summary>
    public async Task AbortResponse(CancellationToken cancellationToken)
    {
        var session = GetCallerSession();
        await session.AbortAsync(cancellationToken);
    }

    /// <summary>
    /// Removes the caller's session from the pool when the connection closes.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await sessionPool.RemoveAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Resolves the pooled session for the current caller.
    /// </summary>
    private IGatewaySession GetCallerSession()
    {
        var session = sessionPool.TryGet(Context.ConnectionId);
        if (session is null)
        {
            throw new InvalidOperationException($"No session is tracked for caller '{Context.ConnectionId}'.");
        }

        return session;
    }
}
