# Gateway SignalR + OpenResponses Analysis

## Executive Summary

The Gateway currently starts a Copilot SDK client in its hosted service and maps an empty SignalR hub at `/gateway`, but neither surface can carry a conversation. This feature connects the dots: a shared agent runtime that owns session lifecycle and concurrency, a strongly-typed SignalR hub that streams agent events to web clients, an OpenResponses-compliant HTTP endpoint (in a separate middleware library) for stateless API consumers, a static-file chat UI for immediate interaction, and upgraded health probes that reflect actual runtime state.

| Pattern | Integration Point |
|---------|-------------------|
| Copilot SDK session → IAgentRuntime | Wraps CreateSessionAsync/ResumeSessionAsync, maps SDK events to StreamEvent |
| IdentityLoader → Agent Runtime | System message injected into every new session |
| GatewayHostedService → IAgentRuntime | Hosted service owns CopilotClient; runtime receives it via DI |
| GatewayHub → IAgentRuntime | Hub methods delegate to runtime, stream results as IAsyncEnumerable |
| OpenResponses middleware → IAgentRuntime | HTTP endpoint delegates to same runtime, maps to SSE |
| MindReader → Bundled tools (future) | Runtime will register MindReader-backed tools for agent self-service |

## Architecture Comparison

### Current Architecture

```
┌────────────────────────────────────────────────┐
│  msclaw start --mind ~/src/ernist              │
│                                                │
│  ┌──────────────────┐  ┌────────────────────┐  │
│  │ GatewayHosted    │  │ StartCommand       │  │
│  │   Service        │  │   ConfigureServices│  │
│  │   ─ validate     │  │   MapEndpoints     │  │
│  │   ─ load identity│  │                    │  │
│  │   ─ start client │  │  GET /healthz      │  │
│  └────────┬─────────┘  │  POST /gateway ──► │──┼── GatewayHub : Hub { }
│           │             └────────────────────┘  │       (empty)
│           ▼                                     │
│  ┌──────────────────┐                           │
│  │ CopilotClient    │                           │
│  │   (SDK, started)  │                           │
│  │   (no sessions)  │                           │
│  └──────────────────┘                           │
└────────────────────────────────────────────────┘
```

- `GatewayHostedService` owns the `CopilotClient` lifecycle but doesn't expose it
- `GatewayHub` extends `Hub` with zero methods — clients can connect but do nothing
- `/healthz` returns Healthy/Unhealthy based on `IGatewayHostedService.IsReady`
- No session management, no message sending, no streaming

### Target Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  msclaw start --mind ~/src/ernist                               │
│                                                                 │
│  ┌────────────────────┐  ┌──────────────────────────────────┐   │
│  │ GatewayHosted      │  │ HTTP Pipeline                    │   │
│  │   Service           │  │                                  │   │
│  │   ─ validate        │  │  GET  /health        (liveness)  │   │
│  │   ─ load identity   │  │  GET  /health/ready  (readiness) │   │
│  │   ─ start client ───┼──┤  POST /v1/responses  (SSE)      │   │
│  │   ─ create runtime  │  │  POST /gateway       (SignalR)   │   │
│  └────────┬────────────┘  │  GET  /              (static UI) │   │
│           │               └──────────┬───────────────────────┘   │
│           ▼                          │                           │
│  ┌──────────────────┐                ▼                           │
│  │ AgentRuntime     │◄──── GatewayHub : Hub<IGatewayHubClient>  │
│  │  (IAgentRuntime) │◄──── OpenResponsesMiddleware              │
│  │  ─ sessions      │                                           │
│  │  ─ concurrency   │      ┌──────────────────┐                 │
│  │  ─ streaming     │◄─────│ CopilotClient    │                 │
│  └──────────────────┘      │  (SDK singleton)  │                 │
│                            └──────────────────┘                 │
└─────────────────────────────────────────────────────────────────┘
```

## Pattern Mapping

### 1. CopilotClient Lifecycle → Behind IGatewayClient Boundary

**Current Implementation:**
`GatewayHostedService` creates a `CopilotGatewayClient` (private nested class wrapping `CopilotClient`) via a factory lambda. The client is started/stopped but never exposed for session creation.

**Target Evolution:**
`GatewayHostedService` still owns the `CopilotClient` lifecycle. The existing `IGatewayClient` interface is **extended** with session operations (`CreateSessionAsync`, `ResumeSessionAsync`, `ListSessionsAsync`, `DeleteSessionAsync`). A new `IGatewaySession` interface wraps `CopilotSession`. The hub and middleware depend on `IGatewayClient` — never on `CopilotClient` directly. This preserves the Dependency Rule and keeps tests fast/deterministic. SDK **data types** (`SessionEvent`, `SessionConfig`) still flow through untransformed.

### 2. Identity Loading → Session System Message

**Current Implementation:**
`GatewayHostedService.StartAsync` calls `identityLoader.LoadSystemMessageAsync(mindPath)` but discards the result — it only validates that identity loading succeeds.

**Target Evolution:**
The loaded system message is passed to `IAgentRuntime` at initialization. When the runtime creates a Copilot SDK session, it sets `SessionConfig.SystemMessage` with `Mode = Append` and the loaded content, ensuring every session carries the mind's personality.

### 3. GatewayHub → Thin Routing Layer

**Current Implementation:**
`GatewayHub : Hub { }` — empty class, no client contract.

**Target Evolution:**
`GatewayHub : Hub<IGatewayHubClient>` as a **thin routing layer**. Each hub method is a one-liner delegation. `SendMessage` delegates to `AgentMessageService.SendAsync()`. Session operations delegate to `IGatewayClient`. No orchestration, concurrency management, or bridging logic lives in the hub itself. The `IGatewayHubClient` interface defines server-to-client push methods: `ReceiveEvent`, `ReceivePresence`.

### 4. Health Endpoint → Structured Health Probes

**Current Implementation:**
Single `GET /healthz` returning `{ status: "Healthy" }` or `{ status: "Unhealthy", error: "..." }` based on `IGatewayHostedService.IsReady`.

**Target Evolution:**
Two endpoints: `GET /health` (liveness — is the process alive?) and `GET /health/ready` (readiness — is runtime ready to serve?). Readiness checks: mind validated, identity loaded, CopilotClient connected, runtime state = Ready. Maps to spec REQ-001/REQ-002 from gateway-http-surface.md.

### 5. Static Files → Chat UI

**Current Implementation:**
No static file serving. No `wwwroot/` directory.

**Target Evolution:**
`UseDefaultFiles()` + `UseStaticFiles()` added to pipeline. `wwwroot/index.html` serves a vanilla HTML/JS chat UI using `@microsoft/signalr` client library. Connects to `/gateway`, sends messages, renders streamed assistant responses. No build toolchain — plain HTML/CSS/JS.

## What Exists vs What's Needed

### Currently Built
| Component | Status | Notes |
|-----------|--------|-------|
| GatewayHostedService | ✅ | Validates mind, loads identity, starts CopilotClient |
| GatewayHub (empty) | ✅ | Extends Hub, mapped at /gateway |
| GatewayState enum | ✅ | Starting → Validating → Ready/Failed → Stopping → Stopped |
| GatewayOptions | ✅ | MindPath, Host, Port |
| StartCommand DI wiring | ✅ | SignalR, validators, identity loader registered |
| /healthz endpoint | ✅ | Basic healthy/unhealthy |
| MsClaw.Core interfaces | ✅ | IMindValidator, IIdentityLoader, IMindReader, IMindScaffold |
| MsClawClientFactory | ✅ | Creates CopilotClient from mind path |
| Test infrastructure | ✅ | Hand-written stubs, xUnit, DI tests, HTTP result tests |

### Needed
| Component | Status | Source |
|-----------|--------|--------|
| IConcurrencyGate + ISessionMap + CallerRegistry | ❌ | New — split per ISP: concurrency gating + caller-session mapping |
| IGatewayClient extension + IGatewaySession | ❌ | Extend existing interface with session ops; new session boundary |
| SessionEventBridge | ❌ | New — shared push-to-pull bridge using Channel&lt;SessionEvent&gt; |
| AgentMessageService | ❌ | New — orchestrates gate → session → bridge → yield → release |
| IGatewayHubClient | ❌ | New — strongly-typed client contract |
| GatewayHub methods | ❌ | Evolve empty hub → thin routing layer delegating to AgentMessageService |
| MsClaw.OpenResponses project | ❌ | New — separate middleware library (SDK events → OpenResponses JSON) |
| OpenResponses DTOs | ❌ | New — request/response matching spec (genuinely new, SDK has no equivalent) |
| SSE formatting | ❌ | New — maps SDK events to OpenResponses SSE format |
| /health + /health/ready | ❌ | Replace /healthz with structured probes |
| wwwroot/ chat UI | ❌ | New — vanilla HTML/JS/CSS |
| System message exposure | ❌ | Modify hosted service to capture and expose loaded system message |
| Integration tests | ❌ | New — hub streaming, concurrency rejection |

## Key Insights

### What Works Well
1. **GatewayHostedService lifecycle is solid** — validates mind, loads identity, manages CopilotClient start/stop with proper state transitions. The runtime can piggyback on this lifecycle.
2. **DI registration is centralized** — `StartCommand.ConfigureServices` is the single place to add new services. Adding `IAgentRuntime` as a singleton fits naturally.
3. **Test patterns are consistent** — hand-written sealed stubs, factory lambdas, NullLogger. New tests for runtime and hub follow the same style.
4. **GatewayState enum is extensible** — could add `Degraded` state to match agent-runtime REQ-017.
5. **MsClaw.Core already provides all mind operations** — `IIdentityLoader`, `IMindReader`, `IMindValidator` cover identity loading, file reading, and validation.

### Gaps/Limitations
| Limitation | Solution |
|------------|----------|
| CopilotClient is private to hosted service | Extend IGatewayClient with session ops; hosted service already owns the boundary |
| No session management | Hub delegates to AgentMessageService which calls IGatewayClient; ISessionMap tracks caller-to-session mapping |
| Hub has no client contract | Define `IGatewayHubClient` interface; change base class to `Hub<IGatewayHubClient>` |
| Single /healthz endpoint | Replace with /health (liveness) and /health/ready (readiness) per spec |
| No static file middleware | Add `UseDefaultFiles()` + `UseStaticFiles()` before endpoint mapping |
| No OpenResponses endpoint | New `MsClaw.OpenResponses` middleware library with POST /v1/responses |
| GatewayState missing Degraded | Add `Degraded` variant for runtime REQ-017 (future — MVP uses Ready/Failed only) |
| Identity string is discarded | Capture return value of `LoadSystemMessageAsync`, expose via hosted service property |
| Push-to-pull bridge needed by both hub and middleware | Extract SessionEventBridge as shared utility (DRY) |
