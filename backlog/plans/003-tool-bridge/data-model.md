# Data Model: Tool Bridge

## Entities

### ToolDescriptor

Immutable value object describing a tool's identity and catalog placement. The `AIFunction` inside is the source of truth for name, description, and parameter schema.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| Function | `AIFunction` | Yes | — | The SDK tool instance; passed directly into `SessionConfig.Tools` |
| ProviderName | `string` | Yes | — | Name of the owning provider (e.g., "mcporter", "bundled") |
| Tier | `ToolSourceTier` | Yes | — | Sourcing tier inherited from provider |
| AlwaysVisible | `bool` | No | `false` | When true, included in `SessionConfig.Tools` on every new session |

**Relationships:**
- Owned by one `IToolProvider` (via ProviderName)
- Indexed in `ToolBridge` by `Function.Name` (unique key)
- Referenced by `IToolCatalog` for lookup and search

**Invariants:**
- `Function.Name` must be non-null and non-empty
- `ProviderName` must be non-null and non-empty
- `Function.Name` is unique across the entire catalog (collision rules enforce this)
- Immutable after creation — create new descriptor on change

### ToolSourceTier

Priority tier for collision resolution.

| Value | Priority | Description |
|-------|----------|-------------|
| `Bundled` | Highest | Shipped with the gateway; always present |
| `Workspace` | Medium | Discovered from mind directory |
| `Managed` | Lowest | Installed from external sources (reserved) |

**Invariants:**
- Cross-tier collision: higher tier wins, lower tier skipped
- Same-tier collision: `InvalidOperationException` at registration

### ToolStatus

Operational readiness of a registered tool.

| Value | Description |
|-------|-------------|
| `Ready` | All requirements satisfied; tool can be invoked |
| `Degraded` | Missing optional prerequisites; registered but may fail |
| `Unavailable` | Missing required capability (e.g., no matching node) |

**Invariants:**
- Only `Ready` tools are returned by `GetDefaultTools()` and `GetToolsByName()`
- Status tracked separately from descriptor in `ConcurrentDictionary<string, ToolStatus>`
- Status updated via `RefreshProviderAsync`

### IToolProvider

Contract for any tool source.

| Property/Method | Type | Description |
|-----------------|------|-------------|
| Name | `string` | Unique provider identifier |
| Tier | `ToolSourceTier` | Provider's sourcing tier |
| DiscoverAsync(ct) | `Task<IReadOnlyList<ToolDescriptor>>` | Discover tools and build AIFunction handlers |
| WaitForSurfaceChangeAsync(ct) | `Task` | Awaitable signal for surface changes |
| DisposeAsync() | `ValueTask` | Clean up provider resources |

**Invariants:**
- `Name` is unique across all registered providers
- `DiscoverAsync` may be called multiple times (on initial registration and refresh)
- `WaitForSurfaceChangeAsync` returns when a change occurs or cancellation

### SessionHolder

Thread-safe deferred binding wrapper for session reference in expand_tools closure. Uses `TaskCompletionSource<IGatewaySession>` to eliminate race conditions.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| _tcs | `TaskCompletionSource<IGatewaySession>` | Yes | `new()` | Internal completion source |

| Method | Returns | Description |
|--------|---------|-------------|
| Bind(session) | `void` | Calls `_tcs.SetResult(session)` — unblocks awaiting callers |
| GetSessionAsync() | `Task<IGatewaySession>` | Returns immediately if bound, otherwise awaits binding |

**Invariants:**
- `Bind` must be called exactly once; second call throws `InvalidOperationException` (TCS guarantee)
- `GetSessionAsync` is safe to call from any thread — `TaskCompletionSource` handles synchronization
- `expand_tools` `await`s `GetSessionAsync()` — no null checks needed

## State Transitions

### Provider Lifecycle

```
                RegisterProviderAsync
Unregistered ──────────────────────────► Registered
                                            │
                                            │ WaitForSurfaceChangeAsync
                                            │ (background loop)
                                            ▼
                                       ┌─────────┐
                                       │Watching  │◄──── RefreshProviderAsync
                                       │for change│      (re-discover + update catalog)
                                       └────┬─────┘
                                            │
                                            │ UnregisterProviderAsync
                                            ▼
                                       Unregistered
                                       (tools removed, loop cancelled, disposed)
```

| State | Description |
|-------|-------------|
| Unregistered | Provider not known to the bridge |
| Registered | Provider's tools are in the catalog |
| Watching | Background loop awaiting surface change signal |

### Tool Visibility Lifecycle

```
Discovered ──► Cataloged ──► Visible (on session)
    │              │              │
    │              │              │ (session reap)
    │              │              ▼
    │              │          Orphaned
    │              │          (handler throws on invoke)
    │              │
    │              │ (provider unregistered)
    │              ▼
    │          Removed (from catalog)
    │
    │ (validation fail)
    ▼
  Rejected (not cataloged)
```

| State | Description |
|-------|-------------|
| Discovered | Provider returned descriptor from `DiscoverAsync` |
| Cataloged | Descriptor passed validation and added to catalog index |
| Visible | Tool's `AIFunction` is in a session's `Tools` list |
| Orphaned | Session retains handler but provider has been unregistered; invoke throws `ObjectDisposedException` |
| Removed | Descriptor removed from catalog; no new sessions will include it |
| Rejected | Descriptor failed validation (empty name, tier collision) |

## Data Flow

### Registration

```
HostedService     IToolRegistrar        IToolProvider         Catalog (internal)
     │                 │                      │                     │
     │ RegisterProvider │                      │                     │
     │────────────────►│                      │                     │
     │                 │  DiscoverAsync()      │                     │
     │                 │─────────────────────►│                     │
     │                 │  [ToolDescriptor[]]   │                     │
     │                 │◄─────────────────────│                     │
     │                 │                      │                     │
     │                 │──── validate names (non-empty, unique)     │
     │                 │──── enforce tier priority                  │
     │                 │──── set status = Ready                     │
     │                 │                      │                     │
     │                 │          index descriptors                 │
     │                 │─────────────────────────────────────────► │
     │                 │                      │                     │
     │                 │──── start WaitForSurfaceChangeAsync loop   │
     │                 │                      │                     │
     │    completed    │                      │                     │
     │◄────────────────│                      │                     │
```

### Expansion

```
Agent          expand_tools          IToolCatalog        IGatewayClient
  │                 │                      │                    │
  │ names: ["x"]    │                      │                    │
  │────────────────►│                      │                    │
  │                 │ GetToolsByName(["x"])│                    │
  │                 │─────────────────────►│                    │
  │                 │ [AIFunction]         │                    │
  │                 │◄─────────────────────│                    │
  │                 │                      │                    │
  │                 │── append to mutable tool list             │
  │                 │                      │                    │
  │                 │ ResumeSessionAsync(  │                    │
  │                 │   Tools = list)      │                    │
  │                 │─────────────────────────────────────────►│
  │                 │                      │           session  │
  │                 │◄─────────────────────────────────────────│
  │                 │                      │                    │
  │ { enabled: ["x"], count: 1 }          │                    │
  │◄────────────────│                      │                    │
```

## Validation Summary

| Entity | Rule | Error |
|--------|------|-------|
| ToolDescriptor | `Function.Name` non-null and non-empty | `ArgumentException` |
| ToolDescriptor | `ProviderName` non-null and non-empty | `ArgumentException` |
| Registration | Same-tier name collision | `InvalidOperationException` |
| Registration | Cross-tier collision | Higher tier wins; lower silently skipped |
| expand_tools | Session not yet bound | Returns error result (not exception) |
| expand_tools | Unknown tool name | Silently skipped; result notes skipped names |
| Teardown | Invoke after provider disposed | `ObjectDisposedException` |
