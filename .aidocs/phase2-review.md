# Phase 2: Extension System — Code Review

*2026-03-02 · Reviewed against `extension-spec.md`*

---

## ✅ What's Solid

1. **Spec compliance is excellent.** All 5 capability types (tools, hooks, services, commands, HTTP routes), all 7 hook events, two-tier discovery, plugin.json manifest, SemVer dependency ordering, enable/disable via config, error isolation at every failure point, and warm reload — all present and implemented correctly.

2. **Error isolation is thorough.** Every spec-required failure mode (invalid manifest → skip, missing dep → skip dependents, constructor failure → skip, Register() throws → skip, StartAsync() throws → mark failed) is implemented with appropriate logging. One misbehaving extension cannot crash the runtime.

3. **Thread safety looks correct.** `_reloadLock` serializes init/reload/shutdown. `_stateLock` protects shared state mutations. `FireHookAsync` snapshots handlers under lock then iterates outside it — clean pattern.

4. **`AssemblyLoadContext` usage is proper.** Collectible contexts with `isCollectible: true`, unloaded on failure or during reload. `AssemblyDependencyResolver` for transitive deps.

5. **Core extensions (MindReaderExtension, RuntimeControlExtension) prove the API.** They use the same contract as external extensions — good dogfooding.

6. **Test coverage** — 7 tests covering init, idempotency, command dispatch, reload, shutdown, and session cycling. All 64 tests pass (including pre-existing tests).

---

## 🔴 Bugs

### 1. `CycleSessionsAsync` race between Clear and hook firing

**File:** `CopilotRuntimeClient.cs:104-120`

`_sessions.Clear()` runs first, then hooks fire. Any in-flight `SendMessageAsync` that calls `GetOrResumeSessionAsync` between the `Clear()` and hook firing could re-add a session to the dictionary. The `session:end` hooks fire for the old ID, but the zombie session persists.

**Fix:** Snapshot keys, fire hooks, *then* clear — or hold a lock around the entire cycle operation.

### 2. Topo sort rejects valid extensions whose deps were resolved in prior passes

**File:** `ExtensionManager.cs:479-530` (`HasPermanentDependencyFailure`)

The failure-detection pass checks `pending.ContainsKey(depId) || coreIds.Contains(depId)` but doesn't check *already-resolved* extensions in the result list. If extension A depends on extension B and B was resolved in a prior topo-sort pass (moved from `pending` into `result`), the next pass sees B as missing from both `pending` and `coreIds`, and incorrectly flags A as permanently failed.

The separate `ready` check on line 431 does include `result.Select(r => r.Id)`, but `HasPermanentDependencyFailure` runs first and prematurely removes the candidate.

**Fix:** Pass the `result` list (or a set of resolved IDs) into `HasPermanentDependencyFailure` and include it in the "known dependency" check.

---

## 🟡 Issues Worth Fixing

### 3. Warm reload doesn't cycle sessions itself — relies on RuntimeControlExtension

**File:** `ExtensionManager.cs:110-130`

`ReloadExternalAsync` strips and reloads external extensions, but the tools wired into *existing* Copilot sessions are stale (captured at `CreateSessionAsync` time via `SessionConfig.Tools`). The spec says "active sessions are destroyed and recreated" during reload, but that only happens if the caller is the `/reload` command (which calls `CycleSessionsAsync` separately). Programmatic callers of `ReloadExternalAsync` get stale sessions.

**Decision:** Move session cycling into `ReloadExternalAsync` so it owns the full reload contract. Resolve `ISessionControl` lazily from `_services` at call time (avoids circular DI — same pattern RuntimeControlExtension already uses). Order: cycle sessions first → remove old extensions → load new → start new. This eliminates the stale-tools window. RuntimeControlExtension's `/reload` handler then just calls `ReloadExternalAsync` — no separate `CycleSessionsAsync` call.

### ~~4. Resumed sessions don't get updated tools~~ — Resolved by #3

Moot: if `ReloadExternalAsync` cycles all sessions first, no sessions survive to be resumed with stale tools. A client reusing a dead session ID post-reload is already out-of-contract per spec.

### 5. `extensions.lock.json` is scaffolded but never used

**File:** `MindScaffold.cs:31`

The spec's "Distribution" section requires: lockfile tracking installed extensions, committed to git, with CLI commands (`install`, `uninstall`, `list`, `update`, `restore`). The file is scaffolded with empty JSON but the runtime never reads, writes, or validates it. None of the CLI commands exist.

**Decision:** Keep as placeholder. Add roadmap note that extension distribution (lockfile, CLI install/uninstall/list/update/restore) is deferred to a future phase.

### ✅ 6. `MapRoutes` called once at startup — reloaded extension routes don't register

**File:** `Program.cs:122`, `ExtensionManager.cs:110-130`

After `ReloadExternalAsync`, new external extensions' HTTP routes are added to `_httpRoutes`, but `MapRoutes(app)` already ran at startup. ASP.NET Core doesn't support adding routes after the middleware pipeline is built. External extensions that register HTTP routes won't work after reload.

**Decision:** Known ASP.NET limitation — route table is frozen after pipeline build. Tools, hooks, and commands reload fine; only HTTP routes are static after initial load. **DOCUMENTED** in `docs/extension-developer-guide.md` "Part 7: Registering HTTP Routes" section with workaround (use commands/tools instead).

### 7. Core extensions should be in separate files

**File:** `ExtensionManager.cs:946-1019`

`MindReaderExtension` and `RuntimeControlExtension` are appended to the 944-line ExtensionManager file. These are separate concerns.

**Fix:** Move to `Core/Extensions/MindReaderExtension.cs` and `Core/Extensions/RuntimeControlExtension.cs`.

### 8. Walkthrough has a duplicated section

**File:** `docs/msclaw-walkthrough.md:160-174`

The "Extension System: A New Architecture" + "Extension Abstractions" heading block appears twice. Looks like a merge artifact from the Showboat generation.

**Fix:** Remove the duplicate block (lines 160-167).

---

## 🟢 Minor Nits

### 9. Deep manifest scanning may pick up unintended plugin.json files

**File:** `ExtensionManager.cs:306`

`ScanManifestsInto` uses `SearchOption.AllDirectories`. If a mind has nested dirs (e.g. `extensions/foo/node_modules/bar/plugin.json`), it picks up unintended manifests.

**Fix:** Limit scanning to depth 1 (immediate subdirectories of `extensions/`).

### 10. Sync-over-async in shutdown hook

**File:** `Program.cs:67`

```csharp
app.Lifetime.ApplicationStopping.Register(() =>
    extensionManager.ShutdownAsync().GetAwaiter().GetResult());
```

`.GetAwaiter().GetResult()` is a deadlock risk on some hosting environments.

**Fix:** Use `IHostedService` or `IHostApplicationLifetime` with proper async shutdown.

---

## Summary

The implementation faithfully covers the spec's core requirements. Extension contract, discovery, loading, hooks, commands, and error isolation are well-built. The main concerns are:

- **Topo sort bug (#2)** — can reject valid extensions whose dependencies resolved in earlier passes
- **Warm reload stale-session semantics (#3, #4)** — should be addressed or documented
- **Lockfile/CLI commands (#5)** — spec'd but not implemented; needs roadmap callout
- **Duplicated walkthrough section (#8)** — quick fix
