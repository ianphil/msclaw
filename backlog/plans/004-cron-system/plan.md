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
                    │                                      │
                    │  ┌────────────┐  ┌────────────────┐  │
                    │  │  Evaluate  │  │  Dispatch      │  │
                    │  │  due jobs  │  │  to executor   │  │
                    │  └─────┬──────┘  └────────┬───────┘  │
                    └────────┼──────────────────┼──────────┘
                             │                  │
                    ┌────────▼──────┐   ┌───────▼──────────┐
                    │ CronJobStore  │   │ICronJobExecutor   │
                    │               │   │                   │
                    │ jobs.json     │   │┌─────────────────┐│
                    │ history/      │   ││PromptJobExecutor││
                    │               │   ││(SessionPool)    ││
                    │~/.msclaw/cron/│   │├─────────────────┤│
                    │               │   ││CommandJobExecutor│
                    │               │   ││(Process.Start)  ││
                    └───────────────┘   │└─────────────────┘│
                                        └─────────┬────────┘
                                                  │
                                        ┌─────────▼────────┐
                                        │IHubContext        │
                                        │<GatewayHub,      │
                                        │ IGatewayHubClient>│
                                        │                   │
                                        │ReceiveEvent()     │
                                        └───────────────────┘
```

## Detailed Architecture

### Component Responsibilities

| Component | Role | Integrates With |
|-----------|------|-----------------|
| `CronToolProvider` | Exposes 7 `AIFunction` tools, delegates to engine/store | `IToolProvider`, `ToolBridge`, `CronEngine` |
| `CronEngine` | Hosted service, timer tick, job evaluation, dispatch | `CronJobStore`, `ICronJobExecutor`, `IHubContext` |
| `CronJobStore` | JSON persistence, atomic writes, hot reload, history | Filesystem (`~/.msclaw/cron/`) |
| `ICronJobExecutor` | Abstraction for job execution by payload type | `CronJob`, `CronRunResult` |
| `PromptJobExecutor` | Creates isolated session, sends prompt, collects output | `ISessionPool`, `IGatewayClient`, `IToolCatalog` |
| `CommandJobExecutor` | Runs shell command, captures stdout/stderr | `System.Diagnostics.Process` |
| `CronRunHistory` | Per-job history files with auto-pruning | `CronJobStore` path |

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
│   │   ├── CronJobStore.cs                  # NEW: JSON persistence + history
│   │   ├── ICronJobStore.cs                 # NEW: Interface for testability
│   │   ├── CronRunResult.cs                 # NEW: Execution result model
│   │   ├── CronRunHistory.cs                # NEW: History model + pruning
│   │   ├── ICronJobExecutor.cs              # NEW: Executor abstraction
│   │   ├── PromptJobExecutor.cs             # NEW: Isolated session executor
│   │   ├── CommandJobExecutor.cs            # NEW: Shell command executor
│   │   ├── CronToolProvider.cs              # NEW: IToolProvider with 7 tools
│   │   └── CronErrorClassifier.cs           # NEW: Transient vs permanent
│   └── Tools/
│       └── (existing — no changes)
├── Extensions/
│   └── GatewayServiceExtensions.cs          # MODIFY: register cron services
└── MsClaw.Gateway.csproj                    # MODIFY: add Cronos NuGet

src/MsClaw.Gateway.Tests/
├── Cron/
│   ├── CronJobTests.cs                      # NEW: Model serialization tests
│   ├── CronJobStoreTests.cs                 # NEW: Persistence round-trip tests
│   ├── CronEngineTests.cs                   # NEW: Timer evaluation + dispatch
│   ├── PromptJobExecutorTests.cs            # NEW: Session creation + execution
│   ├── CommandJobExecutorTests.cs           # NEW: Process execution + timeout
│   ├── CronToolProviderTests.cs             # NEW: Tool discovery tests
│   ├── CronRunHistoryTests.cs               # NEW: History + pruning tests
│   └── CronErrorClassifierTests.cs          # NEW: Error classification tests
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
| Timer period | 2 seconds (`PeriodicTimer`) | Matches REQ-018 minimum refire gap |
| Payload type system | `[JsonPolymorphic]` on abstract record | Type-safe, extensible, clean JSON |
| Executor resolution | DI `IEnumerable<ICronJobExecutor>` keyed by payload type | Open/closed principle — new payloads need no engine changes |
| Session key format | `"cron:{jobId}:{runId}"` | Unique per execution, no collision with user sessions |
| Tool tier | `Bundled` + `AlwaysVisible = true` | Cron tools always available in every session |
| Concurrency default | 1 (serial execution) | Matches spec assumption for most deployments |
| Stagger algorithm | Deterministic hash of job ID modulo stagger window | Same job always gets same offset |
| One-shot finalization | Always disable (never delete) | Simpler; operator can re-enable or delete manually |
| `cron_update` scope | All fields updatable (name, schedule, payload, maxConcurrency) | Maximum flexibility; delete+recreate is unnecessary |
| Backoff intervals | Global-only (30s, 1m, 5m, 15m, 60m) | Simpler; per-job config adds complexity without clear need |
| SignalR cron output | Dedicated `ReceiveCronResult` method on `IGatewayHubClient` | Clean separation from chat event stream |
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
| `src/MsClaw.Gateway/Services/Cron/CronJob.cs` | Job model, schedule types, payload types, status enum |
| `src/MsClaw.Gateway/Services/Cron/CronRunResult.cs` | Execution result (Content, Outcome, ErrorMessage, DurationMs) |
| `src/MsClaw.Gateway/Services/Cron/CronRunHistory.cs` | History entry model + history file with pruning |
| `src/MsClaw.Gateway/Services/Cron/CronErrorClassifier.cs` | Transient vs permanent error classification |
| `src/MsClaw.Gateway/Services/Cron/ICronJobStore.cs` | Store interface |
| `src/MsClaw.Gateway/Services/Cron/CronJobStore.cs` | JSON persistence with atomic writes |
| `src/MsClaw.Gateway/Services/Cron/ICronEngine.cs` | Engine interface for testability |
| `src/MsClaw.Gateway/Services/Cron/CronEngine.cs` | Hosted service with PeriodicTimer |
| `src/MsClaw.Gateway/Services/Cron/ICronJobExecutor.cs` | Executor abstraction |
| `src/MsClaw.Gateway/Services/Cron/PromptJobExecutor.cs` | Isolated session executor |
| `src/MsClaw.Gateway/Services/Cron/CommandJobExecutor.cs` | Shell command executor |
| `src/MsClaw.Gateway/Services/Cron/CronToolProvider.cs` | IToolProvider with 7 AIFunction tools |
| 8 test files in `src/MsClaw.Gateway.Tests/Cron/` | Unit tests for all components |

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

## References

- [specs/gateway-cron.md](../../specs/gateway-cron.md) — Product specification
- [backlog/plans/20260309-cron-system.md](../20260309-cron-system.md) — Quick plan
- [backlog/plans/_completed/003-tool-bridge/](../_completed/003-tool-bridge/) — IToolProvider pattern reference
- [Cronos NuGet](https://github.com/HangfireIO/Cronos) — Cron expression parser
