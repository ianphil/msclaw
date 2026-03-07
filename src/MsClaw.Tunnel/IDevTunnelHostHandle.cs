namespace MsClaw.Tunnel;

internal interface IDevTunnelHostHandle : IAsyncDisposable
{
    event Action<string>? OutputLine;

    event Action<string>? ErrorLine;

    bool HasExited { get; }

    int? ExitCode { get; }

    void Start();

    Task StopAsync(CancellationToken cancellationToken = default);
}
