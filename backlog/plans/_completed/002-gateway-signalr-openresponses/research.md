# Gateway SignalR + OpenResponses Conformance Research

**Date**: 2026-03-06
**Spec Versions Reviewed**: [gateway-protocol.md v1.3](../../../specs/gateway-protocol.md), [gateway-http-surface.md v1.2](../../../specs/gateway-http-surface.md), [gateway-agent-runtime.md v1.0](../../../specs/gateway-agent-runtime.md), [OpenResponses Specification](https://www.openresponses.org/specification)
**Plan Version**: plan.md (002-gateway-signalr-openresponses)

## Summary

This feature implements a subset of three specs (protocol, HTTP surface, agent runtime) plus the OpenResponses specification. The plan is CONFORMANT with the targeted requirements and intentionally defers others as documented non-goals. The OpenResponses implementation follows the basic subset (single message input, no tool execution) per the v1 scope decision.

## Conformance Analysis

### 1. Agent Runtime (gateway-agent-runtime.md)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Single instance (REQ-001) | IAgentRuntime registered as singleton | One runtime per gateway | CONFORMANT |
| Mind-backed identity (REQ-002) | System message from IdentityLoader, append mode | SOUL.md + agent files, append mode | CONFORMANT |
| Session-per-caller (REQ-003) | Caller key to session mapping in runtime | Independent sessions by caller key | CONFORMANT |
| Streaming events (REQ-004) | StreamEvent types: lifecycle, assistant delta, tool, final | Lifecycle + text + tool events | CONFORMANT |
| Event ordering (REQ-005) | Monotonic sequence number per run | Required | CONFORMANT |
| Per-caller concurrency (REQ-006) | SemaphoreSlim(1) per caller, reject mode | Reject/queue/replace modes | PARTIAL - reject only |
| Global concurrency (REQ-007) | Deferred | Configurable max concurrent | DEFERRED (non-goal) |
| Abort support (REQ-008) | AbortResponse hub method to session abort | Cancel inference, emit abort event | CONFORMANT |
| Session listing (REQ-009) | ListSessions hub method | List with metadata | CONFORMANT |
| Session deletion (REQ-010) | Deferred to next feature | Delete by caller key | DEFERRED |
| Session reset (REQ-011) | Deferred to next feature | Clear history, preserve key | DEFERRED |
| Bundled tools (REQ-012) | Deferred | Working memory read/write, mind listing | DEFERRED (non-goal) |
| Workspace skills (REQ-013) | Deferred | Skill discovery at startup | DEFERRED (non-goal) |
| Node-provided tools (REQ-014) | Deferred | Node capability registration | DEFERRED (non-goal) |
| Identity reload (REQ-015) | Deferred | Manual reload without restart | DEFERRED |
| Runtime state (REQ-017) | GatewayState: Starting/Ready/Failed/Stopped | starting/ready/degraded/stopped | PARTIAL - no degraded |
| Model selection (REQ-020) | Default model from config | Default + per-request override | PARTIAL - default only |
| File attachments (REQ-021) | Deferred | Files forwarded to model | DEFERRED |

**Recommendation**: Partial coverage is acceptable for v1. Reject-only concurrency, no degraded state, and deferred tools/skills are documented non-goals aligned with the quick plan.

### 2. SignalR Protocol (gateway-protocol.md)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Transport negotiation (REQ-001) | ASP.NET Core SignalR handles automatically | WebSocket to SSE to Long Polling | CONFORMANT |
| Authentication (REQ-002) | Loopback bypass only | Bearer/device tokens | DEFERRED (non-goal) |
| Agent streaming (REQ-005) | IAsyncEnumerable from hub | Lifecycle + text + tool events | CONFORMANT |
| Session management (REQ-007) | CreateSession, ListSessions, GetHistory | List, preview, reset, delete | PARTIAL - no reset/delete |
| Chat operations (REQ-008) | GetHistory, AbortResponse | History retrieval, abort | CONFORMANT |
| Graceful shutdown (REQ-018) | Deferred | Notify clients, drain in-flight | DEFERRED |
| Loopback bypass (REQ-019) | Default mode for v1 | MAY allow unauthenticated local | CONFORMANT |

### 3. HTTP Surface (gateway-http-surface.md)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Liveness probe (REQ-001) | GET /health | GET /health | CONFORMANT |
| Readiness probe (REQ-002) | GET /health/ready with component detail | GET /health/ready with failing component | CONFORMANT |
| OpenResponses endpoint (REQ-004) | POST /v1/responses in MsClaw.OpenResponses | POST /v1/responses | CONFORMANT |
| SSE streaming (REQ-005) | stream: true to SSE events, terminal [DONE] | Optional SSE with terminal chunk | CONFORMANT |
| Error termination (REQ-006) | Terminal error event, close stream | Error event, close stream | CONFORMANT |
| Session mapping (REQ-007) | Caller key from request, one active run | One active run per caller key | CONFORMANT |
| Concurrent run conflict (REQ-019) | 409 Conflict | Conflict error code | CONFORMANT |
| Mind-derived identity (REQ-020) | Same IAgentRuntime as SignalR | Same runtime for both surfaces | CONFORMANT |
| Consistent errors (REQ-018) | Error code + message + request ID | Machine-readable + human-readable | CONFORMANT |
| Chat completions (REQ-003) | Deferred | POST /v1/chat/completions | DEFERRED (user decision) |
| Webhook ingress (REQ-008) | Deferred | POST /hooks/{name} | DEFERRED (non-goal) |
| Canvas serving (REQ-011) | Deferred | Capability token assets | DEFERRED (non-goal) |

### 4. OpenResponses Specification (openresponses.org)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| POST /v1/responses endpoint | Implemented | Required | CONFORMANT |
| model field (required) | Accepted, passed to runtime | Required string | CONFORMANT |
| input field (string or array) | Single message input only | String or message array | PARTIAL - basic subset |
| stream field (boolean) | Supported | Optional | CONFORMANT |
| SSE event types | response.created, output_text.delta, response.completed | Full event taxonomy | PARTIAL - basic events |
| Terminal [DONE] | Sent after final event | Required | CONFORMANT |
| Response object structure | object: response, status, output | Full response schema | CONFORMANT |
| tools/tool_choice | Deferred | Optional | DEFERRED (user decision) |
| instructions field | Deferred | Optional | DEFERRED |
| max_output_tokens | Deferred | Optional | DEFERRED |

## Recommendations

### Critical Updates
None - the plan covers the targeted subset correctly.

### Minor Updates
1. Include `response.output_item.added` and `response.output_item.done` SSE events for spec compliance (not just text deltas)
2. Add `request_id` to all error responses per HTTP surface REQ-018

### Future Enhancements
1. Queue and replace concurrency modes (agent-runtime REQ-006)
2. Global concurrency limit (agent-runtime REQ-007)
3. Session delete and reset (agent-runtime REQ-010, REQ-011)
4. `/v1/chat/completions` endpoint (HTTP surface REQ-003)
5. Bundled tools registration (agent-runtime REQ-012)
6. Bearer token authentication (protocol REQ-002)
7. Multi-turn items and tool execution in OpenResponses

## Sources
- [Gateway Protocol Spec](../../../specs/gateway-protocol.md)
- [Gateway HTTP Surface Spec](../../../specs/gateway-http-surface.md)
- [Gateway Agent Runtime Spec](../../../specs/gateway-agent-runtime.md)
- [OpenResponses Specification](https://www.openresponses.org/specification)
- [Quick Plan](../../../backlog/plans/20260306-gateway-signalr-openresponses.md)

## Conclusion

The plan implements the core requirements from three specs (agent runtime, protocol, HTTP surface) and the basic OpenResponses subset. All deferred items are documented non-goals aligned with the v1 scope decision. The plan is conformant for targeted requirements and does not violate any MUST-level constraints from deferred areas.
