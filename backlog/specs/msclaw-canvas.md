# MsClaw Canvas Host Specification

> Canvas delivery, capability-token auth, and node UI interaction bridge for MsClaw Gateway.

## Overview

The MsClaw Canvas Host is the Gateway subsystem that serves interactive HTML/JS UI
surfaces to nodes, and routes user interactions from those surfaces back into the
agent runtime.

It is the MsClaw equivalent of OpenClaw's `canvas` + `a2ui` subsystem, adapted to:

- ASP.NET Core hosting
- SignalR-based node communication
- MsClaw Gateway contracts (`NodeInvoke`, `NodeInvokeResult`, node registration)
- Mind-driven agent orchestration

## Goals

- Serve canvas assets through Gateway-controlled, time-limited capability URLs.
- Keep node access scoped to canvas only (no broad gateway token exposure).
- Provide a consistent `canvas.*` command surface for agent-driven UI control.
- Bridge node WebView user actions back to the running agent session.
- Support safe local development via optional live reload.

## Non-Goals (v1)

- Multi-tenant canvas roots per mind in one process.
- Full browser sandboxing policy engine.
- Cross-node shared canvas state synchronization.

## Architecture

```
                      ┌──────────────────────────────────────────────┐
                      │               MSCLAW GATEWAY                 │
                      │                                              │
                      │  ┌────────────────────────────────────────┐  │
                      │  │ Canvas Orchestrator                   │  │
                      │  │ - validates node + command            │  │
                      │  │ - mints/refreshes capability token    │  │
                      │  │ - emits node invoke requests          │  │
                      │  └───────────────┬────────────────────────┘  │
                      │                  │                           │
                      │  ┌───────────────▼────────────────────────┐  │
                      │  │ Canvas HTTP Surface                    │  │
                      │  │ GET /canvas/{token}/*                 │  │
                      │  │ - token auth + sliding TTL refresh     │  │
                      │  │ - static file serving                  │  │
                      │  │ - optional live-reload script inject   │  │
                      │  └───────────────┬────────────────────────┘  │
                      │                  │                           │
                      │  ┌───────────────▼────────────────────────┐  │
                      │  │ Agent Runtime + Session Router         │  │
                      │  │ - receives canvas.input events         │  │
                      │  │ - routes to active session/tool flow   │  │
                      │  └────────────────────────────────────────┘  │
                      └───────────────┬──────────────────────────────┘
                                      │ SignalR
                                      │
                    ┌─────────────────▼─────────────────┐
                    │            NODE (device)          │
                    │ - receives canvas.present etc.    │
                    │ - opens WebView                   │
                    │ - runs A2UI bridge helper         │
                    │ - sends user actions upstream     │
                    └───────────────────────────────────┘
```

## Core Components

### 1) Canvas HTTP Surface

Responsible for serving canvas assets over:

- `GET /canvas/{token}/{*assetPath}`

Behavior:

- Validates capability token before serving.
- Resolves paths under configured canvas root only.
- Rejects traversal attempts.
- Returns `404` for missing assets.
- Adds `Cache-Control: no-store` for HTML and bridge-sensitive responses.

### 2) Capability Token Service

Responsible for minting and validating node-scoped canvas access tokens.

Token characteristics (aligned with OpenClaw behavior):

- 18 random bytes (144-bit entropy), Base64URL encoded.
- Default TTL: 10 minutes.
- Sliding expiration on valid canvas access.
- Bound to a currently connected node identity.

### 3) Canvas Orchestrator

Gateway-side coordinator that:

- Resolves target node and authorization.
- Ensures a valid canvas capability exists.
- Builds scoped canvas URL.
- Sends `node.invoke` for `canvas.*` commands.
- Correlates command results back to requesting client/session.

### 4) A2UI Bridge Layer

A helper script injected into served HTML (or loaded from bundled helper) that:

- Exposes global bridge helpers in WebView:
  - `openclawSendUserAction(...)` style compatibility shim
  - `OpenClaw.sendUserAction(...)` style compatibility shim
- Bridges to native handlers on node platforms (iOS/Android equivalents).
- Dispatches action status events to the page (`openclaw:a2ui-action-status` pattern).

MsClaw naming can evolve, but compatibility shims are recommended for portability.

## Route Surface

### External Route (node-facing)

| Route | Method | Purpose | Auth |
|------|--------|---------|------|
| `/canvas/{token}/{*assetPath}` | GET/HEAD | Serve canvas app assets | Capability token |

### Internal/Hosted Helpers

MsClaw v1 keeps the public surface minimal via `/canvas/{token}/*`.
Any A2UI helper asset can be:

- injected inline for HTML responses, or
- served from an internal bundled path under the same token-scoped route.

No separate unauthenticated `/a2ui/*` endpoint is required in v1.

## Capability Token Lifecycle

### Mint

Tokens are minted when:

- node requests refresh (`node.canvas.capability.refresh`), and/or
- gateway prepares first `canvas.show` for a node without a valid token.

### Validate

On each canvas HTTP request:

1. Parse token from route segment.
2. Check token exists and is not expired.
3. Confirm token is associated with an active node session.
4. Refresh expiration (sliding TTL).
5. Continue to file resolution.

### Revoke

Tokens become invalid when:

- TTL expires,
- node disconnects,
- node session rotates capability token.

## SignalR Contracts (Canvas-Critical)

This section defines canvas-specific contracts used through existing gateway methods.

### Node commands (via `node.invoke`)

| Command | Purpose | Typical params |
|--------|---------|----------------|
| `canvas.present` | Open/show canvas UI | `{ url, title?, sessionKey? }` |
| `canvas.hide` | Hide canvas surface | `{}` |
| `canvas.navigate` | Navigate current canvas | `{ url }` |
| `canvas.eval` | Execute JS in WebView | `{ script }` |
| `canvas.snapshot` | Capture canvas screenshot | `{ format?, quality? }` |
| `canvas.a2ui.push` | Push UI event payload | `{ event, payload }` |
| `canvas.a2ui.pushJSONL` | Push streamed UI events | `{ lines[] }` |
| `canvas.a2ui.reset` | Reset UI state | `{}` |

### Node capability refresh

```csharp
Task<NodeCanvasCapabilityRefreshResult> NodeCanvasCapabilityRefresh();

public record NodeCanvasCapabilityRefreshResult
{
    public required string CanvasCapability { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required string CanvasHostUrl { get; init; } // includes scoped token path base
}
```

### Node → Gateway user action bridge

MsClaw SHOULD treat user actions as first-class node events:

```csharp
public record CanvasInputEvent
{
    public required string NodeId { get; init; }
    public required string SessionKey { get; init; }
    public required string ActionId { get; init; }
    public required string ActionName { get; init; }
    public string? SurfaceId { get; init; }
    public string? SourceComponentId { get; init; }
    public JsonElement? Context { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}
```

`CanvasInputEvent` is delivered into the active agent session as a tool/input event.

## End-to-End Flows

### 1) Present Canvas

```
Operator/Agent                   Gateway                          Node
     │                             │                               │
     │ canvas.show(nodeId, app)    │                               │
     │────────────────────────────►│                               │
     │                             │ Mint/refresh capability       │
     │                             │ Build /canvas/{token}/app...  │
     │                             │ node.invoke(canvas.present)   │
     │                             │──────────────────────────────►│
     │                             │                               │ Open WebView
     │                             │                               │ GET /canvas/{token}/app.html
     │                             │◄──────────────────────────────│
     │                             │ Validate token, serve asset   │
```

### 2) User Action to Agent

```
Canvas WebView                    Node                              Gateway/Agent
     │                             │                                  │
     │ openclawSendUserAction()    │                                  │
     │────────────────────────────►│                                  │
     │                             │ canvas.input event via SignalR    │
     │                             │─────────────────────────────────►│
     │                             │                                  │ route to session
     │                             │                                  │ continue tool/agent run
```

### 3) Live Reload (dev mode only)

```
File change in canvas root
        │
        ▼
Gateway file watcher detects update
        │
        ▼
Broadcasts reload signal to connected canvas pages
        │
        ▼
WebView reloads current document
```

## Data Shapes

```csharp
public record CanvasShowRequest
{
    public required string NodeId { get; init; }
    public required string AppPath { get; init; }    // e.g. "dashboard/index.html"
    public string? SessionKey { get; init; }
    public string? Title { get; init; }
}

public record CanvasEvalResult
{
    public required bool Ok { get; init; }
    public string? ValueJson { get; init; }
    public string? Error { get; init; }
}
```

## Security Model

### Access Control

- Canvas HTTP requests are authorized by:
  1) gateway bearer token (operator/admin usage), or
  2) valid node capability token (node WebView usage).
- Loopback-local requests may be allowed by gateway policy for local tooling.

### Path Safety

- Resolve all assets against configured root using canonical path checks.
- Deny any request resolving outside root.

### Token Safety

- Constant-time token comparison recommended.
- Never log raw capability tokens.
- Rotate on refresh and on reconnect.

## Configuration

```json
{
  "MsClaw": {
    "Canvas": {
      "Enabled": true,
      "Root": null,
      "CapabilityTtlMinutes": 10,
      "LiveReload": false,
      "InjectA2UiBridge": true
    }
  }
}
```

## Protocol Alignment Notes

`msclaw-gateway-protocol.md` already models core node invocation flow.
To fully support this canvas spec, the protocol should include:

- explicit `NodeCanvasCapabilityRefresh` method or equivalent
- explicit node event ingress path for `canvas.input` (if not reusing generic node event channel)
- typed payload models for `canvas.*` command params/results

## OpenClaw → MsClaw Mapping

| OpenClaw | MsClaw |
|----------|--------|
| `/__openclaw__/canvas/*` | `/canvas/{token}/*` |
| `/__openclaw__/a2ui/*` | A2UI bridge folded into token-scoped canvas host |
| `oc_cap` + scoped capability path | Path token segment (`/canvas/{token}/...`) |
| Node canvas capability refresh method | `NodeCanvasCapabilityRefresh` (or equivalent) |
| Canvas/A2UI native bridge helpers | Compatibility helper + MsClaw-typed event ingestion |

## Failure Modes

| Scenario | Expected behavior |
|----------|-------------------|
| Expired token | `401` + node requests refresh |
| Invalid token format | `401` without leaking parse detail |
| Missing asset | `404` |
| Node disconnected during invoke | `UNAVAILABLE` node invoke result |
| `canvas.eval` JS runtime error | `ok=false` + error payload |

## Open Questions

- Should canvas capability refresh be proactive heartbeat-based or strictly on-demand?
- Should MsClaw keep OpenClaw-compatible global JS helper names long-term?
- Should live reload be gateway-global or per-node/per-surface?
- Should canvas apps be allowed from remote origins by policy, or only gateway-hosted assets?

## Future Considerations

- Multi-mind canvas roots.
- Signed canvas bundles with integrity metadata.
- Stateful canvas session persistence and resume.
- Cross-node synchronized multi-display canvases.
