using Microsoft.Extensions.Hosting;

namespace MsClaw.Gateway.Services.Cron;

/// <summary>
/// Manages cron job evaluation, dispatch, and in-memory running state.
/// </summary>
public interface ICronEngine : IHostedService
{
    /// <summary>
    /// Gets whether the engine background loop is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets the number of jobs currently executing.
    /// </summary>
    int ActiveJobCount { get; }

    /// <summary>
    /// Returns whether the specified job is currently executing.
    /// </summary>
    /// <param name="jobId">The job identifier to inspect.</param>
    /// <returns><see langword="true"/> when the job is active; otherwise, <see langword="false"/>.</returns>
    bool IsJobActive(string jobId);
}
