using System.Diagnostics;

namespace MsClaw.Core;

public sealed class MindReader : IMindReader
{
    private readonly string _mindRoot;
    private readonly bool _autoGitPull;

    public MindReader(string mindRoot, bool autoGitPull = false)
    {
        _mindRoot = Path.GetFullPath(mindRoot);
        _autoGitPull = autoGitPull;
    }

    public async Task EnsureSyncedAsync(CancellationToken cancellationToken = default)
    {
        if (!_autoGitPull)
        {
            return;
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"-C \"{_mindRoot}\" pull --ff-only --quiet",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };

        process.Start();

        // Read redirected streams before WaitForExitAsync to avoid pipe buffer deadlocks.
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stderr = await stderrTask;
        _ = await stdoutTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git pull failed (exit code {process.ExitCode}): {stderr}");
        }
    }

    public async Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        await EnsureSyncedAsync(cancellationToken);
        var fullPath = ResolvePath(path);
        return await File.ReadAllTextAsync(fullPath, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        await EnsureSyncedAsync(cancellationToken);
        var fullPath = ResolvePath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }

        return Directory.EnumerateFileSystemEntries(fullPath)
            .Select(p => Path.GetRelativePath(_mindRoot, p))
            .ToArray();
    }

    private string ResolvePath(string path)
    {
        var relative = path.Replace('\\', '/').TrimStart('/');
        var candidate = Path.GetFullPath(Path.Combine(_mindRoot, relative));

        if (!candidate.StartsWith(_mindRoot, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Path traversal outside mind root is not allowed.");
        }

        return candidate;
    }
}
