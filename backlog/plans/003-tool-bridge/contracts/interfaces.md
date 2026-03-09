# Tool Bridge Interface Contracts

## Value Types

### ToolSourceTier

```csharp
public enum ToolSourceTier { Bundled, Workspace, Managed }
```

### ToolStatus

```csharp
public enum ToolStatus { Ready, Degraded, Unavailable }
```

### ToolDescriptor

```csharp
/// <summary>Registry metadata wrapping an AIFunction.
/// Name, description, and parameter schema come from Function.
/// Immutable — operational status tracked separately.</summary>
public sealed record ToolDescriptor
{
    /// <summary>The SDK tool instance. Passed directly into SessionConfig.Tools.</summary>
    public required AIFunction Function { get; init; }

    /// <summary>Provider that owns this tool.</summary>
    public required string ProviderName { get; init; }

    /// <summary>Tier inherited from the owning provider.</summary>
    public required ToolSourceTier Tier { get; init; }

    /// <summary>When true, included on every new session without expand_tools.</summary>
    public bool AlwaysVisible { get; init; }
}
```

## Provider Contract

### IToolProvider

```csharp
/// <summary>Contract for any tool source (MCPorter, cron, bundled, MCP servers).
/// Implementers discover tools, build AIFunction handlers, and signal surface changes.</summary>
public interface IToolProvider : IAsyncDisposable
{
    /// <summary>Unique provider name (e.g., "mcporter", "cron", "bundled").</summary>
    string Name { get; }

    /// <summary>Sourcing tier for collision resolution.</summary>
    ToolSourceTier Tier { get; }

    /// <summary>Discover tools and build AIFunction handlers.
    /// Called at registration and on refresh.</summary>
    Task<IReadOnlyList<ToolDescriptor>> DiscoverAsync(CancellationToken cancellationToken);

    /// <summary>Awaitable signal that tool surface may have changed.
    /// Returns on change or cancellation. Providers that never change
    /// should await Task.Delay(Timeout.Infinite, ct).</summary>
    Task WaitForSurfaceChangeAsync(CancellationToken cancellationToken);
}
```

## Catalog Contract (Read-Side)

### IToolCatalog

```csharp
/// <summary>Read-side tool catalog. Consumed by session factory and expander.</summary>
public interface IToolCatalog
{
    /// <summary>Tools registered on every new session (AlwaysVisible = true, status = Ready).
    /// Does NOT include expand_tools.</summary>
    IReadOnlyList<AIFunction> GetDefaultTools();

    /// <summary>Fetch specific tools by name for lazy registration.
    /// Returns only Ready tools. Unknown names silently skipped.</summary>
    IReadOnlyList<AIFunction> GetToolsByName(IEnumerable<string> names);

    /// <summary>All tool names in the catalog.</summary>
    IReadOnlyList<string> GetCatalogToolNames();

    /// <summary>Tool names belonging to a specific provider.</summary>
    IReadOnlyList<string> GetToolNamesByProvider(string providerName);

    /// <summary>Keyword search against tool names and descriptions.</summary>
    IReadOnlyList<string> SearchTools(string query);

    /// <summary>Full descriptor by tool name, or null if not found.</summary>
    ToolDescriptor? GetDescriptor(string toolName);
}
```

## Registrar Contract (Write-Side)

### IToolRegistrar

```csharp
/// <summary>Write-side tool registrar. Consumed by hosting layer.</summary>
public interface IToolRegistrar
{
    /// <summary>Register a provider. Discovery runs immediately.
    /// Same-tier name collisions throw InvalidOperationException.</summary>
    Task RegisterProviderAsync(IToolProvider provider, CancellationToken cancellationToken);

    /// <summary>Unregister a provider and remove its tools.
    /// Active sessions retain handlers (throw ObjectDisposedException if invoked).</summary>
    Task UnregisterProviderAsync(string providerName, CancellationToken cancellationToken);

    /// <summary>Re-discover tools from a specific provider.</summary>
    Task RefreshProviderAsync(string providerName, CancellationToken cancellationToken);
}
```

## Expander Contract (Session-Aware)

### IToolExpander

```csharp
/// <summary>Creates per-session expand_tools AIFunction instances.</summary>
public interface IToolExpander
{
    /// <summary>Creates expand_tools bound to a session via deferred holder.
    /// Two modes:
    /// - Load: names param → fetch from catalog, ResumeSessionAsync
    /// - Query: query param → search catalog, return matches
    /// </summary>
    AIFunction CreateExpandToolsFunction(
        SessionHolder sessionHolder,
        IList<AIFunction> currentSessionTools);
}
```

### SessionHolder

```csharp
/// <summary>Thread-safe deferred session binding for expand_tools closure.
/// Uses TaskCompletionSource to eliminate race conditions between session
/// creation and expand_tools invocation.</summary>
public sealed class SessionHolder
{
    private readonly TaskCompletionSource<IGatewaySession> _tcs = new();

    /// <summary>Bind the session after CreateSessionAsync completes.
    /// Unblocks any expand_tools invocations awaiting the session.</summary>
    public void Bind(IGatewaySession session) => _tcs.SetResult(session);

    /// <summary>Await the session. Returns immediately if already bound,
    /// otherwise blocks until Bind is called.</summary>
    public Task<IGatewaySession> GetSessionAsync() => _tcs.Task;
}
```
