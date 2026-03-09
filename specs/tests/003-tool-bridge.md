---
target:
  - src/MsClaw.Gateway/Services/Tools/IToolProvider.cs
  - src/MsClaw.Gateway/Services/Tools/IToolCatalog.cs
  - src/MsClaw.Gateway/Services/Tools/IToolRegistrar.cs
  - src/MsClaw.Gateway/Services/Tools/IToolExpander.cs
  - src/MsClaw.Gateway/Services/Tools/ToolDescriptor.cs
  - src/MsClaw.Gateway/Services/Tools/ToolBridge.cs
  - src/MsClaw.Gateway/Services/Tools/ToolExpander.cs
  - src/MsClaw.Gateway/Services/AgentMessageService.cs
  - src/MsClaw.Gateway/Extensions/GatewayServiceExtensions.cs
---

# Tool Bridge Spec Tests

Validates the provider abstraction, registry, catalog, expander, and session integration for the Tool Bridge feature.

## Core Abstractions

### IToolProvider defines provider contract

The bridge needs a stable contract so any tool source (MCPorter, cron, bundled mind tools) can plug in identically. Without this interface, each provider would couple directly to the catalog internals.

```
Given the src/MsClaw.Gateway/Services/Tools/IToolProvider.cs file
When examining the IToolProvider interface
Then it extends IAsyncDisposable
And it declares a Name property of type string
And it declares a Tier property of type ToolSourceTier
And it declares a DiscoverAsync method returning Task<IReadOnlyList<ToolDescriptor>>
And it declares a WaitForSurfaceChangeAsync method returning Task with CancellationToken parameter
```

### ToolDescriptor wraps AIFunction with catalog metadata

Tool identity and schema must come from the AIFunction itself, not be duplicated. The descriptor adds only catalog-level metadata (provider, tier, visibility) so there's one source of truth.

```
Given the src/MsClaw.Gateway/Services/Tools/ToolDescriptor.cs file
When examining the ToolDescriptor type
Then it is a sealed record
And it has a required AIFunction property named Function
And it has a required string property named ProviderName
And it has a required ToolSourceTier property named Tier
And it has a bool property named AlwaysVisible with default false
```

### ToolSourceTier defines three-tier priority

The spec requires Bundled > Workspace > Managed priority ordering. Without an enum, collision resolution would depend on ad-hoc string comparisons.

```
Given the src/MsClaw.Gateway/Services/Tools/ToolDescriptor.cs file
When examining the ToolSourceTier enum
Then it defines values Bundled, Workspace, and Managed
And Bundled has the highest priority (lowest ordinal value)
```

### ToolStatus tracks operational readiness

Tools may be registered but not invocable (missing binary, no matching node). Without status tracking, degraded tools would silently fail at invocation time instead of being filtered from catalog queries.

```
Given the src/MsClaw.Gateway/Services/Tools/ToolDescriptor.cs file
When examining the ToolStatus enum
Then it defines values Ready, Degraded, and Unavailable
```

## Catalog (Read-Side)

### IToolCatalog exposes read-only catalog operations

Session creation and expansion need to query the catalog without mutating it. Separating read from write prevents session code from accidentally registering or removing tools.

```
Given the src/MsClaw.Gateway/Services/Tools/IToolCatalog.cs file
When examining the IToolCatalog interface
Then it declares GetDefaultTools returning IReadOnlyList<AIFunction>
And it declares GetToolsByName accepting IEnumerable<string> and returning IReadOnlyList<AIFunction>
And it declares GetCatalogToolNames returning IReadOnlyList<string>
And it declares GetToolNamesByProvider accepting string and returning IReadOnlyList<string>
And it declares SearchTools accepting string and returning IReadOnlyList<string>
And it declares GetDescriptor accepting string and returning nullable ToolDescriptor
```

## Registrar (Write-Side)

### IToolRegistrar exposes write-only registry operations

Provider registration, unregistration, and refresh are hosting concerns that should not leak into session or message code. The write interface ensures only the hosting layer mutates the catalog.

```
Given the src/MsClaw.Gateway/Services/Tools/IToolRegistrar.cs file
When examining the IToolRegistrar interface
Then it declares RegisterProviderAsync accepting IToolProvider and CancellationToken
And it declares UnregisterProviderAsync accepting string providerName and CancellationToken
And it declares RefreshProviderAsync accepting string providerName and CancellationToken
```

## Expander (Session-Aware)

### IToolExpander creates per-session expand_tools

Each session needs its own expand_tools function with its own tool list and session reference captured in a closure. A shared function would mix tool sets across sessions.

```
Given the src/MsClaw.Gateway/Services/Tools/IToolExpander.cs file
When examining the IToolExpander interface
Then it declares CreateExpandToolsFunction that returns AIFunction
And the method accepts a session holder parameter and a mutable tool list parameter
```

## ToolBridge Implementation

### ToolBridge implements both IToolCatalog and IToolRegistrar

A single class implementing both interfaces keeps the catalog and registrar sharing the same data structure. This avoids synchronization issues between separate catalog and registrar instances.

```
Given the src/MsClaw.Gateway/Services/Tools/ToolBridge.cs file
When examining the ToolBridge class
Then it implements IToolCatalog
And it implements IToolRegistrar
And it is a sealed class
```

### ToolBridge uses ConcurrentDictionary for thread-safe catalog

Multiple providers may register concurrently during startup, and the catalog is read from session creation threads. Without thread-safe storage, race conditions could corrupt the tool index.

```
Given the src/MsClaw.Gateway/Services/Tools/ToolBridge.cs file
When examining the ToolBridge class fields or constructor
Then it uses ConcurrentDictionary for tool storage
```

### ToolBridge enforces same-tier collision as hard error

If two providers in the same tier expose a tool with the same name, it's a configuration error. Silent skip would make DI registration order matter — a fragile implicit coupling.

```
Given the src/MsClaw.Gateway/Services/Tools/ToolBridge.cs file
When examining the RegisterProviderAsync method
Then it throws InvalidOperationException when two providers at the same tier register a tool with the same name
```

### ToolBridge resolves cross-tier collisions by priority

When a Bundled and Workspace provider both offer a tool named "read_memory", the Bundled version must win. This prevents workspace tools from shadowing safety-critical bundled tools.

```
Given the src/MsClaw.Gateway/Services/Tools/ToolBridge.cs file
When examining the registration logic
Then a higher-tier tool takes precedence over a lower-tier tool with the same name
And the lower-tier tool is not added to the catalog
```

### GetDefaultTools returns only AlwaysVisible Ready tools

Sessions should start with a minimal tool set. Including non-AlwaysVisible or non-Ready tools would bloat session payloads and expose degraded capabilities.

```
Given the src/MsClaw.Gateway/Services/Tools/ToolBridge.cs file
When examining the GetDefaultTools method
Then it returns AIFunction instances only for tools where AlwaysVisible is true
And it returns only tools with Ready status
```

## ToolExpander Implementation

### ToolExpander creates expand_tools with load and query modes

The agent needs both capabilities in one tool: loading specific tools onto the session (load mode) and discovering what tools exist (query mode). Two separate tools would double the base tool surface.

```
Given the src/MsClaw.Gateway/Services/Tools/ToolExpander.cs file
When examining the CreateExpandToolsFunction method or the AIFunction it creates
Then the created function supports a names parameter for load mode
And the created function supports a query parameter for query mode
```

### ToolExpander uses ResumeSessionAsync for lazy registration

Adding tools to an existing session requires re-sending the full tool list via ResumeSessionAsync. The expander must call this with the combined current + new tool list.

```
Given the src/MsClaw.Gateway/Services/Tools/ToolExpander.cs file
When examining the expand_tools load mode logic
Then it calls ResumeSessionAsync on the gateway client with the updated tool list
```

## Session Integration

### AgentMessageService populates SessionConfig.Tools

Without tools in the session config, the agent has no custom capabilities. The service must inject default tools + expand_tools from the catalog and expander.

```
Given the src/MsClaw.Gateway/Services/AgentMessageService.cs file
When examining the GetOrCreateSessionAsync method or session creation logic
Then SessionConfig.Tools is populated with tools from IToolCatalog.GetDefaultTools
And SessionConfig.Tools includes the expand_tools function from IToolExpander
```

### SessionConfig does not set AvailableTools or ExcludedTools

Setting AvailableTools creates a whitelist that hides the CLI's built-in tools (file editing, terminal, search). This would break core agent functionality and require enumerating every CLI built-in.

```
Given the src/MsClaw.Gateway/Services/AgentMessageService.cs file
When examining the session creation logic
Then AvailableTools is not set on SessionConfig
And ExcludedTools is not set on SessionConfig
```

## DI Registration

### Tool bridge services registered in GatewayServiceExtensions

All gateway services use singleton lifetime and register through the central extension method. The tool bridge must follow the same pattern for consistency and discoverability.

```
Given the src/MsClaw.Gateway/Extensions/GatewayServiceExtensions.cs file
When examining the AddGatewayServices method
Then IToolCatalog is registered as a singleton
And IToolRegistrar is registered as a singleton resolving to the same ToolBridge instance as IToolCatalog
And IToolExpander is registered as a singleton
```
