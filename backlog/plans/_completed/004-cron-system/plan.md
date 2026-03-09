# Plan: Cron System — Scheduled Agent Autonomy

## Summary

Implement a timer-based cron engine as an `IHostedService` and expose job management through an `IToolProvider` on the gateway's tool bridge. The system has four layers: `CronToolProvider` (7 tools), `CronEngine` (hosted service with `PeriodicTimer`), `CronJobStore` (JSON persistence at `~/.msclaw/cron/`), and `ICronJobExecutor` implementations (`PromptJobExecutor` for isolated LLM sessions, `CommandJobExecutor` for shell commands). Jobs are self-programmed by the agent and survive gateway restarts.

## Architecture

```
                    ┌──────────────────────────────────────┐
                    │            ToolBridge                 │
                    │  ┌─────────────┐  ┌───────────────┐  │
                    │  │EchoTool     │  │CronTool       │  │
                    │  │Provider     │  │Provider       │  │
                    │  │(Workspace)  │  │(Bundled)      │  │
                    │  └─────────────┘  └───────┬───────┘  │
                    └───────────────────────────┼──────────┘
                                                │
                                    delegates to│
                                                ▼
                    ┌──────────────────────────────────────┐
                    │            CronEngine                 │
                    │         (IHostedService)              │
                    │    PeriodicTimer (2s tick)            │
                    │    _activeJobIds (in-memory)          │
                    │                                      │
                    │  ┌────────────┐  ┌────────────────┐  │
                    │  │  Evaluate  │  │  Dispatch      │  │
                    │  │  due jobs  │  │  to executor   │  │
                    │  └─────┬──────┘  └────────┬───────┘  │
                    └────────┼──────────────────┼──────────┘
                             │                  │
              ┌──────────────┼──────┐   ┌───────▼──────────┐
              │              │      │   │ICronJobExecutor   │
              │   ┌──────────▼────┐ │   │                   │
              │   │ICronJobStore  │ │   │┌─────────────────┐│
              │   │(in-memory     │ │   ││PromptJobExecutor││
              │   │ + flush)      │ │   ││(SessionPool)    ││
              │   └───────────────┘ │   │├─────────────────┤│
              │                     │   ││CommandJobExecutor│
              │   ┌─────────────────┤   ││(Process.Start)  ││
              │   │ICronRunHistory  │   │└─────────────────┘│
              │   │Store            │   └─────────┬────────┘
              │   │(per-job files)  │             │
              │   └─────────────────┤   ┌─────────▼────────┐
              │                     │   │ICronOutputSink    │
              │   ~/.msclaw/cron/   │   │                   │
              └─────────────────────┘   │SignalRCronOutput  │
                                        │Sink → IHubContext │
                                        └──────────────────┘
```

## Detailed Architecture

### Component Responsibilities

| Component | Role | Integrates With |
|-----------|------|-----------------|
| `CronToolProvider` | Exposes 7 `AIFunction` tools, delegates to store | `IToolProvider`, `ToolBridge`, `ICronJobStore` |
| `CronEngine` | Hosted service, timer tick, job evaluation, dispatch | `ICronJobStore`, `ICronRunHistoryStore`, `ICronJobExecutor`, `ICronOutputSink`, `ICronErrorClassifier` |
| `CronJobStore` | In-memory cache with atomic disk flush, implements both `ICronJobStore` and `ICronRunHistoryStore` | Filesystem (`~/.msclaw/cron/`) |
| `ICronJobExecutor` | Abstraction for job execution by payload type | `CronJob`, `CronRunResult` |
| `PromptJobExecutor` | Creates isolated session, sends prompt, collects output | `ISessionPool`, `IGatewayClient`, `IToolCatalog` |
| `CommandJobExecutor` | Runs shell command, captures stdout/stderr | `System.Diagnostics.Process` |
| `ICronOutputSink` | Publishes execution results to external consumers | Decouples engine from SignalR |
| `SignalRCronOutputSink` | Default output sink via SignalR | `IHubContext<GatewayHub, IGatewayHubClient>` |
| `CronScheduleCalculator` | Pure static helper for computing next run times | Cronos, `JobSchedule` |
| `CronStaggerCalculator` | Pure static helper for deterministic stagger offsets | Job ID hash |
| `ICronErrorClassifier` | Classifies errors as transient or permanent | `Exception` types |

### Data Flow: Operator Creates a Recurring Job

```
Operator ──"check inbox every morning at 9am"──► GatewayHub.SendMessage()
                                                        │
                                                        ▼
                                                AgentMessageService
                                                        │
                                                  LLM reasons...
                                                        │
                                                   calls cron_create
                                                        │
                                                        ▼
                                              CronToolProvider.cron_create
                                                        │
                                                        ▼
                                              CronJobStore.AddAsync()
                                                        │
                                              writes ~/.msclaw/cron/jobs.json
                                                        │
                                                   returns job ID
```

### Data Flow: Cron Engine Fires a Prompt Job

```
PeriodicTimer tick (2s) ──► CronEngine.EvaluateDueJobs()
                                    │
                              load CronJobStore
                                    │
                              find due + enabled jobs
                                    │
                              check concurrency limit
                                    │
                              resolve ICronJobExecutor by payload type
                                    │
                                    ▼
                            PromptJobExecutor.ExecuteAsync()
                                    │
                              SessionPool.GetOrCreateAsync("cron:{jobId}:{runId}", factory)
                                    │
                              factory: SessionConfig { Tools = defaultTools, SystemMessage = ... }
                                    │
                              session.SendAsync(prompt)
                                    │
                              wait for SessionIdleEvent
                                    │
                              collect AssistantMessageEvent.Content
                                    │
                              return CronRunResult { Content, Outcome, Duration }
                                    │
                                    ▼
                            CronEngine records history
                                    │
                            IHubContext.Clients.All.ReceiveEvent(...)
                                    │
                            SessionPool.RemoveAsync("cron:{jobId}:{runId}")
```

## File Structure

```
src/MsClaw.Gateway/
├── Services/
│   ├── Cron/
│   │   ├── CronEngine.cs                    # NEW: IHostedService + PeriodicTimer
│   │   ├── ICronEngine.cs                   # NEW: Interface for testability
│   │   ├── CronJob.cs                       # NEW: Job model + schedule types
│   │   ├── CronJobStore.cs                  # NEW: In-memory cache + atomic disk flush
│   │   ├── ICronJobStore.cs                 # NEW: Job CRUD interface
│   │   ├── ICronRunHistoryStore.cs           # NEW: Run history interface (ISP split)
│   │   ├── CronRunResult.cs                 # NEW: Execution result model
│   │   ├── CronRunHistory.cs                # NEW: History model + pruning
│   │   ├── ICronJobExecutor.cs              # NEW: Executor abstraction
│   │   ├── PromptJobExecutor.cs             # NEW: Isolated session executor
│   │   ├── CommandJobExecutor.cs            # NEW: Shell command executor
│   │   ├── CronToolProvider.cs              # NEW: IToolProvider with 7 tools
│   │   ├── ICronErrorClassifier.cs          # NEW: Error classifier interface
│   │   ├── DefaultCronErrorClassifier.cs    # NEW: Default transient vs permanent
│   │   ├── ICronOutputSink.cs               # NEW: Output publishing abstraction
│   │   ├── SignalRCronOutputSink.cs          # NEW: SignalR output publisher
│   │   ├── CronScheduleCalculator.cs        # NEW: Pure schedule computation
│   │   └── CronStaggerCalculator.cs         # NEW: Deterministic stagger offsets
│   └── Tools/
│       └── (existing — no changes)
├── Extensions/
│   └── GatewayServiceExtensions.cs          # MODIFY: register cron services
└── MsClaw.Gateway.csproj                    # MODIFY: add Cronos NuGet

src/MsClaw.Gateway.Tests/
├── Cron/
│   ├── CronJobTests.cs                      # NEW: Model serialization tests
│   ├── CronJobStoreTests.cs                 # NEW: Persistence round-trip tests
│   ├── CronRunHistoryStoreTests.cs          # NEW: History + pruning tests
│   ├── CronEngineTests.cs                   # NEW: Timer evaluation + dispatch
│   ├── PromptJobExecutorTests.cs            # NEW: Session creation + execution
│   ├── CommandJobExecutorTests.cs           # NEW: Process execution + timeout
│   ├── CronToolProviderTests.cs             # NEW: Tool discovery tests
│   ├── CronScheduleCalculatorTests.cs       # NEW: Schedule computation tests
│   ├── CronStaggerCalculatorTests.cs        # NEW: Stagger offset tests
│   ├── DefaultCronErrorClassifierTests.cs   # NEW: Error classification tests
│   └── SignalRCronOutputSinkTests.cs        # NEW: Output publishing tests
```

## Critical: Polymorphic JSON Serialization

**Problem**: `JobPayload` is a base type with `PromptPayload` and `CommandPayload` variants. `System.Text.Json` needs a type discriminator to deserialize correctly.

**Solution**: Use `[JsonPolymorphic]` + `[JsonDerivedType]` attributes (available in .NET 7+):

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PromptPayload), "prompt")]
[JsonDerivedType(typeof(CommandPayload), "command")]
public abstract record JobPayload;
```

This produces human-readable JSON:
```json
{
  "payload": {
    "type": "prompt",
    "prompt": "Check my inbox and summarize new messages",
    "preloadToolNames": ["mcporter"]
  }
}
```

## Implementation Phases

| Phase | Name | Tasks | Focus |
|-------|------|-------|-------|
| 1 | Core Models | T001–T010 | `CronJob`, `JobPayload`, `CronRunResult`, `CronRunHistory`, `CronErrorClassifier` |
| 2 | Persistence | T011–T020 | `CronJobStore` with atomic writes, history files, pruning |
| 3 | Executors | T021–T032 | `ICronJobExecutor`, `PromptJobExecutor`, `CommandJobExecutor` |
| 4 | Engine | T033–T046 | `CronEngine` hosted service, timer tick, evaluation, dispatch |
| 5 | Tool Provider | T047–T056 | `CronToolProvider` with 7 tools, DI registration |
| 6 | Integration | T057–T062 | End-to-end wiring, SignalR publishing, startup |

## Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Cron parsing library | Cronos | IANA timezone support, well-maintained, MIT license |
| Job store location | `~/.msclaw/cron/jobs.json` | Follows `~/.msclaw/` convention from `UserConfigLoader` |
| Persistence format | JSON with `WriteIndented` | Human-inspectable per spec requirement |
| Atomic writes | Write-temp-then-rename | Prevents corruption on crash per spec edge case |
| Store caching strategy | In-memory `ConcurrentDictionary`, flush-on-mutate | Eliminates load-modify-save race between engine ticks and CRUD tools |
| Hot-reload (FR-8.2) | Requires gateway restart for v1 | In-memory canonical state eliminates per-tick disk reads; file watcher is future work |
| Timer period | 2 seconds (`PeriodicTimer`) | Matches REQ-018 minimum refire gap |
| Payload type system | `[JsonPolymorphic]` on abstract record | Type-safe, extensible, clean JSON |
| Executor resolution | DI `IEnumerable<ICronJobExecutor>` keyed by payload type | Open/closed principle — new payloads need no engine changes |
| Session key format | `"cron:{jobId}:{runId}"` | Unique per execution, no collision with user sessions |
| Tool tier | `Bundled` + `AlwaysVisible = true` | Cron tools always available in every session |
| Concurrency default | 1 (serial execution) | Matches spec assumption for most deployments |
| Stagger algorithm | `CronStaggerCalculator` — deterministic hash of job ID modulo stagger window | Same job always gets same offset; pure static helper |
| Schedule computation | `CronScheduleCalculator` — pure static helper | Extracted from engine for SRP; trivially testable without mocking |
| One-shot finalization | Always disable (never delete) | Simpler; operator can re-enable or delete manually |
| `cron_update` scope | All fields updatable (name, schedule, payload, maxConcurrency) | Maximum flexibility; delete+recreate is unnecessary |
| Backoff intervals | Global-only (30s, 1m, 5m, 15m, 60m) | Simpler; per-job config adds complexity without clear need |
| Output publishing | `ICronOutputSink` interface with `SignalRCronOutputSink` default | Decouples engine from SignalR; future sinks (webhooks, Teams) need no engine changes |
| Error classification | `ICronErrorClassifier` interface with `DefaultCronErrorClassifier` | Extensible for payload-specific classifiers (exit codes vs SDK exceptions) |
| Running state tracking | In-memory `HashSet<string>` in engine, NOT persisted | Prevents crash recovery bug where persisted Running status permanently blocks a job |
| Job store interface split | `ICronJobStore` (CRUD) + `ICronRunHistoryStore` (history) | ISP — consumers depend only on what they use; `cron_get` needs history, engine needs CRUD |
| Cronos version | 0.11.1 (verified net10.0 compatible) | Targets .NET 6.0+ and .NET Standard 1.0 |

## Configuration Example

Jobs are stored at `~/.msclaw/cron/jobs.json`:

```json
{
  "jobs": [
    {
      "id": "inbox-check",
      "name": "Morning Inbox Check",
      "schedule": {
        "type": "cron",
        "expression": "0 9 * * *",
        "timezone": "America/New_York"
      },
      "payload": {
        "type": "prompt",
        "prompt": "Check my inbox and summarize any new messages since yesterday.",
        "preloadToolNames": ["mcporter"]
      },
      "status": "enabled",
      "maxConcurrency": 1,
      "createdAtUtc": "2026-03-09T12:00:00Z",
      "lastRunAtUtc": "2026-03-09T14:00:00Z",
      "nextRunAtUtc": "2026-03-10T14:00:00Z",
      "backoff": null
    }
  ]
}
```

## Files to Modify

| File | Change |
|------|--------|
| `src/MsClaw.Gateway/Extensions/GatewayServiceExtensions.cs` | Register `CronEngine`, `CronJobStore`, `CronToolProvider`, executors |
| `src/MsClaw.Gateway/MsClaw.Gateway.csproj` | Add Cronos NuGet package reference |

## New Files

| File | Purpose |
|------|---------|
| `src/MsClaw.Gateway/Services/Cron/CronJob.cs` | Job model, schedule types, payload types, status enum (Enabled/Disabled only) |
| `src/MsClaw.Gateway/Services/Cron/CronRunResult.cs` | Execution result (Content, Outcome, ErrorMessage, DurationMs) |
| `src/MsClaw.Gateway/Services/Cron/CronRunHistory.cs` | History entry model + history file with pruning |
| `src/MsClaw.Gateway/Services/Cron/ICronJobStore.cs` | Job CRUD interface (6 methods) |
| `src/MsClaw.Gateway/Services/Cron/ICronRunHistoryStore.cs` | Run history interface (2 methods, ISP split) |
| `src/MsClaw.Gateway/Services/Cron/CronJobStore.cs` | Implements both `ICronJobStore` and `ICronRunHistoryStore` — in-memory cache with atomic flush |
| `src/MsClaw.Gateway/Services/Cron/ICronEngine.cs` | Engine interface (IsRunning, ActiveJobCount, IsJobActive) |
| `src/MsClaw.Gateway/Services/Cron/CronEngine.cs` | Hosted service with PeriodicTimer, in-memory `_activeJobIds` |
| `src/MsClaw.Gateway/Services/Cron/ICronJobExecutor.cs` | Executor abstraction |
| `src/MsClaw.Gateway/Services/Cron/PromptJobExecutor.cs` | Isolated session executor |
| `src/MsClaw.Gateway/Services/Cron/CommandJobExecutor.cs` | Shell command executor |
| `src/MsClaw.Gateway/Services/Cron/CronToolProvider.cs` | IToolProvider with 7 AIFunction tools |
| `src/MsClaw.Gateway/Services/Cron/ICronErrorClassifier.cs` | Error classification interface |
| `src/MsClaw.Gateway/Services/Cron/DefaultCronErrorClassifier.cs` | Default transient vs permanent classification |
| `src/MsClaw.Gateway/Services/Cron/ICronOutputSink.cs` | Output publishing abstraction |
| `src/MsClaw.Gateway/Services/Cron/SignalRCronOutputSink.cs` | SignalR output publisher |
| `src/MsClaw.Gateway/Services/Cron/CronScheduleCalculator.cs` | Pure static schedule computation |
| `src/MsClaw.Gateway/Services/Cron/CronStaggerCalculator.cs` | Pure static deterministic stagger offsets |
| 11 test files in `src/MsClaw.Gateway.Tests/Cron/` | Unit tests for all components |

## Verification

1. `dotnet build src/MsClaw.slnx --nologo` passes
2. `dotnet test src/MsClaw.Gateway.Tests/MsClaw.Gateway.Tests.csproj --nologo` passes
3. All 7 cron tools discoverable via `IToolCatalog`
4. Job round-trip: create → persist → restart → resume → fire → record history
5. Spec tests pass: `specs/tests/004-cron-system.md`

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Cronos net10.0 compatibility | Verify during Phase 1; fallback to `CronExpression` parsing |
| File corruption on crash | Write-temp-then-rename; refuse to start on parse error |
| Runaway process in CommandPayload | Configurable timeout (default: 5 min); `Process.Kill()` on timeout |
| SessionPool key collision | `"cron:{jobId}:{runId}"` format guarantees uniqueness |
| Clock skew | Re-evaluate each tick; `lastRunAtUtc` prevents duplicates |
| Timer drift | `PeriodicTimer` self-corrects; no accumulated drift |

## Limitations (MVP)

1. No main session jobs — requires heartbeat system (REQ-003/REQ-005)
2. No channel delivery modes — agent uses MCPorter tools directly (REQ-006)
3. No job chaining or DAG dependencies
4. No visual UI — management is conversational
5. Session retention follows SessionPool defaults, not per-job configuration
6. No process sandboxing for `CommandPayload` — trusts the operator
7. Manual edits to `jobs.json` require gateway restart (hot-reload deferred to file watcher)

## References

- [specs/gateway-cron.md](../../specs/gateway-cron.md) — Product specification
- [backlog/plans/20260309-cron-system.md](../20260309-cron-system.md) — Quick plan
- [backlog/plans/_completed/003-tool-bridge/](../_completed/003-tool-bridge/) — IToolProvider pattern reference
- [Cronos NuGet](https://github.com/HangfireIO/Cronos) — Cron expression parser
