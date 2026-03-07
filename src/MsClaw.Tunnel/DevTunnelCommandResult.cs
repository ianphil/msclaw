namespace MsClaw.Tunnel;

internal sealed record DevTunnelCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
