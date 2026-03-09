using System.Text.Json.Serialization;

namespace MsClaw.Gateway.Services.Cron;

/// <summary>
/// Represents a single persisted cron run history entry.
/// </summary>
/// <param name="RunId">Gets the unique run identifier.</param>
/// <param name="JobId">Gets the parent job identifier.</param>
/// <param name="StartedAtUtc">Gets when the run started in UTC.</param>
/// <param name="CompletedAtUtc">Gets when the run completed in UTC.</param>
/// <param name="Outcome">Gets whether the run succeeded or failed.</param>
/// <param name="ErrorMessage">Gets the optional error message when the run fails.</param>
/// <param name="DurationMs">Gets the run duration in milliseconds.</param>
public sealed record CronRunRecord(
    [property: JsonPropertyName("runId")] string RunId,
    [property: JsonPropertyName("jobId")] string JobId,
    [property: JsonPropertyName("startedAtUtc")] DateTimeOffset StartedAtUtc,
    [property: JsonPropertyName("completedAtUtc")] DateTimeOffset CompletedAtUtc,
    [property: JsonPropertyName("outcome")] CronRunOutcome Outcome,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage,
    [property: JsonPropertyName("durationMs")] long DurationMs);
