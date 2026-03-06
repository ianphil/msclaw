# Product Specification: Gateway HTTP Surface

**Document Owner(s):** MsClaw Team  
**Status:** Draft  
**Target Release:** TBD  
**Link to Technical Spec:** [msclaw-http-surface.md](msclaw-http-surface.md)  

## Version History
*Rule: No silent mutations. All changes after baseline must be recorded here.*

| Version | Date | Author | Description of Changes |
| :--- | :--- | :--- | :--- |
| 1.0 | 2026-03-03 | MsClaw Team | Initial Draft — derived from technical HTTP surface spec |
| 1.1 | 2026-03-03 | MsClaw Team | Reconciled with Gateway spec — added endpoint paths, health probe 200ms latency, streaming-begin 5s constraint |
| | | | |

---

> **⚠️ Author Guidelines (Read Before Writing)**
> *   **Focus on the "What" and "Why".** Absolutely NO technical implementation details (the "How"). No database schemas, API JSON payloads, or code snippets.
> *   **No Subjective Language.** Ban words like *fast, seamless, modern, intuitive, or robust*. Use empirical, verifiable metrics.
> *   **Testability.** Every requirement must be written so QA can translate it into a definitive Pass/Fail test.
> *   **Terminology.** Use RFC 2119 keywords: **MUST**, **SHOULD**, **MAY**.

---

## 1. Executive Summary & Problem Statement

### 1.1 The Problem (The "Why")

The MsClaw Gateway provides a real-time SignalR protocol for operators and device nodes, but many integration scenarios cannot use persistent bidirectional connections. Automation scripts, CI/CD pipelines, OpenAI-compatible SDKs, webhook-emitting services, and infrastructure probes all require stateless HTTP endpoints. Without a dedicated HTTP surface, these consumers would need to implement the full SignalR protocol — or be excluded entirely.

### 1.2 Business Value

- **OpenAI SDK compatibility:** Consumers using standard OpenAI client libraries MUST be able to interact with a MsClaw agent without learning a proprietary protocol, lowering the barrier to adoption.
- **Webhook-driven integrations:** External systems (chat platforms, source control, CI/CD) MUST be able to push events into the agent runtime via standard HTTP callbacks, enabling channel adapters without custom WebSocket clients.
- **Operational observability:** Infrastructure tooling (load balancers, container orchestrators, monitoring) MUST be able to probe Gateway health and readiness through standard HTTP health endpoints.
- **Canvas asset delivery:** Node-rendered interactive applications MUST be served over HTTP with scoped, time-limited access control.

### 1.3 Success Metrics (KPIs)

*   **Metric 1:** Any standard OpenAI-compatible client library MUST be able to complete a chat request and a responses request against the Gateway HTTP surface without modification.
*   **Metric 2:** A webhook provider MUST be able to deliver a payload to the Gateway and receive a synchronous accept/reject response within a single HTTP round-trip.
*   **Metric 3:** Container orchestrators MUST be able to determine Gateway liveness and readiness using the health endpoints without authentication.

## 2. Target Audience & User Personas

| Persona Name | Description | Key Needs for this Feature |
| :--- | :--- | :--- |
| **Automation Client** | A script or bot interacting with the agent via OpenAI-compatible HTTP endpoints | Needs to send prompts and receive agent responses (streaming or one-shot) using standard OpenAI client libraries. |
| **Webhook Provider** | An external system (chat platform, CI/CD, source control) pushing events into the Gateway | Needs a stable HTTP ingress endpoint that accepts provider-native payloads and returns synchronous acceptance status. |
| **Infrastructure Operator** | A load balancer, orchestrator, or monitoring system probing Gateway availability | Needs unauthenticated HTTP health and readiness endpoints that return machine-readable status. |
| **Canvas Consumer** | A node-rendered WebView loading interactive application assets from the Gateway | Needs time-limited, scoped HTTP access to canvas assets without user-identity-based authentication. |
| **Operator (SignalR handshake)** | A CLI, web UI, or desktop app initiating a real-time connection to the Gateway | Needs the HTTP handshake surface to negotiate transport and authenticate before upgrading to a persistent connection. |

## 3. Functional Requirements (The "What")
*Rule: Use unique identifiers (e.g., REQ-001) for traceability.*

### 3.1 Core User Flows

1.  **Health Probe:** An infrastructure system issues an HTTP request to the liveness endpoint and receives a status indicating whether the Gateway process is alive. A separate readiness endpoint indicates whether the Gateway is fully initialized and able to serve traffic.
2.  **OpenAI-Compatible Chat:** An automation client sends a chat request with a message history and receives either a single response or a server-sent event stream of incremental text, depending on the caller's streaming preference.
3.  **OpenAI-Compatible Responses:** An automation client sends a single-input prompt and receives a structured response containing the agent's output.
4.  **Webhook Delivery:** An external system posts a provider-native payload to a named webhook endpoint. The Gateway validates the payload, matches it to a configured binding, and returns a synchronous acceptance or rejection.
5.  **Canvas Asset Retrieval:** A node WebView requests a static asset using a capability token embedded in the URL path. The Gateway validates the token's scope and expiry and serves the asset if authorized.
6.  **SignalR Transport Negotiation:** A client initiates an HTTP handshake to negotiate the real-time transport. The Gateway authenticates the client and completes the transport upgrade.

### 3.2 Feature Requirements & Acceptance Criteria

| ID | Feature | Description | Acceptance Criteria (Pass/Fail) |
| :--- | :--- | :--- | :--- |
| **REQ-001** | Liveness Probe | The Gateway MUST expose an unauthenticated liveness endpoint at `GET /health` that indicates whether the process is running. | - IF the Gateway process is running, the endpoint MUST return a success status.<br>- IF the process is started but unable to serve requests, the endpoint MUST return an unavailable status. |
| **REQ-002** | Readiness Probe | The Gateway MUST expose an unauthenticated readiness endpoint at `GET /health/ready` that reports whether all initialization checks have passed. | - The readiness check MUST verify: mind directory is valid, agent identity is loaded, and the Copilot client is connected.<br>- IF all checks pass, the endpoint MUST return a ready status.<br>- IF any check fails, the endpoint MUST return an unavailable status identifying the failing component. |
| **REQ-003** | OpenAI-Compatible Chat Endpoint | The Gateway MUST expose an endpoint at `POST /v1/chat/completions` that accepts chat requests compatible with the OpenAI chat completions contract. | - The endpoint MUST accept a message history and return an agent response.<br>- The endpoint MUST require bearer token authentication (unless loopback bypass is enabled).<br>- The response format MUST be compatible with standard OpenAI client libraries. |
| **REQ-004** | OpenAI-Compatible Responses Endpoint | The Gateway MUST expose an endpoint at `POST /v1/responses` that accepts single-input prompts compatible with the OpenAI responses contract. | - The endpoint MUST accept a prompt input and return a structured agent response.<br>- The endpoint MUST require bearer token authentication (unless loopback bypass is enabled).<br>- The response format MUST be compatible with standard OpenAI client libraries. |
| **REQ-005** | Streaming Mode (SSE) | The OpenAI-compatible endpoints MUST support optional server-sent event streaming. | - IF the caller requests streaming, the Gateway MUST return incremental text chunks as server-sent events.<br>- The stream MUST include a terminal chunk indicating completion.<br>- IF the caller does not request streaming, the Gateway MUST return a single complete response. |
| **REQ-006** | Streaming Error Termination | IF an error occurs during an active SSE stream, the Gateway MUST emit a terminal error event and close the stream. | - The client MUST NOT be left waiting indefinitely after a runtime or tool failure.<br>- Partial content MAY have been delivered before the error event. |
| **REQ-007** | Session Mapping for HTTP Callers | The Gateway MUST map each HTTP caller to an internal session based on the caller's authenticated identity. | - One active agent run per caller key MUST be enforced at a time.<br>- IF a caller-adapter supplies session metadata, the Gateway SHOULD reuse the corresponding session. |
| **REQ-008** | Webhook Ingress | The Gateway MUST expose named webhook endpoints at `POST /hooks/{name}` that accept provider-native payloads from external systems. | - The endpoint MUST match the webhook name to a configured binding.<br>- IF the name does not match any configured binding, the endpoint MUST return a not-found status.<br>- On successful acceptance, the endpoint MUST return a synchronous acknowledgement. |
| **REQ-009** | Webhook HMAC Verification | The Gateway MUST support optional per-webhook HMAC signature verification. | - IF a secret is configured for a webhook, the Gateway MUST validate the request signature.<br>- IF the signature is missing or invalid, the Gateway MUST reject the request.<br>- IF no secret is configured, the Gateway MUST accept requests without signature validation. |
| **REQ-010** | Webhook Rate Limiting | The Gateway MUST enforce admission and rate limits on webhook ingress. | - IF the rate limit is exceeded, the endpoint MUST return a rate-limited status.<br>- Requests within the allowed rate MUST be processed. |
| **REQ-011** | Canvas Asset Serving | The Gateway MUST serve canvas application assets over HTTP using capability tokens embedded in the URL path. | - The token MUST be opaque and high-entropy (minimum 144 bits).<br>- The token MUST have a default time-to-live of 10 minutes with sliding expiration.<br>- Assets MUST only be served if the requested path falls within the token's allowed scope. |
| **REQ-012** | Canvas Path Traversal Protection | The Gateway MUST reject any canvas asset request that attempts to traverse outside the token's allowed path scope. | - Requests containing path traversal sequences MUST be rejected.<br>- Requests for paths outside the token's scope MUST return a forbidden status.<br>- The Gateway MUST NOT disclose filesystem structure in rejection responses. |
| **REQ-013** | Canvas Token Expiry | The Gateway MUST reject canvas asset requests using expired capability tokens. | - IF the token has exceeded its time-to-live, the Gateway MUST return an unauthorized status.<br>- Valid tokens within their TTL MUST be accepted. |
| **REQ-014** | SignalR Transport Negotiation | The Gateway MUST expose an HTTP handshake surface for real-time transport negotiation. | - Clients MUST be able to negotiate the transport type via HTTP before upgrading to a persistent connection.<br>- The negotiation endpoint MUST require authentication via bearer token or access token query parameter. |
| **REQ-015** | Bearer Token Authentication | The Gateway MUST authenticate OpenAI-compatible and SignalR endpoints using bearer tokens. | - Requests with a valid bearer token MUST be accepted.<br>- Requests with a missing or invalid token MUST be rejected with an unauthorized error.<br>- The bearer token MUST be accepted via the Authorization header. |
| **REQ-016** | Loopback Bypass | The Gateway MAY allow unauthenticated requests from the local machine when loopback bypass is enabled. | - IF loopback bypass is enabled, requests originating from the local machine MAY be accepted without a token.<br>- IF loopback bypass is disabled, all requests MUST present a valid token regardless of origin. |
| **REQ-017** | Canvas Capability Token Auth | Canvas endpoints MUST authenticate requests using the capability token embedded in the URL path rather than user identity claims. | - The token scope MUST include the originating node identifier and an allowed path prefix.<br>- Requests with a valid, non-expired, in-scope token MUST be served.<br>- Requests with an invalid token MUST return an unauthorized status. |
| **REQ-018** | Consistent Error Responses | The Gateway MUST return consistent, machine-readable error responses for all non-streaming HTTP error conditions. | - Every error response MUST include a machine-readable error code and a human-readable message.<br>- Every error response MUST include a request identifier for correlation.<br>- Error codes MUST distinguish: invalid request, unauthorized, forbidden, not found, conflict, rate limited, runtime error, and upstream unavailable. |
| **REQ-019** | Concurrent Run Conflict | The Gateway MUST reject a new agent request from a caller key that already has an active run in progress. | - The rejection MUST use a conflict error code.<br>- The error response MUST include the request identifier. |
| **REQ-020** | Mind-Derived Agent Identity | The OpenAI-compatible endpoints MUST route requests through the same mind-backed agent runtime used by the SignalR protocol. | - The agent's system message MUST include content from SOUL.md and any agent definition files.<br>- Responses MUST reflect the agent's configured personality and capabilities. |

### 3.3 Edge Cases & Error Handling

*   **Missing or invalid bearer token on `/v1/*`:** The Gateway MUST reject the request with an unauthorized error. No agent processing MUST occur.
*   **Malformed request body on `/v1/*`:** The Gateway MUST reject the request with an invalid-request error before invoking the agent runtime.
*   **Webhook name not configured:** The Gateway MUST return a not-found status. The response MUST NOT reveal which webhook names are configured.
*   **Webhook signature mismatch:** The Gateway MUST return an unauthorized status. The response MUST NOT reveal the expected signature.
*   **Canvas token expired mid-asset-load:** IF a browser requests multiple canvas assets and the token expires between requests, subsequent requests MUST receive an unauthorized status. Previously served assets are unaffected.
*   **Upstream Copilot client unavailable:** IF the Copilot client or model provider is unreachable, the Gateway MUST return an upstream-unavailable error. The readiness endpoint MUST reflect this state.
*   **SSE stream interrupted by runtime error:** The Gateway MUST emit a terminal error event and close the connection. The client MUST NOT be left in a waiting state.
*   **Concurrent agent run conflict:** IF a second request arrives for the same caller key while a run is active, the Gateway MUST reject it with a conflict error.
*   **Path traversal on canvas requests:** The Gateway MUST reject requests containing encoded traversal sequences, double-dot segments, or absolute path escapes. The rejection MUST NOT disclose the filesystem layout.

## 4. Non-Functional Requirements (Constraints)
*Rule: All constraints must be quantifiable and measurable.*

*   **Performance:** The Gateway MUST respond to health probe requests within 200 milliseconds. The Gateway MUST deliver SSE streaming events to the HTTP client within 100ms of the event being produced by the agent runtime (excluding network latency). Agent reply streaming MUST begin within 5 seconds of receiving a user message under normal load.
*   **Scalability:** The Gateway MUST support concurrent HTTP API requests without degradation of streaming performance for active SSE connections.
*   **Security & Compliance:** All authenticated endpoints MUST reject requests with missing or invalid credentials. Canvas capability tokens MUST use a minimum of 144 bits of entropy. Path-traversal protections MUST prevent access to files outside the allowed scope.
*   **Platform / Environment:** The Gateway HTTP surface MUST operate on Windows, macOS, and Linux. The Gateway MUST bind to a configurable host and port (default: `127.0.0.1:18789`).
*   **Availability:** Health and readiness endpoints MUST remain responsive even when the agent runtime is degraded or unavailable.

## 5. User Experience (UX) & Design

*   **Design Assets:** Not applicable — the Gateway HTTP surface is an API consumed by automation clients, OpenAI-compatible SDKs, webhook integrations, and infrastructure tooling. There is no user-facing UI.
*   **Prototypes:** OpenAI-compatible client libraries and webhook provider integrations will serve as the primary interaction model. Reference usage will be documented in the Gateway operator guide.
*   **Copy & Messaging:** Error responses MUST use consistent, machine-readable error codes and descriptive human-readable messages. Health and readiness responses MUST use consistent status identifiers across all probe endpoints.

## 6. Out of Scope (Anti-Goals)
*Rule: Explicitly state what we are NOT building to prevent scope creep.*

*   A full REST replacement for all SignalR hub methods is out of scope — the HTTP surface covers health, OpenAI-compatible APIs, webhooks, and canvas serving only.
*   Long-lived HTTP sessions independent of the Copilot SDK session model are out of scope.
*   Public multi-tenant routing across multiple mind roots is out of scope.
*   Channel adapter logic (how webhook payloads are interpreted per provider) is out of scope for this specification — it is a separate concern.
*   Asynchronous callback receipts for long-running webhook processing are out of scope for v1.
*   The specific SignalR hub method contracts are out of scope — they are defined in the [Gateway Protocol product spec](gateway-protocol.md).

## 7. Dependencies & Assumptions

### 7.1 Dependencies

*   **MsClaw.Core library:** The HTTP surface depends on MsClaw.Core for mind validation, identity loading, mind file reading, and Copilot client creation.
*   **GitHub Copilot SDK:** The agent runtime depends on the Copilot SDK for model inference, session management, and tool execution.
*   **GitHub Copilot CLI:** The Copilot SDK requires the `copilot` CLI binary to be installed and available on PATH.
*   **Gateway Protocol:** The SignalR transport negotiation endpoint depends on the Gateway Protocol specification for hub and method contracts.

### 7.2 Assumptions

*   We assume one Gateway process per host — there is no requirement for multi-instance coordination or shared session state.
*   We assume the mind directory is on local disk accessible to the Gateway process.
*   We assume OpenAI-compatible callers will use standard client libraries that conform to the OpenAI chat completions and responses API contracts.
*   We assume webhook providers will deliver payloads over HTTPS in production environments; transport-level encryption is the responsibility of the deployment configuration, not this specification.
*   We assume canvas capability tokens are generated by the Gateway at canvas-creation time and are not caller-supplied.
