# AI Notes — Log

## 2026-03-09
- testing: Reflection-based shape tests work well for contract-only C# records when SDK types like AIFunction are awkward to instantiate directly.
- planning: Tool Bridge feature (003) uses CQRS-style interface split — IToolCatalog (read) and IToolRegistrar (write) backed by a single ToolBridge singleton. This avoids synchronization issues while enforcing access separation at the consumer level.
- sdk: ResumeSessionAsync accepts Tools in ResumeSessionConfig, enabling lazy tool addition to existing sessions. expand_tools uses this for incremental session growth.
- sdk: Setting AvailableTools on SessionConfig creates a whitelist across ALL tools including CLI built-ins — never use it. Register only the tools you want; CLI built-ins stay visible by default.
- design: expand_tools needs the session reference but the session needs expand_tools in its config — solved with deferred binding via SessionHolder wrapper and mutable tool list captured in closure.
- design: Same-tier tool name collision is a hard error (InvalidOperationException), intentionally stricter than spec (which says log and skip). Makes DI registration order irrelevant.
- reference: Copilot SDK source available at C:\src\copilot-sdk for verifying API contracts during implementation.
- bug: Windows Path.GetFullPath("~/path") treats ~ as a literal directory name, not home. CLI args need explicit ExpandHome before GetFullPath.
- bug: SignalR abort race condition — AbortResponse cancelled the CTS but the SendAsync generator's finally block (gate release) ran asynchronously. Next message arrived before gate was freed. Fix: idempotent TryRelease + abort force-releases the gate.
- ui: SignalR JS client subscription.dispose() stops delivering events to callbacks but the server stream continues until CancelInvocation arrives. Dispose subscription BEFORE awaiting server-side AbortResponse for instant UI feedback.
- concurrency: ConcurrencyGate (CallerRegistry) needs both strict Release (throws on double-release) for normal flow and idempotent TryRelease for abort/cleanup paths.
- planning: Decomposed single ToolBridge singleton into three classes — ToolCatalogStore (shared ConcurrentDictionary), ToolBridge (IToolCatalog read), ToolRegistrar (IToolRegistrar write). SRP wins: catalog lookups and provider registration change for different reasons.
- planning: SessionHolder upgraded from nullable property to TaskCompletionSource<IGatewaySession> — eliminates race conditions between session creation and expand_tools invocation. Callers await rather than null-check.
- planning: WaitForSurfaceChangeAsync watch loops moved from registrar to ToolBridgeHostedService — mutation driver (hosted service) separated from mutation executor (registrar) for testability.
- testing: Gateway internal infrastructure tests need InternalsVisibleTo in MsClaw.Gateway when new non-public services like ToolCatalogStore are introduced.
- sdk: AIFunctionFactory.Create requires default-valued optional parameters when expand_tools supports either names or query input; omitted required parameters fail binding before handler logic runs.
- sdk: AIFunctionFactory serializes record results with camelCase JSON property names by default, so tests should read tool results case-insensitively or expect camelCase.
- testing: ServiceProvider instances that own IAsyncDisposable services like SessionPool must be disposed with `await using`; plain `using` throws at teardown.
- hosting: Provider watch loops should live in a hosted service and back off after refresh failures so registrar logic stays synchronous and errors do not spin in a tight retry loop.
