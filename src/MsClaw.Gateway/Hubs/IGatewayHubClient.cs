using GitHub.Copilot.SDK;
using MsClaw.Gateway.Services.Cron;

namespace MsClaw.Gateway.Hubs;

/// <summary>
/// Defines the strongly typed client contract for gateway hub callbacks.
/// </summary>
public interface IGatewayHubClient
{
    /// <summary>
    /// Receives a session event pushed from the gateway.
    /// </summary>
    Task ReceiveEvent(SessionEvent sessionEvent);

    /// <summary>
    /// Receives a presence update for connected operators.
    /// </summary>
    Task ReceivePresence(PresenceSnapshot presence);

    /// <summary>
    /// Receives refreshed authentication context when gateway rotates access tokens.
    /// </summary>
    Task ReceiveAuthContext(GatewayAuthContext authContext);

    /// <summary>
    /// Receives a completed cron run event pushed from the gateway.
    /// </summary>
    Task ReceiveCronResult(CronRunEvent cronRunEvent);
}

/// <summary>
/// Represents a lightweight presence snapshot for gateway clients.
/// </summary>
/// <param name="ConnectionCount">Gets the number of active gateway connections.</param>
public sealed record PresenceSnapshot(int ConnectionCount);

/// <summary>
/// Represents refreshed auth state pushed to browser clients.
/// </summary>
/// <param name="Authenticated">Gets whether the user is authenticated.</param>
/// <param name="Username">Gets the authenticated username.</param>
/// <param name="AccessToken">Gets the current bearer token.</param>
/// <param name="ExpiresAtUtc">Gets the token expiry in UTC.</param>
public sealed record GatewayAuthContext(bool Authenticated, string? Username, string? AccessToken, DateTimeOffset? ExpiresAtUtc);
