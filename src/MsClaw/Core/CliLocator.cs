using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MsClaw.Core;

public static class CliLocator
{
    public static string ResolveCopilotCliPath() => ResolveCliPath("copilot");

    internal static string ResolveCliPath(string binaryName)
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var command = isWindows ? "where" : "which";

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = binaryName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                throw new InvalidOperationException(
                    $"Copilot CLI not found on PATH. " +
                    $"Ensure '{binaryName}' is installed and available on your system PATH.");
            }

            var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();

            // On Windows, 'where' may return multiple matches (e.g. bare script, .cmd, .exe).
            // Prefer .exe, then .cmd — bare extensionless scripts are not directly executable.
            if (isWindows)
            {
                var exe = lines.FirstOrDefault(l => l.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                if (exe != null) return exe;

                var cmd = lines.FirstOrDefault(l => l.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase));
                if (cmd != null) return cmd;
            }

            return lines[0];
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to locate Copilot CLI on PATH using '{command} {binaryName}': {ex.Message}", ex);
        }
    }
}
