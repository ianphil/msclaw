namespace MsClaw.Gateway.Services.Cron;

/// <summary>
/// Classifies cron execution failures as transient or permanent.
/// </summary>
public interface ICronErrorClassifier
{
    /// <summary>
    /// Returns <see langword="true"/> when the exception should be treated as retryable.
    /// </summary>
    /// <param name="exception">The execution failure to classify.</param>
    bool IsTransient(Exception exception);
}
