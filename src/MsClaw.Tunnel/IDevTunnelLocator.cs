namespace MsClaw.Tunnel;

/// <summary>
/// Resolves the devtunnel CLI binary path on the current machine.
/// </summary>
public interface IDevTunnelLocator
{
    /// <summary>
    /// Resolves the full path to the devtunnel CLI executable.
    /// </summary>
    string ResolveDevTunnelCliPath();
}
