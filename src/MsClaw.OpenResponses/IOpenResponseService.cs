using GitHub.Copilot.SDK;
using Microsoft.AspNetCore.Http;
using MsClaw.OpenResponses.Models;

namespace MsClaw.OpenResponses;

/// <summary>
/// Sends OpenResponses requests through the shared gateway runtime.
/// </summary>
public interface IOpenResponseService
{
    /// <summary>
    /// Sends the specified request and streams the resulting SDK session events.
    /// </summary>
    IAsyncEnumerable<SessionEvent> SendAsync(
        HttpContext httpContext,
        ResponseRequest request,
        CancellationToken cancellationToken = default);
}
