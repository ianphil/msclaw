using System.Text.Json;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;
using MsClaw.Gateway.Hosting;

namespace MsClaw.Gateway.Services.Tools;

/// <summary>
/// Creates the per-session expand_tools function used to lazily load additional tools.
/// </summary>
public sealed class ToolExpander : IToolExpander
{
    private static readonly TimeSpan DefaultSessionBindTimeout = TimeSpan.FromSeconds(5);

    private readonly IToolCatalog toolCatalog;
    private readonly IGatewayClient gatewayClient;
    private readonly TimeSpan sessionBindTimeout;

    /// <summary>
    /// Initializes the expander with the catalog and gateway client needed to mutate sessions.
    /// </summary>
    public ToolExpander(IToolCatalog toolCatalog, IGatewayClient gatewayClient)
        : this(toolCatalog, gatewayClient, DefaultSessionBindTimeout)
    {
    }

    /// <summary>
    /// Initializes the expander with an explicit session-bind timeout for targeted testing.
    /// </summary>
    internal ToolExpander(IToolCatalog toolCatalog, IGatewayClient gatewayClient, TimeSpan sessionBindTimeout)
    {
        ArgumentNullException.ThrowIfNull(toolCatalog);
        ArgumentNullException.ThrowIfNull(gatewayClient);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sessionBindTimeout, TimeSpan.Zero);

        this.toolCatalog = toolCatalog;
        this.gatewayClient = gatewayClient;
        this.sessionBindTimeout = sessionBindTimeout;
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
                await ExpandToolsAsync(sessionHolder, currentSessionTools, names, query, cancellationToken),
            "expand_tools",
            "Searches the registered tool catalog or lazily enables additional tools on the current session.");
    }

    /// <summary>
    /// Resolves query and load requests for expand_tools.
    /// </summary>
    private async Task<object> ExpandToolsAsync(
        SessionHolder sessionHolder,
        IList<AIFunction> currentSessionTools,
        string[]? names,
        string? query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) is false)
        {
            var matches = toolCatalog.SearchTools(query);

            return new ExpandToolsQueryResult(matches.ToArray(), matches.Count);
        }

        if (names is null || names.Length == 0)
        {
            return new ExpandToolsLoadResult([], [], 0, "Provide either query or names.");
        }

        var resolvedNames = ResolveRequestedToolNames(names);
        var requestedTools = toolCatalog.GetToolsByName(resolvedNames);
        var enabledNames = AppendMissingTools(currentSessionTools, requestedTools);
        var skippedNames = resolvedNames
            .Except(enabledNames, StringComparer.Ordinal)
            .ToArray();

        if (enabledNames.Count == 0)
        {
            return new ExpandToolsLoadResult([], skippedNames, 0, null);
        }

        var session = await TryGetBoundSessionAsync(sessionHolder, cancellationToken);
        if (session is null)
        {
            return new ExpandToolsLoadResult([], skippedNames, 0, "Session is not yet bound.");
        }

        await gatewayClient.ResumeSessionAsync(
            session.SessionId,
            new ResumeSessionConfig
            {
                Tools = currentSessionTools
            },
            cancellationToken);

        return new ExpandToolsLoadResult(enabledNames.ToArray(), skippedNames, enabledNames.Count, null);
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
    /// Waits for the session binding to complete and converts timeout into a tool-shaped error result.
    /// </summary>
    private async Task<IGatewaySession?> TryGetBoundSessionAsync(SessionHolder sessionHolder, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(sessionBindTimeout);

        try
        {
            return await sessionHolder.GetSessionAsync().WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested is false)
        {
            return null;
        }
    }

    /// <summary>
    /// Represents the JSON result returned when expand_tools enables tools.
    /// </summary>
    private sealed record ExpandToolsLoadResult(string[] Enabled, string[] Skipped, int Count, string? Error);

    /// <summary>
    /// Represents the JSON result returned when expand_tools performs a search-only query.
    /// </summary>
    private sealed record ExpandToolsQueryResult(string[] Matches, int Count);
}
