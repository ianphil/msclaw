using System.Diagnostics;

namespace MsClaw.Tunnel;

/// <summary>
/// Default <see cref="ICommandRunner"/> that delegates to <see cref="Process"/>.
/// </summary>
public sealed class SystemCommandRunner : ICommandRunner
{
    /// <inheritdoc />
    public CommandRunnerResult Run(string fileName, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        return new CommandRunnerResult(process.ExitCode, output);
    }
}
