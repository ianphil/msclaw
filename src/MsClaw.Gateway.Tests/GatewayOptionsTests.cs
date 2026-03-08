using Xunit;

namespace MsClaw.Gateway.Tests;

public class GatewayOptionsTests
{
    [Fact]
    public void GatewayOptions_DefaultValues_AreExpected()
    {
        var options = new GatewayOptions
        {
            MindPath = "C:\\mind"
        };

        Assert.Equal("127.0.0.1", options.Host);
        Assert.Equal(18789, options.Port);
        Assert.False(options.TunnelEnabled);
        Assert.Null(options.TunnelId);
    }
}
