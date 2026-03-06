using GitHub.Copilot.SDK;
using Microsoft.AspNetCore.Http;
using MsClaw.OpenResponses;
using MsClaw.OpenResponses.Models;

namespace MsClaw.Gateway.Services;

/// <summary>
/// Adapts OpenResponses HTTP requests to the shared gateway message service.
/// </summary>
public sealed class GatewayOpenResponseService(AgentMessageService agentMessageService) : IOpenResponseService
{
    /// <summary>
    /// Sends the validated request through the shared gateway runtime and streams SDK events back.
    /// </summary>
    public IAsyncEnumerable<SessionEvent> SendAsync(
        HttpContext httpContext,
        ResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(request);

        var callerKey = string.IsNullOrWhiteSpace(request.User)
            ? httpContext.TraceIdentifier
            : request.User;

        return agentMessageService.SendAsync(callerKey, request.GetRequiredPrompt(), cancellationToken);
    }
}
