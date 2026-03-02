# Phase 2: Extension System — Specification

## Problem

MsClaw's capabilities are hardwired in DI. Adding new tools, hooks, or services means modifying MsClaw source. There's no plugin API, no discovery mechanism, no lifecycle management. The system isn't composable.

## Goal

A modular extension system where capabilities register through a plugin API. Extensions come in two tiers — core extensions that ship with MsClaw and external extensions loaded dynamically from manifested directories.

---

## Requirements

### Extension Contract

- Every extension implements a single interface: `IExtension`.
- Lifecycle: `Register()` → `StartAsync()` → `StopAsync()` → `DisposeAsync()`.
- Extensions receive dependencies via constructor injection from the app's `IServiceProvider`.
- Extensions declare capabilities during `Register()` by calling methods on the plugin API.

### Plugin API Capabilities

Extensions can register five types of capabilities:

| Capability | Purpose |
|------------|---------|
| **Tools** | Agent-callable functions wired into SDK `SessionConfig.Tools` |
| **Hooks** | Lifecycle event handlers fired on session, message, and system events |
| **Services** | Long-running background services (`IHostedService` semantics) |
| **Commands** | Direct commands that bypass the LLM (e.g., `/status`) |
| **HTTP Routes** | Additional endpoints on the Kestrel server |

### Hook Events

The runtime fires hooks on these lifecycle events:

| Event | Trigger |
|-------|---------|
| `session:create` | New SDK session created |
| `session:resume` | Existing session resumed |
| `session:end` | Session destroyed |
| `message:received` | Inbound message from any channel |
| `message:sent` | Outbound response ready |
| `agent:bootstrap` | Bootstrap complete, before first session |
| `extension:loaded` | All extensions finished registration |

Hooks are fire-and-await. A throwing hook logs an error but does not block other hooks or crash the runtime.

### Extension Manifest

Every extension has a `plugin.json` manifest containing: unique ID, name, SemVer version, entry assembly, entry type, optional dependencies (other extension IDs with SemVer range constraints), and optional config.

The `config` field is free-form. The runtime passes it through as-is; validation is the extension's responsibility.

A single assembly may contain multiple extensions via separate manifests.

### Two-Tier Loading

| Tier | Discovery | Criticality |
|------|-----------|-------------|
| **Core** | Project references in the MsClaw solution, statically registered | Always present, ships with the tool |
| **External** | Dynamically loaded from `extensions/` directories via manifest + `AssemblyLoadContext` | User-installable, removable, updatable |

`AssemblyLoadContext` provides isolation for unloading during warm reload. No additional sandboxing or permission model is in scope for Phase 2.

Both tiers implement the same contract. Core extensions load first.

Extensions can depend on services registered by other extensions. The dependency graph declared in `plugin.json` determines load order. Circular dependencies are an error.

### Discovery Locations

1. `{appRoot}/extensions/` — core extensions, deployed with MsClaw
2. `{mindRoot}/extensions/` — user-installed extensions, per-mind

Same ID in both locations: mind-root wins (override semantics).

The mind-root extensions directory is scaffolded by `--new-mind` and gitignored (binary artifacts, not source).

### Load Order

1. Core extensions register first (static, always available).
2. External extensions are scanned, validated, topologically sorted by dependencies, then loaded and registered in dependency order. Dependencies declare SemVer ranges; the runtime enforces compatibility using range matching (NuGet semantics).
3. All collected tools are wired into `SessionConfig`.
4. `StartAsync()` is called on all extensions (core first).
5. `agent:bootstrap` hook fires.

### Enable/Disable

Extensions are enabled by default. Individual extensions can be disabled via MsClaw config by ID.

### Error Isolation

The runtime never crashes due to a broken extension:

- Invalid manifest → skip, log warning
- Missing dependency → skip extension and its dependents, log error
- Constructor failure → skip, log error
- `Register()` throws → skip, log error
- `StartAsync()` throws → mark failed, continue others

### Warm Reload

External extensions can be reloaded without restarting the process. The `CopilotClient` singleton stays alive; sessions are cycled.

- Core extensions are NOT reloaded (require app restart).
- In-flight messages are lost; callers must retry.
- Session history persists, but active sessions are destroyed and recreated.
- Bootstrap does NOT re-run.

Triggered by the `/reload` command.

### Distribution

Extensions are distributed as NuGet packages (`MsClaw.Extensions.<Name>` convention).

CLI commands for extension management: `install`, `uninstall`, `list`, `update`, `restore`.

A lockfile (`extensions.lock.json`) in the mind root tracks installed extensions and is committed to git. `msclaw extension restore` reinstalls exact versions from the lockfile.

