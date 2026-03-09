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
| Agent developer registers provider | FR-1, FR-3 | IToolProvider defines provider contract, IToolRegistrar exposes write-only operations |
| Agent discovers/loads tools | FR-2, FR-4 | IToolCatalog exposes read-only operations, IToolExpander creates per-session expand_tools |
| Sessions start with default tools | FR-5 | AgentMessageService populates SessionConfig.Tools |
| Tools refresh without restart | FR-3.6, FR-6 | ToolBridge uses ConcurrentDictionary, enforces collision rules |

## Dependencies

```
Phase 1 (Abstractions) ──► Phase 2 (ToolBridge) ──► Phase 3 (ToolExpander)
                                                          │
                                                          ▼
                                                    Phase 4 (Integration)
```

## Phase 1: Core Abstractions

Define interfaces, value types, and enums. No behavior — just contracts.

### Value Types
- [ ] T001 [TEST] Write test that `ToolDescriptor` is a sealed record with required `Function`, `ProviderName`, `Tier` properties and optional `AlwaysVisible` defaulting to false
- [ ] T002 [IMPL] Implement `ToolDescriptor` record, `ToolSourceTier` enum, `ToolStatus` enum in `Services/Tools/ToolDescriptor.cs`

### Provider Interface
- [ ] T003 [TEST] Write test that `IToolProvider` extends `IAsyncDisposable` and declares `Name`, `Tier`, `DiscoverAsync`, `WaitForSurfaceChangeAsync`
- [ ] T004 [IMPL] Define `IToolProvider` interface in `Services/Tools/IToolProvider.cs`

### Catalog Interface
- [ ] T005 [TEST] Write test that `IToolCatalog` declares `GetDefaultTools`, `GetToolsByName`, `GetCatalogToolNames`, `GetToolNamesByProvider`, `SearchTools`, `GetDescriptor`
- [ ] T006 [IMPL] Define `IToolCatalog` interface in `Services/Tools/IToolCatalog.cs`

### Registrar Interface
- [ ] T007 [IMPL] Define `IToolRegistrar` interface in `Services/Tools/IToolRegistrar.cs`

### Expander Interface
- [ ] T008 [IMPL] Define `IToolExpander` interface and `SessionHolder` class in `Services/Tools/IToolExpander.cs`

## Phase 2: ToolBridge Implementation

Singleton implementing `IToolCatalog` + `IToolRegistrar`. Uses `ConcurrentDictionary` for thread-safe catalog storage.

### Registration — Happy Path
- [ ] T009 [TEST] Write test: register a mock provider with 2 tools → `GetCatalogToolNames()` returns both tool names
- [ ] T010 [IMPL] Implement `ToolBridge` constructor and `RegisterProviderAsync` — call `DiscoverAsync`, index descriptors by `Function.Name`
- [ ] T011 [TEST] Write test: register provider → `GetDefaultTools()` returns only AlwaysVisible tools
- [ ] T012 [IMPL] Implement `GetDefaultTools()` — filter by `AlwaysVisible == true` and status `Ready`

### Catalog Lookups
- [ ] T013 [TEST] Write test: `GetToolsByName(["tool_a", "tool_b"])` returns matching AIFunctions; unknown names silently skipped
- [ ] T014 [IMPL] Implement `GetToolsByName` — dictionary lookup, filter by Ready status
- [ ] T015 [TEST] Write test: `GetToolNamesByProvider("providerA")` returns that provider's tools; unknown provider returns empty
- [ ] T016 [IMPL] Implement `GetToolNamesByProvider` — filter catalog by ProviderName
- [ ] T017 [TEST] Write test: `SearchTools("post message")` matches tool names and descriptions containing keywords
- [ ] T018 [IMPL] Implement `SearchTools` — case-insensitive keyword match against `Function.Name` and `Function.Description`
- [ ] T019 [TEST] Write test: `GetDescriptor("tool_a")` returns descriptor; `GetDescriptor("nonexistent")` returns null
- [ ] T020 [IMPL] Implement `GetDescriptor` — direct dictionary lookup

### Collision Resolution
- [ ] T021 [TEST] Write test: two providers at same tier with same tool name → `RegisterProviderAsync` throws `InvalidOperationException` with message identifying both providers
- [ ] T022 [TEST] Write test: Bundled provider and Workspace provider with same tool name → Bundled wins, Workspace tool not cataloged
- [ ] T023 [IMPL] Implement collision detection — same-tier throws, cross-tier keeps higher priority

### Unregistration & Teardown
- [ ] T024 [TEST] Write test: `UnregisterProviderAsync("providerA")` → provider's tools removed from catalog; `GetCatalogToolNames()` no longer includes them
- [ ] T025 [IMPL] Implement `UnregisterProviderAsync` — remove tools, cancel watch loop, call `DisposeAsync` on provider

### Refresh
- [ ] T026 [TEST] Write test: `RefreshProviderAsync` re-calls `DiscoverAsync` and updates catalog with new tool set (added/removed tools)
- [ ] T027 [IMPL] Implement `RefreshProviderAsync` — remove old tools, re-discover, re-index

### Background Watch Loop
- [ ] T028 [TEST] Write test: after registration, bridge awaits `WaitForSurfaceChangeAsync` and calls `RefreshProviderAsync` when it returns
- [ ] T029 [IMPL] Implement background loop per provider with dedicated `CancellationTokenSource`

### Phase 2 Spec Check
- [ ] T030 [SPEC] Run spec tests — verify ToolBridge-related specs pass

## Phase 3: ToolExpander Implementation

Creates per-session `expand_tools` AIFunction with load/query modes.

### expand_tools Creation
- [ ] T031 [TEST] Write test: `CreateExpandToolsFunction` returns an `AIFunction` with name "expand_tools"
- [ ] T032 [IMPL] Implement `ToolExpander.CreateExpandToolsFunction` — use `AIFunctionFactory.Create` with captured closure

### Load Mode
- [ ] T033 [TEST] Write test: expand_tools invoked with `names: ["tool_a"]` → fetches from catalog, appends to tool list, calls `ResumeSessionAsync` with updated tools
- [ ] T034 [IMPL] Implement load mode — resolve provider names via `GetToolNamesByProvider`, fetch via `GetToolsByName`, mutate tool list, call `ResumeSessionAsync`
- [ ] T035 [TEST] Write test: expand_tools with `names: ["providerA"]` → resolves to all tools from that provider
- [ ] T036 [IMPL] Implement provider-name resolution — check if name matches a provider, expand to tool list

### Query Mode
- [ ] T037 [TEST] Write test: expand_tools invoked with `query: "send message"` → calls `SearchTools`, returns matching names without modifying session
- [ ] T038 [IMPL] Implement query mode — delegate to `IToolCatalog.SearchTools`, return result object

### Edge Cases
- [ ] T039 [TEST] Write test: expand_tools invoked before session is bound → returns error result, does not throw
- [ ] T040 [TEST] Write test: expand_tools with unknown tool name → silently skips, returns result noting which names were not found
- [ ] T041 [IMPL] Implement edge case handling — null session check, unknown name reporting

### Phase 3 Spec Check
- [ ] T042 [SPEC] Run spec tests — verify ToolExpander-related specs pass

## Phase 4: Integration

Wire ToolBridge and ToolExpander into gateway DI, session creation, and startup.

### DI Registration
- [ ] T043 [TEST] Write test: resolve `IToolCatalog` and `IToolRegistrar` from service provider → both resolve to same `ToolBridge` instance
- [ ] T044 [TEST] Write test: resolve `IToolExpander` from service provider → resolves to `ToolExpander`
- [ ] T045 [IMPL] Register services in `GatewayServiceExtensions.AddGatewayServices` — `ToolBridge` singleton for both interfaces, `ToolExpander` singleton

### Session Creation
- [ ] T046 [TEST] Write test: `AgentMessageService.GetOrCreateSessionAsync` creates session with `SessionConfig.Tools` containing catalog default tools + expand_tools
- [ ] T047 [TEST] Write test: session config does NOT set `AvailableTools` or `ExcludedTools`
- [ ] T048 [IMPL] Modify `AgentMessageService.GetOrCreateSessionAsync` — inject `IToolCatalog` + `IToolExpander`, build tool list, populate `SessionConfig.Tools`

### Hosted Service for Provider Registration
- [ ] T049 [TEST] Write test: `ToolBridgeHostedService.StartAsync` registers all `IToolProvider` instances via `IToolRegistrar`
- [ ] T050 [TEST] Write test: `ToolBridgeHostedService.StopAsync` unregisters all providers
- [ ] T051 [IMPL] Implement `ToolBridgeHostedService` — iterate `IEnumerable<IToolProvider>`, register each, handle individual failures

### Phase 4 Spec Check
- [ ] T052 [SPEC] Run spec tests — verify all session integration and DI specs pass

## Task Summary

| Phase | Tasks | [TEST] | [IMPL] | [SPEC] |
|-------|-------|--------|--------|--------|
| Phase 1: Core Abstractions | T001–T008 | 3 | 5 | 0 |
| Phase 2: ToolBridge | T009–T030 | 11 | 10 | 1 |
| Phase 3: ToolExpander | T031–T042 | 7 | 4 | 1 |
| Phase 4: Integration | T043–T052 | 5 | 3 | 1 |
| **Total** | **52** | **26** | **22** | **3** |

## Final Validation

After all implementation phases are complete:

- [ ] `dotnet build src/MsClaw.slnx --nologo` passes
- [ ] `dotnet test src/MsClaw.Gateway.Tests/MsClaw.Gateway.Tests.csproj --nologo` passes
- [ ] Run spec tests with `/spec-tests` skill using `specs/tests/003-tool-bridge.md`
- [ ] All spec tests pass → feature complete
