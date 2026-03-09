using Microsoft.Extensions.AI;

namespace MsClaw.Gateway.Services.Tools;

/// <summary>
/// Creates the per-session expand_tools function used to lazily load additional tools.
/// </summary>
public sealed class ToolExpander : IToolExpander
{
    private readonly IToolCatalog toolCatalog;

    /// <summary>
    /// Initializes the expander with the tool catalog for discovery and lookup.
    /// </summary>
    public ToolExpander(IToolCatalog toolCatalog)
    {
        ArgumentNullException.ThrowIfNull(toolCatalog);
        this.toolCatalog = toolCatalog;
    }

    /// <summary>
    /// Creates an expand_tools function bound to the supplied session holder and mutable tool list.
    /// </summary>
    public AIFunction CreateExpandToolsFunction(SessionHolder sessionHolder, IList<AIFunction> currentSessionTools)
    {
        ArgumentNullException.ThrowIfNull(sessionHolder);
        ArgumentNullException.ThrowIfNull(currentSessionTools);

        return AIFunctionFactory.Create(
            async (string[]? names = null, string? query = null, CancellationToken cancellationToken = default) =>
                await ExpandToolsAsync(currentSessionTools, names, query, cancellationToken),
            "expand_tools",
            "Searches the registered tool catalog or lazily enables additional tools on the current session.");
    }

    /// <summary>
    /// Resolves query and load requests for expand_tools.
    /// </summary>
    private Task<object> ExpandToolsAsync(
        IList<AIFunction> currentSessionTools,
        string[]? names,
        string? query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) is false)
        {
            var matches = toolCatalog.SearchTools(query);

            return Task.FromResult<object>(new ExpandToolsQueryResult(matches.ToArray(), matches.Count));
        }

        if (names is null || names.Length == 0)
        {
            return Task.FromResult<object>(new ExpandToolsLoadResult([], [], 0, "Provide either query or names.", null));
        }

        var resolvedNames = ResolveRequestedToolNames(names);
        var requestedTools = toolCatalog.GetToolsByName(resolvedNames);
        var enabledNames = AppendMissingTools(currentSessionTools, requestedTools);
        var skippedNames = resolvedNames
            .Except(enabledNames, StringComparer.Ordinal)
            .ToArray();

        if (enabledNames.Count == 0)
        {
            return Task.FromResult<object>(new ExpandToolsLoadResult([], skippedNames, 0, null, null));
        }

        return Task.FromResult<object>(new ExpandToolsLoadResult(
            enabledNames.ToArray(), skippedNames, enabledNames.Count, null,
            "Tools will be callable on the next message. Tell the user to send a follow-up."));
    }

    /// <summary>
    /// Expands provider names into their registered tool names while preserving explicit tool names.
    /// </summary>
    private List<string> ResolveRequestedToolNames(IEnumerable<string> names)
    {
        var resolved = new List<string>();

        foreach (var name in names.Where(static name => string.IsNullOrWhiteSpace(name) is false))
        {
            var providerToolNames = toolCatalog.GetToolNamesByProvider(name);
            if (providerToolNames.Count > 0)
            {
                foreach (var providerToolName in providerToolNames)
                {
                    if (resolved.Contains(providerToolName, StringComparer.Ordinal) is false)
                    {
                        resolved.Add(providerToolName);
                    }
                }

                continue;
            }

            if (resolved.Contains(name, StringComparer.Ordinal) is false)
            {
                resolved.Add(name);
            }
        }

        return resolved;
    }

    /// <summary>
    /// Appends only new tools to the mutable session list and returns the names newly enabled.
    /// </summary>
    private static List<string> AppendMissingTools(IList<AIFunction> currentSessionTools, IReadOnlyList<AIFunction> requestedTools)
    {
        var existingToolNames = currentSessionTools
            .Select(static tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);
        var enabledNames = new List<string>();

        foreach (var tool in requestedTools)
        {
            if (existingToolNames.Add(tool.Name))
            {
                currentSessionTools.Add(tool);
                enabledNames.Add(tool.Name);
            }
        }

        return enabledNames;
    }

    /// <summary>
    /// Represents the JSON result returned when expand_tools enables tools.
    /// </summary>
    private sealed record ExpandToolsLoadResult(string[] Enabled, string[] Skipped, int Count, string? Error, string? Note);

    /// <summary>
    /// Represents the JSON result returned when expand_tools performs a search-only query.
    /// </summary>
    private sealed record ExpandToolsQueryResult(string[] Matches, int Count);
}
