using Microsoft.Extensions.AI;

namespace MsClaw.Gateway.Services.Tools;

/// <summary>
/// Defines read-only access to the registered tool catalog.
/// </summary>
public interface IToolCatalog
{
    /// <summary>
    /// Returns the default ready tools included on every new session.
    /// </summary>
    IReadOnlyList<AIFunction> GetDefaultTools();

    /// <summary>
    /// Returns ready tools for the requested tool names.
    /// </summary>
    IReadOnlyList<AIFunction> GetToolsByName(IEnumerable<string> names);

    /// <summary>
    /// Returns every tool name currently known to the catalog.
    /// </summary>
    IReadOnlyList<string> GetCatalogToolNames();

    /// <summary>
    /// Returns the tool names owned by the specified provider.
    /// </summary>
    IReadOnlyList<string> GetToolNamesByProvider(string providerName);

    /// <summary>
    /// Searches tool names and descriptions using a provider-defined keyword strategy.
    /// </summary>
    IReadOnlyList<string> SearchTools(string query);

    /// <summary>
    /// Returns the descriptor for the specified tool name, or <see langword="null"/> when not found.
    /// </summary>
    ToolDescriptor? GetDescriptor(string toolName);
}
