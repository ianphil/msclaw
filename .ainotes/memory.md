# AI Notes — MsClaw

## Architecture
- SDK data types flow through directly; service lifecycles held behind `IGatewayClient` / `IGatewaySession` boundaries
- `CopilotGatewayClient` extracted from nested class in `GatewayHostedService` — nested class doesn't scale with session ops
- `ISessionPool` replaced per-message create/resume/destroy — sessions created once, reused, disposed on SignalR disconnect
- `ISessionMap` removed; `CallerRegistry` trimmed to `IConcurrencyGate` only; `SessionPool` holds sessions keyed by caller
- SDK push events bridge to SignalR streaming via `Channel<SessionEvent>` — `SessionIdleEvent`/`SessionErrorEvent` are terminal markers
- OpenResponses HTTP surface reuses `AgentMessageService` via thin adapter — SignalR and `/v1/responses` share session/concurrency
- SSE framing: `response.created` synthetic, delta/message as payload frames, `SessionIdleEvent` as `[DONE]`
- Tool Bridge (003): `ToolCatalogStore` (shared `ConcurrentDictionary`) + `ToolBridge` (`IToolCatalog` read) + `ToolRegistrar` (`IToolRegistrar` write)
- `ToolExpander` creates per-session `expand_tools` AIFunction for lazy tool loading via `ResumeSessionAsync`
- `SessionHolder` uses `TaskCompletionSource<IGatewaySession>` — thread-safe deferred binding, callers await instead of null-check
- `ToolBridgeHostedService` owns `WaitForSurfaceChangeAsync` watch loops; registrar executes mutations only

## SDK
- `CopilotClient` requires `OnPermissionRequest` on both `SessionConfig` and `ResumeSessionConfig` — `PermissionHandler.ApproveAll` is gateway default
- `SessionConfig.Streaming = true` required for delta/message events with text content; without it only lifecycle events arrive
- Wire events carry `type` field distinguishing `assistant.message.delta` from `assistant.reasoning.delta` — only reliable discriminator
- `ResumeSessionAsync` accepts `Tools` in `ResumeSessionConfig` — enables lazy tool addition to existing sessions
- `AvailableTools` whitelists ALL tools including CLI built-ins — never use it; register only your tools, CLI built-ins stay visible
- SDK source at `C:\src\copilot-sdk` for verifying API contracts

## DI & Services
- CallerRegistry registered as concrete singleton, forwarded to interfaces via `GetRequiredService` for single-instance guarantee
- `GatewayHostedService` registered as both hosted service and `IGatewayClient` — consumers must handle async disposal
- Liveness (`/health`) split from readiness (`/health/ready`) — startup failures keep liveness green, readiness reports errors
- Minimal API route lambdas need `[FromServices]` to avoid body inference on service parameters

## Auth
- Device-code flow triggers Entra CA error 530033 even on compliant devices — cannot present device PRT
- MSAL.NET `AcquireTokenInteractive` with `http://localhost` redirect and `WithUseEmbeddedWebView(false)` satisfies CA policies
- Token cached in `~/.msclaw/config.json` under `auth` object; server injects into `index.html` via `AuthContextMiddleware`
- Tunnel startup hard-fails with actionable guidance when no valid auth session exists
- All auth originates from CLI login — removed `msal-browser.min.js` from browser UI
- Tunnel access defaults to private (owner-only) — removed `--tenant` from `devtunnel access create`

## Testing
- `FakeGatewayClient`/`FakeGatewaySession` stubs duplicated across test files — candidate for shared TestDoubles
- Integration tests build `WebApplication` directly (not `WebApplicationFactory`); use `app.Urls.Add("http://127.0.0.1:0")` then `IServerAddressesFeature`
- SDK event data types have required `MessageId` property — must set in test construction or CS9035
- Concurrent rejection tests more practical via HTTP `/v1/responses` than SignalR hub (`MaximumParallelInvocationsPerClient` = 1)
- Static-file middleware unit tested via separate `ConfigurePipeline` method with stub `IWebHostEnvironment`
- `DevTunnelLocator` accepts `ICommandRunner` for deterministic testing — apply same pattern to `CliLocator`

## DevTunnel
- Use `devtunnel host <tunnelId>` without `-p` for existing tunnels — `-p` triggers "Batch update of ports is not supported"
- `devtunnel port create` not idempotent — treat existing-port conflict as success path
- `--tunnel` with failed mind validation cascades hosted-service startup failures — surface specific actionable errors

## UX
- Terminal-style chat: input pinned top, new messages prepend below, `flex-direction: column` with `prepend()` (not `column-reverse`)
- Activity log: right-side drawer streaming real-time via SignalR, click-to-expand JSON per event row

## Scaffold & Bootstrap
- GENESIS: scaffold → `copilot-instructions.md` → `bootstrap.md` (2 questions: character + role) → derive SOUL.md/agent/memory → delete bootstrap, rewrite copilot-instructions to permanent
- MSBuild embeds `.github` as `..github.` prefix; folder hyphens → underscores; filename hyphens preserved. Use `ReadTemplateByResourceName`
- Three canonical skills: commit, capture, daily-report (sourced from `ianphil/public-notes`)
