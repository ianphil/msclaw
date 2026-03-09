# Tool Bridge Tasks (TDD)

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
| Agent developer registers provider | FR-1, FR-3 | IToolProvider defines provider contract, IToolRegistrar (ToolRegistrar) exposes write-only operations |
| Agent discovers/loads tools | FR-2, FR-4 | IToolCatalog (ToolBridge) exposes read-only operations, IToolExpander creates per-session expand_tools |
| Sessions start with default tools | FR-5 | AgentMessageService populates SessionConfig.Tools |
| Tools refresh without restart | FR-3.6, FR-6 | ToolBridgeHostedService owns watch loops, ToolRegistrar executes refresh, ToolCatalogStore enforces collision rules |

## Dependencies

```
Phase 1 (Abstractions) ──► Phase 2 (Store + Bridge + Registrar) ──► Phase 3 (ToolExpander)
                                                                          │
                                                                          ▼
                                                                    Phase 4 (Integration + Watch Loops)
```

## Phase 1: Core Abstractions

Define interfaces, value types, and enums. No behavior — just contracts.

### Value Types
- [x] T001 [TEST] Write test that `ToolDescriptor` is a sealed record with required `Function`, `ProviderName`, `Tier` properties and optional `AlwaysVisible` defaulting to false
- [x] T002 [IMPL] Implement `ToolDescriptor` record, `ToolSourceTier` enum, `ToolStatus` enum in `Services/Tools/ToolDescriptor.cs`

### Provider Interface
- [x] T003 [IMPL] Define `IToolProvider` interface in `Services/Tools/IToolProvider.cs`

### Catalog Interface
- [x] T004 [IMPL] Define `IToolCatalog` interface in `Services/Tools/IToolCatalog.cs`

### Registrar Interface
- [x] T005 [IMPL] Define `IToolRegistrar` interface in `Services/Tools/IToolRegistrar.cs`

### Expander Interface
- [x] T006 [IMPL] Define `IToolExpander` interface and `SessionHolder` class (with `TaskCompletionSource<IGatewaySession>`) in `Services/Tools/IToolExpander.cs`

## Phase 2: ToolCatalogStore, ToolBridge (Read), and ToolRegistrar (Write)

Shared `ToolCatalogStore` holds the `ConcurrentDictionary` + status map + provider index. `ToolBridge` implements `IToolCatalog` (read-side) over the store. `ToolRegistrar` implements `IToolRegistrar` (write-side) over the store. Watch loops are NOT here — they belong to `ToolBridgeHostedService` (Phase 4).

### ToolCatalogStore — Internal Shared Data
- [x] T007 [TEST] Write test: add a descriptor to store → retrieve by name returns it
- [x] T008 [IMPL] Implement `ToolCatalogStore` — internal class with `ConcurrentDictionary<string, ToolDescriptor>`, `ConcurrentDictionary<string, ToolStatus>`, methods: `Add`, `Remove`, `TryGet`, `GetAll`, `GetByProvider`, `GetStatus`, `SetStatus`

### Registration via ToolRegistrar
- [x] T009 [TEST] Write test: register a mock provider with 2 tools → store contains both tool names
- [x] T010 [IMPL] Implement `ToolRegistrar.RegisterProviderAsync` — call `DiscoverAsync`, validate, index descriptors into `ToolCatalogStore`
- [x] T011 [TEST] Write test: `UnregisterProviderAsync("providerA")` → provider's tools removed from store; call `DisposeAsync` on provider
- [x] T012 [IMPL] Implement `UnregisterProviderAsync` — remove tools from store, call `DisposeAsync` on provider
- [x] T013 [TEST] Write test: `RefreshProviderAsync` re-calls `DiscoverAsync` and updates store with new tool set (added/removed tools)
- [x] T014 [IMPL] Implement `RefreshProviderAsync` — remove old tools, re-discover, re-index

### Collision Resolution (in ToolRegistrar)
- [x] T015 [TEST] Write test: two providers at same tier with same tool name → `RegisterProviderAsync` throws `InvalidOperationException` with message identifying both providers
- [x] T016 [TEST] Write test: Bundled provider and Workspace provider with same tool name → Bundled wins, Workspace tool not cataloged
- [x] T017 [IMPL] Implement collision detection in `RegisterProviderAsync` — same-tier throws, cross-tier keeps higher priority

### Catalog Lookups via ToolBridge
- [x] T018 [TEST] Write test: register provider → `GetDefaultTools()` returns only AlwaysVisible tools with status Ready
- [x] T019 [IMPL] Implement `ToolBridge.GetDefaultTools()` — read from `ToolCatalogStore`, filter by `AlwaysVisible == true` and status `Ready`
- [x] T020 [TEST] Write test: `GetToolsByName(["tool_a", "tool_b"])` returns matching AIFunctions; unknown names silently skipped
- [x] T021 [IMPL] Implement `GetToolsByName` — store lookup, filter by Ready status
- [x] T022 [TEST] Write test: `GetToolNamesByProvider("providerA")` returns that provider's tools; unknown provider returns empty
- [x] T023 [IMPL] Implement `GetToolNamesByProvider` — delegate to store
- [x] T024 [TEST] Write test: `SearchTools("post message")` matches tool names and descriptions containing keywords (substring, case-insensitive)
- [x] T025 [IMPL] Implement `SearchTools` — intentionally simple substring match against `Function.Name` and `Function.Description`. No ranking, no fuzzy matching.
- [x] T026 [TEST] Write test: `GetDescriptor("tool_a")` returns descriptor; `GetDescriptor("nonexistent")` returns null
- [x] T027 [IMPL] Implement `GetDescriptor` — direct store lookup

## Phase 3: ToolExpander Implementation

Creates per-session `expand_tools` AIFunction with load/query modes.

### expand_tools Creation
- [ ] T029 [TEST] Write test: `CreateExpandToolsFunction` returns an `AIFunction` with name "expand_tools"
- [ ] T030 [IMPL] Implement `ToolExpander.CreateExpandToolsFunction` — use `AIFunctionFactory.Create` with captured closure

### Load Mode
- [ ] T031 [TEST] Write test: expand_tools invoked with `names: ["tool_a"]` → fetches from catalog, appends to tool list, calls `ResumeSessionAsync` with updated tools
- [ ] T032 [IMPL] Implement load mode — resolve provider names via `GetToolNamesByProvider`, fetch via `GetToolsByName`, mutate tool list, call `ResumeSessionAsync`
- [ ] T033 [TEST] Write test: expand_tools with `names: ["providerA"]` → resolves to all tools from that provider
- [ ] T034 [IMPL] Implement provider-name resolution — check if name matches a provider, expand to tool list

### Query Mode
- [ ] T035 [TEST] Write test: expand_tools invoked with `query: "send message"` → calls `SearchTools`, returns matching names without modifying session
- [ ] T036 [IMPL] Implement query mode — delegate to `IToolCatalog.SearchTools`, return result object

### Edge Cases
- [ ] T037 [TEST] Write test: expand_tools invoked before session is bound → `GetSessionAsync` with timeout returns error result, does not throw
- [ ] T038 [TEST] Write test: expand_tools with unknown tool name → silently skips, returns result noting which names were not found
- [ ] T039 [IMPL] Implement edge case handling — `await GetSessionAsync()` with cancellation timeout, unknown name reporting

## Phase 4: Integration

Wire ToolCatalogStore, ToolBridge, ToolRegistrar, and ToolExpander into gateway DI, session creation, and startup. Hosted service owns provider registration AND watch loops.

### DI Registration
- [ ] T041 [TEST] Write test: resolve `IToolCatalog` from service provider → resolves to `ToolBridge`; resolve `IToolRegistrar` → resolves to `ToolRegistrar`; both share the same `ToolCatalogStore`
- [ ] T042 [TEST] Write test: resolve `IToolExpander` from service provider → resolves to `ToolExpander`
- [ ] T043 [IMPL] Register services in `GatewayServiceExtensions.AddGatewayServices` — `ToolCatalogStore` singleton, `ToolBridge` as `IToolCatalog` singleton, `ToolRegistrar` as `IToolRegistrar` singleton, `ToolExpander` singleton

### Session Creation
- [ ] T044 [TEST] Write test: `AgentMessageService.GetOrCreateSessionAsync` creates session with `SessionConfig.Tools` containing catalog default tools + expand_tools
- [ ] T045 [TEST] Write test: session config does NOT set `AvailableTools` or `ExcludedTools`
- [ ] T046 [IMPL] Modify `AgentMessageService.GetOrCreateSessionAsync` — inject `IToolCatalog` + `IToolExpander`, build tool list, populate `SessionConfig.Tools`, bind session to `SessionHolder`

### Hosted Service — Provider Registration + Watch Loops
- [ ] T047 [TEST] Write test: `ToolBridgeHostedService.StartAsync` registers all `IToolProvider` instances via `IToolRegistrar`
- [ ] T048 [TEST] Write test: after registration, hosted service starts per-provider `WaitForSurfaceChangeAsync` loop and calls `RefreshProviderAsync` when signal fires
- [ ] T049 [TEST] Write test: `ToolBridgeHostedService.StopAsync` cancels watch loops and unregisters all providers
- [ ] T050 [IMPL] Implement `ToolBridgeHostedService` — iterate `IEnumerable<IToolProvider>`, register each, start watch loops with per-provider `CancellationTokenSource`, handle individual failures

## Task Summary

| Phase | Tasks | [TEST] | [IMPL] |
|-------|-------|--------|--------|
| Phase 1: Core Abstractions | T001–T006 | 1 | 5 |
| Phase 2: Store + Bridge + Registrar | T007–T027 | 11 | 11 |
| Phase 3: ToolExpander | T029–T039 | 7 | 4 |
| Phase 4: Integration + Watch Loops | T041–T050 | 6 | 3 |
| **Total** | **48** | **25** | **23** |

## Final Validation

After all implementation phases are complete:

- [ ] `dotnet build src/MsClaw.slnx --nologo` passes
- [ ] `dotnet test src/MsClaw.Gateway.Tests/MsClaw.Gateway.Tests.csproj --nologo` passes
