using Microsoft.AspNetCore.SignalR;
using MsClaw.Gateway.Hubs;

namespace MsClaw.Gateway.Services.Cron;

/// <summary>
/// Publishes cron run events to all connected gateway clients over SignalR.
/// </summary>
public sealed class SignalRCronOutputSink(IHubContext<GatewayHub, IGatewayHubClient> hubContext) : ICronOutputSink
{
    /// <summary>
    /// Broadcasts a completed cron run event to all connected clients.
    /// </summary>
    /// <param name="result">The run event to publish.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    public Task PublishResultAsync(CronRunEvent result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        return hubContext.Clients.All.ReceiveCronResult(result);
    }
}
