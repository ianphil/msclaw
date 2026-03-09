# Tools Developer Guide

MsClaw agents start with a baseline set of capabilities, but the real power comes from **tool providers** — pluggable modules that extend what the agent can do at runtime. This guide covers everything you need to build one.

## Architecture Overview

The tool bridge separates concerns across four interfaces:

| Interface | Role | Owner |
|-----------|------|-------|
| `IToolProvider` | Discovers tools from a source (filesystem, MCP server, etc.) | You implement this |
| `IToolCatalog` | Read-only queries against registered tools | `ToolBridge` |
| `IToolRegistrar` | Write operations — register, unregister, refresh providers | `ToolBridge` |
| `IToolExpander` | Per-session `expand_tools` function factory | `ToolExpander` |

`ToolBridge` is the central orchestrator — it implements both `IToolCatalog` and `IToolRegistrar`, backed by a thread-safe `ToolCatalogStore`. The `ToolBridgeHostedService` manages provider lifecycle (registration at startup, watch loops for hot-reload, cleanup at shutdown).

```
Startup                          Runtime
───────                          ───────
ToolBridgeHostedService          Agent conversation
  │                                │
  ├─ RegisterProviderAsync()       ├─ Session created with default tools + expand_tools
  │   └─ provider.DiscoverAsync()  │
  │       └─ Index in catalog      ├─ Agent calls expand_tools(names: ["my_provider"])
  │                                │   └─ Catalog lookup → append to session → resume
  └─ WatchProviderAsync() loop     │
      └─ provider.WaitFor…()       └─ Next message syncs new tools into session
          └─ RefreshProviderAsync()
```

## Core Concepts

### ToolDescriptor

Every tool in the catalog is wrapped in a `ToolDescriptor` — an immutable record that pairs the SDK's `AIFunction` with catalog metadata:

```csharp
public sealed record ToolDescriptor
{
    public required AIFunction Function { get; init; }
    public required string ProviderName { get; init; }
    public required ToolSourceTier Tier { get; init; }
    public bool AlwaysVisible { get; init; }
}
```

- **`Function`** — The `AIFunction` from `Microsoft.Extensions.AI`. Carries the tool's name, description, parameter schema, and implementation.
- **`ProviderName`** — Which provider owns this tool. Used for filtering and refresh operations.
- **`Tier`** — Priority level for collision resolution (see below).
- **`AlwaysVisible`** — If `true`, the tool is included in every new session automatically. If `false`, the agent must discover and load it via `expand_tools`.

### Source Tiers

```csharp
public enum ToolSourceTier
{
    Bundled,    // Highest priority — shipped with gateway
    Workspace,  // Mid priority — defined by mind or local config
    Managed     // Lowest priority — external/third-party
}
```

Tiers determine what happens when two providers offer a tool with the same name:
- **Different tiers:** Higher tier wins, lower is skipped, warning logged.
- **Same tier:** Hard error. Same-tier collisions are bugs — fix them.

### Tool Status

```csharp
public enum ToolStatus
{
    Ready,       // All requirements met
    Degraded,    // Missing optional prerequisites
    Unavailable  // Missing required capability (e.g., no device node)
}
```

Only tools with `Ready` status are served to sessions.

## Creating a Tool Provider

### Step 1: Implement `IToolProvider`

Here's the full contract:

```csharp
public interface IToolProvider : IAsyncDisposable
{
    string Name { get; }
    ToolSourceTier Tier { get; }
    Task<IReadOnlyList<ToolDescriptor>> DiscoverAsync(CancellationToken cancellationToken);
    Task WaitForSurfaceChangeAsync(CancellationToken cancellationToken);
}
```

And here's a real working example — `EchoToolProvider`, the simplest possible provider:

```csharp
using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace MsClaw.Gateway.Services.Tools;

public sealed class EchoToolProvider : IToolProvider
{
    public string Name => "echo";

    public ToolSourceTier Tier => ToolSourceTier.Workspace;

    public Task<IReadOnlyList<ToolDescriptor>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var fn = AIFunctionFactory.Create(
            ([Description("The text to echo back")] string text) => $"Echo: {text}",
            "echo_text",
            "Echoes the input text back to the caller. Useful for verifying tool bridge wiring.");

        ToolDescriptor descriptor = new()
        {
            Function = fn,
            ProviderName = Name,
            Tier = Tier,
            AlwaysVisible = false
        };

        return Task.FromResult<IReadOnlyList<ToolDescriptor>>([descriptor]);
    }

    public Task WaitForSurfaceChangeAsync(CancellationToken cancellationToken)
    {
        return Task.Delay(Timeout.Infinite, cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

Key decisions to make for your provider:

| Decision | Options | Guidance |
|----------|---------|----------|
| **Name** | Any unique string | Lowercase, no spaces. The agent uses this to load all tools from your provider via `expand_tools(names: ["your_name"])`. |
| **Tier** | `Bundled`, `Workspace`, `Managed` | Use `Workspace` for mind-local providers. `Bundled` for gateway-shipped. `Managed` for third-party integrations. |
| **AlwaysVisible** | `true` / `false` | Set `true` for tools the agent always needs. Set `false` for tools loaded on demand — keeps initial session payload small. |
| **Hot-reload** | Implement `WaitForSurfaceChangeAsync` or block forever | Block forever (`Task.Delay(Timeout.Infinite, ct)`) if your tools are static. Return from the method to trigger a catalog refresh if tools change. |

### Step 2: Register in DI

Add your provider to the service collection in `GatewayServiceExtensions.cs`:

```csharp
services.AddSingleton<IToolProvider, MyToolProvider>();
```

That's it. The `ToolBridgeHostedService` discovers all `IToolProvider` registrations via DI and handles the rest — registration, watch loops, cleanup.

The existing wiring looks like this:

```csharp
// Tool bridge infrastructure (already registered — don't duplicate)
services.AddSingleton<ToolBridge>();
services.AddSingleton<IToolCatalog>(sp => sp.GetRequiredService<ToolBridge>());
services.AddSingleton<IToolRegistrar>(sp => sp.GetRequiredService<ToolBridge>());
services.AddSingleton<IToolExpander, ToolExpander>();
services.AddHostedService(sp => sp.GetRequiredService<ToolBridgeHostedService>());

// Tool providers — add yours here
services.AddSingleton<IToolProvider, EchoToolProvider>();
services.AddSingleton<IToolProvider, MyToolProvider>();  // ← your provider
```

### Step 3: Choose Visibility Strategy

**Always-visible tools** are loaded into every new session automatically:

```csharp
AlwaysVisible = true  // Agent sees this tool immediately
```

**On-demand tools** require the agent to discover and load them via `expand_tools`:

```csharp
AlwaysVisible = false  // Agent must call expand_tools to access
```

The agent discovers on-demand tools in two ways:

1. **Search mode** — `expand_tools(query: "teams")` → returns matching tool names
2. **Load mode** — `expand_tools(names: ["my_provider"])` → enables all tools from that provider

On-demand is preferred for most providers. It keeps the initial session payload small (~50KB vs ~120KB+ with everything loaded) and lets the agent pull in tools only when relevant.

## How Sessions Wire Tools

Understanding the session lifecycle helps when debugging tool issues:

### 1. Session Creation

When a new caller connects, `AgentMessageService.GetOrCreateSessionAsync()`:

1. Calls `IToolCatalog.GetDefaultTools()` — returns `AIFunction` objects for all `AlwaysVisible=true`, `Ready` tools
2. Creates a `SessionHolder` (deferred binding — the session doesn't exist yet)
3. Calls `IToolExpander.CreateExpandToolsFunction(sessionHolder, toolList)` — creates the per-session `expand_tools` function
4. Adds `expand_tools` to the tool list
5. Creates the SDK session with all tools
6. Binds the session to the `SessionHolder` — any `expand_tools` calls that were waiting now unblock

### 2. Tool Expansion (mid-conversation)

When the agent calls `expand_tools(names: ["echo"])`:

1. `ToolExpander` resolves the requested names — if a name matches a provider, it expands to all tools from that provider
2. Fetches `AIFunction` objects from the catalog
3. Appends new tools to the session's mutable tool list (skips duplicates)
4. Returns `{ enabled: [...], skipped: [...], count: N, note: "Tools will be callable on the next message..." }`

### 3. Tool Sync (next message)

On the next `SendAsync()` call, `AgentMessageService.SyncToolsIfNeededAsync()`:

1. Detects that the tool list grew (expand_tools mutated it)
2. Calls `ResumeSessionAsync` with the updated tool list
3. Updates the sync counter
4. The agent can now invoke the newly loaded tools

> **Why the one-message delay?** The SDK session must be resumed with the new tool list before the agent can call the tools. This happens at the start of the *next* message, not during the current one.

## Implementing Hot-Reload

If your tools can change at runtime (e.g., an MCP server adds new tools, a filesystem watcher detects new skill files), implement `WaitForSurfaceChangeAsync` to signal changes:

```csharp
public async Task WaitForSurfaceChangeAsync(CancellationToken cancellationToken)
{
    // Block until something changes, then return.
    // The ToolBridgeHostedService will call RefreshProviderAsync(),
    // which re-runs DiscoverAsync() and updates the catalog.

    await _fileWatcher.WaitForChangeAsync(cancellationToken);

    // Returning from this method = "my tools may have changed, please refresh"
    // The watch loop will call this method again immediately after refreshing.
}
```

The `ToolBridgeHostedService` runs a per-provider watch loop:

```
∞ loop:
  await provider.WaitForSurfaceChangeAsync()   ← blocks until change
  await toolRegistrar.RefreshProviderAsync()    ← re-discovers and re-indexes
  on error: log + retry after 1s
```

During refresh, new tools are indexed *before* stale tools are removed — concurrent readers never see an empty catalog window.

For **static providers** (tools don't change), block forever:

```csharp
public Task WaitForSurfaceChangeAsync(CancellationToken cancellationToken)
{
    return Task.Delay(Timeout.Infinite, cancellationToken);
}
```

## Multi-Tool Providers

A single provider can expose multiple tools. Return them all from `DiscoverAsync`:

```csharp
public Task<IReadOnlyList<ToolDescriptor>> DiscoverAsync(CancellationToken cancellationToken)
{
    var tools = new List<ToolDescriptor>
    {
        new()
        {
            Function = AIFunctionFactory.Create(
                ([Description("Channel name")] string channel) => SendToChannel(channel),
                "teams_send",
                "Send a message to a Teams channel"),
            ProviderName = Name,
            Tier = Tier,
            AlwaysVisible = false
        },
        new()
        {
            Function = AIFunctionFactory.Create(
                ([Description("Channel name")] string channel,
                 [Description("Max messages")] int count) => ReadChannel(channel, count),
                "teams_read",
                "Read recent messages from a Teams channel"),
            ProviderName = Name,
            Tier = Tier,
            AlwaysVisible = false
        }
    };

    return Task.FromResult<IReadOnlyList<ToolDescriptor>>(tools);
}
```

When the agent calls `expand_tools(names: ["teams"])`, *all* tools from the `teams` provider are loaded together.

## Collision Resolution

When two providers offer a tool with the same name:

| Scenario | Result |
|----------|--------|
| Bundled vs Workspace | Bundled wins |
| Bundled vs Managed | Bundled wins |
| Workspace vs Managed | Workspace wins |
| Same tier vs Same tier | **Hard error** — startup fails |

This is intentional. Same-tier collisions mean two providers are fighting over the same name — that's a configuration bug, not something to silently resolve.

## Testing Your Provider

The test pattern follows the existing `EchoToolProvider` tests:

```csharp
[Fact]
public async Task DiscoverAsync_Returns_Expected_Tools()
{
    var provider = new MyToolProvider();
    var tools = await provider.DiscoverAsync(CancellationToken.None);

    Assert.Single(tools);  // or Assert.Equal(expectedCount, tools.Count)
    Assert.Equal("my_tool_name", tools[0].Function.Name);
    Assert.Equal("my_provider", tools[0].ProviderName);
    Assert.Equal(ToolSourceTier.Workspace, tools[0].Tier);
}
```

For integration testing with the full bridge:

```csharp
[Fact]
public async Task Provider_Tools_Appear_In_Catalog()
{
    var bridge = new ToolBridge(NullLogger<ToolBridge>.Instance);
    var provider = new MyToolProvider();

    await bridge.RegisterProviderAsync(provider, CancellationToken.None);

    var names = bridge.GetToolNamesByProvider("my_provider");
    Assert.Contains("my_tool_name", names);
}
```

## Quick Reference

| I want to... | Do this |
|--------------|---------|
| Create a new tool provider | Implement `IToolProvider`, register as `services.AddSingleton<IToolProvider, MyProvider>()` |
| Make a tool always available | Set `AlwaysVisible = true` on the `ToolDescriptor` |
| Make a tool load-on-demand | Set `AlwaysVisible = false` — agent uses `expand_tools` to load it |
| Support hot-reload | Return from `WaitForSurfaceChangeAsync` when tools change |
| Expose multiple tools | Return multiple `ToolDescriptor` objects from `DiscoverAsync` |
| Prevent name collisions | Use a provider-specific prefix (e.g., `teams_send`, `teams_read`) |
| Test the provider in isolation | Call `DiscoverAsync` directly and assert on returned descriptors |
| Test with the full bridge | Create a `ToolBridge`, register your provider, query the catalog |

## File Map

```
src/MsClaw.Gateway/Services/Tools/
├── IToolProvider.cs           ← Interface you implement
├── IToolCatalog.cs            ← Read-only catalog queries
├── IToolRegistrar.cs          ← Write operations (register/unregister/refresh)
├── IToolExpander.cs           ← expand_tools factory + SessionHolder
├── ToolDescriptor.cs          ← Tool metadata record + enums
├── ToolBridge.cs              ← Core orchestrator (implements catalog + registrar)
├── ToolCatalogStore.cs        ← Thread-safe backing store (internal)
├── ToolExpander.cs            ← expand_tools implementation
├── ToolBridgeHostedService.cs ← Lifecycle: startup registration + watch loops
└── EchoToolProvider.cs        ← Reference implementation
```
