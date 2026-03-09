# Data Model: Cron System

## Entities

### CronJob

The root entity representing a scheduled job created by the agent on behalf of the operator.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `id` | `string` | Yes | — | Unique job identifier (kebab-case slug, e.g., `inbox-check`) |
| `name` | `string` | Yes | — | Human-readable display name |
| `schedule` | `JobSchedule` | Yes | — | Polymorphic schedule definition |
| `payload` | `JobPayload` | Yes | — | Polymorphic execution payload |
| `status` | `CronJobStatus` | Yes | `Enabled` | Current lifecycle state |
| `maxConcurrency` | `int` | No | `1` | Max concurrent executions for this job |
| `createdAtUtc` | `DateTimeOffset` | Yes | `UtcNow` | When the job was created |
| `lastRunAtUtc` | `DateTimeOffset?` | No | `null` | When the job last started executing |
| `nextRunAtUtc` | `DateTimeOffset?` | No | `null` | Calculated next fire time |
| `backoff` | `BackoffState?` | No | `null` | Current backoff state (null = no backoff) |

**Relationships:**
- Owns 1 `JobSchedule`
- Owns 1 `JobPayload`
- Owns 0..1 `BackoffState`
- Has 0..N `CronRunRecord` (in separate history file)

**Invariants:**
- `id` must be non-empty and unique within the store
- `name` must be non-empty
- `status` transitions: `Enabled` ↔ `Disabled`, `Enabled` → `Running` → `Enabled`/`Disabled`
- `nextRunAtUtc` is recalculated on each engine tick based on schedule

### JobSchedule (Polymorphic)

Discriminated union for schedule types, serialized with `type` field.

| Variant | Fields | Description |
|---------|--------|-------------|
| `OneShot` | `fireAtUtc: DateTimeOffset` | Fires once at the specified timestamp |
| `FixedInterval` | `intervalMs: long` | Fires repeatedly every N milliseconds |
| `CronExpression` | `expression: string`, `timezone: string?` | 5/6-field cron expression with optional IANA timezone |

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `type` | `string` (discriminator) | Yes | — | `"oneShot"`, `"fixedInterval"`, or `"cron"` |
| `fireAtUtc` | `DateTimeOffset` | OneShot only | — | Specific timestamp for one-shot |
| `intervalMs` | `long` | FixedInterval only | — | Interval in milliseconds |
| `expression` | `string` | CronExpression only | — | Cron expression string |
| `timezone` | `string?` | CronExpression only | `null` (UTC) | IANA timezone name |

**Invariants:**
- `OneShot.fireAtUtc` must be in the future at creation time
- `FixedInterval.intervalMs` must be ≥ 2000 (minimum refire gap)
- `CronExpression.expression` must be valid per Cronos parser
- `CronExpression.timezone` must be a valid IANA timezone name if provided

### JobPayload (Polymorphic)

Discriminated union for execution payloads, serialized with `type` field.

| Variant | Fields | Description |
|---------|--------|-------------|
| `PromptPayload` | `prompt`, `preloadToolNames?`, `model?` | Creates isolated LLM session |
| `CommandPayload` | `command`, `arguments?`, `workingDirectory?`, `timeoutSeconds?` | Runs shell command |

#### PromptPayload

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `type` | `string` (discriminator) | Yes | `"prompt"` | Identifies payload type |
| `prompt` | `string` | Yes | — | User message sent to isolated session |
| `preloadToolNames` | `string[]?` | No | `null` | Tool names to pre-expand in the session |
| `model` | `string?` | No | `null` | Override default model for this job |

**Invariants:**
- `prompt` must be non-empty

#### CommandPayload

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `type` | `string` (discriminator) | Yes | `"command"` | Identifies payload type |
| `command` | `string` | Yes | — | Command to execute |
| `arguments` | `string?` | No | `null` | Command arguments |
| `workingDirectory` | `string?` | No | `null` | Working directory (defaults to user home) |
| `timeoutSeconds` | `int` | No | `300` | Max execution time (5 minutes default) |

**Invariants:**
- `command` must be non-empty
- `timeoutSeconds` must be > 0

### CronJobStatus (Enum)

| Value | Description |
|-------|-------------|
| `Enabled` | Job is active and will be scheduled |
| `Disabled` | Job is paused — retains config, skips scheduling |
| `Running` | Job is currently executing — prevents concurrent dispatch |

### BackoffState

Tracks exponential backoff state for error recovery.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `consecutiveFailures` | `int` | Yes | `0` | Number of consecutive failures |
| `nextRetryAtUtc` | `DateTimeOffset` | Yes | — | Earliest time for next retry |
| `lastErrorMessage` | `string` | Yes | — | Most recent error message |

**Invariants:**
- `consecutiveFailures` must be ≥ 0
- Backoff steps: 30s, 1m, 5m, 15m, 60m (capped at 60m for recurring)
- Backoff resets to `null` on successful execution

### CronRunResult

Payload-agnostic execution result returned by all executors.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `content` | `string` | Yes | — | Output content (assistant message or stdout) |
| `outcome` | `CronRunOutcome` | Yes | — | Success or Failure |
| `errorMessage` | `string?` | No | `null` | Error description when outcome is Failure |
| `durationMs` | `long` | Yes | — | Execution duration in milliseconds |
| `isTransient` | `bool` | No | `false` | Whether the error is retryable |

### CronRunOutcome (Enum)

| Value | Description |
|-------|-------------|
| `Success` | Job completed successfully |
| `Failure` | Job failed |

### CronRunRecord

A single entry in a job's execution history.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `runId` | `string` | Yes | — | Unique run identifier (GUID) |
| `jobId` | `string` | Yes | — | Parent job ID |
| `startedAtUtc` | `DateTimeOffset` | Yes | — | When execution started |
| `completedAtUtc` | `DateTimeOffset` | Yes | — | When execution completed |
| `outcome` | `CronRunOutcome` | Yes | — | Success or Failure |
| `errorMessage` | `string?` | No | `null` | Error message if failed |
| `durationMs` | `long` | Yes | — | Execution duration |

### CronJobStoreDocument

Root document for `jobs.json` serialization.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `jobs` | `CronJob[]` | Yes | `[]` | All job definitions |

## State Transitions

### CronJob Lifecycle

```
                   ┌──────────┐
         create───►│ Enabled  │◄──────resume
                   └────┬─────┘
                        │
              ┌─────────┼─────────┐
              │         │         │
           pause     due tick   delete
              │         │         │
              ▼         ▼         ▼
         ┌─────────┐ ┌─────────┐
         │Disabled │ │ Running │  [removed]
         └─────────┘ └────┬────┘
                          │
                    ┌─────┼──────┐
                    │            │
                 success      failure
                    │            │
                    ▼            ▼
              ┌─────────┐  ┌─────────────┐
              │ Enabled  │  │  Enabled    │ (recurring: backoff applied)
              │          │  │  Disabled   │ (one-shot: on permanent error)
              └──────────┘  └─────────────┘
```

| Transition | From | To | Trigger |
|------------|------|----|---------|
| Create | — | Enabled | `cron_create` tool |
| Pause | Enabled | Disabled | `cron_pause` tool |
| Resume | Disabled | Enabled | `cron_resume` tool |
| Start execution | Enabled | Running | Engine dispatches job |
| Complete (success) | Running | Enabled | Executor returns Success |
| Complete (failure, recurring) | Running | Enabled + Backoff | Executor returns Failure |
| Complete (failure, one-shot, transient) | Running | Enabled + Backoff | Retry available |
| Complete (failure, one-shot, permanent) | Running | Disabled | Max retries reached or permanent error |
| Finalize (one-shot success) | Running | Disabled | One-shot completes successfully (always disable, never delete) |
| Delete | Any | [removed] | `cron_delete` tool |

### One-Shot Job Finalization

```
One-Shot Created ──► Enabled ──► Running ──► Success ──► Disabled (finalized)
                                    │
                                    ├──► Transient Failure ──► Enabled + Backoff ──► Running (retry)
                                    │                              (up to maxRetries)
                                    └──► Permanent Failure ──► Disabled (finalized)
                                    └──► Max Retries Reached ──► Disabled (finalized)
```

## Data Flow

### Job Creation Flow

```
Agent ──cron_create──► CronToolProvider
                            │
                       validate inputs
                            │
                       compute nextRunAtUtc
                            │
                       CronJobStore.AddAsync(job)
                            │
                       load jobs.json
                       append new job
                       write temp file
                       rename to jobs.json
                            │
                       return job summary
```

### Timer Tick Evaluation Flow

```
PeriodicTimer (2s) ──► CronEngine.OnTickAsync()
                            │
                       CronJobStore.LoadAsync()
                            │
                       for each job where:
                         status == Enabled
                         nextRunAtUtc <= UtcNow
                         no backoff blocking
                            │
                       check concurrency limit
                            │
                       set status = Running
                       CronJobStore.SaveAsync()
                            │
                       resolve ICronJobExecutor by payload type
                            │
                       executor.ExecuteAsync(job, runId, ct)
                            │
                       record CronRunRecord
                            │
                       update job status + nextRunAtUtc + backoff
                       CronJobStore.SaveAsync()
                            │
                       publish to IHubContext
```

## Validation Summary

| Entity | Rule | Error |
|--------|------|-------|
| CronJob | `id` non-empty, unique | `ArgumentException` |
| CronJob | `name` non-empty | `ArgumentException` |
| OneShot | `fireAtUtc` in future at creation | `ArgumentOutOfRangeException` |
| FixedInterval | `intervalMs` ≥ 2000 | `ArgumentOutOfRangeException` |
| CronExpression | Valid per Cronos parser | `CronFormatException` |
| CronExpression | Valid IANA timezone (if provided) | `TimeZoneNotFoundException` |
| PromptPayload | `prompt` non-empty | `ArgumentException` |
| CommandPayload | `command` non-empty | `ArgumentException` |
| CommandPayload | `timeoutSeconds` > 0 | `ArgumentOutOfRangeException` |
| BackoffState | `consecutiveFailures` ≥ 0 | `ArgumentOutOfRangeException` |

## JSON Serialization

### Polymorphic Configuration

```csharp
// JobSchedule
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(OneShotSchedule), "oneShot")]
[JsonDerivedType(typeof(FixedIntervalSchedule), "fixedInterval")]
[JsonDerivedType(typeof(CronExpressionSchedule), "cron")]
public abstract record JobSchedule;

// JobPayload
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PromptPayload), "prompt")]
[JsonDerivedType(typeof(CommandPayload), "command")]
public abstract record JobPayload;
```

### Serializer Options

```csharp
internal static readonly JsonSerializerOptions Options = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};
```
