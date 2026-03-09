using System.Diagnostics;

namespace MsClaw.Gateway.Services.Cron;

/// <summary>
/// Executes command payloads directly on the host without creating a model session.
/// </summary>
public sealed class CommandJobExecutor : ICronJobExecutor
{

    /// <inheritdoc />
    public Type PayloadType => typeof(CommandPayload);

    /// <inheritdoc />
    public async Task<CronRunResult> ExecuteAsync(CronJob job, string runId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        if (job.Payload is not CommandPayload payload)
        {
            throw new ArgumentException($"Job payload must be a {nameof(CommandPayload)}.", nameof(job));
        }

        var stopwatch = Stopwatch.StartNew();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = payload.Command,
                Arguments = payload.Arguments ?? string.Empty,
                WorkingDirectory = string.IsNullOrWhiteSpace(payload.WorkingDirectory)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    : payload.WorkingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            if (process.Start() is false)
            {
                stopwatch.Stop();
                return new CronRunResult(string.Empty, CronRunOutcome.Failure, "The process could not be started.", stopwatch.ElapsedMilliseconds, false);
            }

            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(payload.TimeoutSeconds));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested is false)
            {
                await KillTimedOutProcessAsync(process);
                stopwatch.Stop();
                var timedOutOutput = await CombineOutputAsync(standardOutputTask, standardErrorTask);

                return new CronRunResult(
                    timedOutOutput,
                    CronRunOutcome.Failure,
                    $"The command timed out after {payload.TimeoutSeconds} seconds.",
                    stopwatch.ElapsedMilliseconds,
                    true);
            }

            var output = await CombineOutputAsync(standardOutputTask, standardErrorTask);
            stopwatch.Stop();

            return process.ExitCode switch
            {
                0 => new CronRunResult(output, CronRunOutcome.Success, null, stopwatch.ElapsedMilliseconds, false),
                _ => new CronRunResult(output, CronRunOutcome.Failure, $"The command exited with code {process.ExitCode}.", stopwatch.ElapsedMilliseconds, false)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            return new CronRunResult(string.Empty, CronRunOutcome.Failure, ex.Message, stopwatch.ElapsedMilliseconds, false);
        }
    }

    /// <summary>
    /// Combines stdout and stderr into a single text payload while preserving both streams.
    /// </summary>
    /// <param name="standardOutputTask">The asynchronous stdout read.</param>
    /// <param name="standardErrorTask">The asynchronous stderr read.</param>
    private static async Task<string> CombineOutputAsync(Task<string> standardOutputTask, Task<string> standardErrorTask)
    {
        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;
        var segments = new[] { standardOutput.TrimEnd(), standardError.TrimEnd() }
            .Where(static segment => string.IsNullOrWhiteSpace(segment) is false);

        return string.Join(Environment.NewLine, segments);
    }

    /// <summary>
    /// Terminates a timed-out process and waits for the process tree to exit.
    /// </summary>
    /// <param name="process">The timed-out process.</param>
    private static async Task KillTimedOutProcessAsync(Process process)
    {
        if (process.HasExited is false)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }
}
