# MsClaw HTTP Surface

> Detailed HTTP surface specification for MsClaw Gateway.
> Complements [msclaw-gateway-protocol.md](msclaw-gateway-protocol.md) (SignalR contract) and [msclaw-gateway.md](msclaw-gateway.md) (system architecture).

## Overview

MsClaw Gateway exposes a mixed transport surface:

- **SignalR** at `/gateway` for typed, bidirectional, real-time operations.
- **HTTP** for stateless operations, OpenAI-compatible APIs, webhook ingress, and canvas asset serving.

This spec defines endpoint contracts, auth, request/response schemas, streaming semantics, and failure behavior for the HTTP side of the gateway.

## Goals

- Provide a stable, documented HTTP surface for operators, automation, and integrations.
- Preserve compatibility with OpenAI client libraries for chat and responses flows.
- Support non-SignalR integrations (webhooks, probes, one-shot invocations).
- Keep behavior aligned with gateway architecture and mind-backed agent runtime constraints.

## Non-Goals (v1)

- Full REST replacement for all SignalR hub methods.
- Long-lived HTTP sessions independent of the Copilot SDK session model.
- Public multi-tenant routing across multiple mind roots.

## Transport Topology

```
                   ┌────────────────────────────────────┐
                   │           MSCLAW GATEWAY           │
                   │        ASP.NET Core host           │
                   │                                    │
                   │  SignalR hub: /gateway             │
                   │  HTTP APIs: /health, /v1/*, /hooks │
                   │  Canvas host: /canvas/{token}/*    │
                   └───────────────┬────────────────────┘
                                   │
         ┌─────────────────────────┼─────────────────────────┐
         │                         │                         │
         ▼                         ▼                         ▼
   CLI / Web UI             Automation / SDK          Channel adapters
 (SignalR + HTTP)         (OpenAI-compatible HTTP)      (webhooks)
```

## Surface Summary

| Path | Method | Auth | Streaming | Purpose |
|------|--------|------|-----------|---------|
| `/health` | GET | None | No | Liveness probe |
| `/health/ready` | GET | None | No | Readiness probe |
| `/v1/chat/completions` | POST | Bearer token | Optional SSE | OpenAI-compatible chat |
| `/v1/responses` | POST | Bearer token | Optional SSE | OpenAI-compatible responses |
| `/hooks/{name}` | POST | Optional HMAC | No | Webhook ingress |
| `/canvas/{token}/*` | GET | Capability token in path | No | Canvas app/assets |
| `/gateway` | SignalR negotiate + transport | Bearer/device token | Native SignalR | Typed real-time API |

---

## 1. Health Endpoints

### 1.1 `GET /health`

**Purpose**
- Fast process-level probe for supervisors/load balancers.

**Success Criteria**
- ASP.NET host is running and request pipeline is healthy.

**Response**
```json
{
  "status": "ok",
  "service": "msclaw-gateway"
}
```

**Status Codes**
- `200 OK` — process is alive.
- `503 Service Unavailable` — process started but cannot serve requests.

### 1.2 `GET /health/ready`

**Purpose**
- Readiness probe for traffic admission.

**Ready Definition**
- Mind root validated.
- Identity loaded (`SOUL.md` + agents).
- `CopilotClient` singleton created and connected.

**Response**
```json
{
  "status": "ready",
  "checks": {
    "mind": "ok",
    "identity": "ok",
    "copilotClient": "ok"
  }
}
```

**Status Codes**
- `200 OK` — ready for operator traffic.
- `503 Service Unavailable` — one or more readiness checks failed.

---

## 2. OpenAI-Compatible APIs

These endpoints provide drop-in compatibility for common OpenAI clients while routing internally through the same MsClaw agent runtime used by SignalR callers.

### 2.1 `POST /v1/chat/completions`

**Auth**
- `Authorization: Bearer <gateway_token>` required unless loopback bypass is enabled.

**Request (compatible subset)**
```json
{
  "model": "gpt-5",
  "messages": [
    { "role": "system", "content": "optional caller instructions" },
    { "role": "user", "content": "summarize the latest deploy status" }
  ],
  "stream": true,
  "tools": []
}
```

**Behavior**
- Validates token and request shape.
- Resolves or creates a Copilot SDK session.
- Appends mind-derived system identity.
- Executes prompt through gateway runtime.
- Returns either one-shot JSON or SSE stream depending on `stream`.

**Response (non-streaming)**
```json
{
  "id": "chatcmpl_x",
  "object": "chat.completion",
  "model": "gpt-5",
  "choices": [
    {
      "index": 0,
      "finish_reason": "stop",
      "message": { "role": "assistant", "content": "..." }
    }
  ]
}
```

**Response (streaming)**
- `Content-Type: text/event-stream`
- Emits OpenAI-style incremental chunks.
- Terminal chunk includes `finish_reason`; stream closes after completion.

### 2.2 `POST /v1/responses`

**Auth**
- Same as `/v1/chat/completions`.

**Request (compatible subset)**
```json
{
  "model": "gpt-5",
  "input": "draft a release note from these commits",
  "stream": false
}
```

**Response**
```json
{
  "id": "resp_x",
  "object": "response",
  "model": "gpt-5",
  "output": [
    {
      "type": "message",
      "role": "assistant",
      "content": [{ "type": "output_text", "text": "..." }]
    }
  ]
}
```

### 2.3 Streaming Semantics for `/v1/*`

- `stream: false` → single JSON response.
- `stream: true` → SSE with incremental payloads.
- Gateway maps internal runtime events to OpenAI-compatible chunk envelopes.
- Runtime/tool failures produce a terminal error event and close the stream.

### 2.4 Session Mapping

- HTTP callers are mapped to a gateway caller key (token subject + optional client id).
- One active agent run per caller key at a time.
- Session reuse is allowed when `session_id` metadata is supplied by caller-adapter logic.

---

## 3. Webhook Ingress

### 3.1 `POST /hooks/{name}`

**Purpose**
- Entry point for external systems (channels, CI/CD, automations).

**Auth**
- Optional per-hook HMAC verification configured under `MsClaw:Http:Hooks`.

**Request**
- Provider-native JSON payload.
- Gateway does not require one canonical inbound schema from providers.

**Processing Pipeline**
1. Match `{name}` to configured webhook binding.
2. Validate signature if secret configured.
3. Normalize provider payload into internal envelope.
4. Route to channel adapter and/or agent runtime.
5. Return synchronous accept/fail result.

**Response**
```json
{
  "accepted": true,
  "hook": "slack-events",
  "eventId": "evt_123"
}
```

**Status Codes**
- `202 Accepted` — payload accepted for processing.
- `400 Bad Request` — invalid body/signature format.
- `401 Unauthorized` — signature/token invalid.
- `404 Not Found` — hook name not configured.
- `429 Too Many Requests` — admission/rate limit exceeded.

---

## 4. Canvas Asset Host

### 4.1 `GET /canvas/{token}/*`

**Purpose**
- Serve canvas HTML/JS/CSS assets to node-rendered WebViews.

**Auth Model**
- Capability token in URL path (opaque, high entropy).
- Token scope includes node id, optional app id, and allowed path prefix.
- Default token TTL is 10 minutes with sliding expiration.

**Behavior**
- Validate token existence, signature/lookup, expiry, and scope.
- Serve requested static asset only if within allowed path.
- Reject traversal attempts (`..`, encoded traversal, absolute path escapes).

**Status Codes**
- `200 OK` — asset served.
- `401 Unauthorized` — token invalid/expired.
- `403 Forbidden` — token valid but path outside scope.
- `404 Not Found` — asset missing.

---

## 5. SignalR Gateway Endpoint (HTTP Handshake Surface)

### 5.1 `/gateway` (SignalR)

Although protocol semantics are defined in `msclaw-gateway-protocol.md`, the hub exposes an HTTP-visible handshake surface:

- `POST /gateway/negotiate` — transport negotiation.
- `GET /gateway?...` — selected transport (WebSocket preferred, fallback to SSE/long-polling).

**Auth**
- Bearer token via header or `access_token` query for transport negotiation.
- Node/device identity can use post-pairing device token claims.

**Connection Lifecycle**
1. Client negotiates transport.
2. Middleware authenticates principal.
3. Hub `OnConnectedAsync` assigns role groups.
4. Presence snapshot pushed to caller.

---

## 6. Authentication and Authorization

### 6.1 Supported Mechanisms

| Mechanism | Surface | Notes |
|-----------|---------|-------|
| Bearer gateway token | `/v1/*`, `/gateway` | Primary operator auth mechanism |
| Device token claims | `/gateway` | Node role and per-device routing |
| Webhook HMAC secret | `/hooks/{name}` | Optional per webhook |
| Loopback bypass | local requests | Optional; default for local dev only |
| Canvas capability token | `/canvas/{token}/*` | Path-embedded scoped token |

### 6.2 Policy Boundaries

- HTTP endpoints use middleware + endpoint policy checks.
- SignalR methods use per-method policies (`OperatorRead`, `OperatorWrite`, `NodeRole`, etc.).
- Canvas tokens authorize by capability scope rather than user identity claims.

---

## 7. Error Model

### 7.1 JSON Error Envelope (HTTP)

MsClaw should emit consistent JSON errors for non-streaming HTTP requests:

```json
{
  "error": {
    "code": "unauthorized",
    "message": "Bearer token is invalid or missing.",
    "requestId": "req_abc123"
  }
}
```

### 7.2 Suggested Error Codes

| Code | Meaning |
|------|---------|
| `invalid_request` | Schema or validation failure |
| `unauthorized` | Missing/invalid credentials |
| `forbidden` | Authenticated but not allowed |
| `not_found` | Route/resource not found |
| `conflict` | Concurrent run conflict (same caller key) |
| `rate_limited` | Admission/rate policy enforced |
| `runtime_error` | Agent runtime/tool execution failure |
| `upstream_unavailable` | Copilot client/session unavailable |

### 7.3 Streaming Error Behavior

- SSE streams emit terminal error payload and then close.
- Partial chunks may have already been delivered before terminal error.
- HTTP status can be `200` once stream starts; downstream must handle terminal error events.

---

## 8. Configuration

```json
{
  "MsClaw": {
    "Gateway": {
      "BindHost": "127.0.0.1",
      "Port": 18789,
      "Token": null,
      "LoopbackBypass": true
    },
    "Agent": {
      "DefaultModel": "gpt-5",
      "Streaming": true,
      "TimeoutSeconds": 600
    },
    "Canvas": {
      "Enabled": true,
      "CapabilityTtlMinutes": 10
    },
    "Http": {
      "ChatCompletions": true,
      "Responses": true,
      "Hooks": {}
    }
  }
}
```

---

## 9. OpenClaw Mapping

The OpenClaw architecture reference describes:

- Gateway daemon with **WS + HTTP** on port `18789`.
- WS request/response/event framing with mandatory connect handshake.
- Token-based authentication and pairing model.
- Canvas host under `__openclaw__` paths.

MsClaw intentionally preserves equivalent capabilities but maps real-time transport to SignalR primitives and keeps a dedicated HTTP surface for compatibility and integrations.

| Concern | OpenClaw architecture | MsClaw surface |
|---------|-----------------------|----------------|
| Real-time transport | Raw WebSocket JSON frames | SignalR hub at `/gateway` |
| OpenAI compatibility | Conceptual agent APIs | `/v1/chat/completions`, `/v1/responses` |
| Canvas host | `/__openclaw__/canvas/` | `/canvas/{token}/*` |
| Auth token | env/flag token | bearer middleware + optional loopback bypass |
| Streaming | WS events | SignalR streams + SSE for `/v1/*` |

---

## 10. Implementation Notes

- Use endpoint filters/middleware for shared auth and request-id correlation.
- Keep HTTP handlers thin; delegate to the same runtime services used by `GatewayHub`.
- Reuse one `CopilotClient` singleton and explicit per-request/per-session lifecycle boundaries.
- Ensure hook normalization and canvas token validation are deterministic and audited.

## 11. Open Questions

- Should `/v1/*` expose explicit session identifiers for caller-controlled continuity?
- Should webhook handlers support asynchronous callback receipts for long-running processing?
- Should readiness fail closed when no model provider is currently reachable?
- Should loopback bypass be disabled by default outside `Development`?
