namespace MsClaw.Tunnel;

/// <summary>
/// Abstracts execution of system commands (e.g., <c>where</c> / <c>which</c>) for testability.
/// </summary>
public interface ICommandRunner
{
    /// <summary>
    /// Runs a command and returns its standard output and exit code.
    /// </summary>
    CommandRunnerResult Run(string fileName, string arguments);
}

/// <summary>
/// Represents the result of a command execution.
/// </summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="Output">The trimmed standard output content.</param>
public sealed record CommandRunnerResult(int ExitCode, string Output);
