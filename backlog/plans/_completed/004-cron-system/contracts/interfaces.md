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

    /// <summary>
    /// Returns true if the job is currently being executed by the engine.
    /// This is an in-memory check — Running state is never persisted to disk.
    /// </summary>
    bool IsJobActive(string jobId);
}
```

The engine is also registered as `IHostedService` — `StartAsync` creates the `PeriodicTimer` and starts the tick loop; `StopAsync` cancels the timer and waits for in-flight executions.

### In-Memory Active Job Tracking

The engine maintains a `HashSet<string> _activeJobIds` in memory to track currently executing jobs. This replaces the former persisted `Running` status value. On gateway restart, the set is empty — all persisted jobs are either `Enabled` or `Disabled`, and the engine re-evaluates from there. This prevents the crash recovery bug where a persisted `Running` status would permanently block a job after an unclean shutdown.

## ICronJobStore

Manages job persistence with in-memory canonical state.

```csharp
/// <summary>
/// Persists cron job definitions to disk. Maintains an in-memory cache
/// loaded once at startup and flushed atomically on every mutation.
/// </summary>
public interface ICronJobStore
{
    /// <summary>
    /// Loads all jobs from disk into memory. Called once at startup.
    /// Returns empty list and creates directory if store doesn't exist.
    /// Throws on parse error.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all jobs from the in-memory cache.
    /// </summary>
    Task<IReadOnlyList<CronJob>> GetAllJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a job by ID from the in-memory cache. Returns null if not found.
    /// </summary>
    Task<CronJob?> GetJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new job. Updates in-memory state and flushes to disk atomically.
    /// Throws <see cref="InvalidOperationException"/> if a job with the same ID already exists.
    /// </summary>
    Task AddJobAsync(CronJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing job. Updates in-memory state and flushes to disk atomically.
    /// Throws <see cref="InvalidOperationException"/> if the job doesn't exist.
    /// </summary>
    Task UpdateJobAsync(CronJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a job by ID. Updates in-memory state and flushes to disk atomically.
    /// No-op if the job doesn't exist.
    /// </summary>
    Task RemoveJobAsync(string jobId, CancellationToken cancellationToken = default);
}
```

### Store Behavior

- **Initialize**: Reads `~/.msclaw/cron/jobs.json` into a `ConcurrentDictionary<string, CronJob>`. Returns empty if file doesn't exist. Throws on parse error. Called once by `CronEngine.StartAsync`.
- **Reads** (`GetAllJobsAsync`, `GetJobAsync`): Served from in-memory `ConcurrentDictionary`. No disk I/O.
- **Mutations** (`AddJobAsync`, `UpdateJobAsync`, `RemoveJobAsync`): Update in-memory state first, then flush full state to disk via atomic write-temp-then-rename. A `SemaphoreSlim(1,1)` serializes flushes to prevent concurrent writes.
- **Hot-reload**: Manual edits to `jobs.json` require a gateway restart for v1. File watcher support is a future enhancement.

## ICronRunHistoryStore

Manages per-job execution history, separated from job CRUD per Interface Segregation Principle.

```csharp
/// <summary>
/// Persists cron job execution history to per-job files.
/// </summary>
public interface ICronRunHistoryStore
{
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

### History Behavior

- **Storage**: Per-job JSON files at `~/.msclaw/cron/history/{jobId}.json`.
- **Pruning**: Auto-prunes by size (2MB) and line count (2000).
- **Independence**: History I/O is fully independent from the job store. Consumers that only need history (e.g., `cron_get` tool) depend on `ICronRunHistoryStore` alone.

The concrete `CronJobStore` class implements both `ICronJobStore` and `ICronRunHistoryStore` (shared filesystem path at `~/.msclaw/cron/`), but consumers inject only the interface they need.

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

## ICronErrorClassifier

Classifies execution errors as transient (retryable) or permanent.

```csharp
/// <summary>
/// Classifies execution errors to determine retry eligibility.
/// </summary>
public interface ICronErrorClassifier
{
    /// <summary>
    /// Returns true if the error is transient and the job should be retried.
    /// </summary>
    bool IsTransient(Exception exception);
}
```

### Default Implementation: `DefaultCronErrorClassifier`

| Error Type | Classification | Examples |
|------------|---------------|----------|
| Transient | Retryable | `HttpRequestException` (5xx, timeout), `TaskCanceledException`, `IOException` |
| Permanent | Not retryable | `UnauthorizedAccessException`, `ArgumentException`, `JsonException` |

The interface allows future payload-specific classifiers (e.g., `CommandPayload` classifying by process exit code vs `PromptPayload` classifying by SDK exception type) without modifying the engine.

## ICronOutputSink

Decouples the engine from output delivery infrastructure.

```csharp
/// <summary>
/// Publishes cron job execution results to external consumers.
/// </summary>
public interface ICronOutputSink
{
    /// <summary>
    /// Publishes the result of a completed job execution.
    /// </summary>
    Task PublishResultAsync(CronRunEvent result, CancellationToken cancellationToken);
}
```

### Default Implementation: `SignalRCronOutputSink`

Injects `IHubContext<GatewayHub, IGatewayHubClient>` and calls `Clients.All.ReceiveCronResult(result)`. The engine depends on `ICronOutputSink` — it never references `IHubContext` directly.

Future sinks (file logging, webhooks, Teams channels) can be added as decorators or additional registrations without modifying the engine.

## CronScheduleCalculator

Pure static helper for computing next run times from schedule definitions.

```csharp
/// <summary>
/// Computes the next run time for a job based on its schedule type.
/// </summary>
public static class CronScheduleCalculator
{
    /// <summary>
    /// Returns the next run time, or null if the schedule is exhausted (e.g., finalized one-shot).
    /// </summary>
    /// <param name="schedule">The job's schedule definition.</param>
    /// <param name="lastRunAtUtc">The last execution time, or null if never run.</param>
    /// <param name="now">The current time for evaluation.</param>
    public static DateTimeOffset? ComputeNextRun(
        JobSchedule schedule, DateTimeOffset? lastRunAtUtc, DateTimeOffset now);
}
```

### Schedule Rules

| Schedule Type | Computation |
|---------------|-------------|
| `OneShotSchedule` | Returns `fireAtUtc` if not yet passed; `null` after execution |
| `FixedIntervalSchedule` | Returns `lastRunAtUtc + intervalMs`, or `now` if never run |
| `CronExpressionSchedule` | Uses Cronos to compute next occurrence from `now` in the specified timezone |

## CronStaggerCalculator

Pure static helper for deterministic job stagger offsets.

```csharp
/// <summary>
/// Computes a deterministic stagger offset for a job to spread concurrent schedules.
/// </summary>
public static class CronStaggerCalculator
{
    /// <summary>
    /// Returns a deterministic offset in [0, window) based on the job ID hash.
    /// The same job always receives the same offset.
    /// </summary>
    public static TimeSpan ComputeOffset(string jobId, TimeSpan window);
}
```

## CronToolProvider Tools

| Tool Name | Parameters | Returns | Delegates To |
|-----------|-----------|---------|-------------|
| `cron_create` | `name`, `scheduleType`, `scheduleValue`, `timezone?`, `payloadType`, `prompt`/`command`, `preloadToolNames?`, `model?`, `arguments?`, `workingDirectory?` | Job summary | `ICronJobStore.AddJobAsync` |
| `cron_list` | (none) | All jobs with status, schedule, last/next run | `ICronJobStore.GetAllJobsAsync` |
| `cron_get` | `jobId` | Single job details + recent history | `ICronJobStore.GetJobAsync` + `ICronRunHistoryStore.GetRunHistoryAsync` |
| `cron_update` | `jobId`, `name?`, `scheduleType?`, `scheduleValue?`, `timezone?`, `payloadType?`, `prompt?`/`command?`, `preloadToolNames?`, `model?`, `arguments?`, `workingDirectory?`, `maxConcurrency?` | Updated job summary | `ICronJobStore.UpdateJobAsync` |
| `cron_delete` | `jobId` | Confirmation | `ICronJobStore.RemoveJobAsync` |
| `cron_pause` | `jobId` | Confirmation | `ICronJobStore.UpdateJobAsync` (status → Disabled) |
| `cron_resume` | `jobId` | Confirmation | `ICronJobStore.UpdateJobAsync` (status → Enabled) |
