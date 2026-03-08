---
title: "Gateway SignalR Hub — OpenResponses + UI Foundation"
status: open
priority: high
created: 2026-03-06
---

# Gateway SignalR Hub — OpenResponses + UI Foundation

## Summary

Wire the empty `GatewayHub` to the Copilot SDK session, implement the core SignalR contract from `gateway-protocol.md`, and add the OpenResponses-compliant HTTP surface from `gateway-http-surface.md` — giving a web UI everything it needs to connect and chat with the agent.

## Motivation

The Gateway currently starts a Copilot SDK client in the hosted service but the SignalR hub has zero methods and the HTTP surface is just `/healthz`. A UI has nothing to talk to. This plan connects the dots: hub methods for real-time streaming, OpenResponses endpoints for stateless API access, and the shared session/identity plumbing both surfaces need.

## Proposal

### Goals

- Strongly-typed SignalR hub with streaming agent conversation (send message → receive streamed events)
- Session create/resume/list/delete through the hub
- Per-caller concurrency enforcement in the agent runtime (#17 — reject/queue/replace modes)
- Web chat UI served from `wwwroot/` via static files (vanilla JS + SignalR client, similar to aspnet ChatRoom sample)
- OpenResponses-compliant `POST /v1/responses` HTTP endpoint with SSE streaming
- Health/readiness probes that reflect actual Copilot client and mind state
- Shared agent runtime: both SignalR and HTTP route through the same mind-backed session

### Non-Goals

- Node/device pairing and invocation (protocol spec REQ-009, REQ-010)
- Exec approval workflows (REQ-011)
- Canvas rendering and asset serving (REQ-020)
- Webhook ingress (REQ-008)
- Authentication and authorization (loopback-only for now)
- Channel adapters (Telegram, Discord, etc.)

## Design

The `GatewayHostedService` already owns the Copilot SDK client. Expose it to the hub and HTTP endpoints via a singleton service (`IAgentRuntime` or similar) that wraps session lifecycle and message sending. The runtime enforces per-caller concurrency (#17) using a `SemaphoreSlim(1)` per caller key — defaulting to reject mode (return conflict error if a run is active), with queue and replace modes configurable later. The hub gets a strongly-typed client interface (`IGatewayHubClient`) with methods like `ReceiveStreamEvent`, `ReceivePresence`, etc. Agent responses stream back as `IAsyncEnumerable<StreamEvent>` through the hub. The OpenResponses HTTP endpoint shares the same runtime but maps request/response to the OpenResponses JSON schema with SSE for streaming. Both surfaces use `IdentityLoader` to inject the mind's system message into sessions. A static `wwwroot/` directory serves the chat UI — vanilla HTML/JS using the `@microsoft/signalr` client library, no build toolchain. The UI connects to `/gateway`, sends messages, and renders streamed assistant responses as chat bubbles.

## Tasks

- [ ] Define `IGatewayHubClient` strongly-typed client interface (stream events, lifecycle markers, presence)
- [ ] Create `IAgentRuntime` service wrapping Copilot SDK session create/resume/send/abort
- [ ] Implement per-caller concurrency gate in `IAgentRuntime` — `SemaphoreSlim(1)` per caller key, reject mode default (#17)
- [ ] Implement hub methods: `SendMessage`, `CreateSession`, `ListSessions`, `GetHistory`, `AbortResponse`
- [ ] Wire hub streaming: `SendMessage` returns `IAsyncEnumerable<StreamEvent>` with assistant deltas, tool events, lifecycle markers
- [ ] Add `UseDefaultFiles()` + `UseStaticFiles()` to Gateway startup, create `wwwroot/` directory
- [ ] Build chat UI: `wwwroot/index.html` with SignalR JS client — connect to `/gateway`, send messages, render streamed responses
- [ ] Add `wwwroot/css/site.css` for chat bubble styling
- [ ] Add OpenResponses `POST /v1/responses` endpoint with SSE streaming support
- [ ] Upgrade `/healthz` to proper liveness + readiness (`/health`, `/health/ready`) reflecting Copilot client state
- [ ] Add integration test: connect to hub, send message, receive streamed response
- [ ] Add integration test: verify concurrent send from same caller is rejected

## Open Questions

- Should the OpenResponses endpoint live in the Gateway project directly or in a separate middleware library for reuse?
- What subset of the OpenResponses spec do we implement for v1? (likely: responses + streaming, skip multi-turn items and tool execution initially)
- Do we need a `/v1/chat/completions` compat endpoint in addition to `/v1/responses`, or is OpenResponses sufficient?
