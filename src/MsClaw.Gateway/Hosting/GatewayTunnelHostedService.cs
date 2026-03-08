using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MsClaw.Tunnel;

namespace MsClaw.Gateway.Hosting;

/// <summary>
/// Starts and stops dev tunnel hosting alongside the gateway runtime when enabled.
/// </summary>
public sealed class GatewayTunnelHostedService : IHostedService
{
    private readonly GatewayOptions options;
    private readonly IGatewayHostedService gatewayHostedService;
    private readonly ITunnelManager tunnelManager;
    private readonly ILogger<GatewayTunnelHostedService> logger;

    /// <summary>
    /// Creates a new hosted service for tunnel lifecycle management.
    /// </summary>
    public GatewayTunnelHostedService(
        GatewayOptions options,
        IGatewayHostedService gatewayHostedService,
        ITunnelManager tunnelManager,
        ILogger<GatewayTunnelHostedService>? logger = null)
    {
        this.options = options;
        this.gatewayHostedService = gatewayHostedService;
        this.tunnelManager = tunnelManager;
        this.logger = logger ?? NullLogger<GatewayTunnelHostedService>.Instance;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.TunnelEnabled is false)
        {
            return;
        }

        if (gatewayHostedService.IsReady is false)
        {
            throw new InvalidOperationException("Cannot start dev tunnel before gateway startup is ready.");
        }

        try
        {
            await tunnelManager.StartAsync(cancellationToken);
        }
        catch (InvalidOperationException ex) when (IsMissingDevTunnelCliError(ex))
        {
            throw new InvalidOperationException(
                "Dev tunnel was requested, but the `devtunnel` CLI is not available on PATH." + Environment.NewLine +
                "Install the Dev Tunnels CLI, then run `devtunnel user login`, and retry `msclaw start --tunnel`." + Environment.NewLine +
                "You can also start without remote access by removing the `--tunnel` flag.",
                ex);
        }

        var status = tunnelManager.GetStatus();
        if (string.IsNullOrWhiteSpace(status.PublicUrl) is false)
        {
            logger.LogInformation("Gateway dev tunnel URL: {PublicUrl}", status.PublicUrl);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return tunnelManager.StopAsync(cancellationToken);
    }

    private static bool IsMissingDevTunnelCliError(InvalidOperationException exception)
    {
        return exception.Message.Contains("devtunnel CLI not found on PATH", StringComparison.OrdinalIgnoreCase);
    }
}
