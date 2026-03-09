using System.Security.Cryptography;
using System.Text;

namespace MsClaw.Gateway.Services.Cron;

/// <summary>
/// Computes deterministic stagger offsets so recurring jobs do not all fire at the same instant.
/// </summary>
public static class CronStaggerCalculator
{
    /// <summary>
    /// Returns a deterministic offset within the supplied window for the specified job.
    /// </summary>
    /// <param name="jobId">The unique job identifier.</param>
    /// <param name="window">The stagger window.</param>
    public static TimeSpan ComputeOffset(string jobId, TimeSpan window)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "The stagger window must be positive.");
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(jobId));
        var bucket = BitConverter.ToUInt64(hash, 0);
        var ticks = (long)(bucket % (ulong)window.Ticks);

        return TimeSpan.FromTicks(ticks);
    }
}
