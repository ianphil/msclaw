namespace MsClaw.Gateway.Services.Cron;

/// <summary>
/// Pure state-transition functions that apply execution results to persisted job state.
/// Extracted from <see cref="CronEngine"/> for independent testability and single responsibility.
/// </summary>
public static class CronJobLifecycle
{
    private static readonly TimeSpan[] BackoffSteps =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(60)
    ];

    private const int MaxOneShotFailures = 3;

    /// <summary>
    /// Applies the execution result to the persisted job lifecycle state.
    /// </summary>
    /// <param name="job">The original job definition.</param>
    /// <param name="result">The execution result.</param>
    /// <param name="startedAtUtc">The execution start time.</param>
    /// <param name="completedAtUtc">The execution completion time.</param>
    /// <returns>The updated persisted job state.</returns>
    public static CronJob ApplyResult(
        CronJob job,
        CronRunResult result,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(result);

        return result.Outcome switch
        {
            CronRunOutcome.Success => ApplySuccess(job, startedAtUtc, completedAtUtc),
            CronRunOutcome.Failure => ApplyFailure(job, result, startedAtUtc, completedAtUtc),
            _ => throw new ArgumentOutOfRangeException(nameof(result), $"Unsupported run outcome '{result.Outcome}'.")
        };
    }

    /// <summary>
    /// Creates the updated job state for a successful execution.
    /// </summary>
    private static CronJob ApplySuccess(
        CronJob job,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc)
    {
        if (job.Schedule is OneShotSchedule)
        {
            return job with
            {
                Status = CronJobStatus.Disabled,
                LastRunAtUtc = startedAtUtc,
                NextRunAtUtc = null,
                Backoff = null
            };
        }

        return job with
        {
            Status = CronJobStatus.Enabled,
            LastRunAtUtc = startedAtUtc,
            NextRunAtUtc = CronScheduleCalculator.ComputeNextRun(job.Schedule, startedAtUtc, completedAtUtc),
            Backoff = null
        };
    }

    /// <summary>
    /// Creates the updated job state for a failed execution.
    /// </summary>
    private static CronJob ApplyFailure(
        CronJob job,
        CronRunResult result,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc)
    {
        var backoff = ComputeBackoff(job.Backoff, completedAtUtc, result.ErrorMessage ?? "Cron job failed.");

        if (job.Schedule is OneShotSchedule)
        {
            var failureCount = backoff.ConsecutiveFailures;
            if (result.IsTransient && failureCount < MaxOneShotFailures)
            {
                return job with
                {
                    Status = CronJobStatus.Enabled,
                    LastRunAtUtc = startedAtUtc,
                    Backoff = backoff
                };
            }

            return job with
            {
                Status = CronJobStatus.Disabled,
                LastRunAtUtc = startedAtUtc,
                NextRunAtUtc = null,
                Backoff = null
            };
        }

        return job with
        {
            Status = CronJobStatus.Enabled,
            LastRunAtUtc = startedAtUtc,
            Backoff = backoff
        };
    }

    /// <summary>
    /// Creates the next backoff state using the configured exponential schedule.
    /// </summary>
    /// <param name="existingBackoff">The existing backoff state, if any.</param>
    /// <param name="now">The current completion time.</param>
    /// <param name="errorMessage">The failure message to retain.</param>
    /// <returns>The next backoff state.</returns>
    internal static BackoffState ComputeBackoff(BackoffState? existingBackoff, DateTimeOffset now, string errorMessage)
    {
        var consecutiveFailures = (existingBackoff?.ConsecutiveFailures ?? 0) + 1;
        var backoffIndex = Math.Min(consecutiveFailures - 1, BackoffSteps.Length - 1);
        var delay = BackoffSteps[backoffIndex];

        return new BackoffState(consecutiveFailures, now.Add(delay), errorMessage);
    }
}
