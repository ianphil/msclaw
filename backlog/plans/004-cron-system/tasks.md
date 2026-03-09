# Cron System Tasks (TDD)

## TDD Approach

All implementation follows strict Red-Green-Refactor:
1. **RED**: Write failing test first
2. **GREEN**: Write minimal code to pass test
3. **REFACTOR**: Clean up while keeping tests green

### Test Layer

Unit tests drive implementation via Red-Green-Refactor. Spec test definitions exist at `specs/tests/004-cron-system.md` as acceptance criteria but are not executed as part of this task list.

## User Story Mapping

| Story | spec.md Reference | Spec Tests |
|-------|-------------------|------------|
| Operator schedules a reminder | FR-1, FR-3, FR-4 | CronJob has required fields, JobSchedule supports variants, JobPayload supports variants |
| Operator creates recurring job | FR-1.3, FR-2, FR-8 | CronJobStore persists, CronEngine is hosted service |
| Operator manages jobs | FR-4.4, FR-4.5, FR-4.6 | CronToolProvider implements IToolProvider with 7 tools |
| Agent self-programs | FR-3, FR-4, FR-5 | ICronJobExecutor defines contract, executors create sessions/processes |
| Jobs survive restart | FR-2, FR-8.2, FR-8.3 | CronJobStore persists (in-memory + flush), engine re-evaluates on startup |
| Errors handled with backoff | FR-6, FR-7 | ICronErrorClassifier, CronRunResult, history tracking via ICronRunHistoryStore |

## Dependencies

```
Phase 1 (Models) ──► Phase 2 (Persistence) ──► Phase 3 (Executors + Helpers) ──► Phase 4 (Engine)
                                                                                       │
                                                                                       ▼
                                                                                Phase 5 (Tools)
                                                                                       │
                                                                                       ▼
                                                                                Phase 6 (Integration)
```

## Phase 1: Core Models

Define record types, enums, and value objects. No behavior — just data structures and serialization.

### CronJob Model
- [x] T001 [TEST] Write test that `CronJob` is a sealed record with required `Id`, `Name`, `Schedule`, `Payload`, `Status` properties and optional `MaxConcurrency` (default 1), `CreatedAtUtc`, `LastRunAtUtc`, `NextRunAtUtc`, `Backoff` fields
- [x] T002 [IMPL] Implement `CronJob` record and `CronJobStatus` enum (`Enabled`, `Disabled` — no `Running`, that's in-memory only) in `Services/Cron/CronJob.cs`

### Schedule Types
- [x] T003 [TEST] Write test that `JobSchedule` serializes polymorphically with `type` discriminator: `OneShotSchedule` → `"oneShot"`, `FixedIntervalSchedule` → `"fixedInterval"`, `CronExpressionSchedule` → `"cron"`. Round-trip through JSON.
- [x] T004 [IMPL] Implement `JobSchedule` abstract record with `[JsonPolymorphic]`, `OneShotSchedule(DateTimeOffset FireAtUtc)`, `FixedIntervalSchedule(long IntervalMs)`, `CronExpressionSchedule(string Expression, string? Timezone)`

### Payload Types
- [x] T005 [TEST] Write test that `JobPayload` serializes polymorphically with `type` discriminator: `PromptPayload` → `"prompt"`, `CommandPayload` → `"command"`. Round-trip with all fields.
- [x] T006 [IMPL] Implement `JobPayload` abstract record with `[JsonPolymorphic]`, `PromptPayload(string Prompt, string[]? PreloadToolNames, string? Model)`, `CommandPayload(string Command, string? Arguments, string? WorkingDirectory, int TimeoutSeconds = 300)`

### Result and History Models
- [x] T007 [TEST] Write test that `CronRunResult` is a sealed record with `Content`, `Outcome`, `ErrorMessage?`, `DurationMs`, `IsTransient` fields. `CronRunOutcome` enum has Success and Failure values.
- [x] T008 [IMPL] Implement `CronRunResult` record and `CronRunOutcome` enum in `Services/Cron/CronRunResult.cs`
- [x] T009 [TEST] Write test that `CronRunRecord` is a sealed record with `RunId`, `JobId`, `StartedAtUtc`, `CompletedAtUtc`, `Outcome`, `ErrorMessage?`, `DurationMs` fields. Round-trip JSON serialization.
- [x] T010 [IMPL] Implement `CronRunRecord` record in `Services/Cron/CronRunHistory.cs`

## Phase 2: Persistence

Implement in-memory-cached job store with atomic disk flush, and separate run history store.

### ICronJobStore — In-Memory Cache with Atomic Flush
- [x] T011 [TEST] Write test: `InitializeAsync` from empty state (no `jobs.json`) → `GetAllJobsAsync` returns empty list
- [x] T012 [TEST] Write test: `AddJobAsync` → flush to disk → new `CronJobStore` instance `InitializeAsync` → `GetJobAsync` returns the job (round-trip through disk)
- [x] T013 [IMPL] Implement `ICronJobStore` interface in `Services/Cron/ICronJobStore.cs` with `InitializeAsync`, `GetAllJobsAsync`, `GetJobAsync`, `AddJobAsync`, `UpdateJobAsync`, `RemoveJobAsync`. Implement `CronJobStore` class with `ConcurrentDictionary<string, CronJob>` in-memory cache and `SemaphoreSlim(1,1)` for serialized flush.

### Atomic Writes
- [x] T014 [TEST] Write test: `AddJobAsync` writes to temp file then renames — verify no partial writes (file exists after add, content is complete JSON)
- [x] T015 [IMPL] Implement atomic write-temp-then-rename in `CronJobStore` flush method (private), called after every mutation

### CRUD Operations
- [x] T016 [TEST] Write test: `AddJobAsync` adds a job → `GetJobAsync` returns it from in-memory cache. Adding a job with duplicate ID throws `InvalidOperationException`
- [x] T017 [TEST] Write test: `UpdateJobAsync` modifies an existing job → `GetJobAsync` reflects changes. Updating non-existent job throws `InvalidOperationException`
- [x] T018 [TEST] Write test: `RemoveJobAsync` removes a job → `GetJobAsync` returns null. Removing non-existent job is a no-op
- [x] T019 [IMPL] Implement `AddJobAsync`, `UpdateJobAsync`, `RemoveJobAsync`, `GetJobAsync`, `GetAllJobsAsync` using in-memory `ConcurrentDictionary` with flush-on-mutate

### ICronRunHistoryStore — Per-Job History (ISP Split)
- [x] T020 [TEST] Write test: `AppendRunRecordAsync` creates history file for new job → `GetRunHistoryAsync` returns the record. Verify these methods are on `ICronRunHistoryStore` interface (not `ICronJobStore`)
- [x] T021 [TEST] Write test: `AppendRunRecordAsync` with history exceeding line limit → old records pruned
- [x] T022 [IMPL] Implement `ICronRunHistoryStore` interface in `Services/Cron/ICronRunHistoryStore.cs`. Implement `AppendRunRecordAsync` and `GetRunHistoryAsync` in `CronJobStore` (which implements both `ICronJobStore` and `ICronRunHistoryStore`) with per-job JSON files in `history/` subdirectory and auto-pruning

## Phase 3: Executors and Helpers

Implement `ICronJobExecutor` abstraction, both executor implementations, error classifier interface, schedule calculator, and stagger calculator.

### Executor Interface
- [x] T023 [IMPL] Define `ICronJobExecutor` interface in `Services/Cron/ICronJobExecutor.cs` with `PayloadType` property and `ExecuteAsync` method

### PromptJobExecutor
- [x] T024 [TEST] Write test: `PromptJobExecutor.PayloadType` returns `typeof(PromptPayload)`
- [x] T025 [TEST] Write test: `ExecuteAsync` with `PromptPayload` calls `SessionPool.GetOrCreateAsync` with key `"cron:{jobId}:{runId}"`, sends prompt via `session.SendAsync`, returns `CronRunResult` with Success
- [x] T026 [TEST] Write test: `ExecuteAsync` with `preloadToolNames` populates `SessionConfig.Tools` with tools fetched from `IToolCatalog.GetToolsByName`
- [x] T027 [TEST] Write test: `ExecuteAsync` that throws → returns `CronRunResult` with Failure outcome and error message
- [x] T028 [IMPL] Implement `PromptJobExecutor` — inject `ISessionPool`, `IGatewayClient`, `IGatewayHostedService`, `IToolCatalog`; create isolated session via factory, send prompt, bridge events to collect `AssistantMessageEvent.Content`, wait for `SessionIdleEvent`, return result, remove session from pool

### CommandJobExecutor
- [x] T029 [TEST] Write test: `CommandJobExecutor.PayloadType` returns `typeof(CommandPayload)`
- [x] T030 [TEST] Write test: `ExecuteAsync` with `CommandPayload` starts a process with the command and arguments, captures stdout
- [x] T031 [TEST] Write test: `ExecuteAsync` where process exceeds `timeoutSeconds` → kills process, returns Failure with timeout message
- [x] T032 [IMPL] Implement `CommandJobExecutor` — `Process.Start()` with `RedirectStandardOutput/Error`, `WaitForExitAsync` with timeout, `Kill()` on timeout, return `CronRunResult`

### ICronErrorClassifier (Interface, Not Static)
- [x] T033 [TEST] Write test: `DefaultCronErrorClassifier` implements `ICronErrorClassifier`. `IsTransient` returns true for `HttpRequestException`, `TaskCanceledException`, `IOException`; returns false for `UnauthorizedAccessException`, `ArgumentException`, `JsonException`
- [x] T034 [IMPL] Define `ICronErrorClassifier` interface in `Services/Cron/ICronErrorClassifier.cs`. Implement `DefaultCronErrorClassifier` class with `IsTransient(Exception)` method

### CronScheduleCalculator (Pure Static Helper)
- [x] T035 [TEST] Write test: `CronScheduleCalculator.ComputeNextRun` with `OneShotSchedule` → returns `fireAtUtc` when not yet passed; returns `null` after execution
- [x] T036 [TEST] Write test: `CronScheduleCalculator.ComputeNextRun` with `FixedIntervalSchedule` → returns `lastRunAtUtc + intervalMs`; returns `now` if never run
- [x] T037 [TEST] Write test: `CronScheduleCalculator.ComputeNextRun` with `CronExpressionSchedule` → returns next occurrence via Cronos with timezone
- [x] T038 [IMPL] Implement `CronScheduleCalculator` static class in `Services/Cron/CronScheduleCalculator.cs` — switch on schedule type, use Cronos for cron expressions

### CronStaggerCalculator (Pure Static Helper)
- [x] T039 [TEST] Write test: two recurring jobs with identical cron expressions get different stagger offsets from `CronStaggerCalculator.ComputeOffset`. Same job always gets same offset (deterministic hash).
- [x] T040 [IMPL] Implement `CronStaggerCalculator` static class in `Services/Cron/CronStaggerCalculator.cs` — hash job ID, modulo stagger window (default 0–5 minutes)

## Phase 4: CronEngine

Hosted service with `PeriodicTimer` that evaluates due jobs and dispatches to executors. Uses in-memory `_activeJobIds` for running state (not persisted).

### Engine Basics
- [x] T041 [TEST] Write test: `CronEngine` implements `IHostedService`. `StartAsync` calls `ICronJobStore.InitializeAsync` and sets `IsRunning = true`. `StopAsync` sets `IsRunning = false`.
- [x] T042 [IMPL] Define `ICronEngine` interface in `Services/Cron/ICronEngine.cs` (with `IsRunning`, `ActiveJobCount`, `IsJobActive`). Implement `CronEngine` class skeleton with `IHostedService`, `PeriodicTimer`, start/stop lifecycle, and `HashSet<string> _activeJobIds`

### Job Evaluation
- [x] T043 [TEST] Write test: engine tick with one enabled job whose `nextRunAtUtc` is in the past → executor called with that job
- [x] T044 [TEST] Write test: engine tick with one disabled job → executor NOT called
- [x] T045 [TEST] Write test: engine tick with one job that is in `_activeJobIds` (in-memory active set) → executor NOT called (no concurrent dispatch)
- [x] T046 [IMPL] Implement `OnTickAsync` — get jobs from store (in-memory), filter enabled + not in `_activeJobIds` + due + not in backoff, dispatch to executor

### Concurrency Control
- [x] T047 [TEST] Write test: engine with concurrency limit 1, two due jobs → only first job dispatched, second waits
- [x] T048 [IMPL] Implement concurrency tracking — `SemaphoreSlim` with configurable count, acquire before dispatch, release after completion

### Job Lifecycle After Execution
- [x] T049 [TEST] Write test: recurring job succeeds → job removed from `_activeJobIds`, `lastRunAtUtc` updated, `nextRunAtUtc` recalculated via `CronScheduleCalculator`, `backoff` cleared, `ICronOutputSink.PublishResultAsync` called
- [x] T050 [TEST] Write test: recurring job fails → job removed from `_activeJobIds`, `backoff` set with exponential delay (30s, 1m, 5m, 15m, 60m), error classified via `ICronErrorClassifier`
- [x] T051 [TEST] Write test: one-shot job succeeds → `status` set to Disabled (finalized), job removed from `_activeJobIds`
- [x] T052 [TEST] Write test: one-shot job fails with transient error (per `ICronErrorClassifier`), retries remaining → `backoff` set, still Enabled
- [x] T053 [TEST] Write test: one-shot job fails with permanent error (per `ICronErrorClassifier`) → `status` set to Disabled immediately
- [x] T054 [IMPL] Implement post-execution lifecycle — remove from `_activeJobIds`, update status, compute next run via `CronScheduleCalculator`, apply/clear backoff using `ICronErrorClassifier`, record history via `ICronRunHistoryStore`, publish via `ICronOutputSink`, save store

### Overdue-on-Startup
- [x] T055 [TEST] Write test: job with `nextRunAtUtc` in the past (simulating downtime) → fires on first tick after startup
- [x] T056 [IMPL] Implement overdue detection in `OnTickAsync` — any enabled job where `nextRunAtUtc <= UtcNow` and not in `_activeJobIds` is due

## Phase 5: CronToolProvider

Implement `IToolProvider` with 7 tools delegating to `ICronJobStore` and `ICronRunHistoryStore`.

### Provider Contract
- [ ] T057 [TEST] Write test: `CronToolProvider` implements `IToolProvider`. `Name` is `"cron"`. `Tier` is `Bundled`. `WaitForSurfaceChangeAsync` never completes (static surface).
- [ ] T058 [TEST] Write test: `DiscoverAsync` returns exactly 7 `ToolDescriptor` instances with names `cron_create`, `cron_list`, `cron_get`, `cron_update`, `cron_delete`, `cron_pause`, `cron_resume`. All have `AlwaysVisible = true`.
- [ ] T059 [IMPL] Implement `CronToolProvider` class skeleton — `IToolProvider` implementation with `AIFunctionFactory.Create` for each tool

### Tool Handlers
- [ ] T060 [TEST] Write test: `cron_create` handler with name, schedule type "cron", expression, prompt → calls `ICronJobStore.AddJobAsync` with correct `CronJob`, `nextRunAtUtc` computed via `CronScheduleCalculator`
- [ ] T061 [TEST] Write test: `cron_list` handler → calls `ICronJobStore.GetAllJobsAsync`, returns formatted summary
- [ ] T062 [TEST] Write test: `cron_get` handler with `jobId` → calls `ICronJobStore.GetJobAsync` + `ICronRunHistoryStore.GetRunHistoryAsync` (separate interfaces)
- [ ] T063 [TEST] Write test: `cron_delete` handler → calls `ICronJobStore.RemoveJobAsync`
- [ ] T064 [TEST] Write test: `cron_pause` handler → loads job via `ICronJobStore.GetJobAsync`, updates status to Disabled, saves via `ICronJobStore.UpdateJobAsync`
- [ ] T065 [TEST] Write test: `cron_resume` handler → loads job via `ICronJobStore.GetJobAsync`, updates status to Enabled, saves via `ICronJobStore.UpdateJobAsync`
- [ ] T066 [IMPL] Implement all 7 tool handler methods delegating to `ICronJobStore` and `ICronRunHistoryStore`

## Phase 6: Integration

Wire all cron services into gateway DI, register hosted service, verify end-to-end.

### DI Registration
- [ ] T067 [TEST] Write test: resolve `ICronJobStore` from service provider → resolves to `CronJobStore`
- [ ] T068 [TEST] Write test: resolve `ICronRunHistoryStore` from service provider → resolves to same `CronJobStore` instance (implements both interfaces)
- [ ] T069 [TEST] Write test: resolve `ICronEngine` from service provider → resolves to `CronEngine`
- [ ] T070 [TEST] Write test: resolve `IEnumerable<ICronJobExecutor>` → contains `PromptJobExecutor` and `CommandJobExecutor`
- [ ] T071 [TEST] Write test: resolve `IEnumerable<IToolProvider>` → contains instance with Name "cron"
- [ ] T072 [TEST] Write test: resolve `ICronErrorClassifier` from service provider → resolves to `DefaultCronErrorClassifier`
- [ ] T073 [TEST] Write test: resolve `ICronOutputSink` from service provider → resolves to `SignalRCronOutputSink`
- [ ] T074 [IMPL] Register all cron services in `GatewayServiceExtensions.AddGatewayServices`: `CronJobStore` as both `ICronJobStore` and `ICronRunHistoryStore` (singleton), `CronEngine` as `ICronEngine` + `IHostedService`, `CronToolProvider` as `IToolProvider`, executors as `ICronJobExecutor`, `DefaultCronErrorClassifier` as `ICronErrorClassifier`, `SignalRCronOutputSink` as `ICronOutputSink`

### NuGet Package
- [ ] T075 [IMPL] Add `Cronos` NuGet package to `MsClaw.Gateway.csproj`

### Output Sink
- [ ] T076 [TEST] Write test: `SignalRCronOutputSink.PublishResultAsync` calls `IHubContext<GatewayHub, IGatewayHubClient>.Clients.All.ReceiveCronResult()` with a `CronRunEvent` containing job ID, job name, run ID, outcome, content, and duration
- [ ] T077 [IMPL] Define `ICronOutputSink` interface in `Services/Cron/ICronOutputSink.cs`. Define `CronRunEvent` sealed record (JobId, JobName, RunId, Outcome, Content, ErrorMessage?, DurationMs). Implement `SignalRCronOutputSink` injecting `IHubContext<GatewayHub, IGatewayHubClient>`. Add `ReceiveCronResult(CronRunEvent)` method to `IGatewayHubClient`.

## Task Summary

| Phase | Tasks | [TEST] | [IMPL] |
|-------|-------|--------|--------|
| Phase 1: Core Models | T001–T010 | 5 | 5 |
| Phase 2: Persistence | T011–T022 | 7 | 5 |
| Phase 3: Executors + Helpers | T023–T040 | 11 | 7 |
| Phase 4: Engine | T041–T056 | 11 | 5 |
| Phase 5: Tool Provider | T057–T066 | 8 | 2 |
| Phase 6: Integration | T067–T077 | 8 | 3 |
| **Total** | **77** | **50** | **27** |

## Final Validation

After all implementation phases are complete:

- [ ] `dotnet build src/MsClaw.slnx --nologo` passes
- [ ] `dotnet test src/MsClaw.Gateway.Tests/MsClaw.Gateway.Tests.csproj --nologo` passes
