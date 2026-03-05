namespace MsClaw.Gateway;

public sealed class GatewayOptions
{
    public required string MindPath { get; set; }

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 18789;
}
