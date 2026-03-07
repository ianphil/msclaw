using MsClaw.Gateway.Hosting;
using MsClaw.Tunnel;
using Xunit;

namespace MsClaw.Gateway.Tests;

public sealed class GatewayTunnelHostedServiceTests
{
    [Fact]
    public async Task StartAsync_TunnelDisabled_DoesNotStartTunnel()
    {
        var tunnelManager = new StubTunnelManager();
        var gatewayHostedService = new StubGatewayHostedService { IsReady = true };
        var sut = new GatewayTunnelHostedService(
            new GatewayOptions { MindPath = "C:\\mind", TunnelEnabled = false },
            gatewayHostedService,
            tunnelManager);

        await sut.StartAsync(CancellationToken.None);

        Assert.False(tunnelManager.StartCalled);
    }

    [Fact]
    public async Task StartAsync_TunnelEnabled_StartsTunnelWhenGatewayReady()
    {
        var tunnelManager = new StubTunnelManager();
        var gatewayHostedService = new StubGatewayHostedService { IsReady = true };
        var sut = new GatewayTunnelHostedService(
            new GatewayOptions { MindPath = "C:\\mind", TunnelEnabled = true },
            gatewayHostedService,
            tunnelManager);

        await sut.StartAsync(CancellationToken.None);

        Assert.True(tunnelManager.StartCalled);
    }

    [Fact]
    public async Task StopAsync_TunnelEnabled_StopsTunnel()
    {
        var tunnelManager = new StubTunnelManager();
        var gatewayHostedService = new StubGatewayHostedService { IsReady = true };
        var sut = new GatewayTunnelHostedService(
            new GatewayOptions { MindPath = "C:\\mind", TunnelEnabled = true },
            gatewayHostedService,
            tunnelManager);

        await sut.StopAsync(CancellationToken.None);

        Assert.True(tunnelManager.StopCalled);
    }

    [Fact]
    public async Task StartAsync_TunnelEnabled_WhenDevTunnelMissing_ThrowsActionableMessage()
    {
        var tunnelManager = new StubTunnelManager
        {
            StartException = new InvalidOperationException("devtunnel CLI not found on PATH. Install it and ensure it is available.")
        };
        var gatewayHostedService = new StubGatewayHostedService { IsReady = true };
        var sut = new GatewayTunnelHostedService(
            new GatewayOptions { MindPath = "C:\\mind", TunnelEnabled = true },
            gatewayHostedService,
            tunnelManager);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.StartAsync(CancellationToken.None));

        Assert.Contains("not available on PATH", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("devtunnel user login", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(ex.InnerException);
    }

    private sealed class StubTunnelManager : ITunnelManager
    {
        public bool StartCalled { get; private set; }

        public bool StopCalled { get; private set; }

        public Exception? StartException { get; set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCalled = true;
            if (StartException is not null)
            {
                throw StartException;
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalled = true;
            return Task.CompletedTask;
        }

        public TunnelStatus GetStatus()
        {
            return new TunnelStatus
            {
                Enabled = true,
                IsRunning = StartCalled,
                PublicUrl = "https://example.devtunnels.ms"
            };
        }
    }

    private sealed class StubGatewayHostedService : IGatewayHostedService
    {
        public string? SystemMessage { get; set; }

        public GatewayState State { get; set; } = GatewayState.Ready;

        public string? Error { get; set; }

        public bool IsReady { get; set; }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
