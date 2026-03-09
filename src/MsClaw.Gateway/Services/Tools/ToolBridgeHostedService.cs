using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MsClaw.Gateway.Services.Tools;

/// <summary>
/// Registers tool providers at startup and watches them for surface changes during runtime.
/// </summary>
public sealed class ToolBridgeHostedService : IHostedService
{
    private static readonly TimeSpan FailureRetryDelay = TimeSpan.FromSeconds(1);
    private readonly IToolRegistrar toolRegistrar;
    private readonly IReadOnlyList<IToolProvider> toolProviders;
    private readonly ILogger<ToolBridgeHostedService> logger;
    private readonly Dictionary<string, CancellationTokenSource> watchLoopSources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task> watchLoopTasks = new(StringComparer.Ordinal);
    private readonly HashSet<string> registeredProviderNames = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates the hosted service responsible for provider registration and refresh loops.
    /// </summary>
    public ToolBridgeHostedService(
        IToolRegistrar toolRegistrar,
        IEnumerable<IToolProvider> toolProviders,
        ILogger<ToolBridgeHostedService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(toolRegistrar);
        ArgumentNullException.ThrowIfNull(toolProviders);

        this.toolRegistrar = toolRegistrar;
        this.toolProviders = toolProviders.ToArray();
        this.logger = logger ?? NullLogger<ToolBridgeHostedService>.Instance;
    }

    /// <summary>
    /// Registers all discovered providers and starts one watch loop per successfully registered provider.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var provider in toolProviders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await toolRegistrar.RegisterProviderAsync(provider, cancellationToken);
                registeredProviderNames.Add(provider.Name);

                var watchLoopCts = new CancellationTokenSource();
                watchLoopSources[provider.Name] = watchLoopCts;
                watchLoopTasks[provider.Name] = WatchProviderAsync(provider, watchLoopCts.Token);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to register tool provider '{ProviderName}'.", provider.Name);
            }
        }
    }

    /// <summary>
    /// Cancels provider watch loops and unregisters any providers that were registered successfully.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var watchLoopSource in watchLoopSources.Values)
        {
            watchLoopSource.Cancel();
        }

        await AwaitWatchLoopsAsync();

        foreach (var providerName in registeredProviderNames.ToArray())
        {
            try
            {
                await toolRegistrar.UnregisterProviderAsync(providerName, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to unregister tool provider '{ProviderName}'.", providerName);
            }
        }

        foreach (var watchLoopSource in watchLoopSources.Values)
        {
            watchLoopSource.Dispose();
        }

        watchLoopSources.Clear();
        watchLoopTasks.Clear();
        registeredProviderNames.Clear();
    }

    /// <summary>
    /// Waits for change signals from a single provider and refreshes its catalog entries after each signal.
    /// </summary>
    private async Task WatchProviderAsync(IToolProvider provider, CancellationToken cancellationToken)
    {
        while (cancellationToken.IsCancellationRequested is false)
        {
            try
            {
                await provider.WaitForSurfaceChangeAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await toolRegistrar.RefreshProviderAsync(provider.Name, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tool provider '{ProviderName}' failed during its watch loop.", provider.Name);

                try
                {
                    await Task.Delay(FailureRetryDelay, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Awaits all running watch loops while suppressing expected cancellation exits.
    /// </summary>
    private async Task AwaitWatchLoopsAsync()
    {
        foreach (var watchLoop in watchLoopTasks.Values)
        {
            try
            {
                await watchLoop;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
