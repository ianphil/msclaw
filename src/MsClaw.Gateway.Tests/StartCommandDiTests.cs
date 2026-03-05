using Microsoft.Extensions.DependencyInjection;
using MsClaw.Core;
using MsClaw.Gateway.Commands;
using MsClaw.Gateway.Hosting;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class StartCommandDiTests
{
    [Fact]
    public void ConfigureServices_RegistersCoreServicesAndHostedService()
    {
        var services = new ServiceCollection();
        var options = new GatewayOptions { MindPath = "C:\\mind" };

        StartCommand.ConfigureServices(services, options);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IMindValidator>());
        Assert.NotNull(provider.GetService<IIdentityLoader>());
        Assert.NotNull(provider.GetService<IMindScaffold>());
        Assert.NotNull(provider.GetService<IGatewayHostedService>());
    }
}
