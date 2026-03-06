using Microsoft.Extensions.Hosting;

namespace MsClaw.Gateway.Hosting;

public interface IGatewayHostedService : IHostedService
{
    string? SystemMessage { get; }

    GatewayState State { get; }

    string? Error { get; }

    bool IsReady { get; }
}
