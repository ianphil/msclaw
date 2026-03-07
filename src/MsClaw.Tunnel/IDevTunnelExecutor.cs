namespace MsClaw.Tunnel;

internal interface IDevTunnelExecutor
{
    Task<DevTunnelCommandResult> RunAsync(string cliPath, string arguments, CancellationToken cancellationToken = default);

    IDevTunnelHostHandle CreateHost(string cliPath, string arguments);
}
