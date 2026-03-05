namespace MsClaw.Gateway.Hosting;

public interface IGatewayClient : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);
}
