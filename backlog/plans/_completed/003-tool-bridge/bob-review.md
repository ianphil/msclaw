# Uncle Bob's Code Review: `feature/003-tool-bridge`

*All 103 tests pass. The project builds clean on .NET 10. Every implementation and test file on this branch reviewed.*

---

## ✅ What's Done Well

**Interface Segregation is textbook.** `IToolCatalog` (read), `IToolRegistrar` (write), `IToolProvider` (source contract) — each interface has a single, cohesive reason to exist. `ToolBridge` implementing both while consumers only see their slice? That's ISP done right.

**The Dependency Rule is respected.** Dependencies point inward. `ToolExpander` depends on `IToolCatalog`, not `ToolBridge`. `ToolBridgeHostedService` depends on interfaces, not concretes. DI registration is the only place that knows about concrete types.

**Tests use stubs, not mocks.** Hand-rolled `StubToolProvider`, `StubGatewayClient`, `StubToolCatalog` — testing behavior, not implementation. No `Verify()` calls. Test names describe behavior: `RegisterProviderAsync_SameTierCollision_ThrowsInvalidOperationException`.

**The `ToolBridgeHostedService` lifecycle is solid.** Start → register → spawn watch loops with per-provider CancellationTokenSources → stop cancels → awaits → unregisters. Clean resource management.

**The deferred-sync design is clever.** Tools appended to a mutable list during the current message, then `SyncToolsIfNeededAsync` detects the delta on the next `SendAsync`. Avoids resume mid-conversation.

---

## 🟡 Issue 1: `CallerRegistry.TryRelease` TOCTOU race on SemaphoreSlim

**`CallerRegistry.cs:45-61`** — `CurrentCount` check and `Release()` are not atomic. Two threads calling `TryRelease` simultaneously can both pass the guard, and the second `Release()` throws `SemaphoreFullException`. The race window exists when abort arrives while the stream is completing.

**Fix:** Replace the check-then-release with `try { gate.Release(); return true; } catch (SemaphoreFullException) { return false; }`.

## 🟡 Issue 2: `ToolExpander` retains vestigial parameters from pre-deadlock design

**`ToolExpander.cs:16-28`** — `gatewayClient` is injected, null-checked nowhere, and never stored. `sessionBindTimeout` in the internal constructor is also unused. `sessionHolder` in `ExpandToolsAsync` is passed but never read.

**Context:** These parameters aren't speculative generality — they're remnants of the original design that called `ResumeSessionAsync` from inside the tool handler. That design caused a JSON-RPC deadlock (see `.aidocs/invariants/no-resume-session-in-tool-handler.md`) and was replaced with deferred tool sync. The invariant doc is the institutional memory of *why* these were removed; dangling constructor params don't serve that purpose.

**Fix:** Remove `gatewayClient`, `sessionBindTimeout`, and `sessionHolder` entirely. The invariant doc already records the design history — dead parameters shouldn't be carrying that load.

## 🟡 Issue 3: `ToolBridge.RefreshProviderAsync` non-atomic remove-then-add

**`ToolBridge.cs:79-95`** — Between removing all provider tools and re-adding them after `await DiscoverAsync()`, concurrent readers (`GetDefaultTools`, `GetToolsByName`) see the provider's tools as absent. A new session created during this window will be missing default tools.

**Mitigating factor:** Since the deadlock invariant moved `ResumeSessionAsync` out of tool handlers and into the between-message `SyncToolsIfNeededAsync` path, sessions only read from the catalog at creation time and at sync boundaries — not continuously during a conversation. This shrinks the race window considerably but doesn't eliminate it; a session created during a provider refresh still gets an incomplete tool set.

**Fix:** Index new tools into a temporary collection first, then swap: add new, remove stale. Or use a reader-writer lock around refresh.

## 🟢 Issue 4: `SessionPool.GetOrCreateAsync` check-then-act race

**`SessionPool.cs:37-46`** — Two concurrent calls for the same `callerKey` could both miss `TryGetValue`, both invoke the factory, and the first session is silently overwritten and leaked. Currently mitigated by the concurrency gate, but `SessionPool` doesn't encode this precondition.

**Fix:** Use `ConcurrentDictionary.GetOrAdd` with `Lazy<Task<>>`, or document the single-caller precondition in the interface contract.

## 🟢 Issue 5: `ToolDescriptorTests` tests compiler-generated internals

**`ToolDescriptorTests.cs:18`** — Asserts existence of `PrintMembers` via reflection — a compiler-generated record detail. If `ToolDescriptor` becomes a class with identical behavior, the test breaks for no behavioral reason.

**Fix:** Remove the `PrintMembers` assertion. Test `Equals` behavior directly if value equality is the concern.

---

*"The ratio of time spent reading versus writing code is well over 10 to 1. Making the code easy to read makes it easier to write."* — The code on this branch reads well. Fix the concurrency gaps and the dead parameters, and this is clean work.
