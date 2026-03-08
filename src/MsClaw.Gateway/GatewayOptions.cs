namespace MsClaw.Gateway;

public sealed class GatewayOptions
{
    public required string MindPath { get; set; }

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 18789;

    public bool TunnelEnabled { get; set; }

    public string? TunnelId { get; set; }
}
