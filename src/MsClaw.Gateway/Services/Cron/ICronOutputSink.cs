namespace MsClaw.Gateway.Services.Cron;

/// <summary>
/// Publishes cron job execution results to external consumers.
/// </summary>
public interface ICronOutputSink
{
    /// <summary>
    /// Publishes a completed cron run result.
    /// </summary>
    /// <param name="result">The run event to publish.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    Task PublishResultAsync(CronRunEvent result, CancellationToken cancellationToken);
}

/// <summary>
/// Represents the published result of a completed cron run.
/// </summary>
/// <param name="JobId">Gets the job identifier.</param>
/// <param name="JobName">Gets the human-readable job name.</param>
/// <param name="RunId">Gets the run identifier.</param>
/// <param name="Outcome">Gets whether the run succeeded or failed.</param>
/// <param name="Content">Gets the output content produced by the run.</param>
/// <param name="ErrorMessage">Gets the optional error message for a failed run.</param>
/// <param name="DurationMs">Gets the run duration in milliseconds.</param>
public sealed record CronRunEvent(
    string JobId,
    string JobName,
    string RunId,
    CronRunOutcome Outcome,
    string Content,
    string? ErrorMessage,
    long DurationMs);
