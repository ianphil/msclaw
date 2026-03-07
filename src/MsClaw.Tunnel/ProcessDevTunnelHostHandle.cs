using System.Diagnostics;

namespace MsClaw.Tunnel;

internal sealed class ProcessDevTunnelHostHandle(Process process) : IDevTunnelHostHandle
{
    public event Action<string>? OutputLine;

    public event Action<string>? ErrorLine;

    public bool HasExited => process.HasExited;

    public int? ExitCode => process.HasExited ? process.ExitCode : null;

    public void Start()
    {
        process.OutputDataReceived += OnOutputDataReceived;
        process.ErrorDataReceived += OnErrorDataReceived;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (process.HasExited)
        {
            return Task.CompletedTask;
        }

        process.Kill(entireProcessTree: true);
        return process.WaitForExitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (process.HasExited is false)
        {
            await StopAsync();
        }

        process.OutputDataReceived -= OnOutputDataReceived;
        process.ErrorDataReceived -= OnErrorDataReceived;
        process.Dispose();
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Data) is false)
        {
            OutputLine?.Invoke(args.Data);
        }
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Data) is false)
        {
            ErrorLine?.Invoke(args.Data);
        }
    }
}
