# Plan: Tool Bridge — Provider Abstraction & Registry

## Summary

Build a tool bridge that decouples tool lifecycle management from individual tool sources. The bridge has three internal parts: a `ToolCatalogStore` (shared `ConcurrentDictionary` + lookup logic), a `ToolBridge` implementing `IToolCatalog` (read), and a `ToolRegistrar` implementing `IToolRegistrar` (write). Both `ToolBridge` and `ToolRegistrar` compose over the shared `ToolCatalogStore` — they share data, not responsibility. A separate `ToolExpander` creates per-session `expand_tools` AIFunction instances for lazy tool loading. Sessions start with only default tools + `expand_tools`; additional tools are added via `ResumeSessionAsync`. No `AvailableTools`/`ExcludedTools` are used. Provider surface-change watch loops are owned by `ToolBridgeHostedService`, not by the registrar — the hosted service drives mutations, the registrar executes them.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    AgentMessageService                       │
│  GetOrCreateSessionAsync:                                   │
│    tools = catalog.GetDefaultTools()                        │
│    + expander.CreateExpandToolsFunction(session, tools)     │
│    → SessionConfig { Tools = tools }                        │
└──────────────────────────┬──────────────────────────────────┘
                           │
              ┌────────────┴────────────┐
              │                         │
              ▼                         ▼
┌──────────────────────┐   ┌──────────────────────────────────┐
│ IToolCatalog (read)  │   │ IToolExpander (session-aware)    │
│  GetDefaultTools()   │   │  CreateExpandToolsFunction()     │
│  GetToolsByName()    │   │    ├─ Load mode: names → add     │
│  SearchTools()       │   │    └─ Query mode: search catalog │
│  GetDescriptor()     │   └──────────────────────────────────┘
└──────────┬───────────┘
           │ (shared ToolCatalogStore)
┌──────────┴───────────┐
│ IToolRegistrar (write)│       ┌───────────────────────────┐
│  RegisterProvider()   │       │ToolBridgeHostedService    │
│  UnregisterProvider() │◄──────│  owns watch loops         │
│  RefreshProvider()    │       │  WaitForSurfaceChangeAsync│
└──────────┬───────────┘       └───────────────────────────┘
           │
    ┌──────┴──────┬──────────────┐
    ▼             ▼              ▼
┌─────────┐ ┌─────────┐  ┌─────────┐
│Provider │ │Provider │  │Provider │
│(Bundled)│ │(Cron)   │  │(MCPorter│
└─────────┘ └─────────┘  └─────────┘
```

## Detailed Architecture

### Component Responsibilities

| Component | Role | Integrates With |
|-----------|------|-----------------|
| `ToolCatalogStore` | Internal shared data structure — `ConcurrentDictionary` + status map + provider index. No public interface. | `ToolBridge`, `ToolRegistrar` |
| `ToolBridge` | Read-side catalog — implements `IToolCatalog`. Lookups, search, default tools. | `ToolCatalogStore`, `AgentMessageService`, `ToolExpander` |
| `ToolRegistrar` | Write-side registry — implements `IToolRegistrar`. Register, unregister, refresh providers. Validates descriptors, enforces tier priority. | `ToolCatalogStore`, `IToolProvider` implementations, `ToolBridgeHostedService` |
| `ToolBridgeHostedService` | Process driver — registers providers at startup, owns per-provider `WaitForSurfaceChangeAsync` watch loops, calls `RefreshProviderAsync` on change. | `IToolRegistrar`, `IEnumerable<IToolProvider>` |
| `ToolExpander` | Creates per-session `expand_tools` AIFunction with load/query modes | `IToolCatalog`, `IGatewayClient` (for `ResumeSessionAsync`) |
| `IToolProvider` | Discovers tools, provides AIFunction handlers, signals surface changes | `ToolRegistrar` (via `IToolRegistrar`) |
| `ToolDescriptor` | Immutable value type wrapping AIFunction + metadata | Catalog indexing, collision resolution |

### Data Flow: Session Creation

```
AgentMessageService          IToolCatalog           IToolExpander           SDK
       │                         │                       │                   │
       │  GetDefaultTools()      │                       │                   │
       │────────────────────────►│                       │                   │
       │  [AlwaysVisible tools]  │                       │                   │
       │◄────────────────────────│                       │                   │
       │                         │                       │                   │
       │  Build mutable tool list (defaults)             │                   │
       │                         │                       │                   │
       │  CreateExpandToolsFunction(sessionHolder, tools)│                   │
       │────────────────────────────────────────────────►│                   │
       │  expand_tools AIFunction                        │                   │
       │◄────────────────────────────────────────────────│                   │
       │                         │                       │                   │
       │  Add expand_tools to tool list                  │                   │
       │                         │                       │                   │
       │  CreateSessionAsync(SessionConfig {             │                   │
       │    Streaming = true,    │                       │                   │
       │    SystemMessage = ..., │                       │                   │
       │    Tools = [defaults + expand_tools]             │                   │
       │  })─────────────────────────────────────────────────────────────────►│
       │                         │                       │              session
       │◄────────────────────────────────────────────────────────────────────│
       │                         │                       │                   │
       │  Bind session to sessionHolder                  │                   │
```

### Data Flow: Tool Expansion (Load Mode)

```
Agent              expand_tools(names)     IToolCatalog         IGatewayClient
  │                       │                     │                     │
  │  expand_tools(        │                     │                     │
  │    names: ["teams"])  │                     │                     │
  │──────────────────────►│                     │                     │
  │                       │ GetToolNamesByProv  │                     │
  │                       │────────────────────►│                     │
  │                       │  [tool names]       │                     │
  │                       │◄────────────────────│                     │
  │                       │                     │                     │
  │                       │ GetToolsByName      │                     │
  │                       │────────────────────►│                     │
  │                       │  [AIFunctions]      │                     │
  │                       │◄────────────────────│                     │
  │                       │                     │                     │
  │                       │ Append to tool list │                     │
  │                       │                     │                     │
  │                       │ ResumeSessionAsync(sessionId,            │
  │                       │   ResumeSessionConfig {                   │
  │                       │     Tools = current + new                │
  │                       │   })                                      │
  │                       │──────────────────────────────────────────►│
  │                       │                     │                     │
  │  { enabled: ["teams_post_msg", ...], count: 12 }                 │
  │◄──────────────────────│                     │                     │
```

## File Structure

```
src/MsClaw.Gateway/
├── Services/
│   ├── Tools/
│   │   ├── IToolProvider.cs          # NEW: Provider abstraction
│   │   ├── IToolCatalog.cs           # NEW: Read-side catalog interface
│   │   ├── IToolRegistrar.cs         # NEW: Write-side registry interface
│   │   ├── IToolExpander.cs          # NEW: Session-aware expander interface
│   │   ├── ToolCatalogStore.cs       # NEW: Internal shared data structure
│   │   ├── ToolBridge.cs             # NEW: IToolCatalog implementation (read)
│   │   ├── ToolRegistrar.cs          # NEW: IToolRegistrar implementation (write)
│   │   ├── ToolExpander.cs           # NEW: Per-session expand_tools factory
│   │   ├── ToolDescriptor.cs         # NEW: Immutable record + ToolSourceTier + ToolStatus
│   │   └── ToolBridgeHostedService.cs # NEW: Provider registration + watch loops at startup
│   └── AgentMessageService.cs        # MODIFY: Populate SessionConfig.Tools
├── Extensions/
│   └── GatewayServiceExtensions.cs   # MODIFY: Register tool bridge services

src/MsClaw.Gateway.Tests/
├── Services/
│   └── Tools/
│       ├── ToolCatalogStoreTests.cs  # NEW: Shared store tests
│       ├── ToolBridgeTests.cs        # NEW: Catalog read-side tests
│       ├── ToolRegistrarTests.cs     # NEW: Registration, collision, priority, refresh, teardown
│       ├── ToolExpanderTests.cs      # NEW: Load mode, query mode, provider resolution
│       └── ToolBridgeHostedServiceTests.cs # NEW: Watch loop, startup registration
```

## Critical: expand_tools Session Reference

**Problem**: `expand_tools` needs a reference to the `CopilotSession` it modifies, but the session doesn't exist until after `CreateSessionAsync` — which requires `Tools` (including `expand_tools`) in its config.

**Solution**: Use deferred session binding via `TaskCompletionSource<IGatewaySession>`. `CreateExpandToolsFunction` accepts a `SessionHolder` and a mutable `List<AIFunction>` for the tool list. The expand function `await`s the session from the holder. After `CreateSessionAsync`, the session is bound via `SessionHolder.Bind()`. This eliminates race conditions — if `expand_tools` is invoked before binding, it awaits rather than reading null.

```csharp
// SessionHolder — thread-safe deferred binding
public sealed class SessionHolder
{
    private readonly TaskCompletionSource<IGatewaySession> _tcs = new();
    public void Bind(IGatewaySession session) => _tcs.SetResult(session);
    public Task<IGatewaySession> GetSessionAsync() => _tcs.Task;
}

// In AgentMessageService.GetOrCreateSessionAsync:
var sessionHolder = new SessionHolder();
var tools = new List<AIFunction>(catalog.GetDefaultTools());
var expandFn = expander.CreateExpandToolsFunction(sessionHolder, tools);
tools.Add(expandFn);

var session = await client.CreateSessionAsync(new SessionConfig
{
    Streaming = true,
    SystemMessage = ...,
    Tools = tools
}, ct);

sessionHolder.Bind(session);  // Bind after creation — awaiting callers unblock
```

## Implementation Phases

| Phase | Description | Tasks |
|-------|-------------|-------|
| Phase 1 | Core abstractions — interfaces, value types, enums | T001–T008 |
| Phase 2 | ToolBridge — catalog + registrar singleton | T009–T024 |
| Phase 3 | ToolExpander — per-session expand_tools | T025–T036 |
| Phase 4 | Integration — session creation, DI, hosted service | T037–T048 |

Details in `tasks.md`.

## Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Separate catalog/registrar classes over single ToolBridge | `ToolBridge` (read, `IToolCatalog`) + `ToolRegistrar` (write, `IToolRegistrar`) sharing a `ToolCatalogStore` | SRP — catalog lookups and provider registration change for different reasons. Shared store provides data cohesion without class coupling. When approval gates arrive, `ToolRegistrar` changes; `ToolBridge` doesn't. |
| Watch loops in hosted service, not registrar | `ToolBridgeHostedService` owns `WaitForSurfaceChangeAsync` loops | SRP — registrar executes mutations, hosted service drives them. Unit testing the registrar doesn't require async background loops. |
| Same-tier collision = hard error | `InvalidOperationException` | Stricter than spec (which says log and skip). Makes conflicts visible at startup rather than silently depending on DI order. Intentional deviation documented in research.md. |
| No `AvailableTools`/`ExcludedTools` | Lazy registration via `expand_tools` | Setting `AvailableTools` creates a whitelist that hides CLI built-in tools. Fragile and breaks when CLI adds new built-ins. |
| `expand_tools` as single AIFunction with two modes | Load + query in one tool | Supersedes the MCPorter plan's two-tool design. Same capability, one tool surface, simpler for the agent. |
| Deferred session binding via `TaskCompletionSource` | `SessionHolder` wraps `TaskCompletionSource<IGatewaySession>` | Thread-safe: expand_tools `await`s session instead of reading a nullable field. No race conditions. If session never binds, callers await forever (detectable) rather than NPE (silent). |
| Status tracked separately from descriptor | `ConcurrentDictionary<string, ToolStatus>` | Descriptors are immutable value objects. Status is operational and changes independently. |

## Files to Modify

| File | Change |
|------|--------|
| `src/MsClaw.Gateway/Services/AgentMessageService.cs` | Inject `IToolCatalog` + `IToolExpander`; populate `SessionConfig.Tools` in `GetOrCreateSessionAsync` |
| `src/MsClaw.Gateway/Extensions/GatewayServiceExtensions.cs` | Register `ToolCatalogStore` (singleton), `ToolBridge` as `IToolCatalog` (singleton), `ToolRegistrar` as `IToolRegistrar` (singleton), `ToolExpander`, `ToolBridgeHostedService` |

## New Files

| File | Purpose |
|------|---------|
| `Services/Tools/IToolProvider.cs` | Provider interface with discover + change signal |
| `Services/Tools/IToolCatalog.cs` | Read-side catalog interface |
| `Services/Tools/IToolRegistrar.cs` | Write-side registrar interface |
| `Services/Tools/IToolExpander.cs` | Session-aware expander interface + `SessionHolder` |
| `Services/Tools/ToolDescriptor.cs` | Immutable record + `ToolSourceTier` + `ToolStatus` enums |
| `Services/Tools/ToolCatalogStore.cs` | Internal shared data structure — `ConcurrentDictionary` + status map + provider index |
| `Services/Tools/ToolBridge.cs` | `IToolCatalog` implementation — read-side catalog over `ToolCatalogStore` |
| `Services/Tools/ToolRegistrar.cs` | `IToolRegistrar` implementation — write-side registry over `ToolCatalogStore` |
| `Services/Tools/ToolExpander.cs` | Per-session `expand_tools` factory |
| `Services/Tools/ToolBridgeHostedService.cs` | Registers providers at startup, owns per-provider watch loops |
| Tests: `ToolCatalogStoreTests.cs` | Shared store unit tests |
| Tests: `ToolBridgeTests.cs` | Catalog read-side unit tests |
| Tests: `ToolRegistrarTests.cs` | Registration, collision, priority, refresh, teardown tests |
| Tests: `ToolExpanderTests.cs` | ToolExpander unit tests |
| Tests: `ToolBridgeHostedServiceTests.cs` | Watch loop and startup registration tests |

## Verification

1. `dotnet build src/MsClaw.slnx --nologo` — clean build
2. `dotnet test src/MsClaw.Gateway.Tests/MsClaw.Gateway.Tests.csproj --nologo` — all tests pass
3. Spec tests via `specs/tests/003-tool-bridge.md` — all spec tests pass

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| `ResumeSessionAsync` doesn't support Tools | SDK source analysis confirms it does; integration test validates |
| Background loops leak on shutdown | Per-provider `CancellationTokenSource`, cancelled in `UnregisterProviderAsync` and hosted service `StopAsync` |
| Expand fails mid-session | Returns error result to agent (not exception); session continues with current tools |
| Provider `DiscoverAsync` throws | Catch, log error, skip provider, continue registering others |

## Limitations (MVP)

1. No operator admin endpoints (list/detail/re-discover) — catalog exposes the data, endpoints come later
2. No approval gates — interface reserves the hook
3. No managed tier providers — enum reserved
4. No skill change notifications — catalog mutations are silent
5. No timeout enforcement wrapper — providers handle their own timeouts

## References

- `specs/gateway-skills.md` — Full skill system specification
- `specs/gateway-agent-runtime.md` — Runtime tool requirements
- `backlog/plans/20260308-tool-bridge.md` — Quick plan with detailed design
- GitHub Copilot SDK — `SessionConfig.Tools`, `ResumeSessionConfig`, `AIFunction`
