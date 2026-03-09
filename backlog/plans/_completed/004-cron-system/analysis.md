# Cron System Analysis

## Executive Summary

The cron system adds scheduled agent autonomy to the MsClaw gateway. The agent gains the ability to self-program recurring and one-shot work through 7 tools (`cron_create`, `cron_list`, `cron_get`, `cron_update`, `cron_delete`, `cron_pause`, `cron_resume`) exposed via the existing `IToolProvider` surface from feature 003.

| Pattern | Integration Point |
|---------|-------------------|
| `IToolProvider` + `ToolBridgeHostedService` | `CronToolProvider` registers tools at startup, discovered by the bridge |
| `ISessionPool` + `IGatewaySession` | `PromptJobExecutor` creates isolated sessions for prompt-based jobs |
| `IHostedService` | `CronEngine` runs as a hosted service with a 2-second `PeriodicTimer` tick |
| `IGatewayHubClient.ReceiveEvent` | Cron output published to SignalR subscribers via `IHubContext` |
| `~/.msclaw/` config directory | Job store persisted at `~/.msclaw/cron/` alongside `config.json` |
| `System.Text.Json` + `[JsonPropertyName]` | Job model serialization follows existing DTO conventions |

## Architecture Comparison

### Current Architecture

```
Operator ──► GatewayHub.SendMessage() ──► AgentMessageService
                                              │
                                         SessionPool ──► CopilotClient.CreateSessionAsync()
                                              │
                                         ToolBridge ◄── IToolProvider (EchoToolProvider)
```

All agent work is human-initiated. The gateway has no autonomous execution loop.

### Target Architecture

```
Operator ──► GatewayHub.SendMessage() ──► AgentMessageService
                                              │
                                         SessionPool ──► CopilotClient.CreateSessionAsync()
                                              │
                                         ToolBridge ◄── IToolProvider (EchoToolProvider)
                                                    ◄── IToolProvider (CronToolProvider) ◄─── CronEngine
                                                                                                │
                                                                                         PeriodicTimer (2s)
                                                                                                │
                                                                                         CronJobStore
                                                                                         (~/.msclaw/cron/)
                                                                                                │
                                                                                    ┌───────────┴───────────┐
                                                                              PromptJobExecutor    CommandJobExecutor
                                                                              (isolated session)    (Process.Start)
                                                                                    │
                                                                               SessionPool
                                                                           (cron:{jobId}:{runId})
```

The `CronEngine` hosted service adds autonomous execution on a 2-second timer tick. Jobs are persisted to `~/.msclaw/cron/`. The engine resolves the appropriate `ICronJobExecutor` based on the job's payload type. Output flows to the SignalR hub for connected clients.

## Pattern Mapping

### 1. IToolProvider → CronToolProvider

**Current Implementation:**
`EchoToolProvider` is the sole `IToolProvider`. It exposes one `AIFunction` (`echo_text`), is registered in DI as `IToolProvider`, and discovered by `ToolBridgeHostedService` at startup. Tier is `Workspace`, `WaitForSurfaceChangeAsync` returns `Task.Delay(Infinite)` (static surface).

**Target Evolution:**
`CronToolProvider` follows the identical pattern — implements `IToolProvider`, registers 7 `AIFunction` tools via `AIFunctionFactory.Create`, and delegates handler logic to `CronEngine`/`CronJobStore`. Tier is `Bundled` (always available). Surface is static (no `WaitForSurfaceChangeAsync` change signals needed — tools don't change, only job data does). All tools have `AlwaysVisible = true` so they appear in every session's default tool list.

### 2. IHostedService → CronEngine

**Current Implementation:**
`GatewayHostedService` validates mind + starts client. `ToolBridgeHostedService` registers providers and runs per-provider watch loops. `TokenRefreshService` refreshes MSAL tokens periodically. All follow `IHostedService` with `StartAsync`/`StopAsync`.

**Target Evolution:**
`CronEngine` follows the same pattern — `IHostedService` with `StartAsync` (creates `PeriodicTimer`, starts tick loop) and `StopAsync` (cancels timer, waits for in-flight executions). The tick loop pattern is similar to `ToolBridgeHostedService`'s watch loop — a long-running `Task` launched at startup, cancelled on stop.

### 3. SessionPool → Cron Isolated Sessions

**Current Implementation:**
`AgentMessageService.GetOrCreateSessionAsync` creates sessions via `SessionPool.GetOrCreateAsync(callerKey, factory)`. The factory builds `SessionConfig` with tools from `IToolCatalog.GetDefaultTools()` + `IToolExpander.CreateExpandToolsFunction()`, appends the system message.

**Target Evolution:**
`PromptJobExecutor` creates isolated sessions via the same `SessionPool.GetOrCreateAsync("cron:{jobId}:{runId}", factory)`. The factory builds `SessionConfig` with tools from `IToolCatalog.GetDefaultTools()` plus any `preloadToolNames` from the job's payload. System message is appended. After execution, the session is removed from the pool.

### 4. JSON Persistence → CronJobStore

**Current Implementation:**
`UserConfigLoader` reads/writes `~/.msclaw/config.json` using `System.Text.Json` with `PropertyNameCaseInsensitive` read and `JsonNamingPolicy.CamelCase` + `WriteIndented` write. Atomic file I/O pattern: creates parent directories, serializes, writes.

**Target Evolution:**
`CronJobStore` follows the same pattern for `~/.msclaw/cron/jobs.json`. Adds atomic write (write-temp-then-rename) per REQ edge case. Run history stored in separate per-job files at `~/.msclaw/cron/history/{jobId}.json` with automatic pruning.

### 5. SignalR Push → Cron Output

**Current Implementation:**
`GatewayHub` implements `Hub<IGatewayHubClient>`. `IGatewayHubClient.ReceiveEvent(SessionEvent)` pushes events to connected clients. `AgentMessageService` streams events via `SessionEventBridge`.

**Target Evolution:**
`CronEngine` injects `IHubContext<GatewayHub, IGatewayHubClient>` and calls `Clients.All.ReceiveCronResult(...)` after each job execution. This uses a dedicated `ReceiveCronResult(CronRunEvent)` method on `IGatewayHubClient` (not the chat-oriented `ReceiveEvent`) so clients can handle cron output separately from chat streaming.

## What Exists vs What's Needed

### Currently Built

| Component | Status | Notes |
|-----------|--------|-------|
| `IToolProvider` + `ToolBridgeHostedService` | ✅ | Provider abstraction and auto-registration |
| `IToolCatalog` + `ToolBridge` | ✅ | Tool discovery, default tools, search |
| `ISessionPool` + `SessionPool` | ✅ | Pooled sessions with factory pattern |
| `IGatewaySession` | ✅ | Session abstraction over Copilot SDK |
| `IGatewayHubClient.ReceiveEvent` | ✅ | SignalR push to clients |
| `IHubContext<GatewayHub, IGatewayHubClient>` | ✅ | Server-side push outside hub |
| `UserConfigLoader` at `~/.msclaw/` | ✅ | File I/O pattern for config |
| `GatewayServiceExtensions` | ✅ | DI registration extension method |
| `IHostedService` pattern | ✅ | 4 existing hosted services |
| `System.Text.Json` DTO conventions | ✅ | `[JsonPropertyName]`, `sealed record`, etc. |

### Needed

| Component | Status | Source |
|-----------|--------|--------|
| `CronJob` model + `JobPayload` discriminated union | ❌ | New — follows existing record DTO pattern |
| `CronJobStore` (JSON persistence) | ❌ | New — follows `UserConfigLoader` pattern |
| `CronEngine` (hosted service + timer) | ❌ | New — follows `ToolBridgeHostedService` pattern |
| `ICronJobExecutor` + `PromptJobExecutor` | ❌ | New — uses `SessionPool` + `IToolCatalog` |
| `ICronJobExecutor` + `CommandJobExecutor` | ❌ | New — `Process.Start()` with timeout |
| `CronToolProvider` | ❌ | New — follows `EchoToolProvider` pattern |
| `CronRunHistory` (per-job history files) | ❌ | New — file I/O with pruning |
| Cronos NuGet package | ❌ | External dependency — cron expression parsing |
| `~/.msclaw/cron/` directory | ❌ | New storage location |

## Key Insights

### What Works Well

1. **IToolProvider is the perfect registration surface** — `CronToolProvider` slots in exactly like `EchoToolProvider` with zero framework changes. The `ToolBridgeHostedService` discovers and registers it automatically.

2. **SessionPool already supports isolated sessions** — The `GetOrCreateAsync(callerKey, factory)` pattern works directly for cron jobs using `"cron:{jobId}:{runId}"` as the caller key.

3. **IHubContext injection pattern is established** — Pushing cron output to SignalR clients follows the same pattern used for presence broadcasts.

4. **The hosted service pattern is well-tested** — `GatewayHostedService`, `ToolBridgeHostedService`, `TokenRefreshService` all demonstrate the lifecycle management pattern. `CronEngine` follows suit.

5. **JSON serialization conventions are consistent** — `sealed record` with `[JsonPropertyName]`, `CamelCase` naming, `WriteIndented` output. The cron model follows the same patterns.

### Gaps/Limitations

| Limitation | Solution |
|------------|----------|
| No polymorphic JSON serialization yet | Use `[JsonDerivedType]` with type discriminator on `JobPayload` base type |
| No atomic file write pattern | Implement write-temp-then-rename in `CronJobStore` |
| No `Process.Start()` usage in codebase | `CommandJobExecutor` introduces it — needs timeout + cancellation handling |
| Main session jobs require heartbeat | Defer REQ-003/REQ-005 to heartbeat feature |
| Channel delivery requires MCPorter | Defer REQ-006 — agent uses existing MCPorter tools in prompt |
| No existing timer-based service | `CronEngine` introduces `PeriodicTimer` — new pattern for the codebase |
