using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MsClaw.Core;

public static class CliLocator
{
    public static string ResolveCopilotCliPath()
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var command = isWindows ? "where" : "which";

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = "copilot",
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
                    "Copilot CLI not found on PATH. " +
                    "Install it via npm (`npm install -g @anthropic/copilot`) and ensure it is available on your system PATH.");
            }

            // 'where' on Windows can return multiple lines; take the first match
            var resolved = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            return resolved;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to locate Copilot CLI on PATH using '{command} copilot': {ex.Message}", ex);
        }
    }
}
