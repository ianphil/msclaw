# Extension Architecture Review

Critical evaluation of the MsClaw extension system for architectural correctness, safety, and long-term stability as a third-party plugin host with runtime reload support.

**Scope:** `ExtensionManager`, `ExtensionLoadContext`, `ExtensionAbstractions`, `RuntimeControlExtension`, `Program.cs`, `CopilotRuntimeClient`, and associated tests.

---

## 1. Assembly Loading & Isolation

### What works

- **Collectible `AssemblyLoadContext`**: `ExtensionLoadContext` correctly uses `isCollectible: true` (line 936, `ExtensionManager.cs`), which is a prerequisite for assembly unloading.
- **`AssemblyDependencyResolver`**: Each plugin context uses `AssemblyDependencyResolver` to resolve plugin-private dependencies, correctly scoped to the plugin's entry assembly.
- **Shared contract fallback**: `ExtensionLoadContext.Load()` returns `null` for assemblies not found by the resolver, causing the CLR to fall back to the default `AssemblyLoadContext`. This is the correct pattern for ensuring shared contracts (`IExtension`, `IMsClawPluginApi`) use the same type identity as the host.
- **Core extensions skip load contexts entirely**: Core extensions like `RuntimeControlExtension` are instantiated directly via `ActivatorUtilities.CreateInstance` in the default context, which is correct.

### Risks

#### MUST FIX: No shadow-copy — file locking blocks in-place updates on Windows

Assemblies are loaded directly from the plugin directory:

```csharp
// ExtensionManager.cs:559
var assembly = loadContext.LoadFromAssemblyPath(descriptor.EntryAssemblyPath);
```

On Windows, `LoadFromAssemblyPath` places a file lock on the DLL. A developer cannot overwrite the DLL while MsClaw is running, making the "edit → reload" workflow impossible without stopping the host. On Linux/macOS the file can be replaced (inode semantics), but the old bytes remain mapped.

**Fix:** Copy the plugin directory to a temp/shadow location before loading. Load from the shadow copy. On reload, delete the old shadow directory after unload and create a new shadow copy from the updated source.

#### MUST FIX: No explicit shared-assembly boundary

The system relies entirely on `AssemblyDependencyResolver` fallback behavior to share types. If a plugin ships copies of host-shared assemblies (e.g., `Microsoft.Extensions.AI.Abstractions.dll`, `Microsoft.Extensions.Hosting.Abstractions.dll`), the resolver **will** find them and load them from the plugin context, breaking type identity:

```csharp
// A plugin's constructor takes ILogger<T> from the host,
// but if the plugin has its own Microsoft.Extensions.Logging.Abstractions.dll,
// the ILogger<T> types won't match.
```

**Fix:** Override `ExtensionLoadContext.Load()` with an explicit allowlist of assemblies that MUST be loaded from the default context:

```csharp
private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
{
    "MsClaw",
    "Microsoft.Extensions.AI.Abstractions",
    "Microsoft.Extensions.Hosting.Abstractions",
    "Microsoft.Extensions.DependencyInjection.Abstractions",
    "Microsoft.Extensions.Logging.Abstractions",
    // etc.
};

protected override Assembly? Load(AssemblyName assemblyName)
{
    if (SharedAssemblies.Contains(assemblyName.Name!))
        return null; // Force default context

    var path = _resolver.ResolveAssemblyToPath(assemblyName);
    return path is null ? null : LoadFromAssemblyPath(path);
}
```

#### NICE TO IMPROVE: No validation that `.deps.json` exists

`AssemblyDependencyResolver` requires a `.deps.json` file alongside the entry assembly to resolve transitive dependencies. If a developer copies just the DLL (as the docs imply with `cp bin/Release/net9.0/MyExtension.dll`), the resolver will silently fail. The system should validate that `<entryAssembly>.deps.json` exists during manifest loading and warn if absent.

---

## 2. Reload Semantics

### What works

- `StopAndDisposeAsync` correctly sequences: stop services (reverse order) → `StopAsync` → `DisposeAsync` → `Unload()`.
- Each step is independently exception-guarded — one failure doesn't block subsequent cleanup.
- `RemoveRegistration` removes all hooks, commands, tools, and routes from the shared dictionaries.
- The `_reloadLock` semaphore serializes `InitializeAsync`, `ShutdownAsync`, and `ReloadExternalAsync`.

### Risks

#### MUST FIX: Use-after-dispose race condition during reload

`FireHookAsync` and `TryExecuteCommandAsync` do **not** acquire `_reloadLock`. They take snapshots under `_stateLock`, but `StopAndDisposeAsync` runs before `RemoveRegistration`:

```
Thread A (reload):  StopAndDisposeAsync(ext) → ext.Instance.DisposeAsync()
Thread B (request): FireHookAsync → snapshot includes ext's hooks → invokes disposed handler
Thread A (reload):  RemoveRegistration(ext) → too late, Thread B already dispatched
```

**Fix options:**
1. Remove registrations **before** calling `StopAndDisposeAsync`, so in-flight requests see the registration disappear atomically.
2. Or wrap `RemoveExternalExtensionsAsync` to first remove all registrations under `_stateLock`, then stop/dispose the instances.

```csharp
private async Task RemoveExternalExtensionsAsync(CancellationToken cancellationToken)
{
    var external = GetLoadedSnapshot().Where(l => l.Tier == ExtensionTier.External).ToList();

    // Phase 1: Remove registrations (atomic, under lock)
    foreach (var extension in external)
        RemoveRegistration(extension);

    // Phase 2: Stop and dispose (no longer reachable by concurrent callers)
    for (var i = external.Count - 1; i >= 0; i--)
        await StopAndDisposeAsync(external[i], cancellationToken);
}
```

#### MUST FIX: Delegate/closure references will prevent AssemblyLoadContext collection

Even after `Unload()` is called, the `AssemblyLoadContext` will not be garbage-collected if any live reference points to a type from that context. The current code has several leak vectors:

1. **`AIFunction` tool instances**: These are lambda delegates created by the plugin. Even after removal from `_tools`, if any `CopilotSession` was created with `Tools = _extensionManager.GetTools().ToArray()` (see `CopilotRuntimeClient.cs:45`), those sessions retain references to the old tool delegates. Sessions are NOT invalidated on reload (only `CycleSessionsAsync` clears `_sessions`, which fires session:end hooks but doesn't forcibly close the Copilot SDK sessions).

2. **`Action<IEndpointRouteBuilder>` route closures**: Even though removed from `_httpRoutes`, any routes already mapped to ASP.NET Core's router (via `MapRoutes()` at startup) retain the original closure. These closures capture plugin-context objects and will root the old `AssemblyLoadContext` for the lifetime of the host process.

3. **Host service references injected via `ActivatorUtilities`**: If the plugin constructor received an `ILogger<PluginType>`, the logger factory may cache the logger instance, keeping a reference to the plugin's `Type` metadata.

**Fix:** For tools/sessions: invalidate all active sessions on reload (already partially done via `CycleSessionsAsync`, but the SDK sessions need true teardown). For routes: accept that HTTP routes prevent full unload, or use a dynamic route dispatch pattern (a single catch-all route that delegates to the current extension registration). For loggers: create a scoped `ILoggerFactory` per plugin.

#### NICE TO IMPROVE: No verification that unload actually completed

After `Unload()`, the runtime does not guarantee immediate collection. In development mode, it would be valuable to hold a `WeakReference` to the `AssemblyLoadContext` and log a warning if it hasn't been collected after a GC cycle:

```csharp
var weakRef = new WeakReference(extension.LoadContext);
extension.LoadContext.Unload();

GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

if (weakRef.IsAlive)
    _logger.LogWarning("AssemblyLoadContext for '{ExtensionId}' was not collected — likely reference leak.", extension.Id);
```

---

## 3. Dependency Boundaries

### What works

- Semantic version range checking via `NuGet.Versioning` is robust.
- Topological sort for dependency load order handles cycles and missing deps correctly.
- Mind-root extensions can override app-root extensions (explicit priority).

### Risks

#### MUST FIX: Host `IServiceProvider` is fully exposed to plugins

```csharp
// ExtensionManager.cs:571
extensionInstance = (IExtension)ActivatorUtilities.CreateInstance(_services, extensionType);
```

This allows any plugin to resolve arbitrary host services via constructor injection — including `IHostApplicationLifetime` (can shut down the host), `IExtensionManager` (can trigger recursive reloads), `IConfiguration` (can read secrets), or any other singleton.

**Fix:** Create a constrained `IServiceProvider` wrapper that only exposes approved services:

```csharp
var pluginServices = new PluginServiceProvider(_services, allowedTypes: new[]
{
    typeof(ILogger<>),
    typeof(ILoggerFactory),
    typeof(IConfiguration),  // if desired
});
extensionInstance = (IExtension)ActivatorUtilities.CreateInstance(pluginServices, extensionType);
```

#### NICE TO IMPROVE: Cross-plugin type sharing is not possible

Each plugin has its own `AssemblyLoadContext`. Two plugins cannot share custom types. The declared `dependencies` in `plugin.json` only affect load order and version validation — they don't provide any mechanism for Plugin A to expose types to Plugin B. This is acceptable for now, but limits composability.

---

## 4. DI and Hosted Services

### Risks

#### MUST FIX: Plugin `IHostedService` instances bypass the ASP.NET Core lifecycle

Plugin-registered services are manually started/stopped:

```csharp
// ExtensionManager.cs:656-661
foreach (var service in loaded.Registration.Services)
{
    await service.StartAsync(cancellationToken);
    loaded.StartedServices.Add(service);
}
```

These services do NOT participate in:
- The host's graceful shutdown sequence (`IHostApplicationLifetime.StopApplication()`)
- The `IHostedService` ordering guarantees
- Unhandled exception handling that the host provides

If the process crashes or is killed, `ShutdownAsync` in `Program.cs:129` won't run, and plugin services won't be stopped.

**Fix:** At minimum, register a top-level `IHostedLifecycleService` in the DI container that delegates to `ExtensionManager.ShutdownAsync` during `StoppingAsync`. This ensures the host's own shutdown pipeline triggers extension cleanup:

```csharp
builder.Services.AddHostedService<ExtensionManagerLifecycle>();

internal sealed class ExtensionManagerLifecycle(IExtensionManager manager) : IHostedService
{
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask; // Already done manually
    public Task StopAsync(CancellationToken ct) => manager.ShutdownAsync(ct);
}
```

#### NICE TO IMPROVE: No per-plugin DI scope

Plugins receive the root `IServiceProvider`. If a plugin resolves `IServiceScopeFactory` and creates scopes, that's fine. But if it resolves scoped services directly from the root provider, those services will never be disposed (the "captive dependency" anti-pattern). Consider providing a pre-scoped provider per plugin.

---

## 5. HTTP Route Registration

### Risks

#### MUST FIX: Routes cannot be removed or re-registered after startup

ASP.NET Core builds an immutable route table at startup. `MapRoutes(app)` is called once in `Program.cs:122`. After that:

1. Reloaded extensions' new routes are stored in `_httpRoutes` but never applied to the router.
2. Old routes from previous extension versions remain active in the router forever.
3. The developer guide (line 441-446) acknowledges this as a known limitation.

This is not just "nice to improve" — it means extension HTTP routes are fundamentally broken for the reload use case.

**Fix:** Replace per-extension route registration with a single dynamic dispatch endpoint:

```csharp
// At startup, register one catch-all:
app.Map("/ext/{extensionId}/{**path}", async (string extensionId, string path, HttpContext ctx) =>
{
    var handler = extensionManager.ResolveRouteHandler(extensionId, path, ctx.Request.Method);
    if (handler is null) return Results.NotFound();
    return await handler(ctx);
});
```

This way, extensions register route handlers into the `ExtensionManager`'s dictionary (which IS reload-safe), not into ASP.NET Core's static route table.

#### MUST FIX: No route collision protection

Two extensions can register the same path (e.g., `/status`). The first one wins silently. There's no prefixing, namespacing, or conflict detection.

**Fix:** Enforce a prefix convention: all extension routes must be under `/ext/{extensionId}/`. Validate this in `RegisterHttpRoute`.

#### NICE TO IMPROVE: No authentication/authorization on extension routes

Extension routes are exposed with the same (lack of) security as host routes. There's no middleware wrapping. A malicious extension could register a route that exposes internal state.

---

## 6. Failure Isolation

### What works

- Hook handlers are individually wrapped in try/catch — one failing hook doesn't block others.
- Command execution is wrapped in try/catch with an error message returned to the user.
- Extension `StartAsync` failures are caught, and the extension is marked `Failed` without stopping other extensions.
- Route mapping failures are caught per-route.

### Risks

#### MUST FIX: No timeouts on plugin calls

All plugin invocations (`FireHookAsync`, `TryExecuteCommandAsync`, `StartAsync`) pass the caller's `CancellationToken` but do not enforce a timeout. A buggy or malicious plugin can hang indefinitely:

```csharp
// A plugin hook that never returns:
async Task OnMessage(ExtensionHookContext ctx, CancellationToken ct)
{
    await Task.Delay(Timeout.Infinite, ct); // blocks the entire hook pipeline
}
```

Since hooks are dispatched sequentially, one hung hook blocks all subsequent hooks AND the caller.

**Fix:** Wrap each plugin call with a timeout:

```csharp
using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
try
{
    await hook.Handler(context, timeoutCts.Token);
}
catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
{
    _logger.LogError("Hook '{HookEvent}' timed out in extension '{ExtensionId}'.", eventName, hook.ExtensionId);
}
```

#### NICE TO IMPROVE: No process-level isolation

A plugin can crash the entire host via `Environment.Exit()`, stack overflow, out-of-memory allocation, or unsafe native code. True isolation would require out-of-process plugins (gRPC/pipes), which is a significant architectural change. Acceptable for trusted plugins, but should be acknowledged as a trust boundary limitation.

#### NICE TO IMPROVE: Background service exceptions after start are unobserved

If a plugin's `IHostedService` throws after `StartAsync` completes (e.g., in `ExecuteAsync` of a `BackgroundService`), the exception hits the thread pool's unobserved task handler. Consider wrapping service start with an exception-observing continuation.

---

## 7. Observability

### What works

- Log messages use structured logging with `{ExtensionId}` for hooks, commands, lifecycle.
- Extension load/start/stop/fail events are all logged.

### Risks

#### NICE TO IMPROVE: No per-plugin metrics or timing

There's no tracking of:
- How long each hook/command takes to execute
- How many times each hook/command is invoked
- Memory pressure per load context
- Whether a load context was successfully collected after unload

**Fix:** Wrap hook/command dispatch with `Stopwatch`-based timing and emit structured log events or metrics.

#### NICE TO IMPROVE: Tool invocations are invisible

`AIFunction` instances are passed directly to the Copilot SDK. The `ExtensionManager` has no visibility into which tools are called, how often, or their latency. Consider wrapping each `AIFunction` in a proxy that logs invocations.

#### NICE TO IMPROVE: No runaway plugin detection

Without timeouts (see Section 6) or resource tracking, there's no way to detect a plugin that's consuming excessive CPU, memory, or hanging. A watchdog or health-check mechanism would help.

---

## 8. Packaging Expectations

### Risks

#### MUST FIX: Documentation/code path mismatch

The developer guide (line 175-185) shows extensions in `.extensions/`:
```
my-mind/
├── .extensions/
│   └── my-extension/
```

But the code scans `extensions/` (no dot prefix):
```csharp
// ExtensionManager.cs:293-294
ScanManifestsInto(descriptorsById, Path.Combine(appRoot, "extensions"), ...);
ScanManifestsInto(descriptorsById, Path.Combine(mindRoot, "extensions"), ...);
```

This will cause every developer following the guide to find their extensions not loading.

**Fix:** Either update the code to scan `.extensions/` or update the documentation. Given that `.extensions/` follows the convention of other dot-prefixed directories in the mind (`.working-memory/`, `.github/`), updating the code is more consistent.

#### NICE TO IMPROVE: Docs imply raw `dotnet build` output is sufficient

The guide shows:
```bash
cp bin/Release/net9.0/MyExtension.dll ~/test-mind/.extensions/my-extension/
```

But `AssemblyDependencyResolver` requires a `.deps.json` file to resolve transitive dependencies. The correct workflow is `dotnet publish`, not `dotnet build` + manual copy. The guide should specify this and the system should warn when `.deps.json` is missing.

#### NICE TO IMPROVE: No manifest schema validation

The manifest validation checks for empty fields but doesn't validate:
- That `id` is a valid identifier (no spaces, special characters)
- That `version` is valid semver
- That `entryType` is a plausible fully-qualified type name

---

## Summary: Priority Matrix

### Must Fix (correctness/safety)

| # | Issue | Impact |
|---|-------|--------|
| 1 | No shadow-copy — file locking on Windows | Blocks reload workflow on Windows |
| 2 | No shared-assembly boundary — type identity breakage | Plugin load failures with transitive deps |
| 3 | Use-after-dispose race in reload | Invoking disposed plugin code |
| 4 | Tool delegates leak into Copilot sessions after reload | AssemblyLoadContext never collected |
| 5 | Route closures permanently root old load contexts | Memory leak on every reload |
| 6 | Host IServiceProvider fully exposed | Plugins can shut down host, read secrets |
| 7 | Plugin services bypass host lifecycle | Services not stopped on crash/kill |
| 8 | No timeouts on plugin invocations | One plugin can hang entire hook pipeline |
| 9 | HTTP routes broken after reload | Known but unfixed; documented as limitation |
| 10 | No route collision protection | Silent conflict between extensions |
| 11 | Doc says `.extensions/`, code reads `extensions/` | Every guide-following dev will fail |

### Nice to Improve (robustness/DX)

| # | Issue | Impact |
|---|-------|--------|
| 12 | No unload verification (WeakReference check) | Silent memory leaks |
| 13 | No per-plugin DI scope | Captive dependency potential |
| 14 | No authentication on extension routes | Security gap for untrusted plugins |
| 15 | No per-plugin metrics/timing | Operational blindness |
| 16 | Tool invocations invisible to host | No observability for AI function calls |
| 17 | No `.deps.json` validation | Confusing load failures |
| 18 | No manifest field validation | Poor error messages |
| 19 | Background service exceptions unobserved | Silent failures |
| 20 | No process-level isolation | Trust boundary limitation |

---

## Recommended Fix Order

1. **Fix the doc/code path mismatch** (#11) — trivial, unblocks developers immediately.
2. **Add shared-assembly allowlist to `ExtensionLoadContext`** (#2) — prevents type identity issues that will manifest as confusing `InvalidCastException`s.
3. **Reorder reload to remove registrations before dispose** (#3) — prevents use-after-dispose.
4. **Add timeouts to all plugin invocations** (#8) — prevents a single plugin from hanging the host.
5. **Implement shadow-copy loading** (#1) — unblocks Windows development.
6. **Implement dynamic route dispatch** (#9, #10) — replaces broken static route registration.
7. **Constrain the service provider** (#6) — reduces trust surface for third-party plugins.
8. **Register `ExtensionManager` in host lifecycle** (#7) — ensures cleanup on unexpected shutdown.
9. **Invalidate sessions on reload** (#4) — prevents tool delegate leaks.
10. **Address route closure leak** (#5) — follows from #9 if dynamic dispatch is implemented.
