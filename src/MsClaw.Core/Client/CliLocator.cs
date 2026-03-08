using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MsClaw.Core;

public static class CliLocator
{
    public static string ResolveCopilotCliPath() => ResolveCliPath("copilot");

    internal static string ResolveCliPath(string binaryName)
    {
        var candidates = FindCandidatesOnPath(binaryName);
        if (candidates.Length == 0)
        {
            throw new InvalidOperationException(
                $"Copilot CLI not found on PATH. " +
                $"Ensure '{binaryName}' is installed and available on your system PATH.");
        }

        return SelectPreferredCandidate(candidates);
    }

    private static string[] FindCandidatesOnPath(string binaryName)
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
                return [];
            }

            return output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();
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

    /// <summary>
    /// On Windows, prefers .exe over .cmd over bare scripts.
    /// On other platforms, returns the first candidate.
    /// </summary>
    private static string SelectPreferredCandidate(string[] candidates)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var exe = candidates.FirstOrDefault(l => l.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            if (exe is not null) return exe;

            var cmd = candidates.FirstOrDefault(l => l.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase));
            if (cmd is not null) return cmd;
        }

        return candidates[0];
    }
}
