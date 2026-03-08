# AI Notes — Log

## 2026-03-06
- architecture: feature 002 planning keeps Copilot SDK data types flowing through directly while holding service lifecycles behind IGatewayClient and IGatewaySession boundaries.
- planning: feature 002 artifacts currently disagree on hub boundaries, with plan/contracts using split IConcurrencyGate plus ISessionMap while the spec test draft still expects a direct CopilotClient and ICallerRegistry shape.
- impl: CopilotGatewayClient was extracted from a private nested class in GatewayHostedService to its own file — the nested class pattern doesn't scale once session operations are added.
- testing: FakeGatewayClient and FakeGatewaySession stubs are duplicated across GatewayHostedServiceTests and CopilotGatewayClientTests — candidate for a shared TestDoubles file if duplication grows.
- DI: CallerRegistry is registered as concrete singleton, then forwarded to IConcurrencyGate and ISessionMap via GetRequiredService — this ensures both interfaces resolve to the same instance.
- streaming: SDK push events bridge cleanly to SignalR streaming via Channel<SessionEvent>, with SessionIdleEvent and SessionErrorEvent serving as natural terminal markers.
- disposal: registering GatewayHostedService as both hosted service and IGatewayClient means DI consumers must support async disposal semantics in tests and service providers.

- health: Split liveness from readiness because startup failures should keep /health green while /health/ready reports hosted-service initialization errors, and minimal API service parameters in route lambdas need [FromServices] to avoid body inference.
- openresponses: the HTTP surface can reuse AgentMessageService through a thin adapter keyed by request user or TraceIdentifier, so SignalR and /v1/responses share the same session and concurrency behavior.
- sse: OpenResponses framing works cleanly by treating response.created as synthetic, AssistantMessageDeltaEvent and AssistantMessageEvent as payload frames, and SessionIdleEvent as the terminal [DONE] marker.
- testing: ASP.NET Core static-file middleware is easiest to unit test behind a separate ConfigurePipeline method with a stub IWebHostEnvironment and logging-enabled ApplicationBuilder.
- integration-tests: Gateway integration tests avoid WebApplicationFactory by building WebApplication directly with StartCommand.MapEndpoints and stub services — ConfigureWebHostBuilder lacks UseUrls, so use app.Urls.Add("http://127.0.0.1:0") then read IServerAddressesFeature post-start.
- integration-tests: SDK event data types like AssistantMessageDeltaData and AssistantMessageData have required MessageId property — must be set in test event construction or CS9035 fires.
- concurrency: SignalR MaximumParallelInvocationsPerClient defaults to 1, serializing hub calls per connection. Integration tests for concurrent rejection are more practical via the HTTP /v1/responses endpoint with matching User fields than through the hub.
- sdk: CopilotClient requires OnPermissionRequest on both SessionConfig and ResumeSessionConfig — without it, ArgumentException is thrown at runtime. PermissionHandler.ApproveAll is the loopback-gateway default; placed in CopilotGatewayClient (SDK boundary) with ??= so callers can override.
- sdk: SessionConfig.Streaming must be true for the SDK to emit AssistantMessageDeltaEvent and AssistantMessageEvent with text content; without it only lifecycle events (turn_start, turn_end, session.idle) arrive.
- sdk-events: Wire events carry a "type" field distinguishing assistant.message.delta from assistant.reasoning.delta — both have deltaContent/content in data but the type field is the only reliable discriminator. UI must filter reasoning events or users see model thinking instead of the response.
- bootstrap: GENESIS bootstrap design settled — scaffold creates copilot-instructions.md that triggers bootstrap.md (2 questions: character + role), derives SOUL.md/agent file/memory, then deletes bootstrap.md and rewrites copilot-instructions to permanent version. Commit skill ships as embedded resource.
- embedded-resources: MSBuild embeds .github folder as `..github.` prefix (double dot for leading dot in folder name), and hyphens in folder names become underscores (daily-report → daily_report) but hyphens in filenames are preserved. Use ReadTemplateByResourceName for nested paths.
- scaffold: Three canonical skills ship with every mind: commit (stage/observe/push), capture (decompose/route/link context), daily-report (ADO+Teams+Calendar+Email morning briefing). All sourced from ianphil/public-notes.
- session-pool: Replaced per-message session create/resume/destroy with ISessionPool — sessions are created once, reused across messages, disposed on SignalR disconnect. Eliminated 2 unnecessary RPC round-trips per message.
- cleanup: Removed ISessionMap interface, CallerRegistry trimmed to IConcurrencyGate only. SessionPool holds live IGatewaySession instances keyed by caller.
- ux: Terminal-style chat layout — input pinned at top, new messages prepend directly below it, older messages push down. This is stdout order, not chat order; `flex-direction: column` with `prepend()` is the correct implementation, not `column-reverse`.
- ux: Activity log (raw JSON events) works best as a right-side drawer that streams in real-time via SignalR — non-modal so the user can keep chatting while inspecting events. Click-to-expand JSON per event row keeps it compact.

## 2026-03-07
- devtunnel: persistent tunnel hosting should use `devtunnel host <tunnelId>` once the port is already registered; passing `-p` during host can trigger "Batch update of ports is not supported" for existing tunnels.
- devtunnel: `devtunnel port create` is not idempotent by default and returns a conflict when the port already exists, so tunnel startup must treat existing-port conflicts as a success path.
- startup: enabling `--tunnel` while the mind fails validation causes hosted-service startup failures to cascade; surfacing specific actionable errors (missing CLI/login guidance) improves operator recovery.

## 2026-03-08
- auth: Device-code flow triggers Entra Conditional Access error 530033 ("device must be managed") even on compliant devices because the flow cannot present the device PRT. Interactive browser with localhost loopback redirect satisfies CA policies.
- auth: MSAL.NET `AcquireTokenInteractive` with `WithRedirectUri("http://localhost")` and `WithUseEmbeddedWebView(false)` mirrors the msal-node `acquireTokenInteractive` pattern already proven in `scripts/get-token/`.
- auth: Token cached in `~/.msclaw/config.json` under an `auth` object; gateway serves it via `/api/auth/context` so the browser UI can bootstrap SignalR bearer auth without MSAL.js or redirect URIs.
- auth: Tunnel startup now hard-fails with actionable guidance when no valid auth session exists — prevents confusing anonymous-access errors at the tunnel layer.
- cleanup: Removed bundled `msal-browser.min.js` — browser UI no longer runs its own OAuth flow; all auth originates from CLI login.
- security: Replaced `/api/auth/context` endpoint with server-side token injection into index.html via AuthContextMiddleware — eliminates unauthenticated token endpoint and raw token in API response.
- security: Removed `--tenant` from `devtunnel access create` — tunnel access now defaults to private (owner-only) instead of granting tenant-wide access.
