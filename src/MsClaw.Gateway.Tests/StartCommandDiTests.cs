using Microsoft.Extensions.DependencyInjection;
using MsClaw.Core;
using MsClaw.Gateway.Commands;
using MsClaw.Gateway.Hosting;
using MsClaw.Gateway.Services;
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

    [Fact]
    public void ConfigureServices_RegistersConcurrencyGateAsSingleton()
    {
        var services = new ServiceCollection();
        var options = new GatewayOptions { MindPath = "C:\\mind" };

        StartCommand.ConfigureServices(services, options);

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IConcurrencyGate>();
        var second = provider.GetRequiredService<IConcurrencyGate>();

        Assert.Same(first, second);
    }

    [Fact]
    public void ConfigureServices_RegistersSessionMapAsSameCallerRegistryInstance()
    {
        var services = new ServiceCollection();
        var options = new GatewayOptions { MindPath = "C:\\mind" };

        StartCommand.ConfigureServices(services, options);

        using var provider = services.BuildServiceProvider();
        var gate = provider.GetRequiredService<IConcurrencyGate>();
        var sessionMap = provider.GetRequiredService<ISessionMap>();

        Assert.Same(gate, sessionMap);
    }

    [Fact]
    public void ConfigureServices_RegistersAgentMessageService()
    {
        var services = new ServiceCollection();
        var options = new GatewayOptions { MindPath = "C:\\mind" };

        StartCommand.ConfigureServices(services, options);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<AgentMessageService>());
    }
}
