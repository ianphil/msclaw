using Microsoft.Extensions.AI;

namespace MsClaw.Gateway.Services.Tools;

/// <summary>
/// Defines source priority for tool collision resolution.
/// </summary>
public enum ToolSourceTier
{
    Bundled,
    Workspace,
    Managed
}

/// <summary>
/// Defines the operational readiness state for a discovered tool.
/// </summary>
public enum ToolStatus
{
    Ready,
    Degraded,
    Unavailable
}

/// <summary>
/// Wraps an <see cref="AIFunction"/> with catalog metadata needed by the gateway tool bridge.
/// </summary>
public sealed record ToolDescriptor
{
    /// <summary>
    /// Gets the SDK function that provides the tool name, description, and schema.
    /// </summary>
    public required AIFunction Function { get; init; }

    /// <summary>
    /// Gets the provider that owns the tool.
    /// </summary>
    public required string ProviderName { get; init; }

    /// <summary>
    /// Gets the source tier used for collision resolution.
    /// </summary>
    public required ToolSourceTier Tier { get; init; }

    /// <summary>
    /// Gets a value indicating whether the tool should be included on every new session.
    /// </summary>
    public bool AlwaysVisible { get; init; }
}
