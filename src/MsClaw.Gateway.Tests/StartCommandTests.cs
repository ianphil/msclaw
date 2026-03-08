using MsClaw.Gateway.Commands;
using MsClaw.Gateway.Extensions;
using MsClaw.Tunnel;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class StartCommandTests
{
    [Fact]
    public void Create_DefinesMindAndNewMindOptions()
    {
        var command = StartCommand.Create();
        var optionNames = command.Options.Select(option => option.Name).ToArray();

        Assert.Contains("--mind", optionNames);
        Assert.Contains("--new-mind", optionNames);
        Assert.Contains("--tunnel", optionNames);
        Assert.Contains("--tunnel-id", optionNames);
    }

    [Fact]
    public void BuildAccessBanner_TunnelEnabled_IncludesLocalAndRemoteEndpoints()
    {
        var options = new GatewayOptions
        {
            MindPath = "C:\\mind",
            Host = "127.0.0.1",
            Port = 18789
        };
        var status = new TunnelStatus
        {
            Enabled = true,
            IsRunning = true,
            TunnelId = "my-msclaw-tunnel",
            PublicUrl = "https://my-msclaw-tunnel.devtunnels.ms"
        };

        var banner = GatewayBannerBuilder.BuildAccessBanner(options, status);

        Assert.Contains("LOCAL ACCESS", banner, StringComparison.Ordinal);
        Assert.Contains("REMOTE ACCESS (Dev Tunnel)", banner, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:18789/v1/responses", banner, StringComparison.Ordinal);
        Assert.Contains("https://my-msclaw-tunnel.devtunnels.ms/v1/responses", banner, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAccessBanner_TunnelDisabled_ShowsEnableHint()
    {
        var options = new GatewayOptions
        {
            MindPath = "C:\\mind",
            Host = "127.0.0.1",
            Port = 18789
        };
        var status = new TunnelStatus
        {
            Enabled = false,
            IsRunning = false
        };

        var banner = GatewayBannerBuilder.BuildAccessBanner(options, status);

        Assert.Contains("REMOTE ACCESS (Dev Tunnel)", banner, StringComparison.Ordinal);
        Assert.Contains("Start with --tunnel to enable remote access", banner, StringComparison.Ordinal);
    }
}
