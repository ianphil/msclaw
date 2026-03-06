# AI Notes — Log

## 2026-03-06
- architecture: feature 002 planning keeps Copilot SDK data types flowing through directly while holding service lifecycles behind IGatewayClient and IGatewaySession boundaries.
- planning: feature 002 artifacts currently disagree on hub boundaries, with plan/contracts using split IConcurrencyGate plus ISessionMap while the spec test draft still expects a direct CopilotClient and ICallerRegistry shape.
- impl: CopilotGatewayClient was extracted from a private nested class in GatewayHostedService to its own file — the nested class pattern doesn't scale once session operations are added.
- testing: FakeGatewayClient and FakeGatewaySession stubs are duplicated across GatewayHostedServiceTests and CopilotGatewayClientTests — candidate for a shared TestDoubles file if duplication grows.
- DI: CallerRegistry is registered as concrete singleton, then forwarded to IConcurrencyGate and ISessionMap via GetRequiredService — this ensures both interfaces resolve to the same instance.
