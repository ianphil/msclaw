using System.Collections.Concurrent;

namespace MsClaw.Gateway.Services.Tools;

/// <summary>
/// Stores discovered tool descriptors, provider ownership, and operational status for the tool bridge.
/// </summary>
internal sealed class ToolCatalogStore
{
    private readonly ConcurrentDictionary<string, ToolDescriptor> descriptors = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ToolStatus> statuses = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> toolNamesByProvider = new(StringComparer.Ordinal);

    /// <summary>
    /// Adds or replaces a descriptor and records its current readiness state.
    /// </summary>
    public void Add(ToolDescriptor descriptor, ToolStatus status)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Function.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ProviderName);

        if (descriptors.TryGetValue(descriptor.Function.Name, out var existingDescriptor))
        {
            RemoveToolFromProviderIndex(existingDescriptor.ProviderName, existingDescriptor.Function.Name);
        }

        descriptors[descriptor.Function.Name] = descriptor;
        statuses[descriptor.Function.Name] = status;

        var providerTools = toolNamesByProvider.GetOrAdd(
            descriptor.ProviderName,
            static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));

        providerTools[descriptor.Function.Name] = 0;
    }

    /// <summary>
    /// Removes a descriptor, its readiness state, and its provider index entry.
    /// </summary>
    public void Remove(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        if (descriptors.TryRemove(toolName, out var descriptor))
        {
            RemoveToolFromProviderIndex(descriptor.ProviderName, toolName);
        }

        _ = statuses.TryRemove(toolName, out _);
    }

    /// <summary>
    /// Returns the descriptor for the specified tool name, or <see langword="null"/> when absent.
    /// </summary>
    public ToolDescriptor? TryGet(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        descriptors.TryGetValue(toolName, out var descriptor);

        return descriptor;
    }

    /// <summary>
    /// Returns every descriptor currently stored in the catalog.
    /// </summary>
    public IReadOnlyList<ToolDescriptor> GetAll()
    {
        return descriptors.Values
            .OrderBy(static descriptor => descriptor.Function.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Returns all descriptors owned by the specified provider.
    /// </summary>
    public IReadOnlyList<ToolDescriptor> GetByProvider(string providerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        if (toolNamesByProvider.TryGetValue(providerName, out var providerTools) is false)
        {
            return [];
        }

        return providerTools.Keys
            .Select(TryGet)
            .OfType<ToolDescriptor>()
            .OrderBy(static descriptor => descriptor.Function.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Returns the stored readiness state for the specified tool name, or <see langword="null"/> when absent.
    /// </summary>
    public ToolStatus? GetStatus(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        return statuses.TryGetValue(toolName, out var status) ? status : null;
    }

    /// <summary>
    /// Updates the readiness state for an existing descriptor.
    /// </summary>
    public void SetStatus(string toolName, ToolStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        if (descriptors.ContainsKey(toolName) is false)
        {
            throw new InvalidOperationException($"Tool '{toolName}' is not registered.");
        }

        statuses[toolName] = status;
    }

    /// <summary>
    /// Removes a tool name from the provider index and drops empty provider buckets.
    /// </summary>
    private void RemoveToolFromProviderIndex(string providerName, string toolName)
    {
        if (toolNamesByProvider.TryGetValue(providerName, out var providerTools) is false)
        {
            return;
        }

        _ = providerTools.TryRemove(toolName, out _);
        if (providerTools.IsEmpty)
        {
            _ = toolNamesByProvider.TryRemove(providerName, out _);
        }
    }
}
