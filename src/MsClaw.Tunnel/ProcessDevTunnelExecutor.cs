using System.Diagnostics;

namespace MsClaw.Tunnel;

internal sealed class ProcessDevTunnelExecutor : IDevTunnelExecutor
{
    public async Task<DevTunnelCommandResult> RunAsync(string cliPath, string arguments, CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = cliPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new DevTunnelCommandResult(process.ExitCode, standardOutput, standardError);
    }

    public IDevTunnelHostHandle CreateHost(string cliPath, string arguments)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = cliPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        return new ProcessDevTunnelHostHandle(process);
    }
}
