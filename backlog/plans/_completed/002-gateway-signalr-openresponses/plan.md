# Plan: Gateway SignalR Hub + OpenResponses

## Summary

Wire the empty `GatewayHub` to the Copilot SDK via clean architectural boundaries, implement the core SignalR streaming contract through a shared `AgentMessageService`, add an OpenResponses-compliant HTTP surface in a separate middleware library, serve a static chat UI, and upgrade health endpoints. Both SignalR and HTTP surfaces share the same orchestration (`AgentMessageService`) backed by split coordination interfaces (`IConcurrencyGate` + `ISessionMap`) per ISP.

## Architecture

The Copilot SDK's `CopilotClient` does the heavy lifting — session lifecycle, message sending, event streaming. We keep it behind the existing `IGatewayClient` interface boundary (Dependency Rule) and add only what the SDK lacks: caller-key mapping (`ISessionMap`), per-caller concurrency gating (`IConcurrencyGate`), push-to-pull event bridging (`SessionEventBridge`), and send-message orchestration (`AgentMessageService`). SDK **data types** flow through untransformed; SDK **service types** stay behind testable interfaces.

```
┌─────────────────────────────────────────────────────────────────────┐
│  MsClaw Gateway Process                                             │
│                                                                     │
│  ┌─────────────────────┐    ┌──────────────────────────────────┐   │
│  │ GatewayHostedService│    │ ASP.NET Core Pipeline           │    │
│  │  - validate mind    │    │                                 │    │
│  │  - load identity   ─┼──► │  UseDefaultFiles / UseStaticFiles│    │
│  │  - start GatewayClient   │  GET  /             (chat UI)  │    │
│  └──────────┬──────────┘    │  GET  /health       (liveness)  │    │
│             │               │  GET  /health/ready (readiness) │    │
│             │ (DI)          │  POST /v1/responses (OpenResp.) │    │
│             ▼               │  POST /gateway      (SignalR)   │    │
│  ┌──────────────────────┐   └──────────┬──────────────────────┘    │
│  │ IGatewayClient       │              │                           │
│  │  (SDK boundary)      │   ┌──────────┴──────────────────────┐    │
│  │  - CreateSession     │◄──│ GatewayHub<IGatewayHubClient>   │    │
│  │  - ResumeSession     │   │  Thin routing — delegates to    │    │
│  │  - ListSessions      │   │  AgentMessageService            │    │
│  │  - DeleteSession     │   └────────────┬────────────────────┘    │
│  └──────────────────────┘                │                         │
│             ▲               ┌────────────┴────────────────────┐    │
│             │               │ AgentMessageService              │    │
│  ┌──────────┴───────────┐   │  gate → session → bridge →      │    │
│  │ CopilotClient (SDK)  │   │  yield SDK events → release     │    │
│  │  (behind IGateway-   │   └───┬───────────┬────────────────┘    │
│  │   Client boundary)   │       │           │                     │
│  └──────────────────────┘   ┌───┴────────┐ ┌┴──────────────────┐  │
│                             │IConcurrency│ │ISessionMap         │  │
│  ┌──────────────────────┐   │Gate        │ │  CallerKey →       │  │
│  │ SessionEventBridge   │   │  Sema(1)   │ │  SessionId         │  │
│  │  push → Channel<T>   │   └────────────┘ └──────────────────┘  │
│  │  → IAsyncEnumerable  │                                         │
│  └──────────────────────┘   ┌─────────────────────────────────┐   │
│                             │ OpenResponsesMiddleware         │    │
│                             │  Uses AgentMessageService,      │    │
│                             │  maps SDK events → JSON/SSE     │    │
│                             └─────────────────────────────────┘   │
│                             ┌─────────────────────────────────┐   │
│                             │ wwwroot/                        │    │
│                             │  index.html (SignalR JS client) │    │
│                             │  css/site.css                   │    │
│                             └─────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
```

### Component Responsibilities

| Component | Role | Integrates With |
|-----------|------|-----------------|
| IGatewayClient (boundary) | Testable interface over CopilotClient — session lifecycle, messaging | AgentMessageService, GatewayHub |
| IGatewaySession (boundary) | Testable interface over CopilotSession — events, send, abort, history | AgentMessageService, SessionEventBridge |
| IConcurrencyGate | Per-caller SemaphoreSlim(1), reject mode | AgentMessageService |
| ISessionMap | Caller-key → session-ID mapping | AgentMessageService, GatewayHub |
| CallerRegistry | Single class implementing both IConcurrencyGate and ISessionMap | DI (registered once, resolved as both interfaces) |
| SessionEventBridge | Converts SDK push events → IAsyncEnumerable pull via Channel&lt;T&gt; | AgentMessageService, OpenResponsesMiddleware |
| AgentMessageService | Orchestrates: gate → session → bridge → yield → release | GatewayHub, OpenResponsesMiddleware |
| GatewayHub | Thin SignalR routing layer — one-liner delegations | AgentMessageService, IGatewayClient, ISessionMap |
| IGatewayHubClient | Server-to-client push contract | GatewayHub (compile-time safety) |
| OpenResponsesMiddleware | Maps SDK events → OpenResponses JSON/SSE | AgentMessageService |
| GatewayHostedService | Lifecycle: validate, load identity, start IGatewayClient | IGatewayClient |
| Health endpoints | Liveness and readiness probes | IGatewayHostedService |
| wwwroot/ | Static chat UI | GatewayHub (via SignalR JS client) |

### Data Flow: Send Message (SignalR)

```
Browser              GatewayHub        AgentMessage     IConcurrency   ISessionMap    IGatewayClient
  │                     │              Service           Gate            │              │
  │  SendMessage(prompt)│                │                │             │              │
  │────────────────────►│                │                │             │              │
  │                     │  SendAsync()   │                │             │              │
  │                     │───────────────►│                │             │              │
  │                     │               │  TryAcquire()   │             │              │
  │                     │               │────────────────►│             │              │
  │                     │               │  ◄── ok/reject  │             │              │
  │                     │               │                 │             │              │
  │                     │               │  GetSessionId() │             │              │
  │                     │               │─────────────────────────────►│              │
  │                     │               │  ◄── sessionId  │             │              │
  │                     │               │                 │             │              │
  │                     │               │  Resume/Create session ─────────────────────►│
  │                     │               │  Bridge(session) → Channel<SessionEvent>     │
  │                     │               │  session.SendAsync(prompt) ─────────────────►│
  │                     │               │                 │             │              │
  │  ◄── yield event ──┤◄── yield ─────┤◄── channel ────┤  ◄── SDK ──│              │
  │                     │               │                 │             │              │
  │  (SessionIdleEvent) │               │  Release()      │             │              │
  │                     │               │────────────────►│             │              │
```

### Data Flow: POST /v1/responses (SSE)

```
HTTP Client            OpenResponses MW   AgentMessage     IConcurrency   IGatewayClient
  │                         │              Service          Gate            │
  │  POST /v1/responses     │                │               │             │
  │  { model, input,        │  SendAsync()   │               │             │
  │    stream: true }       │───────────────►│               │             │
  │────────────────────────►│               │  TryAcquire()  │             │
  │                         │               │───────────────►│             │
  │                         │               │  ◄── ok / 409  │             │
  │                         │               │                │             │
  │                         │  (same orchestration as above)               │
  │                         │  Maps SDK events → OpenResponses JSON        │
  │  ◄── SSE: response.created                               │             │
  │  ◄── SSE: output_text.delta                               │             │
  │  ◄── SSE: response.completed                              │             │
  │  ◄── data: [DONE]      │  Release()     │               │             │
```

## File Structure

```
src/
├── MsClaw.Gateway/
│   ├── Commands/
│   │   └── StartCommand.cs              # MODIFY: DI, health endpoints, static files, OpenResponses
│   ├── Hosting/
│   │   ├── GatewayHostedService.cs      # MODIFY: expose SystemMessage, extend CopilotGatewayClient with session ops
│   │   ├── IGatewayHostedService.cs     # MODIFY: add SystemMessage property
│   │   ├── IGatewayClient.cs            # MODIFY: add session operations (CreateSessionAsync, etc.)
│   │   ├── IGatewaySession.cs           # NEW: testable boundary around CopilotSession
│   │   └── CopilotGatewaySession.cs     # NEW: thin wrapper delegating to SDK CopilotSession
│   ├── Hubs/
│   │   ├── GatewayHub.cs               # MODIFY: Hub<IGatewayHubClient>, thin routing to AgentMessageService
│   │   └── IGatewayHubClient.cs        # NEW: server-to-client contract
│   ├── Services/
│   │   ├── IConcurrencyGate.cs         # NEW: per-caller concurrency gating interface
│   │   ├── ISessionMap.cs              # NEW: caller-key → session-ID mapping interface
│   │   ├── CallerRegistry.cs           # NEW: implements both IConcurrencyGate + ISessionMap
│   │   ├── SessionEventBridge.cs       # NEW: SDK push → IAsyncEnumerable pull via Channel<T>
│   │   └── AgentMessageService.cs      # NEW: orchestrates gate → session → bridge → yield → release
│   ├── wwwroot/
│   │   ├── index.html                  # NEW: chat UI
│   │   └── css/
│   │       └── site.css                # NEW: chat styling
│   ├── GatewayOptions.cs               # UNCHANGED
│   ├── GatewayState.cs                 # UNCHANGED
│   ├── Program.cs                      # UNCHANGED
│   └── MsClaw.Gateway.csproj           # MODIFY: add MsClaw.OpenResponses reference
├── MsClaw.OpenResponses/
│   ├── OpenResponsesMiddleware.cs      # NEW: HTTP middleware (SDK events → OpenResponses JSON/SSE)
│   ├── Models/
│   │   ├── ResponseRequest.cs          # NEW: request DTO
│   │   ├── ResponseObject.cs           # NEW: response DTO (OpenResponses schema)
│   │   └── OutputItem.cs               # NEW: output item types
│   ├── Extensions/
│   │   └── EndpointRouteBuilderExtensions.cs  # NEW: MapOpenResponses()
│   └── MsClaw.OpenResponses.csproj     # NEW: class library
├── MsClaw.Gateway.Tests/
│   ├── CallerRegistryTests.cs          # NEW: IConcurrencyGate + ISessionMap tests
│   ├── SessionEventBridgeTests.cs      # NEW: push-to-pull bridge tests
│   ├── AgentMessageServiceTests.cs     # NEW: orchestration tests
│   ├── GatewayHubTests.cs             # MODIFY: thin routing delegation tests
│   ├── StartCommandHealthTests.cs      # MODIFY: new health endpoints
│   └── StartCommandDiTests.cs          # MODIFY: verify registrations
├── MsClaw.OpenResponses.Tests/
│   ├── ResponseRequestTests.cs         # NEW: DTO validation tests
│   ├── SseFormatterTests.cs            # NEW: SSE formatting tests
│   └── MsClaw.OpenResponses.Tests.csproj # NEW: test project
└── MsClaw.slnx                         # MODIFY: add new projects
```

## Critical: SDK Event Push-to-Pull Bridge (SessionEventBridge)

**Problem**: The Copilot SDK emits events via a push-based `session.On(evt => ...)` callback, but SignalR's server-to-client streaming requires `IAsyncEnumerable<T>`. We need to bridge from push to pull.

**Solution**: `SessionEventBridge` — a shared internal utility that uses a `Channel<SessionEvent>` as the bridge. The SDK callback writes events to the channel writer. The consumer reads from the channel reader as `IAsyncEnumerable`. When `SessionIdleEvent` fires, the channel completes. Extracted as a class (not inline) because both the hub (via `AgentMessageService`) and the OpenResponses middleware need the same bridge.

```
session.On(evt => channel.Writer.TryWrite(evt))
                            │
     consumer yields ◄── channel.Reader.ReadAllAsync()
```

The OpenResponses middleware uses the same bridge (via `AgentMessageService`), then maps SDK event types to OpenResponses JSON. That mapping IS new logic the SDK doesn't provide.

## Implementation Phases

| Phase | Name | Description |
|-------|------|-------------|
| 1 | Coordination Layer | IConcurrencyGate + ISessionMap (split per ISP), IGatewayClient/IGatewaySession boundary, CallerRegistry |
| 2 | SignalR Hub | SessionEventBridge, AgentMessageService, IGatewayHubClient, thin GatewayHub routing |
| 3 | Health Probes | Replace /healthz with /health and /health/ready |
| 4 | OpenResponses Library | MsClaw.OpenResponses project, DTOs, SDK event → OpenResponses SSE mapping |
| 5 | Chat UI | wwwroot/ static files, SignalR JS client, CSS |
| 6 | Integration Tests | End-to-end hub streaming, concurrency rejection |

## Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Split coordination interfaces | IConcurrencyGate + ISessionMap (not ICallerRegistry) | ISP: concurrency strategy and session mapping change for different reasons |
| SDK service types behind boundary | IGatewayClient + IGatewaySession interfaces | Dependency Rule: hub depends inward on interfaces, not outward on SDK concretes; enables fast deterministic tests |
| SDK data types pass through | SessionEvent, SessionConfig, MessageOptions flow untransformed | "Never Rewrite What You've Already Imported" — data types are stable; wrapping adds zero value |
| Extracted push-to-pull bridge | SessionEventBridge as shared internal class | DRY: both hub and OpenResponses middleware need the same Channel-based bridge |
| Extracted orchestration | AgentMessageService owns gate → session → bridge → yield → release | SRP: hub is thin routing, service is testable without SignalR infrastructure, middleware reuses same logic |
| OpenResponses as separate library | MsClaw.OpenResponses project | User decision: enables reuse without gateway dependency |
| Concurrency model | SemaphoreSlim(1) per caller key, reject mode | Simplest correct implementation; queue/replace modes added later |
| Session mapping | ConcurrentDictionary<string, string> (callerKey → sessionId) | Thread-safe, O(1) lookup |
| Chat UI tech | Vanilla HTML/JS/CSS, no build toolchain | Minimal friction; CDN SignalR JS client; development convenience |
| Hub streaming type | IAsyncEnumerable<SessionEvent> (SDK data type) | SDK data types pass through directly; no mapping layer in the hub |
| System message mode | Append (not Replace) | Preserves SDK safety guardrails per agent-runtime REQ-002 |

## Configuration Example

```json
{
  "Gateway": {
    "MindPath": "~/src/ernist",
    "Host": "127.0.0.1",
    "Port": 18789
  }
}
```

```bash
msclaw start --mind ~/src/ernist
# Gateway starts at http://127.0.0.1:18789
# Chat UI: http://127.0.0.1:18789/
# SignalR:  http://127.0.0.1:18789/gateway
# API:     POST http://127.0.0.1:18789/v1/responses
# Health:  GET  http://127.0.0.1:18789/health
# Ready:   GET  http://127.0.0.1:18789/health/ready
```

## Files to Modify

| File | Change |
|------|--------|
| src/MsClaw.Gateway/Commands/StartCommand.cs | Add IConcurrencyGate/ISessionMap/AgentMessageService DI, health endpoints, static files, OpenResponses |
| src/MsClaw.Gateway/Hosting/GatewayHostedService.cs | Capture and expose SystemMessage; extend CopilotGatewayClient with session ops |
| src/MsClaw.Gateway/Hosting/IGatewayHostedService.cs | Add SystemMessage property |
| src/MsClaw.Gateway/Hosting/IGatewayClient.cs | Add CreateSessionAsync, ResumeSessionAsync, ListSessionsAsync, DeleteSessionAsync |
| src/MsClaw.Gateway/Hubs/GatewayHub.cs | Change to Hub<IGatewayHubClient>, thin routing delegating to AgentMessageService |
| src/MsClaw.Gateway/MsClaw.Gateway.csproj | Add MsClaw.OpenResponses project reference |
| src/MsClaw.Gateway.Tests/GatewayHubTests.cs | Add thin routing delegation tests |
| src/MsClaw.Gateway.Tests/StartCommandDiTests.cs | Verify IConcurrencyGate, ISessionMap, AgentMessageService registration |
| src/MsClaw.Gateway.Tests/StartCommandHealthTests.cs | Test new health/ready endpoints |
| src/MsClaw.slnx | Add MsClaw.OpenResponses and MsClaw.OpenResponses.Tests projects |

## New Files

| File | Purpose |
|------|---------|
| src/MsClaw.Gateway/Hubs/IGatewayHubClient.cs | Server-to-client typed contract |
| src/MsClaw.Gateway/Hosting/IGatewaySession.cs | Testable boundary around SDK CopilotSession |
| src/MsClaw.Gateway/Hosting/CopilotGatewaySession.cs | Thin wrapper delegating to SDK CopilotSession |
| src/MsClaw.Gateway/Services/IConcurrencyGate.cs | Per-caller concurrency gating interface (ISP) |
| src/MsClaw.Gateway/Services/ISessionMap.cs | Caller-key → session-ID mapping interface (ISP) |
| src/MsClaw.Gateway/Services/CallerRegistry.cs | Implements both IConcurrencyGate + ISessionMap |
| src/MsClaw.Gateway/Services/SessionEventBridge.cs | Shared push-to-pull bridge using Channel&lt;SessionEvent&gt; |
| src/MsClaw.Gateway/Services/AgentMessageService.cs | Orchestrates gate → session → bridge → yield → release |
| src/MsClaw.Gateway/wwwroot/index.html | Chat UI |
| src/MsClaw.Gateway/wwwroot/css/site.css | Chat styling |
| src/MsClaw.Gateway.Tests/CallerRegistryTests.cs | IConcurrencyGate + ISessionMap tests |
| src/MsClaw.Gateway.Tests/SessionEventBridgeTests.cs | Push-to-pull bridge tests |
| src/MsClaw.Gateway.Tests/AgentMessageServiceTests.cs | Orchestration tests |
| src/MsClaw.OpenResponses/ (entire project) | OpenResponses middleware library (SDK events → OpenResponses JSON/SSE) |
| src/MsClaw.OpenResponses.Tests/ (entire project) | OpenResponses unit tests |

## Verification

1. `dotnet build src/MsClaw.slnx --nologo` passes
2. `dotnet test src/MsClaw.Gateway.Tests/MsClaw.Gateway.Tests.csproj --nologo` passes
3. `dotnet test src/MsClaw.OpenResponses.Tests/MsClaw.OpenResponses.Tests.csproj --nologo` passes
4. `dotnet test src/MsClaw.Core.Tests/MsClaw.Core.Tests.csproj --nologo` passes (no regressions)
5. Manual: `msclaw start --mind ~/src/ernist`, open browser to http://127.0.0.1:18789/, send message, see streamed reply

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| SDK event model incompatibility | Prototype Channel-based mapping in Phase 1 before building hub |
| SignalR IAsyncEnumerable cancellation | Use CancellationToken from hub context, wire to channel completion |
| SSE formatting errors | Unit test SSE output against known-good OpenResponses examples |
| Static files not served | Test UseDefaultFiles + UseStaticFiles in integration test |

## Limitations (MVP)

1. No authentication - loopback bypass only
2. Reject-only concurrency (no queue/replace modes)
3. No session delete/reset through hub
4. No bundled tools, workspace skills, or node-provided tools
5. OpenResponses basic subset only (no multi-turn, no tool execution)
6. No model selection (uses SDK default)
7. No file attachments
8. Chat UI is a development convenience, not a production surface

## References

- [Gateway Protocol Spec](../../../specs/gateway-protocol.md)
- [Gateway HTTP Surface Spec](../../../specs/gateway-http-surface.md)
- [Gateway Agent Runtime Spec](../../../specs/gateway-agent-runtime.md)
- [OpenResponses Specification](https://www.openresponses.org/specification)
- [Quick Plan](../../../backlog/plans/20260306-gateway-signalr-openresponses.md)
- [Copilot SDK Instructions](../../../.github/instructions/copilot-sdk-csharp.instructions.md)
- [SignalR Instructions](../../../.github/instructions/signalr-csharp.instructions.md)
