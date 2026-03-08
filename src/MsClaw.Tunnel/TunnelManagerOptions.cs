namespace MsClaw.Tunnel;

/// <summary>
/// Represents configuration values used by <see cref="TunnelManager" />.
/// </summary>
public sealed class TunnelManagerOptions
{
    /// <summary>
    /// Gets or sets whether tunnel hosting is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the local gateway port exposed by the tunnel.
    /// </summary>
    public int LocalPort { get; set; } = 18789;

    /// <summary>
    /// Gets or sets an explicit tunnel ID override.
    /// </summary>
    public string? TunnelId { get; set; }
}
