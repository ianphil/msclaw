---
title: "Tool Bridge — Provider Abstraction & Registry"
status: open
priority: high
created: 2026-03-08
---

# Tool Bridge — Provider Abstraction & Registry

## Summary

Extract a general-purpose tool bridge that lets any `IToolProvider` register tools on Copilot SDK sessions. The bridge is split into three focused interfaces: `IToolCatalog` (read — what tools exist), `IToolRegistrar` (write — provider lifecycle), and `IToolExpander` (session-aware — lazy tool registration). Sessions start with only default tools registered; additional tools are added lazily via `expand_tools` using `ResumeSessionAsync`. This avoids interfering with the CLI's built-in tools (no `AvailableTools`/`ExcludedTools` needed) and keeps session payloads minimal. Individual providers (MCPorter, cron, bundled mind tools, future MCP servers) implement a thin `IToolProvider` interface.

## Motivation

The MCPorter plan embeds bridge infrastructure (discovery, registry, lazy meta-tools, refresh) alongside provider-specific concerns. MCPorter, cron, bundled mind tools, and node-routed device capabilities are all tool providers — they differ in how they discover and execute tools, but the lifecycle of getting tools onto a session is identical. Separating the bridge from its providers means new integrations (Slack MCP server, filesystem tools, another agent's surface) require zero bridge changes — implement `IToolProvider`, register it, done.

The existing specs (`gateway-skills.md`, `gateway-agent-runtime.md`) define three sourcing tiers (bundled > workspace > managed), execution modes, descriptor validation, status tracking, and invocation events — but no formal provider abstraction. This plan fills that gap.

## Proposal

### Goals

- Define `IToolProvider` — the contract any tool source implements (discover, describe, execute, tear down, signal surface changes via async pull)
- Define `IToolCatalog` (read) — what tools exist, default tool set, tool lookup by name — consumed by session factory and expander
- Define `IToolRegistrar` (write) — provider registration, unregistration, refresh — consumed by hosting layer
- Define `IToolExpander` (session-aware) — creates per-session `expand_tools` AIFunction instances, lazy-registers tools via `ResumeSessionAsync`
- Integrate the catalog into `AgentMessageService.GetOrCreateSessionAsync` to populate `SessionConfig.Tools` with only default (AlwaysVisible) tools + `expand_tools`
- Do **not** use `AvailableTools`/`ExcludedTools` — every tool registered on the session is visible. The CLI's built-in tools remain unaffected because we never set a whitelist that would hide them
- Leverage the SDK's built-in `ToolExecutionStartEvent` / `ToolExecutionCompleteEvent` for invocation lifecycle; decorate with provider metadata where needed (REQ-015)
- Support hot refresh — providers expose an async pull method; registrar awaits it in a background loop and re-discovers on change

### Non-Goals

- Implementing any specific provider (MCPorter, cron, bundled mind tools) — those are separate plans
- Execution approval gates (REQ-016) — future work, interface reserves the hook
- Managed tier sourcing — reserved in the interface but not implemented
- Node-routed execution — interface supports it, implementation deferred to device node plan

## Design

### Component Diagram — Before Tool Bridge

```
┌─────────────────────────────────────────────────────────────┐
│                        GatewayHub (SignalR)                  │
│                     SendMessage(prompt)                      │
└──────────────────────────┬──────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                    AgentMessageService                       │
│  ┌───────────────┐  ┌─────────────┐  ┌──────────────────┐  │
│  │ConcurrencyGate│  │ SessionPool │  │ GatewayClient    │  │
│  │(1 run/caller) │  │ (30m reap)  │  │ Proxy            │  │
│  └───────────────┘  └──────┬──────┘  └────────┬─────────┘  │
└────────────────────────────┼──────────────────┼─────────────┘
                             │                  │
                             ▼                  ▼
                    ┌──────────────┐   ┌──────────────────┐
                    │ SessionConfig │   │GatewayHosted     │
                    │ {            │   │Service           │
                    │   Streaming, │   │  - MindValidator │
                    │   SysMsg     │   │  - IdentityLoader│
                    │   (no Tools) │   │  - SDK Client    │
                    │ }            │   └──────────────────┘
                    └──────────────┘

  Tools = ∅  ← sessions have NO tool surface
```

### Component Diagram — After Tool Bridge

```
┌─────────────────────────────────────────────────────────────┐
│                        GatewayHub (SignalR)                  │
│                     SendMessage(prompt)                      │
└──────────────────────────┬──────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                    AgentMessageService                       │
│  ┌───────────────┐  ┌─────────────┐  ┌──────────────────┐  │
│  │ConcurrencyGate│  │ SessionPool │  │ GatewayClient    │  │
│  └───────────────┘  └──────┬──────┘  │ Proxy            │  │
│                            │         └────────┬─────────┘  │
│                            │    ┌─────────────┘            │
│                            ▼    ▼                          │
│                    ┌──────────────────────────┐             │
│                    │  SessionConfig           │             │
│                    │  {                       │             │
│                    │    Streaming,            │             │
│          NEW ───►  │    Tools = default +     │             │
│                    │      expand_tools only,  │             │
│                    │    (no AvailableTools —   │             │
│                    │     built-ins unaffected) │             │
│                    │  }                       │             │
│                    └──────────────────────────┘             │
└───────────────────────────┬────────────────────────────────┘
                            │
                 ┌──────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────────────────┐
│          IToolCatalog (read) + IToolRegistrar (write)        │
│                      (singleton impl)                        │
│                                                             │
│  Aggregated tool catalog (all handlers registered):         │
│    ┌─────────────┬─────────────┬─────────────┬─────────┐   │
│    │  Provider A  │  Provider B  │  Provider C  │  ...   │   │
│    └──────┬──────┴──────┬──────┴──────┬──────┴─────────┘   │
│           │             │             │                     │
│  Priority: Bundled > Workspace > Managed                    │
│  Collision: higher tier wins; same-tier = hard error         │
│  Refresh:  registrar awaits provider change signal           │
└───────────┼─────────────┼─────────────┼─────────────────────┘
            │             │             │
            │   ┌─────────────────────────────────────┐
            │   │  IToolExpander (per-session factory) │
            │   │  expand_tools(names[]) →             │
            │   │    ResumeSession w/ current +        │
            │   │    requested tools (lazy reg)        │
            │   └─────────────────────────────────────┘
            │             │             │
            ▼             ▼             ▼
┌───────────────┐ ┌──────────────┐ ┌──────────────┐
│ IToolProvider │ │ IToolProvider │ │ IToolProvider │
│               │ │               │ │               │
│  e.g. Bundled │ │  e.g. Cron   │ │ e.g. MCPorter│
│  mind tools   │ │  (future)    │ │  (future)    │
│               │ │               │ │               │
│ DiscoverAsync │ │ DiscoverAsync │ │ DiscoverAsync │
│ CreateFuncsAs │ │ CreateFuncsAs │ │ CreateFuncsAs │
└───────────────┘ └──────────────┘ └──────────────┘
     (separate plans — plug in identically)
```

### Sequence: Startup — Provider Registration & Discovery

```
GatewayHostedService         IToolRegistrar             IToolProvider(s)
        │                         │                          │
        │  RegisterProviderAsync  │                          │
        │────────────────────────►│                          │
        │                         │     DiscoverAsync()      │
        │                         │─────────────────────────►│
        │                         │                          │
        │                         │   List<ToolDescriptor>   │
        │                         │◄─────────────────────────│
        │                         │                          │
        │                         │── validate descriptors   │
        │                         │── enforce tier priority   │
        │                         │── build catalog index     │
        │                         │                          │
        │  (repeat per provider)  │                          │
        │────────────────────────►│         ...              │
        │                         │                          │
        │        ready            │                          │
        │◄────────────────────────│                          │
```

### Sequence: First Message — Session Gets Only Default Tools

```
Client    GatewayHub    AgentMessageSvc    SessionPool    IToolCatalog     SDK
  │           │               │                │               │            │
  │ SendMsg   │               │                │               │            │
  │──────────►│               │                │               │            │
  │           │  SendAsync    │                │               │            │
  │           │──────────────►│                │               │            │
  │           │               │                │               │            │
  │           │               │ GetOrCreate    │               │            │
  │           │               │───────────────►│               │            │
  │           │               │                │               │            │
  │           │               │                │──── (cache miss, call factory)
  │           │               │                │               │            │
  │           │               │                │ GetDefaultTools()          │
  │           │               │                │──────────────►│            │
  │           │               │                │  [23 AIFuncs  │            │
  │           │               │                │   + expand_   │            │
  │           │               │                │   tools]      │            │
  │           │               │                │◄──────────────│            │
  │           │               │                │               │            │
  │           │               │                │ CreateSessionAsync(        │
  │           │               │                │   SessionConfig {          │
  │           │               │                │     Streaming = true,      │
  │           │               │                │     SysMsg = ...,          │
  │           │               │                │     Tools = [24 funcs],   │
  │           │               │                │   })                       │
  │           │               │                │  (no AvailableTools —      │
  │           │               │                │   built-ins stay visible)  │
  │           │               │                │──────────────────────────► │
  │           │               │                │                            │
  │           │               │                │          session           │
  │           │               │                │◄───────────────────────────│
  │           │               │   session      │               │            │
  │           │               │◄───────────────│               │            │
  │           │               │                │               │            │
  │           │               │── bridge events, send prompt   │            │
  │           │               │                │               │            │
  │  stream   │               │                │               │            │
  │◄──────────│◄──────────────│                │               │            │
```

### Sequence: Tool Expansion — Agent Requests More Tools (Lazy Registration)

```
SDK (Agent)              expand_tools           IToolExpander        GatewayClient
    │                        │                       │                    │
    │ "I need to post to     │                       │                    │
    │  Teams"                │                       │                    │
    │                        │                       │                    │
    │  expand_tools(         │                       │                    │
    │    ["teams_post_msg",  │                       │                    │
    │     "teams_list_chan"]) │                       │                    │
    │───────────────────────►│                       │                    │
    │                        │  GetToolsByName([..]) │                    │
    │                        │──────────────────────►│                    │
    │                        │  [2 AIFunctions]      │ (from IToolCatalog)│
    │                        │◄──────────────────────│                    │
    │                        │                       │                    │
    │                        │  ResumeSessionAsync(sessionId,            │
    │                        │    ResumeSessionConfig {                   │
    │                        │      Tools = current 24 + 2 new = 26     │
    │                        │    })                                      │
    │                        │──────────────────────────────────────────►│
    │                        │                                            │
    │                        │           expanded session                 │
    │                        │◄───────────────────────────────────────────│
    │                        │                       │                    │
    │  "Enabled 2 tools:     │                       │                    │
    │   teams_post_msg,      │                       │                    │
    │   teams_list_chan"      │                       │                    │
    │◄───────────────────────│                       │                    │
    │                                                                     │
    │  teams_post_msg(chatId: "...", content: "...")                      │
    │───────────────────────────────────────────────────────────────────►│
    │                        │                       │  ┌──────────────┐ │
    │                        │                       │  │ IToolProvider │ │
    │                        │                       │  │  (handler)   │ │
    │                        │                       │  └──────┬───────┘ │
    │                        │                       │         │ execute  │
    │                        │                       │         ▼         │
    │  { "id": "msg-42", "status": "sent" }          │   (provider impl)│
    │◄───────────────────────────────────────────────────────────────────│
```

> **Design note — lazy registration, not visibility gating:** The SDK's `SessionConfig`
> and `ResumeSessionConfig` both accept `AvailableTools` (allowlist) and `ExcludedTools`
> (blocklist), but we intentionally do **not** use them. Setting `AvailableTools` creates
> a whitelist across *all* tools — including the CLI's built-in tools (file editing,
> terminal, search). You'd have to enumerate every built-in tool name to keep them visible,
> which is fragile and breaks when the CLI adds new built-ins. Instead, we register only
> the default tools on session creation. Every registered tool is visible. The CLI's
> built-in tools remain unaffected. When the agent needs more tools, `expand_tools`
> fetches their `AIFunction` handlers from the catalog and adds them to the session via
> `ResumeSessionAsync(Tools = current + new)`. The session grows incrementally.
>
> **Design note — why `expand_tools` lives in `IToolExpander`, not the catalog:**
> `expand_tools` needs a reference to the `CopilotSession` it's running inside and the
> current tool set for that session. An `AIFunction` handler doesn't inherently receive
> the session — the SDK invokes it with model-chosen parameters only. Putting this in the
> catalog would couple a passive data structure to session lifecycle. Instead,
> `IToolExpander.CreateExpandToolsFunction(session, currentTools)` produces a per-session
> `AIFunction` with the session and its tool list captured in the closure. The session
> factory calls this after `CreateSessionAsync` and includes it in the initial tool set.

### `IToolProvider`

```csharp
public interface IToolProvider : IAsyncDisposable
{
    /// <summary>Unique name identifying this provider (e.g., "mcporter", "cron", "bundled").</summary>
    string Name { get; }

    /// <summary>Sourcing tier — determines priority when names collide.</summary>
    ToolSourceTier Tier { get; }

    /// <summary>Discover available tools and build their AIFunction handlers.
    /// Called at startup and on refresh. Returns descriptors wrapping ready-to-use
    /// AIFunction instances — the catalog passes Function directly into SessionConfig.Tools.</summary>
    Task<IReadOnlyList<ToolDescriptor>> DiscoverAsync(CancellationToken cancellationToken);

    /// <summary>Awaitable signal that the tool surface may have changed.
    /// Returns when a change occurs or the token is cancelled. The registrar
    /// awaits this in a background loop and calls DiscoverAsync on return.
    /// Providers that never change should return Task.Delay(Timeout.Infinite, ct).</summary>
    Task WaitForSurfaceChangeAsync(CancellationToken cancellationToken);
}
```

The registrar runs a background loop per provider:

```csharp
// Inside ToolBridge (the singleton implementation)
while (!ct.IsCancellationRequested)
{
    await provider.WaitForSurfaceChangeAsync(ct);
    await RefreshProviderAsync(provider.Name, ct);
}
```

This is async-native, cancellation-aware, and free of race conditions — the registrar controls when it processes the notification, serializing catalog mutations.

`ToolSourceTier` is `Bundled`, `Workspace`, or `Managed` — matching the spec's priority ordering.

### `ToolDescriptor`

```csharp
/// <summary>Registry-specific metadata that wraps an AIFunction.
/// Name, description, and parameter schema come from Function — no duplication.
/// Used for catalog indexing, collision resolution, and visibility decisions.
/// Immutable — operational status is tracked separately by the registry.</summary>
public sealed record ToolDescriptor
{
    /// <summary>The SDK tool instance. Source of truth for name, description, and schema.
    /// Passed directly into SessionConfig.Tools.</summary>
    public required AIFunction Function { get; init; }

    /// <summary>Provider that owns this tool.</summary>
    public required string ProviderName { get; init; }

    /// <summary>Tier inherited from the owning provider.</summary>
    public required ToolSourceTier Tier { get; init; }

    /// <summary>When true, this tool is included in SessionConfig.Tools on every new session
    /// without requiring an expand_tools call.</summary>
    public bool AlwaysVisible { get; init; }
}

public enum ToolSourceTier { Bundled, Workspace, Managed }

public enum ToolStatus { Ready, Degraded, Unavailable }
```

`ToolDescriptor` is intentionally immutable — it is a value object describing a tool's identity and catalog placement. Operational status (`ToolStatus`) is tracked internally by the registry implementation in a `ConcurrentDictionary<string, ToolStatus>`, keyed by tool name. Only tools with status `Ready` are included in `GetDefaultTools()` and `GetToolsByName()`. Status changes (e.g., a provider's backing service going down) are applied through `RefreshProviderAsync`, which re-discovers and re-evaluates the provider's surface.

The registry indexes by `Function.Name` for collision resolution and catalog lookups, and passes `Function` directly into `SessionConfig.Tools` — one source of truth, no drift.

### `IToolCatalog` (read-side — consumed by session factory and expander)

```csharp
public interface IToolCatalog
{
    /// <summary>Tools that should be registered on every new session (AlwaysVisible = true).
    /// These are the only custom tools on the session at creation time.
    /// Does NOT include expand_tools — that is added by IToolExpander.</summary>
    IReadOnlyList<AIFunction> GetDefaultTools();

    /// <summary>Fetch specific tools by name for lazy registration during expand.
    /// Returns only tools with status Ready. Unknown names are silently skipped.</summary>
    IReadOnlyList<AIFunction> GetToolsByName(IEnumerable<string> names);

    /// <summary>All known tool names in the catalog, grouped by provider.
    /// Used by expand_tools to build its description so the agent knows what's available.
    /// Also used to resolve provider names to tool lists (e.g., "teams" → all teams tools).</summary>
    IReadOnlyList<string> GetCatalogToolNames();

    /// <summary>Get all tool names belonging to a specific provider.</summary>
    IReadOnlyList<string> GetToolNamesByProvider(string providerName);

    /// <summary>Search the catalog by keyword against tool names and descriptions.
    /// Returns matching tool names across all providers. Used by expand_tools query mode
    /// for semantic discovery (e.g., "send message" → teams_post_message, slack_post_message).</summary>
    IReadOnlyList<string> SearchTools(string query);

    /// <summary>Look up a single descriptor by tool name. Returns null if not found.</summary>
    ToolDescriptor? GetDescriptor(string toolName);
}
```

### `IToolRegistrar` (write-side — consumed by hosting layer)

```csharp
public interface IToolRegistrar
{
    /// <summary>Register a provider. Discovery runs immediately.
    /// Same-tier name collisions throw InvalidOperationException.</summary>
    Task RegisterProviderAsync(IToolProvider provider, CancellationToken cancellationToken);

    /// <summary>Unregister a provider and remove its tools from the catalog.
    /// Active sessions retain their already-registered AIFunction handlers, but
    /// those handlers throw ObjectDisposedException if invoked after the provider
    /// is disposed. See Teardown Contract below.</summary>
    Task UnregisterProviderAsync(string providerName, CancellationToken cancellationToken);

    /// <summary>Re-discover tools from a specific provider.</summary>
    Task RefreshProviderAsync(string providerName, CancellationToken cancellationToken);
}
```

### `IToolExpander` (session-aware — creates per-session expand_tools, lazy-registers)

```csharp
public interface IToolExpander
{
    /// <summary>Creates an expand_tools AIFunction bound to a specific session.
    /// The returned function captures the session and its current tool list in its closure.
    ///
    /// Two invocation modes:
    /// - Load mode (names provided): fetches tools from catalog, adds to session via ResumeSessionAsync.
    /// - Query mode (query provided, no names): searches catalog by keyword, returns matching
    ///   tool names without loading. Agent calls again with specific names.
    ///
    /// Called by the session factory after CreateSessionAsync; the returned AIFunction
    /// is included in the initial SessionConfig.Tools set.</summary>
    AIFunction CreateExpandToolsFunction(
        CopilotSession session,
        IList<AIFunction> currentSessionTools);
}
```

The session factory flow becomes:
1. Get default tools from catalog: `catalog.GetDefaultTools()`
2. Create `expand_tools` via `expander.CreateExpandToolsFunction(session, currentTools)` — but we need the session first
3. So: create the session with default tools, then immediately call `ResumeSessionAsync` to add `expand_tools`, OR include a placeholder that binds lazily
4. **Practical approach**: Build the full initial tool list (default tools + expand_tools) before `CreateSessionAsync`. The `expand_tools` function captures a mutable `List<AIFunction>` that starts with the initial set. When expand adds tools, it mutates the list and calls `ResumeSessionAsync` with the updated list. The session reference is captured after creation via a `TaskCompletionSource<CopilotSession>` or similar deferred binding.

### Lazy registration

Sessions start with only default tools registered (`SessionConfig.Tools = catalog.GetDefaultTools() + expand_tools`). No `AvailableTools` or `ExcludedTools` are set — every registered tool is visible to the model, and the CLI's built-in tools (file editing, terminal, search) remain unaffected.

`expand_tools` operates in two modes via its parameters:

**Load mode** (`names` provided) — adds tools to the session:
```
expand_tools(names: ["teams"])              → loads all 12 teams_* tools
expand_tools(names: ["teams_post_msg"])     → loads one specific tool
expand_tools(names: ["teams", "cron"])      → loads all tools from both providers
```
The expander resolves provider names to tool lists, fetches `AIFunction` instances from the catalog, appends them to the session's tool list, and calls `ResumeSessionAsync(Tools = current + new)`.

**Query mode** (`query` provided, no `names`) — searches without loading:
```
expand_tools(query: "send message")
← { matches: ["teams_post_message", "slack_post_message", "email_send_message"] }
```
Returns matching tool names across all providers by keyword-matching against tool names and descriptions. The agent reads the results, then calls `expand_tools` again in load mode with the specific tools it wants. Nothing is added to the session.

This hybrid gives semantic discovery when the agent doesn't know provider names, without burning catalog tokens in the tool description every turn. The description stays compact: just the provider list with tool counts.

### Integration point

`AgentMessageService.GetOrCreateSessionAsync` changes from:

```csharp
var sessionConfig = new SessionConfig { Streaming = true };
```

to:

```csharp
// Build initial tool set: default tools + expand_tools
var defaultTools = toolCatalog.GetDefaultTools();
var sessionTools = new List<AIFunction>(defaultTools);
var expandFn = toolExpander.CreateExpandToolsFunction(sessionHolder, sessionTools);
sessionTools.Add(expandFn);

var sessionConfig = new SessionConfig
{
    Streaming = true,
    Tools = sessionTools
    // No AvailableTools — all registered tools are visible
    // CLI built-in tools remain unaffected
};
```

Only default tools + `expand_tools` are on the session. The model sees a small, focused tool set. Additional tools are added lazily via `expand_tools` → `ResumeSessionAsync`.

Providers register during `GatewayHostedService.StartAsync` via DI-resolved `IEnumerable<IToolProvider>`. The registrar starts a `WaitForSurfaceChangeAsync` background loop per provider.

### Invocation lifecycle events

The SDK already emits `ToolExecutionStartEvent` and `ToolExecutionCompleteEvent` for every tool call. The bridge does **not** reimplement this. If REQ-015 requires additional metadata (e.g., provider name, tier), the bridge decorates the SDK events in the `AgentMessageService` event handler rather than emitting its own.

### Priority & collision resolution

When two providers expose a tool with the same name, the higher-tier provider wins (Bundled > Workspace > Managed). **Same-tier name collisions are a hard error** — `RegisterProviderAsync` throws `InvalidOperationException` with a message identifying both providers and the conflicting tool name. This makes the conflict visible at startup rather than silently depending on DI registration order, which is an implicit coupling that breaks during refactors.

### Teardown contract

When a provider is unregistered via `UnregisterProviderAsync`:

1. The provider's tools are removed from the catalog immediately — new sessions won't include them.
2. Active sessions retain their already-registered `AIFunction` handlers (the SDK has ownership of those references). If the agent invokes a tool whose provider has been disposed, the handler **throws `ObjectDisposedException`** with a message identifying the provider. This is the explicit contract — orphaned handlers fail loudly.
3. The registrar cancels the `WaitForSurfaceChangeAsync` loop for that provider via the per-provider `CancellationTokenSource`.
4. The registrar calls `provider.DisposeAsync()` after removing tools and cancelling the watch loop.

Providers that manage external connections (e.g., MCPorter's MCP transport) must handle graceful shutdown in their `DisposeAsync` implementation.

### Scalability note: lazy registration payloads

With lazy registration, sessions start with only ~24 tool definitions (~50 KB). Each `expand_tools` call adds a few tools and calls `ResumeSessionAsync`, which re-sends *all currently registered tools* (no delta mechanism in the SDK). After several expansions a session might have 40–60 tools (~120 KB) — well within safe limits.

The practical ceiling per session is ~100–200 tools before payload size (~500 KB+) becomes a concern. This is unlikely to be hit in normal use since tools are added incrementally based on conversation needs, not all at once.

SDK source analysis confirms: tools are stored in a `Dictionary<string, AIFunction>` (O(1) dispatch), serialized as JSON via StreamJsonRpc. No explicit limits in the SDK code.

## Tasks

- [ ] **Define core abstractions**: `IToolProvider` (with `IAsyncDisposable`, `WaitForSurfaceChangeAsync`), `IToolCatalog` (read — `GetDefaultTools`, `GetToolsByName`, `GetCatalogToolNames`, `GetToolNamesByProvider`, `SearchTools`), `IToolRegistrar` (write), `IToolExpander` (session-aware — `CreateExpandToolsFunction`), `ToolDescriptor`, `ToolSourceTier`, `ToolStatus` — interfaces and value types in `MsClaw.Gateway/Services/Tools/`
- [ ] **Implement `ToolBridge`**: Singleton that implements both `IToolCatalog` and `IToolRegistrar`. Aggregates providers, validates descriptors, enforces tier priority (same-tier collision = hard error), tracks `ToolStatus` internally. `GetDefaultTools()` returns only `AlwaysVisible` tools. `GetToolsByName()` fetches specific tools for lazy registration. `SearchTools()` keyword-matches against tool names and descriptions for query mode. Runs `WaitForSurfaceChangeAsync` background loop per provider for hot refresh.
- [ ] **Implement `ToolExpander`**: Implements `IToolExpander`. `CreateExpandToolsFunction` returns a per-session `AIFunction` with two modes: **load** (names param — resolves provider names via `GetToolNamesByProvider`, fetches functions via `GetToolsByName`, appends to session tool list, calls `ResumeSessionAsync`) and **query** (query param — calls `SearchTools`, returns matching names without loading). Description includes compact catalog summary (provider → tool count).
- [ ] **Wire into Gateway DI**: Register `ToolBridge` as singleton for both `IToolCatalog` and `IToolRegistrar` in `GatewayServiceExtensions`. Register `IToolExpander`. Resolve `IEnumerable<IToolProvider>` and register each during hosted service startup. Inject `IToolCatalog` and `IToolExpander` into `AgentMessageService`.
- [ ] **Modify session creation**: Update `AgentMessageService.GetOrCreateSessionAsync` to build `SessionConfig.Tools` from `catalog.GetDefaultTools()` + `expander.CreateExpandToolsFunction(...)`. Do **not** set `AvailableTools` or `ExcludedTools` — CLI built-in tools stay visible.
- [ ] **Unit tests**: `ToolBridge` with mock providers — discovery, same-tier collision error, cross-tier priority, `GetDefaultTools` vs `GetToolsByName`, `SearchTools` keyword matching, refresh via `WaitForSurfaceChangeAsync`, provider unregistration + teardown contract (orphaned handler throws `ObjectDisposedException`). `ToolExpander` — load mode (lazy registration round-trip, provider-name resolution, `ResumeSessionAsync` with current + new tools), query mode (returns matches without loading, empty results for no match).

## Resolved Questions

- **`ResumeSessionAsync` for tool expansion**: Confirmed — `ResumeSessionConfig` accepts `Tools`. The `expand_tools` function uses `Tools` on `ResumeSessionConfig` to add new handlers (current + new). Note: the SDK re-sends all tools on resume (no delta), so expand cost scales with total registered tools per session. Calls flow through `CopilotGatewayClient` which sets `OnPermissionRequest` automatically.
- **Lazy registration over `AvailableTools` gating**: Decided — do **not** use `AvailableTools`/`ExcludedTools`. Setting `AvailableTools` creates a whitelist across *all* tools including CLI built-ins (file editing, terminal, search), requiring enumeration of every built-in name — fragile and breaks when CLI adds new tools. Instead, register only default tools on session creation. `expand_tools` lazy-adds more via `ResumeSessionAsync`. CLI built-ins stay visible without intervention.
- **Invocation events**: The SDK emits `ToolExecutionStartEvent`/`ToolExecutionCompleteEvent` natively. The bridge decorates these with provider metadata if REQ-015 requires it, rather than emitting its own events.
- **Same-tier collision policy**: Hard error, not a warning. `RegisterProviderAsync` throws `InvalidOperationException`. Makes DI registration order irrelevant — conflict is surfaced at startup.
- **`expand_tools` session reference**: Solved via `IToolExpander.CreateExpandToolsFunction(session, currentTools)` — the session and a mutable tool list are captured in a closure at creation time, one `AIFunction` instance per session. This avoids coupling the catalog to session lifecycle.
- **Surface change notification**: Async pull model (`WaitForSurfaceChangeAsync`) replaces bare `Action` event. Registrar controls when it processes changes, serializing catalog mutations. No race conditions.
- **Teardown contract**: Orphaned handlers on active sessions throw `ObjectDisposedException`. Registrar cancels watch loop, then disposes provider.
- **Workspace-tier descriptor format**: Deferred — no workspace-tier provider is in scope for this plan (non-goal). Format will be defined when the first workspace-tier provider is planned.
- **`expand_tools` accepts both provider names and individual tool names**: The agent can call `expand_tools(names: ["teams"])` to add all tools from the `teams` provider, or `expand_tools(names: ["teams_post_msg", "teams_list_chan"])` for specific tools, or mix both. The expander resolves provider names to their full tool list via the catalog before adding to the session.
- **`expand_tools` hybrid: load mode + query mode**: Single tool with two invocation modes. Load mode (`names` param) adds tools to the session via `ResumeSessionAsync`. Query mode (`query` param, no `names`) searches the catalog by keyword against tool names and descriptions, returning matching names without loading. This gives semantic discovery ("send message" → `teams_post_message, slack_post_message`) without burning catalog tokens in the tool description every turn. Supersedes the MCPorter plan's two-tool `tool_registry_search` + `tool_registry_load` design — same capability, one tool surface.

## Open Questions

*None — all questions resolved.*
