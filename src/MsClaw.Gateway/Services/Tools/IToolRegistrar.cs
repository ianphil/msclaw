namespace MsClaw.Gateway.Services.Tools;

/// <summary>
/// Defines write operations that mutate the registered tool catalog.
/// </summary>
public interface IToolRegistrar
{
    /// <summary>
    /// Registers a provider and indexes its discovered tools.
    /// </summary>
    Task RegisterProviderAsync(IToolProvider provider, CancellationToken cancellationToken);

    /// <summary>
    /// Unregisters a provider and removes its tools from the catalog.
    /// </summary>
    Task UnregisterProviderAsync(string providerName, CancellationToken cancellationToken);

    /// <summary>
    /// Re-discovers tools for an already registered provider.
    /// </summary>
    Task RefreshProviderAsync(string providerName, CancellationToken cancellationToken);
}
