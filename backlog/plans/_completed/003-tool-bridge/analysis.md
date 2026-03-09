# Tool Bridge Analysis

## Executive Summary

The Tool Bridge introduces a provider abstraction and registry that decouples tool lifecycle management from individual tool sources. The gateway currently creates sessions with no custom tools — `SessionConfig.Tools` is empty. This analysis maps existing gateway patterns to the bridge design and identifies what exists versus what must be built.

| Pattern | Integration Point |
|---------|-------------------|
| Singleton service + DI | `GatewayServiceExtensions.AddGatewayServices` — same pattern for `ToolBridge`, `ToolExpander` |
| Session factory delegation | `SessionPool.GetOrCreateAsync` factory lambda — extend to populate `Tools` |
| Proxy/adapter layering | `IGatewayClient` / `CopilotGatewayClient` — `IToolProvider` follows the same seam |
| Hosted service startup | `GatewayHostedService.StartAsync` — provider registration runs here |
| Event streaming passthrough | `SessionEventBridge.Bridge` — tool events flow through unchanged |
| `ResumeSessionAsync` for session mutation | SDK supports `ResumeSessionConfig.Tools` — used by `expand_tools` |

## Architecture Comparison

### Current Architecture

```
GatewayHub ──► AgentMessageService ──► SessionPool ──► SDK
                                                     SessionConfig {
                                                       Streaming = true,
                                                       SystemMessage = ...,
                                                       Tools = ∅
                                                     }
```

Sessions have **no custom tool surface**. The agent can only use the Copilot CLI's built-in tools (file editing, terminal, search). No mechanism exists to register, discover, or lazily add tools.

### Target Architecture

```
GatewayHub ──► AgentMessageService ──► SessionPool ──► SDK
                    │                                  SessionConfig {
                    │                                    Streaming = true,
                    │                                    SystemMessage = ...,
                    │                                    Tools = [defaults + expand_tools]
                    │                                  }
                    │
                    ▼
              IToolCatalog ◄── IToolRegistrar ◄── IToolProvider(s)
                    │
                    ▼
              IToolExpander ──► expand_tools (per-session AIFunction)
                                   │
                                   ▼
                              ResumeSessionAsync(Tools = current + new)
```

Sessions start with default (AlwaysVisible) tools plus a single `expand_tools` meta-tool. Additional tools are added lazily when the agent invokes `expand_tools`, which calls `ResumeSessionAsync` with the expanded tool set. No `AvailableTools`/`ExcludedTools` are used — CLI built-in tools remain unaffected.

## Pattern Mapping

### 1. Singleton Service Registration

**Current Implementation:**
`GatewayServiceExtensions` registers `AgentMessageService`, `SessionPool`, `CallerRegistry` as singletons. Each service has a corresponding interface.

**Target Evolution:**
`ToolBridge` (implementing both `IToolCatalog` and `IToolRegistrar`) and `ToolExpander` (implementing `IToolExpander`) register as singletons in the same extension method. `IToolProvider` implementations register via `IEnumerable<IToolProvider>` for automatic discovery.

### 2. Session Factory Pattern

**Current Implementation:**
`AgentMessageService.GetOrCreateSessionAsync` passes a factory lambda to `SessionPool.GetOrCreateAsync`. The factory builds `SessionConfig` with streaming and system message, then calls `client.CreateSessionAsync`.

**Target Evolution:**
The factory lambda is extended to:
1. Call `IToolCatalog.GetDefaultTools()` for AlwaysVisible tool functions
2. Call `IToolExpander.CreateExpandToolsFunction(session, tools)` for the meta-tool
3. Include both in `SessionConfig.Tools`

### 3. Hosted Service Lifecycle

**Current Implementation:**
`GatewayHostedService.StartAsync` validates the mind, loads identity, creates the CopilotClient, and starts it. State transitions through Starting → Validating → Ready/Failed.

**Target Evolution:**
After the client is ready, the hosted service (or a new hosted service) iterates `IEnumerable<IToolProvider>` and calls `IToolRegistrar.RegisterProviderAsync` for each. Registration triggers `DiscoverAsync` on the provider and populates the catalog. A background loop per provider awaits `WaitForSurfaceChangeAsync` for hot refresh.

### 4. Event Passthrough

**Current Implementation:**
`SessionEventBridge.Bridge` converts SDK push events (`SessionEvent`) to an `IAsyncEnumerable` via a bounded channel. Events flow unmodified from SDK → bridge → hub → client.

**Target Evolution:**
No change to event bridging. The SDK already emits `ToolExecutionStartEvent` and `ToolExecutionCompleteEvent` for every tool call, including custom tools registered via `SessionConfig.Tools`. If provider metadata decoration is needed (REQ-015), it happens in `AgentMessageService`'s event handler, not in a new pipeline.

## What Exists vs What's Needed

### Currently Built

| Component | Status | Notes |
|-----------|--------|-------|
| `IGatewayClient` + `CopilotGatewayClient` | ✅ | Wraps `CopilotClient`, supports `CreateSessionAsync`/`ResumeSessionAsync` |
| `IGatewaySession` + `CopilotGatewaySession` | ✅ | Wraps `CopilotSession`, supports `On`, `SendAsync` |
| `SessionPool` | ✅ | Per-caller session reuse with auto-reap |
| `AgentMessageService` | ✅ | Orchestrates concurrency → session → events → stream |
| `SessionEventBridge` | ✅ | Push-to-pull event conversion via channels |
| `GatewayHostedService` | ✅ | Lifecycle, mind validation, client startup |
| `GatewayServiceExtensions` | ✅ | DI registration for all gateway services |
| `GatewayHub` | ✅ | SignalR routing layer |
| `ResumeSessionAsync` on `IGatewayClient` | ✅ | Already exposed; accepts `ResumeSessionConfig` |

### Needed

| Component | Status | Source |
|-----------|--------|--------|
| `IToolProvider` interface | ❌ | New — provider abstraction with discovery + change signal |
| `IToolCatalog` interface | ❌ | New — read-side catalog (default tools, lookup, search) |
| `IToolRegistrar` interface | ❌ | New — write-side registry (register, unregister, refresh) |
| `IToolExpander` interface | ❌ | New — per-session `expand_tools` factory |
| `ToolBridge` implementation | ❌ | New — singleton implementing `IToolCatalog` + `IToolRegistrar` |
| `ToolExpander` implementation | ❌ | New — `CreateExpandToolsFunction` with load/query modes |
| `ToolDescriptor` record | ❌ | New — immutable value type wrapping `AIFunction` + metadata |
| `ToolSourceTier` enum | ❌ | New — Bundled / Workspace / Managed |
| `ToolStatus` enum | ❌ | New — Ready / Degraded / Unavailable |
| Session creation update | ❌ | Modify `AgentMessageService.GetOrCreateSessionAsync` |
| DI registration update | ❌ | Modify `GatewayServiceExtensions` |
| Provider registration in startup | ❌ | Modify or extend `GatewayHostedService` |

## Key Insights

### What Works Well
1. **Clean interface boundaries** — `IGatewayClient`/`IGatewaySession` make the SDK testable and extensible. The tool bridge follows the same pattern with `IToolProvider`/`IToolCatalog`.
2. **Factory pattern for sessions** — `SessionPool.GetOrCreateAsync` already accepts a factory lambda, making it straightforward to inject tool population.
3. **`ResumeSessionAsync` is available** — The SDK and `IGatewayClient` already expose session resumption with tool configuration, which is exactly what `expand_tools` needs.
4. **Event passthrough requires no changes** — SDK-emitted tool events flow through `SessionEventBridge` untouched.

### Gaps/Limitations

| Limitation | Solution |
|------------|----------|
| No `Services/Tools/` directory exists | Create it; all tool bridge types live here |
| `SessionConfig.Tools` is never populated | Extend factory lambda in `GetOrCreateSessionAsync` |
| No mechanism for hosted-service-level provider registration | Register providers during `GatewayHostedService.StartAsync` or a dedicated hosted service |
| SDK `ResumeSessionAsync` re-sends all tools (no delta) | Acceptable — sessions grow incrementally; ~100-200 tool ceiling before payload concern |
| `expand_tools` needs session reference before session exists | Solve with deferred binding — capture mutable list + `TaskCompletionSource<CopilotSession>` |
| No background loop infrastructure for change signals | Implement in `ToolBridge` with per-provider `CancellationTokenSource` |
