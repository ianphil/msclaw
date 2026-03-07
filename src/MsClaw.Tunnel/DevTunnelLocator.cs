using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MsClaw.Tunnel;

/// <summary>
/// Resolves the devtunnel CLI binary from PATH.
/// </summary>
public sealed class DevTunnelLocator : IDevTunnelLocator
{
    /// <inheritdoc />
    public string ResolveDevTunnelCliPath()
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var command = isWindows ? "where" : "which";
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = "devtunnel",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("devtunnel CLI not found on PATH. Install it and ensure it is available.");
        }

        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
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
