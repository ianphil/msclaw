using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MsClaw.Core;
using MsClaw.Gateway.Extensions;
using MsClaw.Gateway.Hosting;
using MsClaw.Gateway.Services.Cron;
using MsClaw.Gateway.Services.Tools;
using Xunit;

namespace MsClaw.Gateway.Tests;

public sealed class CronServiceRegistrationTests
{
    [Fact]
    public async Task AddGatewayServices_ResolvesCronJobStoreFromDependencyInjection()
    {
        await using var provider = CreateServiceProvider();

        var store = provider.GetRequiredService<ICronJobStore>();

        Assert.IsType<CronJobStore>(store);
    }

    [Fact]
    public async Task AddGatewayServices_ResolvesRunHistoryStoreToSameCronJobStoreInstance()
    {
        await using var provider = CreateServiceProvider();

        var jobStore = provider.GetRequiredService<ICronJobStore>();
        var historyStore = provider.GetRequiredService<ICronRunHistoryStore>();

        Assert.IsType<CronJobStore>(historyStore);
        Assert.Same(jobStore, historyStore);
    }

    [Fact]
    public async Task AddGatewayServices_ResolvesCronEngineFromDependencyInjection()
    {
        await using var provider = CreateServiceProvider();

        var engine = provider.GetRequiredService<ICronEngine>();

        Assert.IsType<CronEngine>(engine);
        Assert.IsAssignableFrom<IHostedService>(engine);
    }

    [Fact]
    public async Task AddGatewayServices_ResolvesBothCronExecutors()
    {
        await using var provider = CreateServiceProvider();

        var executors = provider.GetServices<ICronJobExecutor>().ToArray();

        Assert.Contains(executors, static executor => executor is PromptJobExecutor);
        Assert.Contains(executors, static executor => executor is CommandJobExecutor);
    }

    [Fact]
    public async Task AddGatewayServices_ResolvesCronToolProvider()
    {
        await using var provider = CreateServiceProvider();

        var providers = provider.GetServices<IToolProvider>().ToArray();

        Assert.Contains(providers, static provider => string.Equals(provider.Name, "cron", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddGatewayServices_ResolvesDefaultCronErrorClassifier()
    {
        await using var provider = CreateServiceProvider();

        var classifier = provider.GetRequiredService<ICronErrorClassifier>();

        Assert.IsType<DefaultCronErrorClassifier>(classifier);
    }

    [Fact]
    public async Task AddGatewayServices_ResolvesSignalRCronOutputSink()
    {
        await using var provider = CreateServiceProvider();

        var sink = provider.GetRequiredService<ICronOutputSink>();

        Assert.IsType<SignalRCronOutputSink>(sink);
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGatewayServices(CreateTestConfiguration(), new GatewayOptions { MindPath = "C:\\mind" });

        return services.BuildServiceProvider();
    }

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
}
