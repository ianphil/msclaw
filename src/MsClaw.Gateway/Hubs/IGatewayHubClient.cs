using GitHub.Copilot.SDK;

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
}

/// <summary>
/// Represents a lightweight presence snapshot for gateway clients.
/// </summary>
/// <param name="ConnectionCount">Gets the number of active gateway connections.</param>
public sealed record PresenceSnapshot(int ConnectionCount);
