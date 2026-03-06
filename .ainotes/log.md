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
