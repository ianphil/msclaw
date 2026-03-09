using System.Text.Json;

namespace MsClaw.Gateway.Services.Cron;

/// <summary>
/// Applies the default transient-vs-permanent classification used by cron retries.
/// </summary>
public sealed class DefaultCronErrorClassifier : ICronErrorClassifier
{
    /// <inheritdoc />
    public bool IsTransient(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            HttpRequestException => true,
            TaskCanceledException => true,
            IOException => true,
            UnauthorizedAccessException => false,
            ArgumentException => false,
            JsonException => false,
            _ => false
        };
    }
}
