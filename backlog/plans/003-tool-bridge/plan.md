# Plan: Tool Bridge — Provider Abstraction & Registry

## Summary

Build a tool bridge that decouples tool lifecycle management from individual tool sources. The bridge is a singleton (`ToolBridge`) implementing both `IToolCatalog` (read) and `IToolRegistrar` (write), backed by a `ConcurrentDictionary`-based catalog. A separate `ToolExpander` creates per-session `expand_tools` AIFunction instances for lazy tool loading. Sessions start with only default tools + `expand_tools`; additional tools are added via `ResumeSessionAsync`. No `AvailableTools`/`ExcludedTools` are used.

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
           │ (same singleton)
┌──────────┴───────────┐
│ IToolRegistrar (write)│
│  RegisterProvider()   │
│  UnregisterProvider() │
│  RefreshProvider()    │──── WaitForSurfaceChangeAsync loop
└──────────┬───────────┘
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
| `ToolBridge` | Aggregates providers, validates descriptors, enforces tier priority, tracks status, provides catalog lookups | `IToolProvider` implementations, `AgentMessageService`, `ToolExpander` |
| `ToolExpander` | Creates per-session `expand_tools` AIFunction with load/query modes | `IToolCatalog`, `IGatewayClient` (for `ResumeSessionAsync`) |
| `IToolProvider` | Discovers tools, provides AIFunction handlers, signals surface changes | `ToolBridge` (via `IToolRegistrar`) |
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
│   │   ├── ToolBridge.cs             # NEW: Singleton implementation (catalog + registrar)
│   │   ├── ToolExpander.cs           # NEW: Per-session expand_tools factory
│   │   ├── ToolDescriptor.cs         # NEW: Immutable record + ToolSourceTier + ToolStatus
│   │   └── ToolBridgeHostedService.cs # NEW: Provider registration at startup
│   └── AgentMessageService.cs        # MODIFY: Populate SessionConfig.Tools
├── Extensions/
│   └── GatewayServiceExtensions.cs   # MODIFY: Register tool bridge services

src/MsClaw.Gateway.Tests/
├── Services/
│   └── Tools/
│       ├── ToolBridgeTests.cs        # NEW: Discovery, collision, priority, refresh, teardown
│       └── ToolExpanderTests.cs      # NEW: Load mode, query mode, provider resolution
```

## Critical: expand_tools Session Reference

**Problem**: `expand_tools` needs a reference to the `CopilotSession` it modifies, but the session doesn't exist until after `CreateSessionAsync` — which requires `Tools` (including `expand_tools`) in its config.

**Solution**: Use deferred session binding. `CreateExpandToolsFunction` accepts a session holder (wrapper that receives the session after creation) and a mutable `List<AIFunction>` for the tool list. The expand function captures both in its closure. After `CreateSessionAsync`, the session reference is set on the holder. When `expand_tools` is invoked by the agent, it reads the session from the holder, appends new tools to the list, and calls `ResumeSessionAsync`.

```csharp
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

sessionHolder.Session = session;  // Bind after creation
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
| Single ToolBridge vs separate catalog/registrar | Single class, two interfaces | Simpler — catalog and registrar share the same `ConcurrentDictionary`. Two interfaces enforce read/write separation at the consumer level. |
| Same-tier collision = hard error | `InvalidOperationException` | Stricter than spec (which says log and skip). Makes conflicts visible at startup rather than silently depending on DI order. Intentional deviation documented in research.md. |
| No `AvailableTools`/`ExcludedTools` | Lazy registration via `expand_tools` | Setting `AvailableTools` creates a whitelist that hides CLI built-in tools. Fragile and breaks when CLI adds new built-ins. |
| `expand_tools` as single AIFunction with two modes | Load + query in one tool | Supersedes the MCPorter plan's two-tool design. Same capability, one tool surface, simpler for the agent. |
| `WaitForSurfaceChangeAsync` (async pull) over events | Registrar controls processing | Serializes catalog mutations. No race conditions from concurrent event callbacks. |
| Deferred session binding | `SessionHolder` wrapper | Solves circular dependency: expand_tools needs session, session needs expand_tools. |
| Status tracked separately from descriptor | `ConcurrentDictionary<string, ToolStatus>` | Descriptors are immutable value objects. Status is operational and changes independently. |

## Files to Modify

| File | Change |
|------|--------|
| `src/MsClaw.Gateway/Services/AgentMessageService.cs` | Inject `IToolCatalog` + `IToolExpander`; populate `SessionConfig.Tools` in `GetOrCreateSessionAsync` |
| `src/MsClaw.Gateway/Extensions/GatewayServiceExtensions.cs` | Register `ToolBridge` (singleton for both interfaces), `ToolExpander`, `ToolBridgeHostedService` |

## New Files

| File | Purpose |
|------|---------|
| `Services/Tools/IToolProvider.cs` | Provider interface with discover + change signal |
| `Services/Tools/IToolCatalog.cs` | Read-side catalog interface |
| `Services/Tools/IToolRegistrar.cs` | Write-side registrar interface |
| `Services/Tools/IToolExpander.cs` | Session-aware expander interface |
| `Services/Tools/ToolDescriptor.cs` | Immutable record + `ToolSourceTier` + `ToolStatus` enums |
| `Services/Tools/ToolBridge.cs` | Singleton catalog + registrar implementation |
| `Services/Tools/ToolExpander.cs` | Per-session `expand_tools` factory |
| `Services/Tools/ToolBridgeHostedService.cs` | Registers providers at startup, runs change loops |
| Tests: `ToolBridgeTests.cs` | ToolBridge unit tests |
| Tests: `ToolExpanderTests.cs` | ToolExpander unit tests |

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
