# Cron System Interface Contracts

## ICronEngine

Manages job evaluation, dispatch, and lifecycle. Exposed as `IHostedService` for startup/shutdown.

```csharp
/// <summary>
/// Evaluates due jobs and dispatches them to the appropriate executor.
/// </summary>
public interface ICronEngine
{
    /// <summary>
    /// Gets whether the engine is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets the count of jobs currently executing.
    /// </summary>
    int ActiveJobCount { get; }
}
```

The engine is also registered as `IHostedService` — `StartAsync` creates the `PeriodicTimer` and starts the tick loop; `StopAsync` cancels the timer and waits for in-flight executions.

## ICronJobStore

Manages job persistence and run history.

```csharp
/// <summary>
/// Persists cron job definitions and run history to disk.
/// </summary>
public interface ICronJobStore
{
    /// <summary>
    /// Loads all jobs from the store. Returns empty list if store doesn't exist.
    /// </summary>
    Task<IReadOnlyList<CronJob>> LoadJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists all jobs to the store using atomic write-then-rename.
    /// </summary>
    Task SaveJobsAsync(IReadOnlyList<CronJob> jobs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new job to the store. Throws if a job with the same ID already exists.
    /// </summary>
    Task AddJobAsync(CronJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing job in the store. Throws if the job doesn't exist.
    /// </summary>
    Task UpdateJobAsync(CronJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a job from the store by ID. No-op if the job doesn't exist.
    /// </summary>
    Task RemoveJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a job by ID. Returns null if not found.
    /// </summary>
    Task<CronJob?> GetJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a run record to the job's history file. Auto-prunes if limits exceeded.
    /// </summary>
    Task AppendRunRecordAsync(CronRunRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the run history for a job. Returns empty list if no history exists.
    /// </summary>
    Task<IReadOnlyList<CronRunRecord>> GetRunHistoryAsync(string jobId, CancellationToken cancellationToken = default);
}
```

### Store Behavior

- **Load**: Reads `~/.msclaw/cron/jobs.json`. Returns empty list if file doesn't exist. Throws on parse error.
- **Save**: Writes to `jobs.json.tmp` then renames to `jobs.json` (atomic).
- **Add/Update/Remove**: Load → mutate → save (load-modify-save cycle).
- **History**: Per-job files at `~/.msclaw/cron/history/{jobId}.json`. Auto-prunes by size (2MB) and line count (2000).

## ICronJobExecutor

Abstraction for executing a job based on its payload type.

```csharp
/// <summary>
/// Executes a cron job and returns the result.
/// </summary>
public interface ICronJobExecutor
{
    /// <summary>
    /// Gets the payload type this executor handles.
    /// </summary>
    Type PayloadType { get; }

    /// <summary>
    /// Executes the job and returns a payload-agnostic result.
    /// </summary>
    Task<CronRunResult> ExecuteAsync(CronJob job, string runId, CancellationToken cancellationToken);
}
```

### Implementations

| Executor | PayloadType | Behavior |
|----------|-------------|----------|
| `PromptJobExecutor` | `typeof(PromptPayload)` | Creates isolated session via `ISessionPool`, sends prompt, waits for `SessionIdleEvent`, collects `AssistantMessageEvent.Content` |
| `CommandJobExecutor` | `typeof(CommandPayload)` | Runs `Process.Start()`, captures stdout/stderr, enforces timeout, returns combined output |

### Resolution Pattern

The `CronEngine` resolves executors from DI via `IEnumerable<ICronJobExecutor>` and selects based on `job.Payload.GetType()` matching `executor.PayloadType`.

## CronErrorClassifier

Static utility for classifying errors as transient or permanent.

```csharp
/// <summary>
/// Classifies execution errors as transient (retryable) or permanent.
/// </summary>
public static class CronErrorClassifier
{
    /// <summary>
    /// Returns true if the error is transient and the job should be retried.
    /// </summary>
    public static bool IsTransient(Exception exception);
}
```

### Classification Rules

| Error Type | Classification | Examples |
|------------|---------------|----------|
| Transient | Retryable | `HttpRequestException` (5xx, timeout), `TaskCanceledException`, `IOException` |
| Permanent | Not retryable | `UnauthorizedAccessException`, `ArgumentException`, `JsonException` |

## CronToolProvider Tools

| Tool Name | Parameters | Returns | Delegates To |
|-----------|-----------|---------|-------------|
| `cron_create` | `name`, `scheduleType`, `scheduleValue`, `timezone?`, `payloadType`, `prompt`/`command`, `preloadToolNames?`, `model?`, `arguments?`, `workingDirectory?` | Job summary | `ICronJobStore.AddJobAsync` |
| `cron_list` | (none) | All jobs with status, schedule, last/next run | `ICronJobStore.LoadJobsAsync` |
| `cron_get` | `jobId` | Single job details + recent history | `ICronJobStore.GetJobAsync` + `GetRunHistoryAsync` |
| `cron_update` | `jobId`, `name?`, `scheduleType?`, `scheduleValue?`, `timezone?`, `payloadType?`, `prompt?`/`command?`, `preloadToolNames?`, `model?`, `arguments?`, `workingDirectory?`, `maxConcurrency?` | Updated job summary | `ICronJobStore.UpdateJobAsync` |
| `cron_delete` | `jobId` | Confirmation | `ICronJobStore.RemoveJobAsync` |
| `cron_pause` | `jobId` | Confirmation | `ICronJobStore.UpdateJobAsync` (status → Disabled) |
| `cron_resume` | `jobId` | Confirmation | `ICronJobStore.UpdateJobAsync` (status → Enabled) |
