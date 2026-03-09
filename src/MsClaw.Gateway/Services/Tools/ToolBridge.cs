using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace MsClaw.Gateway.Services.Tools;

/// <summary>
/// Provides read and write access to the registered tool catalog.
/// </summary>
public sealed class ToolBridge : IToolCatalog, IToolRegistrar
{
    private readonly ToolCatalogStore store;
    private readonly ConcurrentDictionary<string, IToolProvider> providers = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes the bridge with a new in-memory tool catalog store.
    /// </summary>
    public ToolBridge()
        : this(new ToolCatalogStore())
    {
    }

    /// <summary>
    /// Initializes the bridge with the supplied tool catalog store for testing or shared state.
    /// </summary>
    internal ToolBridge(ToolCatalogStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Registers a provider and indexes its discovered tools into the catalog.
    /// </summary>
    public async Task RegisterProviderAsync(IToolProvider provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider.Name);

        if (providers.TryAdd(provider.Name, provider) is false)
        {
            throw new InvalidOperationException($"Provider '{provider.Name}' is already registered.");
        }

        try
        {
            var discoveredTools = await provider.DiscoverAsync(cancellationToken);
            IndexDiscoveredTools(provider, discoveredTools);
        }
        catch
        {
            _ = providers.TryRemove(provider.Name, out _);
            throw;
        }
    }

    /// <summary>
    /// Unregisters a provider, removes its tools, and disposes the provider.
    /// </summary>
    public async Task UnregisterProviderAsync(string providerName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        cancellationToken.ThrowIfCancellationRequested();

        if (providers.TryRemove(providerName, out var provider) is false)
        {
            return;
        }

        foreach (var descriptor in store.GetByProvider(providerName))
        {
            store.Remove(descriptor.Function.Name);
        }

        await provider.DisposeAsync();
    }

    /// <summary>
    /// Re-discovers tools for an already registered provider and replaces its catalog entries.
    /// New tools are indexed before stale ones are removed so concurrent readers never see an empty window.
    /// </summary>
    public async Task RefreshProviderAsync(string providerName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        if (providers.TryGetValue(providerName, out var provider) is false)
        {
            throw new InvalidOperationException($"Provider '{providerName}' is not registered.");
        }

        var staleTools = store.GetByProvider(providerName);
        var discoveredTools = await provider.DiscoverAsync(cancellationToken);
        IndexDiscoveredTools(provider, discoveredTools);

        var newToolNames = discoveredTools
            .Select(static descriptor => descriptor.Function.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var descriptor in staleTools)
        {
            if (newToolNames.Contains(descriptor.Function.Name) is false)
            {
                store.Remove(descriptor.Function.Name);
            }
        }
    }

    /// <summary>
    /// Returns the always-visible tools that are currently ready.
    /// </summary>
    public IReadOnlyList<AIFunction> GetDefaultTools()
    {
        return store.GetAll()
            .Where(descriptor => descriptor.AlwaysVisible && store.GetStatus(descriptor.Function.Name) is ToolStatus.Ready)
            .Select(static descriptor => descriptor.Function)
            .ToArray();
    }

    /// <summary>
    /// Returns ready tools matching the requested names and silently skips unknown names.
    /// </summary>
    public IReadOnlyList<AIFunction> GetToolsByName(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        return names
            .Where(static name => string.IsNullOrWhiteSpace(name) is false)
            .Select(name => store.TryGet(name))
            .OfType<ToolDescriptor>()
            .Where(descriptor => store.GetStatus(descriptor.Function.Name) is ToolStatus.Ready)
            .Select(static descriptor => descriptor.Function)
            .ToArray();
    }

    /// <summary>
    /// Returns every catalog tool name in ordinal order.
    /// </summary>
    public IReadOnlyList<string> GetCatalogToolNames()
    {
        return store.GetAll()
            .Select(static descriptor => descriptor.Function.Name)
            .ToArray();
    }

    /// <summary>
    /// Returns the tool names owned by the supplied provider in ordinal order.
    /// </summary>
    public IReadOnlyList<string> GetToolNamesByProvider(string providerName)
    {
        return store.GetByProvider(providerName)
            .Select(static descriptor => descriptor.Function.Name)
            .ToArray();
    }

    /// <summary>
    /// Performs a case-insensitive substring search across tool names and descriptions.
    /// </summary>
    public IReadOnlyList<string> SearchTools(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return store.GetAll()
            .Where(descriptor =>
            {
                var searchableText = $"{descriptor.Function.Name} {descriptor.Function.Description}";

                return terms.All(term => searchableText.Contains(term, StringComparison.OrdinalIgnoreCase));
            })
            .Select(static descriptor => descriptor.Function.Name)
            .ToArray();
    }

    /// <summary>
    /// Returns the descriptor for the supplied tool name, or <see langword="null"/> when absent.
    /// </summary>
    public ToolDescriptor? GetDescriptor(string toolName)
    {
        return store.TryGet(toolName);
    }

    /// <summary>
    /// Validates and indexes the descriptors discovered from a provider.
    /// </summary>
    private void IndexDiscoveredTools(IToolProvider provider, IReadOnlyList<ToolDescriptor> discoveredTools)
    {
        ArgumentNullException.ThrowIfNull(discoveredTools);

        foreach (var descriptor in discoveredTools)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Function.Name);

            var normalizedDescriptor = new ToolDescriptor
            {
                Function = descriptor.Function,
                ProviderName = provider.Name,
                Tier = provider.Tier,
                AlwaysVisible = descriptor.AlwaysVisible
            };

            var existingDescriptor = store.TryGet(normalizedDescriptor.Function.Name);
            if (existingDescriptor is null)
            {
                store.Add(normalizedDescriptor, ToolStatus.Ready);
                continue;
            }

            if (existingDescriptor.Tier == normalizedDescriptor.Tier)
            {
                throw new InvalidOperationException(
                    $"Tool '{normalizedDescriptor.Function.Name}' is registered by same-tier providers '{existingDescriptor.ProviderName}' and '{normalizedDescriptor.ProviderName}'.");
            }

            if (normalizedDescriptor.Tier < existingDescriptor.Tier)
            {
                store.Add(normalizedDescriptor, ToolStatus.Ready);
            }
        }
    }
}
