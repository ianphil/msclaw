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
    public async Task ConfigureServices_RegistersCoreServicesAndHostedService()
    {
        var services = new ServiceCollection();
        var options = new GatewayOptions { MindPath = "C:\\mind" };

        StartCommand.ConfigureServices(services, options);

        await using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IMindValidator>());
        Assert.NotNull(provider.GetService<IIdentityLoader>());
        Assert.NotNull(provider.GetService<IMindScaffold>());
        Assert.NotNull(provider.GetService<IGatewayHostedService>());
    }

    [Fact]
    public async Task ConfigureServices_RegistersConcurrencyGateAsSingleton()
    {
        var services = new ServiceCollection();
        var options = new GatewayOptions { MindPath = "C:\\mind" };

        StartCommand.ConfigureServices(services, options);

        await using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IConcurrencyGate>();
        var second = provider.GetRequiredService<IConcurrencyGate>();

        Assert.Same(first, second);
    }

    [Fact]
    public async Task ConfigureServices_RegistersSessionPoolAsSingleton()
    {
        var services = new ServiceCollection();
        var options = new GatewayOptions { MindPath = "C:\\mind" };

        StartCommand.ConfigureServices(services, options);

        await using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<ISessionPool>();
        var second = provider.GetRequiredService<ISessionPool>();

        Assert.Same(first, second);
    }

    [Fact]
    public async Task ConfigureServices_RegistersAgentMessageService()
    {
        var services = new ServiceCollection();
        var options = new GatewayOptions { MindPath = "C:\\mind" };

        StartCommand.ConfigureServices(services, options);

        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<AgentMessageService>());
    }
}
