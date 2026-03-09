using Microsoft.Extensions.Hosting;
using MsClaw.Gateway.Services.Tools;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class ToolBridgeHostedServiceTests
{
    [Fact]
    public async Task StartAsync_RegistersAllProvidersViaRegistrar()
    {
        var registrar = new RecordingToolRegistrar();
        var providers = new[]
        {
            new ControllableToolProvider("provider-a"),
            new ControllableToolProvider("provider-b")
        };
        var sut = CreateHostedService(registrar, providers);

        await sut.StartAsync(CancellationToken.None);

        Assert.Equal(["provider-a", "provider-b"], registrar.RegisteredProviderNames);
        await sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_SurfaceChangeSignal_RefreshesMatchingProvider()
    {
        var registrar = new RecordingToolRegistrar();
        var provider = new ControllableToolProvider("provider-a");
        var sut = CreateHostedService(registrar, [provider]);

        await sut.StartAsync(CancellationToken.None);
        provider.SignalSurfaceChange();
        await registrar.WaitForRefreshAsync("provider-a", CancellationToken.None);

        Assert.Equal(["provider-a"], registrar.RefreshedProviderNames);
        await sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_CancelsWatchLoopsAndUnregistersProviders()
    {
        var registrar = new RecordingToolRegistrar();
        var providers = new[]
        {
            new ControllableToolProvider("provider-a"),
            new ControllableToolProvider("provider-b")
        };
        var sut = CreateHostedService(registrar, providers);

        await sut.StartAsync(CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);

        Assert.Equal(["provider-a", "provider-b"], registrar.UnregisteredProviderNames);
        Assert.All(providers, static provider => Assert.True(provider.WaitCancelled));
    }

    private static IHostedService CreateHostedService(IToolRegistrar registrar, IEnumerable<IToolProvider> providers)
    {
        var assembly = typeof(IToolRegistrar).Assembly;
        var hostedServiceType = assembly.GetType("MsClaw.Gateway.Services.Tools.ToolBridgeHostedService");
        Assert.NotNull(hostedServiceType);

        var hostedService = Activator.CreateInstance(hostedServiceType!, registrar, providers, null);

        return Assert.IsAssignableFrom<IHostedService>(hostedService);
    }

    private sealed class RecordingToolRegistrar : IToolRegistrar
    {
        private readonly Dictionary<string, TaskCompletionSource> refreshSignals = new(StringComparer.Ordinal);

        public List<string> RegisteredProviderNames { get; } = [];

        public List<string> RefreshedProviderNames { get; } = [];

        public List<string> UnregisteredProviderNames { get; } = [];

        public Task RegisterProviderAsync(IToolProvider provider, CancellationToken cancellationToken)
        {
            RegisteredProviderNames.Add(provider.Name);
            refreshSignals.TryAdd(provider.Name, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

            return Task.CompletedTask;
        }

        public Task UnregisterProviderAsync(string providerName, CancellationToken cancellationToken)
        {
            UnregisteredProviderNames.Add(providerName);

            return Task.CompletedTask;
        }

        public Task RefreshProviderAsync(string providerName, CancellationToken cancellationToken)
        {
            RefreshedProviderNames.Add(providerName);

            if (refreshSignals.TryGetValue(providerName, out var signal))
            {
                signal.TrySetResult();
            }

            return Task.CompletedTask;
        }

        public async Task WaitForRefreshAsync(string providerName, CancellationToken cancellationToken)
        {
            var signal = refreshSignals[providerName];
            await signal.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ControllableToolProvider(string name) : IToolProvider
    {
        private TaskCompletionSource changeSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name { get; } = name;

        public ToolSourceTier Tier => ToolSourceTier.Bundled;

        public bool WaitCancelled { get; private set; }

        public Task<IReadOnlyList<ToolDescriptor>> DiscoverAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ToolDescriptor>>([]);
        }

        public async Task WaitForSurfaceChangeAsync(CancellationToken cancellationToken)
        {
            try
            {
                await changeSignal.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                WaitCancelled = true;
                throw;
            }
            finally
            {
                if (changeSignal.Task.IsCompletedSuccessfully)
                {
                    changeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
        }

        public void SignalSurfaceChange()
        {
            changeSignal.TrySetResult();
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
