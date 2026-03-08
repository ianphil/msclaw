using System.Text;
using MsClaw.Tunnel;

namespace MsClaw.Gateway.Extensions;

/// <summary>
/// Builds the gateway startup banner displayed on the console.
/// </summary>
public static class GatewayBannerBuilder
{
    /// <summary>
    /// Builds a startup banner that lists local and remote gateway access endpoints.
    /// </summary>
    public static string BuildAccessBanner(GatewayOptions options, TunnelStatus tunnelStatus)
    {
        var localBaseUrl = $"http://{options.Host}:{options.Port}";
        var buffer = new StringBuilder();
        buffer.AppendLine("MSCLAW GATEWAY READY");
        buffer.AppendLine("===================");
        buffer.AppendLine();
        buffer.AppendLine("LOCAL ACCESS");
        AppendAccessLine(buffer, "UI (browser)", $"{localBaseUrl}/", "Local chat interface");
        AppendAccessLine(buffer, "OpenResponses API", $"{localBaseUrl}/v1/responses", "HTTP JSON/SSE endpoint");
        AppendAccessLine(buffer, "SignalR Hub", $"{localBaseUrl}/gateway", "Realtime streaming channel");
        AppendAccessLine(buffer, "Health", $"{localBaseUrl}/health", "Liveness probe");
        AppendAccessLine(buffer, "Readiness", $"{localBaseUrl}/health/ready", "Runtime readiness");
        AppendAccessLine(buffer, "Tunnel Status", $"{localBaseUrl}/api/tunnel/status", "Remote URL + tunnel state");
        buffer.AppendLine();
        buffer.AppendLine("REMOTE ACCESS (Dev Tunnel)");

        if (tunnelStatus.Enabled && string.IsNullOrWhiteSpace(tunnelStatus.PublicUrl) is false)
        {
            var remoteBaseUrl = tunnelStatus.PublicUrl.TrimEnd('/');
            AppendAccessLine(buffer, "Tunnel URL", remoteBaseUrl, "Public HTTPS entrypoint");
            AppendAccessLine(buffer, "Remote UI", $"{remoteBaseUrl}/", "Browser UI through tunnel");
            AppendAccessLine(buffer, "Remote API", $"{remoteBaseUrl}/v1/responses", "OpenResponses endpoint through tunnel");
            AppendAccessLine(buffer, "Remote SignalR", $"{remoteBaseUrl}/gateway", "SignalR endpoint through tunnel");
        }
        else
        {
            AppendAccessLine(buffer, "Tunnel URL", "(disabled)", "Start with --tunnel to enable remote access");
        }

        buffer.AppendLine();
        buffer.AppendLine("NOTES");
        buffer.AppendLine("  - Tunnel auth: Entra tenant access + gateway JWT auth.");
        buffer.AppendLine("  - Press Ctrl+C to stop gateway and devtunnel.");

        return buffer.ToString();
    }

    /// <summary>
    /// Appends a formatted label + endpoint + description line to the banner buffer.
    /// </summary>
    public static void AppendAccessLine(StringBuilder buffer, string label, string endpoint, string description)
    {
        buffer.Append("  ");
        buffer.Append(label.PadRight(18, ' '));
        buffer.Append(" ");
        buffer.Append(endpoint.PadRight(40, ' '));
        buffer.Append(" ");
        buffer.AppendLine(description);
    }
}
