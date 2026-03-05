using Xunit;

namespace MsClaw.Gateway.Tests;

public class GatewayStateTests
{
    [Fact]
    public void GatewayState_ContainsExpectedLifecycleValues()
    {
        var names = Enum.GetNames<GatewayState>();

        Assert.Equal(
            ["Starting", "Validating", "Ready", "Failed", "Stopping", "Stopped"],
            names);
    }
}
