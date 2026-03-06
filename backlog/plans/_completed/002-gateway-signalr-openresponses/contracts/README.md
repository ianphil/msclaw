# Gateway SignalR + OpenResponses Contracts

Interface definitions for the caller registry, SignalR hub, and OpenResponses middleware.

## Contract Documents

| Contract | Purpose |
|----------|---------|
| [interfaces.md](interfaces.md) | C# interface definitions for caller registry, hub client, and middleware |

## Contract Principles

- SDK types (CopilotClient, SessionEvent, CopilotSession) pass through directly — no wrappers
- IConcurrencyGate + ISessionMap (split per ISP) handle concurrency gating and caller-to-session mapping
- SDK service types stay behind testable interfaces (IGatewayClient, IGatewaySession)
- Strongly-typed hub contract prevents string-based runtime errors
- OpenResponses DTOs use System.Text.Json serialization with camelCase naming
- OpenResponses DTOs are genuinely new — the SDK has no concept of the OpenResponses spec
