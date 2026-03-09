# Specification: Tool Bridge — Provider Abstraction & Registry

## Overview

### Problem Statement

The gateway creates Copilot SDK sessions with no custom tool surface (`SessionConfig.Tools = ∅`). The agent can only use the CLI's built-in tools. There is no mechanism to register, discover, or lazily load tools from external providers (MCPorter, cron, bundled mind tools, MCP servers). Each future provider plan would need to reinvent bridge infrastructure — discovery, registry, lazy meta-tools, refresh — alongside provider-specific concerns.

### Solution Summary

Extract a general-purpose tool bridge that lets any `IToolProvider` register tools on Copilot SDK sessions. The bridge is split into three focused interfaces: `IToolCatalog` (read), `IToolRegistrar` (write), and `IToolExpander` (session-aware lazy registration). Sessions start with only default (AlwaysVisible) tools plus an `expand_tools` meta-tool. Additional tools are loaded lazily via `expand_tools` using `ResumeSessionAsync`. CLI built-in tools remain unaffected because no `AvailableTools`/`ExcludedTools` whitelist is used.

### Business Value

| Benefit | Impact |
|---------|--------|
| Provider independence | New integrations (MCPorter, cron, Slack, filesystem) require zero bridge changes — implement `IToolProvider`, register, done |
| Minimal session payloads | Sessions start with ~24 tools (~50 KB); additional tools added only when needed |
| CLI built-in preservation | No whitelist gating means CLI file editing, terminal, and search tools always work |
| Hot refresh | Providers signal surface changes; tools update without gateway restart |
| Testability | Every component has an interface; mock providers enable full unit test coverage |

## User Stories

### Agent Developer

**As an agent developer**, I want to register a tool provider at startup and have its tools available to the agent, so that I can extend the agent's capabilities without modifying the bridge.

**Acceptance Criteria:**
- Implementing `IToolProvider` with `DiscoverAsync` and registering via DI is sufficient
- Tools appear in the catalog after `RegisterProviderAsync` completes
- AlwaysVisible tools are included on every new session automatically

### Agent (LLM)

**As the agent**, I want to discover and load additional tools during a conversation, so that I can access capabilities beyond the default tool set without restarting the session.

**Acceptance Criteria:**
- `expand_tools` with `names` parameter loads specified tools onto the session
- `expand_tools` with `query` parameter returns matching tool names without loading
- After expansion, newly added tools are callable in the same session
- Expansion failure returns a descriptive error, not an exception

### Gateway Operator

**As a gateway operator**, I want tool providers to register at startup and refresh automatically, so that I don't need to restart the gateway when a provider's tool surface changes.

**Acceptance Criteria:**
- Providers register during hosted service startup
- `WaitForSurfaceChangeAsync` triggers re-discovery when provider signals change
- Registration and refresh are cancellation-aware

## Functional Requirements

### FR-1: Provider Abstraction

| Requirement | Description |
|-------------|-------------|
| FR-1.1 | `IToolProvider` defines `Name`, `Tier`, `DiscoverAsync`, `WaitForSurfaceChangeAsync`, and extends `IAsyncDisposable` |
| FR-1.2 | `DiscoverAsync` returns `IReadOnlyList<ToolDescriptor>` with ready-to-use `AIFunction` instances |
| FR-1.3 | `WaitForSurfaceChangeAsync` is an awaitable that returns when the provider's surface may have changed |
| FR-1.4 | Providers that never change return `Task.Delay(Timeout.Infinite, ct)` |

### FR-2: Tool Catalog (Read-Side)

| Requirement | Description |
|-------------|-------------|
| FR-2.1 | `GetDefaultTools()` returns `AIFunction` instances for all AlwaysVisible tools with status Ready |
| FR-2.2 | `GetToolsByName(names)` returns `AIFunction` instances for specified tool names; unknown names silently skipped |
| FR-2.3 | `GetCatalogToolNames()` returns all known tool names grouped by provider |
| FR-2.4 | `GetToolNamesByProvider(providerName)` returns all tool names for a specific provider |
| FR-2.5 | `SearchTools(query)` keyword-matches against tool names and descriptions, returning matching tool names |
| FR-2.6 | `GetDescriptor(toolName)` returns the full `ToolDescriptor` for a tool, or null if not found |

### FR-3: Tool Registrar (Write-Side)

| Requirement | Description |
|-------------|-------------|
| FR-3.1 | `RegisterProviderAsync` calls `DiscoverAsync` on the provider and populates the catalog |
| FR-3.2 | Same-tier name collisions throw `InvalidOperationException` identifying both providers |
| FR-3.3 | Cross-tier collisions resolve by keeping the higher-tier tool (Bundled > Workspace > Managed) |
| FR-3.4 | `UnregisterProviderAsync` removes provider's tools from catalog; active sessions retain handlers |
| FR-3.5 | `RefreshProviderAsync` re-discovers tools from a specific provider |
| FR-3.6 | The registrar starts a `WaitForSurfaceChangeAsync` background loop per provider |

### FR-4: Tool Expander (Session-Aware)

| Requirement | Description |
|-------------|-------------|
| FR-4.1 | `CreateExpandToolsFunction` returns a per-session `AIFunction` with session and tool list captured in closure |
| FR-4.2 | Load mode: `expand_tools(names: [...])` fetches tools from catalog, adds to session via `ResumeSessionAsync` |
| FR-4.3 | Query mode: `expand_tools(query: "...")` searches catalog and returns matching names without loading |
| FR-4.4 | `names` parameter accepts both provider names (resolves to all provider tools) and individual tool names |
| FR-4.5 | Expand returns a result object with enabled tool names and count |

### FR-5: Session Integration

| Requirement | Description |
|-------------|-------------|
| FR-5.1 | `AgentMessageService.GetOrCreateSessionAsync` populates `SessionConfig.Tools` with default tools + `expand_tools` |
| FR-5.2 | `AvailableTools` and `ExcludedTools` are never set — CLI built-in tools remain visible |
| FR-5.3 | `expand_tools` calls `ResumeSessionAsync` with `Tools = current + new` to add tools |

### FR-6: Lifecycle and Status

| Requirement | Description |
|-------------|-------------|
| FR-6.1 | `ToolStatus` (Ready, Degraded, Unavailable) tracked internally per tool name |
| FR-6.2 | Only tools with status Ready are returned by `GetDefaultTools()` and `GetToolsByName()` |
| FR-6.3 | Teardown: orphaned handlers on active sessions throw `ObjectDisposedException` |
| FR-6.4 | Provider `DisposeAsync` called after tools removed and watch loop cancelled |

## Non-Functional Requirements

### Performance

| Requirement | Target |
|-------------|--------|
| Provider registration latency | < 500ms per provider (excluding provider's own discovery time) |
| `GetDefaultTools()` lookup | O(1) — pre-computed list |
| `GetToolsByName()` lookup | O(n) where n = requested names — dictionary-backed |
| `SearchTools()` keyword match | O(m) where m = total catalog tools |
| Session tool payload | ~50 KB initial (24 tools), ~120 KB after moderate expansion (60 tools) |

### Scalability

| Requirement | Target |
|-------------|--------|
| Concurrent registered tools | Up to 200 without degradation |
| Concurrent providers | Up to 20 |
| Sessions per gateway | Bounded by `SessionPool` (existing limit) |

### Security

| Requirement | Target |
|-------------|--------|
| No AvailableTools gating | CLI built-in tools always visible |
| Tier priority enforcement | Higher-tier tools cannot be overridden by lower-tier |
| Teardown isolation | Disposed providers produce loud failures, not silent misbehavior |

## Scope

### In Scope
- `IToolProvider`, `IToolCatalog`, `IToolRegistrar`, `IToolExpander` interfaces
- `ToolBridge` singleton implementation (catalog + registrar)
- `ToolExpander` implementation with load/query modes
- `ToolDescriptor`, `ToolSourceTier`, `ToolStatus` value types
- Session creation integration in `AgentMessageService`
- DI registration in `GatewayServiceExtensions`
- Provider registration during hosted service startup
- Hot refresh via `WaitForSurfaceChangeAsync` background loop
- Unit tests for all components

### Out of Scope
- Implementing any specific provider (MCPorter, cron, bundled mind tools) — separate plans
- Execution approval gates (REQ-016) — interface reserves the hook, not implemented
- Managed tier sourcing — reserved in enum but not implemented
- Node-routed execution — interface supports it, deferred to device node plan
- Skill descriptor file parsing — providers handle their own discovery
- Operator admin endpoints (list/detail/re-discovery) — future work
- Skill change notifications to connected clients (REQ-022) — future work

### Future Considerations
- Approval gate integration when REQ-016 is implemented
- Workspace-tier descriptor format definition with first workspace provider
- Managed tier sourcing pipeline
- Operator admin API for skill management
- Skill change notification events via SignalR

## Success Criteria

| Metric | Target | Measurement |
|--------|--------|-------------|
| All interfaces defined | 4 interfaces + 3 value types | Code inspection |
| ToolBridge passes unit tests | Discovery, collision, priority, refresh, teardown | `dotnet test` |
| ToolExpander passes unit tests | Load mode, query mode, provider resolution | `dotnet test` |
| Session creation populates Tools | Default tools + expand_tools on every new session | Unit test + manual verification |
| CLI built-ins unaffected | No `AvailableTools`/`ExcludedTools` set | Code inspection |
| Spec tests pass | All spec tests in `specs/tests/003-tool-bridge.md` | Spec test runner |

## Assumptions

1. The Copilot SDK's `ResumeSessionAsync` accepts `Tools` in `ResumeSessionConfig` for modifying a session's tool set
2. `AIFunction` instances from `Microsoft.Extensions.AI` are the correct type for `SessionConfig.Tools`
3. The SDK's `ToolExecutionStartEvent`/`ToolExecutionCompleteEvent` fire for custom-registered tools
4. Provider implementations handle their own discovery (file parsing, MCP server connection, etc.)
5. `CopilotGatewayClient` already sets `OnPermissionRequest = PermissionHandler.ApproveAll`

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| `ResumeSessionAsync` doesn't support `Tools` modification | Low | High | SDK source analysis confirms support; integration test validates |
| Session payload grows too large with many expansions | Low | Medium | Lazy registration limits growth; ~200 tool ceiling is practical |
| Background change-watch loops leak on shutdown | Medium | Low | Per-provider `CancellationTokenSource` cancelled in `UnregisterProviderAsync` and `DisposeAsync` |
| `expand_tools` session reference stale after session reap | Medium | Medium | Closure captures session reference; `ObjectDisposedException` on stale use is acceptable |
| Provider `DiscoverAsync` blocks startup | Medium | Medium | Individual provider timeouts; registration failure logs error and continues |

## Glossary

| Term | Definition |
|------|------------|
| Tool Bridge | The combined catalog + registrar singleton that manages tool lifecycle |
| Tool Provider | An implementation of `IToolProvider` that discovers and executes tools from a specific source |
| Tool Descriptor | Immutable record wrapping an `AIFunction` with provider and tier metadata |
| Expand Tools | Per-session `AIFunction` that lazy-loads additional tools onto a session |
| AlwaysVisible | Tools marked to be included on every new session without requiring `expand_tools` |
| Tier | Priority level (Bundled > Workspace > Managed) for collision resolution |
| Hot Refresh | Re-discovery triggered by provider's surface change signal |
