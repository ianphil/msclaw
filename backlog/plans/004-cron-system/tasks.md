# Cron System Tasks (TDD)

## TDD Approach

All implementation follows strict Red-Green-Refactor:
1. **RED**: Write failing test first
2. **GREEN**: Write minimal code to pass test
3. **REFACTOR**: Clean up while keeping tests green

### Two Test Layers

| Layer | Purpose | When to Run |
|-------|---------|-------------|
| **Unit Tests** | Implementation TDD (Red-Green-Refactor) | During implementation |
| **Spec Tests** | Intent-based acceptance validation | After all phases complete |

## User Story Mapping

| Story | spec.md Reference | Spec Tests |
|-------|-------------------|------------|
| Operator schedules a reminder | FR-1, FR-3, FR-4 | CronJob has required fields, JobSchedule supports variants, JobPayload supports variants |
| Operator creates recurring job | FR-1.3, FR-2, FR-8 | CronJobStore persists, CronEngine is hosted service |
| Operator manages jobs | FR-4.4, FR-4.5, FR-4.6 | CronToolProvider implements IToolProvider with 7 tools |
| Agent self-programs | FR-3, FR-4, FR-5 | ICronJobExecutor defines contract, executors create sessions/processes |
| Jobs survive restart | FR-2, FR-8.2, FR-8.3 | CronJobStore persists, CronEngine hot-reloads |
| Errors handled with backoff | FR-6, FR-7 | CronErrorClassifier, CronRunResult, history tracking |

## Dependencies

```
Phase 1 (Models) ──► Phase 2 (Persistence) ──► Phase 3 (Executors) ──► Phase 4 (Engine)
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
- [ ] T001 [TEST] Write test that `CronJob` is a sealed record with required `Id`, `Name`, `Schedule`, `Payload`, `Status` properties and optional `MaxConcurrency` (default 1), `CreatedAtUtc`, `LastRunAtUtc`, `NextRunAtUtc`, `Backoff` fields
- [ ] T002 [IMPL] Implement `CronJob` record and `CronJobStatus` enum (Enabled, Disabled, Running) in `Services/Cron/CronJob.cs`

### Schedule Types
- [ ] T003 [TEST] Write test that `JobSchedule` serializes polymorphically with `type` discriminator: `OneShotSchedule` → `"oneShot"`, `FixedIntervalSchedule` → `"fixedInterval"`, `CronExpressionSchedule` → `"cron"`. Round-trip through JSON.
- [ ] T004 [IMPL] Implement `JobSchedule` abstract record with `[JsonPolymorphic]`, `OneShotSchedule(DateTimeOffset FireAtUtc)`, `FixedIntervalSchedule(long IntervalMs)`, `CronExpressionSchedule(string Expression, string? Timezone)`

### Payload Types
- [ ] T005 [TEST] Write test that `JobPayload` serializes polymorphically with `type` discriminator: `PromptPayload` → `"prompt"`, `CommandPayload` → `"command"`. Round-trip with all fields.
- [ ] T006 [IMPL] Implement `JobPayload` abstract record with `[JsonPolymorphic]`, `PromptPayload(string Prompt, string[]? PreloadToolNames, string? Model)`, `CommandPayload(string Command, string? Arguments, string? WorkingDirectory, int TimeoutSeconds = 300)`

### Result and History Models
- [ ] T007 [TEST] Write test that `CronRunResult` is a sealed record with `Content`, `Outcome`, `ErrorMessage?`, `DurationMs`, `IsTransient` fields. `CronRunOutcome` enum has Success and Failure values.
- [ ] T008 [IMPL] Implement `CronRunResult` record and `CronRunOutcome` enum in `Services/Cron/CronRunResult.cs`
- [ ] T009 [TEST] Write test that `CronRunRecord` is a sealed record with `RunId`, `JobId`, `StartedAtUtc`, `CompletedAtUtc`, `Outcome`, `ErrorMessage?`, `DurationMs` fields. Round-trip JSON serialization.
- [ ] T010 [IMPL] Implement `CronRunRecord` record in `Services/Cron/CronRunHistory.cs`

## Phase 2: Persistence (CronJobStore)

Implement JSON file persistence with atomic writes, CRUD operations, and run history with pruning.

### Store Basics
- [ ] T011 [TEST] Write test: `LoadJobsAsync` returns empty list when `jobs.json` doesn't exist
- [ ] T012 [TEST] Write test: `SaveJobsAsync` → `LoadJobsAsync` round-trips a `CronJob` with `PromptPayload` and `CronExpressionSchedule` through JSON
- [ ] T013 [IMPL] Implement `ICronJobStore` interface in `Services/Cron/ICronJobStore.cs` and `CronJobStore` class with `LoadJobsAsync`/`SaveJobsAsync` using `System.Text.Json` with `CamelCase` + `WriteIndented`

### Atomic Writes
- [ ] T014 [TEST] Write test: `SaveJobsAsync` writes to temp file then renames — verify no partial writes on simulated crash (file exists after save, content is complete)
- [ ] T015 [IMPL] Implement atomic write-temp-then-rename in `CronJobStore.SaveJobsAsync`

### CRUD Operations
- [ ] T016 [TEST] Write test: `AddJobAsync` adds a job → `GetJobAsync` returns it. Adding a job with duplicate ID throws `InvalidOperationException`
- [ ] T017 [TEST] Write test: `UpdateJobAsync` modifies an existing job → changes persisted. Updating non-existent job throws `InvalidOperationException`
- [ ] T018 [TEST] Write test: `RemoveJobAsync` removes a job → `GetJobAsync` returns null. Removing non-existent job is a no-op
- [ ] T019 [IMPL] Implement `AddJobAsync`, `UpdateJobAsync`, `RemoveJobAsync`, `GetJobAsync` using load-modify-save cycle

### Run History
- [ ] T020 [TEST] Write test: `AppendRunRecordAsync` creates history file for new job → `GetRunHistoryAsync` returns the record
- [ ] T021 [TEST] Write test: `AppendRunRecordAsync` with history exceeding line limit → old records pruned
- [ ] T022 [IMPL] Implement `AppendRunRecordAsync` and `GetRunHistoryAsync` with per-job JSON files in `history/` subdirectory and auto-pruning

## Phase 3: Executors

Implement `ICronJobExecutor` abstraction and both executor implementations.

### Executor Interface
- [ ] T023 [IMPL] Define `ICronJobExecutor` interface in `Services/Cron/ICronJobExecutor.cs` with `PayloadType` property and `ExecuteAsync` method

### PromptJobExecutor
- [ ] T024 [TEST] Write test: `PromptJobExecutor.PayloadType` returns `typeof(PromptPayload)`
- [ ] T025 [TEST] Write test: `ExecuteAsync` with `PromptPayload` calls `SessionPool.GetOrCreateAsync` with key `"cron:{jobId}:{runId}"`, sends prompt via `session.SendAsync`, returns `CronRunResult` with Success
- [ ] T026 [TEST] Write test: `ExecuteAsync` with `preloadToolNames` populates `SessionConfig.Tools` with tools fetched from `IToolCatalog.GetToolsByName`
- [ ] T027 [TEST] Write test: `ExecuteAsync` that throws → returns `CronRunResult` with Failure outcome and error message
- [ ] T028 [IMPL] Implement `PromptJobExecutor` — inject `ISessionPool`, `IGatewayClient`, `IGatewayHostedService`, `IToolCatalog`; create isolated session via factory, send prompt, bridge events to collect `AssistantMessageEvent.Content`, wait for `SessionIdleEvent`, return result, remove session from pool

### CommandJobExecutor
- [ ] T029 [TEST] Write test: `CommandJobExecutor.PayloadType` returns `typeof(CommandPayload)`
- [ ] T030 [TEST] Write test: `ExecuteAsync` with `CommandPayload` starts a process with the command and arguments, captures stdout
- [ ] T031 [TEST] Write test: `ExecuteAsync` where process exceeds `timeoutSeconds` → kills process, returns Failure with timeout message
- [ ] T032 [IMPL] Implement `CommandJobExecutor` — `Process.Start()` with `RedirectStandardOutput/Error`, `WaitForExitAsync` with timeout, `Kill()` on timeout, return `CronRunResult`

### Error Classification
- [ ] T033 [TEST] Write test: `CronErrorClassifier.IsTransient` returns true for `HttpRequestException`, `TaskCanceledException`, `IOException`; returns false for `UnauthorizedAccessException`, `ArgumentException`, `JsonException`
- [ ] T034 [IMPL] Implement `CronErrorClassifier` static class with `IsTransient(Exception)` method

## Phase 4: CronEngine

Hosted service with `PeriodicTimer` that evaluates due jobs and dispatches to executors.

### Engine Basics
- [ ] T035 [TEST] Write test: `CronEngine` implements `IHostedService`. `StartAsync` sets `IsRunning = true`. `StopAsync` sets `IsRunning = false`.
- [ ] T036 [IMPL] Define `ICronEngine` interface in `Services/Cron/ICronEngine.cs`. Implement `CronEngine` class skeleton with `IHostedService`, `PeriodicTimer`, start/stop lifecycle

### Job Evaluation
- [ ] T037 [TEST] Write test: engine tick with one enabled job whose `nextRunAtUtc` is in the past → executor called with that job
- [ ] T038 [TEST] Write test: engine tick with one disabled job → executor NOT called
- [ ] T039 [TEST] Write test: engine tick with one running job → executor NOT called (no concurrent dispatch)
- [ ] T040 [IMPL] Implement `OnTickAsync` — load jobs from store, filter enabled + due + not in backoff, dispatch to executor

### Concurrency Control
- [ ] T041 [TEST] Write test: engine with concurrency limit 1, two due jobs → only first job dispatched, second waits
- [ ] T042 [IMPL] Implement concurrency tracking — `SemaphoreSlim` with configurable count, acquire before dispatch, release after completion

### Job Lifecycle After Execution
- [ ] T043 [TEST] Write test: recurring job succeeds → `status` back to Enabled, `lastRunAtUtc` updated, `nextRunAtUtc` recalculated, `backoff` cleared
- [ ] T044 [TEST] Write test: recurring job fails → `status` back to Enabled, `backoff` set with exponential delay (30s, 1m, 5m, 15m, 60m)
- [ ] T045 [TEST] Write test: one-shot job succeeds → `status` set to Disabled (finalized)
- [ ] T046 [TEST] Write test: one-shot job fails with transient error, retries remaining → `backoff` set, still Enabled
- [ ] T047 [TEST] Write test: one-shot job fails with permanent error → `status` set to Disabled immediately
- [ ] T048 [IMPL] Implement post-execution lifecycle — update status, compute next run, apply/clear backoff, record history, save store

### Schedule Computation
- [ ] T049 [TEST] Write test: `OneShotSchedule` → `nextRunAtUtc` is `fireAtUtc`
- [ ] T050 [TEST] Write test: `FixedIntervalSchedule` → `nextRunAtUtc` is `lastRunAtUtc + intervalMs`
- [ ] T051 [TEST] Write test: `CronExpressionSchedule` → `nextRunAtUtc` computed via Cronos with timezone
- [ ] T052 [IMPL] Implement schedule computation helper — switch on schedule type, use Cronos for cron expressions

### Stagger
- [ ] T053 [TEST] Write test: two recurring jobs with identical cron expressions get different stagger offsets. Same job always gets same offset (deterministic hash).
- [ ] T054 [IMPL] Implement stagger — hash job ID, modulo stagger window (default 0–5 minutes), add to computed `nextRunAtUtc`

### Overdue-on-Startup
- [ ] T055 [TEST] Write test: job with `nextRunAtUtc` in the past (simulating downtime) → fires on first tick after startup
- [ ] T056 [IMPL] Implement overdue detection in `OnTickAsync` — any enabled job where `nextRunAtUtc <= UtcNow` is due

## Phase 5: CronToolProvider

Implement `IToolProvider` with 7 tools delegating to engine and store.

### Provider Contract
- [ ] T057 [TEST] Write test: `CronToolProvider` implements `IToolProvider`. `Name` is `"cron"`. `Tier` is `Bundled`. `WaitForSurfaceChangeAsync` never completes (static surface).
- [ ] T058 [TEST] Write test: `DiscoverAsync` returns exactly 7 `ToolDescriptor` instances with names `cron_create`, `cron_list`, `cron_get`, `cron_update`, `cron_delete`, `cron_pause`, `cron_resume`. All have `AlwaysVisible = true`.
- [ ] T059 [IMPL] Implement `CronToolProvider` class skeleton — `IToolProvider` implementation with `AIFunctionFactory.Create` for each tool

### Tool Handlers
- [ ] T060 [TEST] Write test: `cron_create` handler with name, schedule type "cron", expression, prompt → calls `ICronJobStore.AddJobAsync` with correct `CronJob`
- [ ] T061 [TEST] Write test: `cron_list` handler → calls `ICronJobStore.LoadJobsAsync`, returns formatted summary
- [ ] T062 [TEST] Write test: `cron_get` handler with `jobId` → calls `ICronJobStore.GetJobAsync` + `GetRunHistoryAsync`
- [ ] T063 [TEST] Write test: `cron_delete` handler → calls `ICronJobStore.RemoveJobAsync`
- [ ] T064 [TEST] Write test: `cron_pause` handler → loads job, updates status to Disabled, saves
- [ ] T065 [TEST] Write test: `cron_resume` handler → loads job, updates status to Enabled, saves
- [ ] T066 [IMPL] Implement all 7 tool handler methods delegating to `ICronJobStore`

## Phase 6: Integration

Wire all cron services into gateway DI, register hosted service, verify end-to-end.

### DI Registration
- [ ] T067 [TEST] Write test: resolve `ICronJobStore` from service provider → resolves to `CronJobStore`
- [ ] T068 [TEST] Write test: resolve `ICronEngine` from service provider → resolves to `CronEngine`
- [ ] T069 [TEST] Write test: resolve `IEnumerable<ICronJobExecutor>` → contains `PromptJobExecutor` and `CommandJobExecutor`
- [ ] T070 [TEST] Write test: resolve `IEnumerable<IToolProvider>` → contains instance with Name "cron"
- [ ] T071 [IMPL] Register all cron services in `GatewayServiceExtensions.AddGatewayServices`

### NuGet Package
- [ ] T072 [IMPL] Add `Cronos` NuGet package to `MsClaw.Gateway.csproj`

### SignalR Publishing
- [ ] T073 [TEST] Write test: after job execution, `IHubContext<GatewayHub, IGatewayHubClient>.Clients.All.ReceiveCronResult()` is called with a cron result record containing job ID, job name, run ID, outcome, content, and duration
- [ ] T074 [IMPL] Add `ReceiveCronResult(CronRunEvent)` method to `IGatewayHubClient`. Define `CronRunEvent` sealed record (JobId, JobName, RunId, Outcome, Content, ErrorMessage?, DurationMs). Implement publishing in `CronEngine` after each execution.

## Task Summary

| Phase | Tasks | [TEST] | [IMPL] |
|-------|-------|--------|--------|
| Phase 1: Core Models | T001–T010 | 5 | 5 |
| Phase 2: Persistence | T011–T022 | 7 | 5 |
| Phase 3: Executors | T023–T034 | 8 | 4 |
| Phase 4: Engine | T035–T056 | 14 | 8 |
| Phase 5: Tool Provider | T057–T066 | 8 | 2 |
| Phase 6: Integration | T067–T074 | 5 | 3 |
| **Total** | **74** | **47** | **27** |

## Final Validation

After all implementation phases are complete:

- [ ] `dotnet build src/MsClaw.slnx --nologo` passes
- [ ] `dotnet test src/MsClaw.Gateway.Tests/MsClaw.Gateway.Tests.csproj --nologo` passes
- [ ] Run spec tests with `/spec-tests` skill using `specs/tests/004-cron-system.md`
- [ ] All spec tests pass → feature complete
