namespace MsClaw.Tunnel;

/// <summary>
/// Represents the current runtime status of dev tunnel hosting.
/// </summary>
public sealed class TunnelStatus
{
    /// <summary>
    /// Gets or sets whether tunnel mode is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets whether the tunnel host process is currently running.
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// Gets or sets the resolved tunnel ID.
    /// </summary>
    public string? TunnelId { get; set; }

    /// <summary>
    /// Gets or sets the public URL emitted by dev tunnel host.
    /// </summary>
    public string? PublicUrl { get; set; }

    /// <summary>
    /// Gets or sets the most recent startup/runtime error.
    /// </summary>
    public string? Error { get; set; }
}
