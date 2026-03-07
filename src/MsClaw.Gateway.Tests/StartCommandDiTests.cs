using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MsClaw.Core;
using MsClaw.Gateway.Commands;
using MsClaw.Gateway.Hosting;
using MsClaw.Gateway.Services;
using MsClaw.Tunnel;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class StartCommandDiTests
{
    private static IConfiguration CreateTestConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "test-tenant-id",
                ["AzureAd:ClientId"] = "test-client-id"
            })
            .Build();
    }

    [Fact]
    public async Task ConfigureServices_RegistersCoreServicesAndHostedService()
    {
        var services = new ServiceCollection();
        var options = new GatewayOptions { MindPath = "C:\\mind" };

        StartCommand.ConfigureServices(services, CreateTestConfiguration(), options);

        await using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IMindValidator>());
        Assert.NotNull(provider.GetService<IIdentityLoader>());
        Assert.NotNull(provider.GetService<IMindScaffold>());
        Assert.NotNull(provider.GetService<IGatewayHostedService>());
        Assert.NotNull(provider.GetService<ITunnelManager>());
    }

    [Fact]
    public async Task ConfigureServices_RegistersConcurrencyGateAsSingleton()
    {
        var services = new ServiceCollection();
        var options = new GatewayOptions { MindPath = "C:\\mind" };

        StartCommand.ConfigureServices(services, CreateTestConfiguration(), options);

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

        StartCommand.ConfigureServices(services, CreateTestConfiguration(), options);

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

        StartCommand.ConfigureServices(services, CreateTestConfiguration(), options);

        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<AgentMessageService>());
    }
}
