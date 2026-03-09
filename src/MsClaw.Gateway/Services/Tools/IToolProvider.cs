namespace MsClaw.Gateway.Services.Tools;

/// <summary>
/// Defines the contract for a tool source that can discover tools and signal surface changes.
/// </summary>
public interface IToolProvider : IAsyncDisposable
{
    /// <summary>
    /// Gets the unique provider name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the source tier used for collision resolution.
    /// </summary>
    ToolSourceTier Tier { get; }

    /// <summary>
    /// Discovers the current tool surface for the provider.
    /// </summary>
    Task<IReadOnlyList<ToolDescriptor>> DiscoverAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Waits until the provider's tool surface may have changed.
    /// </summary>
    Task WaitForSurfaceChangeAsync(CancellationToken cancellationToken);
}
