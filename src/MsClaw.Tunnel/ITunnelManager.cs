namespace MsClaw.Tunnel;

/// <summary>
/// Controls dev tunnel lifecycle for remote gateway access.
/// </summary>
public interface ITunnelManager
{
    /// <summary>
    /// Starts the configured dev tunnel and resolves the public URL.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the active dev tunnel host process.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the latest tunnel runtime status.
    /// </summary>
    TunnelStatus GetStatus();
}
