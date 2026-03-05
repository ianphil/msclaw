using Microsoft.AspNetCore.SignalR;
using MsClaw.Gateway.Hubs;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class GatewayHubTests
{
    [Fact]
    public void GatewayHub_ExtendsHub()
    {
        Assert.True(typeof(Hub).IsAssignableFrom(typeof(GatewayHub)));
    }
}
