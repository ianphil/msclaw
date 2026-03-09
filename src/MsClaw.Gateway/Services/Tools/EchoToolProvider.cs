using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace MsClaw.Gateway.Services.Tools;

/// <summary>
/// A minimal tool provider that exposes an echo tool for manual testing.
/// </summary>
public sealed class EchoToolProvider : IToolProvider
{
    /// <inheritdoc />
    public string Name => "echo";

    /// <inheritdoc />
    public ToolSourceTier Tier => ToolSourceTier.Workspace;

    /// <inheritdoc />
    public Task<IReadOnlyList<ToolDescriptor>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var fn = AIFunctionFactory.Create(
            ([Description("The text to echo back")] string text) => $"Echo: {text}",
            "echo_text",
            "Echoes the input text back to the caller. Useful for verifying tool bridge wiring.");

        ToolDescriptor descriptor = new()
        {
            Function = fn,
            ProviderName = Name,
            Tier = Tier,
            AlwaysVisible = false
        };

        return Task.FromResult<IReadOnlyList<ToolDescriptor>>([descriptor]);
    }

    /// <inheritdoc />
    public Task WaitForSurfaceChangeAsync(CancellationToken cancellationToken)
    {
        return Task.Delay(Timeout.Infinite, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
