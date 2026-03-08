using System.Runtime.InteropServices;

namespace MsClaw.Tunnel;

/// <summary>
/// Resolves the devtunnel CLI binary from PATH.
/// </summary>
public sealed class DevTunnelLocator(ICommandRunner commandRunner) : IDevTunnelLocator
{
    /// <summary>
    /// Creates a locator using the default <see cref="SystemCommandRunner"/>.
    /// </summary>
    public DevTunnelLocator() : this(new SystemCommandRunner())
    {
    }

    /// <inheritdoc />
    public string ResolveDevTunnelCliPath()
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var command = isWindows ? "where" : "which";
        var result = commandRunner.Run(command, "devtunnel");

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
        {
            throw new InvalidOperationException("devtunnel CLI not found on PATH. Install it and ensure it is available.");
        }

        var lines = result.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(static value => value.Trim())
            .Where(static value => string.IsNullOrWhiteSpace(value) is false)
            .ToArray();

        if (isWindows)
        {
            var exePath = lines.FirstOrDefault(static value => value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            if (exePath is not null)
            {
                return exePath;
            }

            var cmdPath = lines.FirstOrDefault(static value => value.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase));
            if (cmdPath is not null)
            {
                return cmdPath;
            }
        }

        return lines[0];
    }
}
