using System.Text.Json.Serialization;

namespace MsClaw.Gateway.Services.Cron;

/// <summary>
/// Represents the outcome of a cron job execution.
/// </summary>
public enum CronRunOutcome
{
    Success,
    Failure
}

/// <summary>
/// Represents a payload-agnostic execution result from a cron job run.
/// </summary>
/// <param name="Content">Gets the output content produced by the run.</param>
/// <param name="Outcome">Gets whether the run succeeded or failed.</param>
/// <param name="ErrorMessage">Gets the optional error message when the run fails.</param>
/// <param name="DurationMs">Gets the execution duration in milliseconds.</param>
/// <param name="IsTransient">Gets whether the failure is considered transient.</param>
public sealed record CronRunResult(
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("outcome")] CronRunOutcome Outcome,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage,
    [property: JsonPropertyName("durationMs")] long DurationMs,
    [property: JsonPropertyName("isTransient")] bool IsTransient);
