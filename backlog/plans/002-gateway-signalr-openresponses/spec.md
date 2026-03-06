# Specification: Gateway SignalR Hub + OpenResponses

## Overview

### Problem Statement
The Gateway starts a Copilot SDK client and maps an empty SignalR hub, but clients have no way to send messages, receive agent responses, or manage sessions. HTTP consumers (automation scripts, OpenAI-compatible SDKs) have no endpoint to interact with the agent at all. There is no shared runtime to enforce concurrency or provide a uniform event stream.

### Solution Summary
Introduce an agent runtime service that wraps the Copilot SDK's session lifecycle, a strongly-typed SignalR hub that streams agent events to web clients, an OpenResponses-compliant HTTP endpoint (in a separate middleware library) for stateless API access, a static-file chat UI for immediate browser interaction, and upgraded health probes that reflect actual runtime state.

### Business Value
| Benefit | Impact |
|---------|--------|
| Real-time agent conversation | Operators can chat with the agent through a web browser immediately after launching the gateway |
| OpenResponses compatibility | Any OpenResponses-compliant client library can interact with the agent without custom integration |
| Shared runtime | SignalR and HTTP surfaces use the same session, concurrency, and streaming infrastructure — no duplicated logic |
| Concurrency safety | Per-caller gating prevents conflicting simultaneous runs |

## User Stories

### Operator (Web UI)
**As an operator**, I want to open the gateway in my browser, send a message, and see the agent's response stream in real time, so that I can have an interactive conversation.

**Acceptance Criteria:**
- Browser loads a chat interface at the gateway's root URL
- Sending a message streams back assistant text as it is produced
- Tool execution events are visible in the conversation
- Stream has a clear start and end marker

### Operator (Session Management)
**As an operator**, I want to create, list, and view sessions through the SignalR hub, so that I can manage ongoing conversations.

**Acceptance Criteria:**
- Creating a session returns a session identifier
- Listing sessions returns all tracked sessions with metadata
- Retrieving history returns the conversation events for a session
- Aborting a response terminates the active stream

### Automation Client (HTTP)
**As an automation client**, I want to POST a prompt to `/v1/responses` and receive the agent's reply (streaming or one-shot), so that I can integrate with the agent using standard HTTP tooling.

**Acceptance Criteria:**
- A non-streaming request returns a complete response body
- A streaming request returns SSE events with text deltas and a terminal event
- The response format conforms to the OpenResponses specification
- A concurrent request from the same caller is rejected with a conflict status

### Infrastructure Operator
**As an infrastructure operator**, I want separate liveness and readiness probes, so that I can distinguish between "process alive" and "runtime initialized."

**Acceptance Criteria:**
- `GET /health` returns success when the process is running
- `GET /health/ready` returns success only when mind is validated, identity is loaded, and CopilotClient is connected
- Readiness failure identifies the failing component

### Mind Author
**As a mind author**, I want the agent's personality (SOUL.md + agent files) applied to every session, so that conversations reflect my agent's identity.

**Acceptance Criteria:**
- Every new session's system message includes SOUL.md content
- Agent instruction files from `.github/agents/` are included
- Changing mind files and restarting applies the updated identity

## Functional Requirements

### FR-1: Coordination Layer
| Requirement | Description |
|-------------|-------------|
| FR-1.1 | The gateway MUST keep the SDK's CopilotClient behind a testable interface boundary (IGatewayClient) — not exposed raw via DI |
| FR-1.2 | The gateway MUST map caller keys to SDK session IDs (ISessionMap) separately from concurrency enforcement (IConcurrencyGate) per ISP |
| FR-1.3 | The hosted service MUST expose the loaded system message so sessions can be created with append mode |
| FR-1.4 | The gateway MUST enforce per-caller concurrency (one active run per caller key, reject mode) via IConcurrencyGate |
| FR-1.5 | SDK event data types (SessionEvent subtypes) MUST flow through to consumers without transformation |

### FR-2: SignalR Hub
| Requirement | Description |
|-------------|-------------|
| FR-2.1 | The hub MUST use a strongly-typed client interface (`IGatewayHubClient`) |
| FR-2.2 | `SendMessage` MUST stream SDK `SessionEvent` types via `IAsyncEnumerable` — delegating to a shared `AgentMessageService`, not orchestrating inline |
| FR-2.3 | `CreateSession` MUST return a session identifier |
| FR-2.4 | `ListSessions` MUST return metadata for all tracked sessions |
| FR-2.5 | `GetHistory` MUST return conversation events for a given session |
| FR-2.6 | `AbortResponse` MUST cancel the active run and terminate the stream |

### FR-3: OpenResponses HTTP Endpoint
| Requirement | Description |
|-------------|-------------|
| FR-3.1 | `POST /v1/responses` MUST accept a prompt input and return a structured agent response |
| FR-3.2 | When `stream: true`, the endpoint MUST return SSE events with text deltas and a terminal `[DONE]` event |
| FR-3.3 | When `stream: false`, the endpoint MUST return a single complete JSON response |
| FR-3.4 | A concurrent request from the same caller key MUST be rejected with a 409 Conflict |
| FR-3.5 | The endpoint MUST live in a separate middleware library (`MsClaw.OpenResponses`) |

### FR-4: Health Probes
| Requirement | Description |
|-------------|-------------|
| FR-4.1 | `GET /health` MUST return 200 when the process is running |
| FR-4.2 | `GET /health/ready` MUST return 200 only when runtime state is Ready |
| FR-4.3 | Readiness failure MUST include the failing component in the response body |

### FR-5: Chat UI
| Requirement | Description |
|-------------|-------------|
| FR-5.1 | The gateway MUST serve static files from `wwwroot/` |
| FR-5.2 | `index.html` MUST connect to the SignalR hub and support send/receive |
| FR-5.3 | Streamed assistant responses MUST render incrementally as chat bubbles |
| FR-5.4 | No build toolchain — vanilla HTML, CSS, and JS only |

## Non-Functional Requirements

### Performance
| Requirement | Target |
|-------------|--------|
| Event delivery latency (hub → client) | ≤ 100ms excluding network |
| SSE event delivery latency | ≤ 100ms excluding network |
| Health probe response time | ≤ 200ms |
| First streaming chunk | ≤ 5s under normal load |

### Security
| Requirement | Target |
|-------------|--------|
| Authentication | Loopback bypass only (no auth for v1) |
| Token logging | MUST NOT log tokens in plaintext |
| Path traversal | Enforced by MsClaw.Core MindReader |

## Scope

### In Scope
- Agent runtime wrapping Copilot SDK sessions with concurrency gating
- Strongly-typed SignalR hub with streaming
- OpenResponses `POST /v1/responses` with SSE (separate middleware library)
- Basic chat UI (static files, vanilla JS)
- Liveness and readiness health endpoints
- Per-caller concurrency enforcement (reject mode)
- Integration tests for hub streaming and concurrency rejection

### Out of Scope
- Node/device pairing and invocation (protocol REQ-009, REQ-010)
- Exec approval workflows (REQ-011)
- Canvas rendering and asset serving (REQ-020)
- Webhook ingress (REQ-008)
- Authentication and authorization (loopback-only for now)
- Channel adapters (Telegram, Discord, etc.)
- `/v1/chat/completions` endpoint
- Multi-turn items and tool execution in OpenResponses
- Queue and replace concurrency modes (reject only for v1)
- Bundled tools registration (future feature)
- Skills discovery and invocation
- Hot reload of identity

### Future Considerations
- Queue and replace concurrency modes
- `/v1/chat/completions` compat endpoint
- Multi-turn OpenResponses items
- Tool execution output in OpenResponses
- Bearer token authentication
- Bundled tools (working memory read/write, mind listing)
- Global concurrency limit

## Success Criteria
| Metric | Target | Measurement |
|--------|--------|-------------|
| End-to-end chat | Operator sends message in browser UI, receives streamed reply | Manual test with `--mind ~/src/ernist` |
| OpenResponses compat | `POST /v1/responses` returns valid OpenResponses JSON | Automated integration test |
| Concurrency rejection | Second concurrent send from same caller returns 409/error | Automated integration test |
| Health probes | `/health` returns 200, `/health/ready` returns 200 when ready | Automated test |

## Assumptions
1. The Copilot SDK `CopilotClient` can create multiple concurrent sessions
2. The Copilot SDK's session event model maps cleanly to our `StreamEvent` types
3. Loopback-only access is acceptable for v1 (no bearer token auth)
4. A single `SemaphoreSlim(1)` per caller key is sufficient for reject-mode concurrency
5. The SignalR JS client CDN is acceptable for the static chat UI (no npm build)

## Risks and Mitigations
| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| SDK event model doesn't map cleanly to StreamEvent | Low | High | Prototype event mapping early; adapt StreamEvent types |
| CopilotClient singleton limits concurrent sessions | Low | High | SDK docs confirm multiple sessions per client |
| Static chat UI insufficient for real use | Medium | Low | UI is a development convenience, not production surface |
| OpenResponses spec evolves during development | Low | Medium | Implement basic subset; version the middleware |

## Glossary
| Term | Definition |
|------|------------|
| Agent Runtime | Singleton service wrapping Copilot SDK sessions, concurrency, and event streaming |
| Caller Key | Unique identifier for a caller (ConnectionId for SignalR, derived from request for HTTP) |
| StreamEvent | Discriminated union of event types emitted during an agent run |
| OpenResponses | Vendor-neutral API specification for LLM interactions (openresponses.org) |
| SSE | Server-Sent Events — HTTP streaming protocol for incremental response delivery |
| Mind | Directory on disk defining agent personality (SOUL.md) and memory (.working-memory/) |
