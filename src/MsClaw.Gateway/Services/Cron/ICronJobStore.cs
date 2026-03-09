namespace MsClaw.Gateway.Services.Cron;

/// <summary>
/// Persists cron job definitions and serves them from an in-memory cache.
/// </summary>
public interface ICronJobStore
{
    /// <summary>
    /// Loads persisted jobs into memory.
    /// </summary>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all known jobs from the in-memory cache.
    /// </summary>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>The cached jobs.</returns>
    Task<IReadOnlyList<CronJob>> GetAllJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a job by identifier from the in-memory cache.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>The matching job, or <see langword="null"/> when not found.</returns>
    Task<CronJob?> GetJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new job and flushes the store to disk.
    /// </summary>
    /// <param name="job">The job to add.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    Task AddJobAsync(CronJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing job and flushes the store to disk.
    /// </summary>
    /// <param name="job">The job to update.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    Task UpdateJobAsync(CronJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a job and flushes the store to disk.
    /// </summary>
    /// <param name="jobId">The job identifier to remove.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    Task RemoveJobAsync(string jobId, CancellationToken cancellationToken = default);
}
