namespace MsClaw.Gateway.Services.Cron;

/// <summary>
/// Executes a cron job for a specific payload type and returns a payload-agnostic result.
/// </summary>
public interface ICronJobExecutor
{
    /// <summary>
    /// Gets the payload type handled by this executor.
    /// </summary>
    Type PayloadType { get; }

    /// <summary>
    /// Executes the specified job run.
    /// </summary>
    /// <param name="job">The job definition to execute.</param>
    /// <param name="runId">The unique identifier for this execution attempt.</param>
    /// <param name="cancellationToken">Cancels the execution.</param>
    /// <returns>The payload-agnostic execution result.</returns>
    Task<CronRunResult> ExecuteAsync(CronJob job, string runId, CancellationToken cancellationToken);
}
