namespace MsClaw.Gateway.Services.Cron;

/// <summary>
/// Persists cron job execution history separately from job CRUD operations.
/// </summary>
public interface ICronRunHistoryStore
{
    /// <summary>
    /// Appends a run record to the job history.
    /// </summary>
    /// <param name="record">The run record to append.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    Task AppendRunRecordAsync(CronRunRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all persisted run records for a job.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>The persisted run records.</returns>
    Task<IReadOnlyList<CronRunRecord>> GetRunHistoryAsync(string jobId, CancellationToken cancellationToken = default);
}
