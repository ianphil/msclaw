using System.Text.Json.Serialization;

namespace MsClaw.Gateway.Services.Cron;

/// <summary>
/// Represents the persisted lifecycle state of a cron job.
/// </summary>
public enum CronJobStatus
{
    Enabled,
    Disabled
}

/// <summary>
/// Represents the persisted definition of a scheduled cron job.
/// </summary>
public sealed record CronJob
{
    /// <summary>
    /// Gets the unique job identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Gets the human-readable job name.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Gets the job schedule definition.
    /// </summary>
    [JsonPropertyName("schedule")]
    public required JobSchedule Schedule { get; init; }

    /// <summary>
    /// Gets the execution payload for the job.
    /// </summary>
    [JsonPropertyName("payload")]
    public required JobPayload Payload { get; init; }

    /// <summary>
    /// Gets the persisted status for the job.
    /// </summary>
    [JsonPropertyName("status")]
    public required CronJobStatus Status { get; init; }

    /// <summary>
    /// Gets the maximum allowed concurrency for the job.
    /// </summary>
    [JsonPropertyName("maxConcurrency")]
    public int MaxConcurrency { get; init; } = 1;

    /// <summary>
    /// Gets when the job was created in UTC.
    /// </summary>
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets when the job last ran in UTC.
    /// </summary>
    [JsonPropertyName("lastRunAtUtc")]
    public DateTimeOffset? LastRunAtUtc { get; init; }

    /// <summary>
    /// Gets when the job is next due to run in UTC.
    /// </summary>
    [JsonPropertyName("nextRunAtUtc")]
    public DateTimeOffset? NextRunAtUtc { get; init; }

    /// <summary>
    /// Gets the current retry backoff state, if any.
    /// </summary>
    [JsonPropertyName("backoff")]
    public BackoffState? Backoff { get; init; }
}

/// <summary>
/// Represents retry backoff metadata for a failed cron job.
/// </summary>
public sealed record BackoffState(
    [property: JsonPropertyName("consecutiveFailures")] int ConsecutiveFailures,
    [property: JsonPropertyName("nextRetryAtUtc")] DateTimeOffset NextRetryAtUtc,
    [property: JsonPropertyName("lastErrorMessage")] string LastErrorMessage);

/// <summary>
/// Represents a persisted cron job schedule definition.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(OneShotSchedule), "oneShot")]
[JsonDerivedType(typeof(FixedIntervalSchedule), "fixedInterval")]
[JsonDerivedType(typeof(CronExpressionSchedule), "cron")]
public abstract record JobSchedule;

/// <summary>
/// Represents a single-fire schedule at an exact UTC timestamp.
/// </summary>
/// <param name="FireAtUtc">Gets the instant when the job should fire.</param>
public sealed record OneShotSchedule(
    [property: JsonPropertyName("fireAtUtc")] DateTimeOffset FireAtUtc) : JobSchedule;

/// <summary>
/// Represents a recurring fixed-interval schedule.
/// </summary>
/// <param name="IntervalMs">Gets the interval in milliseconds.</param>
public sealed record FixedIntervalSchedule(
    [property: JsonPropertyName("intervalMs")] long IntervalMs) : JobSchedule;

/// <summary>
/// Represents a cron-expression-based schedule.
/// </summary>
/// <param name="Expression">Gets the cron expression.</param>
/// <param name="Timezone">Gets the optional IANA timezone identifier.</param>
public sealed record CronExpressionSchedule(
    [property: JsonPropertyName("expression")] string Expression,
    [property: JsonPropertyName("timezone")] string? Timezone) : JobSchedule;

/// <summary>
/// Represents the execution payload for a cron job.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PromptPayload), "prompt")]
[JsonDerivedType(typeof(CommandPayload), "command")]
public abstract record JobPayload;

/// <summary>
/// Represents a prompt-based cron payload executed in an isolated model session.
/// </summary>
/// <param name="Prompt">Gets the prompt sent to the isolated session.</param>
/// <param name="PreloadToolNames">Gets the optional tools to preload before execution.</param>
/// <param name="Model">Gets the optional model override.</param>
public sealed record PromptPayload(
    [property: JsonPropertyName("prompt")] string Prompt,
    [property: JsonPropertyName("preloadToolNames")] string[]? PreloadToolNames,
    [property: JsonPropertyName("model")] string? Model) : JobPayload;

/// <summary>
/// Represents a command-based cron payload executed on the host.
/// </summary>
/// <param name="Command">Gets the executable or command name.</param>
/// <param name="Arguments">Gets the optional command arguments.</param>
/// <param name="WorkingDirectory">Gets the optional working directory.</param>
/// <param name="TimeoutSeconds">Gets the timeout in seconds.</param>
public sealed record CommandPayload(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("arguments")] string? Arguments,
    [property: JsonPropertyName("workingDirectory")] string? WorkingDirectory,
    [property: JsonPropertyName("timeoutSeconds")] int TimeoutSeconds = 300) : JobPayload;
