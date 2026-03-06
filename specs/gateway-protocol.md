# Product Specification: Gateway Protocol

**Document Owner(s):** MsClaw Team  
**Status:** Draft  
**Target Release:** TBD  
**Link to Technical Spec:** [msclaw-gateway-protocol.md](msclaw-gateway-protocol.md)  

## Version History
*Rule: No silent mutations. All changes after baseline must be recorded here.*

| Version | Date | Author | Description of Changes |
| :--- | :--- | :--- | :--- |
| 1.0 | 2026-03-03 | MsClaw Team | Initial Draft — derived from technical protocol spec |
| 1.1 | 2026-03-03 | MsClaw Team | Reconciled with Gateway spec — aligned transport, scalability, shutdown, streaming; added REQ-019 through REQ-021 |
| 1.2 | 2026-03-03 | MsClaw Team | Reconciled with Gateway spec — added authorization policy names, pairing specifics (public key, signed challenge), token logging prohibition, agent runtime failure edge case |
| 1.3 | 2026-03-06 | MsClaw Team | Decision: HTTP surface will adopt OpenResponses spec (openresponses.org) for API consumers; SignalR remains the protocol for real-time UI/node communication |
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

MsClaw agents need a standard way to communicate with multiple client types (CLI, web UI, desktop apps) and device nodes (iOS, macOS, headless). Without a defined protocol, each client would implement its own ad-hoc communication layer — leading to duplicated logic, inconsistent behavior, and no shared reconnection or authentication story.

The [OpenClaw project](https://github.com/openclaw/openclaw) solved a similar problem with a raw WebSocket protocol, but that approach requires hand-rolled message framing, manual heartbeats, client-managed reconnection, and runtime JSON validation. MsClaw needs a protocol that eliminates these burdens while preserving the same real-time, streaming interaction model.

### 1.2 Business Value

- **Single integration surface:** One protocol for all client and device types reduces the cost of building new clients.
- **Reliability by default:** Built-in reconnection and transport fallback (WebSocket → Server-Sent Events → Long Polling) means fewer dropped sessions and less client-side error handling.
- **Type safety at the boundary:** Compile-time checked contracts between clients and the Gateway eliminate an entire class of runtime serialization bugs.
- **Faster time-to-market for new platforms:** Adding a new client (e.g., a mobile app or automation bot) requires only implementing the typed client contract — no protocol-level work.

### 1.3 Success Metrics (KPIs)

*   **Metric 1:** All defined client-to-server and server-to-client operations MUST be exercisable through the protocol without any out-of-band communication.
*   **Metric 2:** A new client implementation MUST be able to connect, authenticate, and receive streamed agent responses using only the published contract — no undocumented messages.
*   **Metric 3:** Automatic reconnection MUST restore group membership and resume state without user intervention.

## 2. Target Audience & User Personas

| Persona Name | Description | Key Needs for this Feature |
| :--- | :--- | :--- |
| **Operator** | A human user interacting with the agent via CLI, web UI, or desktop app | Needs to send messages, receive streamed agent responses, manage sessions, and approve agent actions — all in real time. |
| **Node (Device)** | An automated device endpoint (iOS, macOS, Android, headless) registered with the Gateway | Needs to receive targeted task invocations from the agent, return results, and maintain a persistent connection for availability. |
| **Automation Client** | A script or bot connecting to the Gateway for programmatic agent interaction | Needs a well-defined, typed contract to send requests and consume agent events without a GUI. |

## 3. Functional Requirements (The "What")
*Rule: Use unique identifiers (e.g., REQ-001) for traceability.*

### 3.1 Core User Flows

1.  **Connect & Authenticate:** A client connects to the Gateway, authenticates via a bearer token (header or query string) or a device token (post-pairing), and receives an initial presence snapshot of the current system state.
2.  **Agent Conversation (Streaming):** An operator sends a message to the agent. The Gateway streams back a sequence of events — lifecycle markers, assistant text deltas, and tool execution signals — as they are produced. The stream terminates with a lifecycle-end marker.
3.  **Node Invocation:** The agent (via the Gateway) sends a task request to a specific device node. The node executes the task and returns a result through the Gateway.
4.  **Device Pairing:** A new device requests to pair with the Gateway. An operator reviews and approves or rejects the request. On approval, the device receives a device token for future authentication.
5.  **Exec Approval:** The agent requests permission to perform a privileged action. An operator receives the approval request and resolves it (approve or deny). The resolution is broadcast to relevant clients.

### 3.2 Feature Requirements & Acceptance Criteria

| ID | Feature | Description | Acceptance Criteria (Pass/Fail) |
| :--- | :--- | :--- | :--- |
| **REQ-001** | Transport Negotiation | The Gateway MUST support automatic transport negotiation, selecting the most capable real-time transport available and falling back to less capable alternatives without client-side configuration. | - The Gateway MUST negotiate the best available transport with each connecting client.<br>- IF the most capable transport is unavailable, the connection MUST fall back to a less capable alternative without client-side configuration. |
| **REQ-002** | Authentication | The Gateway MUST authenticate every connection before granting access to any operations. | - Connections with a valid bearer token (header or query string) MUST be accepted.<br>- Connections with a valid device token MUST be accepted.<br>- Connections with no token or an invalid token MUST be rejected. |
| **REQ-003** | Role-Based Group Assignment | On connection, the Gateway MUST assign each client to the appropriate group based on its authenticated role and enforce authorization policies. | - Clients with the operator role MUST be placed in the "operators" group.<br>- Clients with the node role MUST be placed in the "nodes" group.<br>- Node clients with a device identifier MUST also be placed in a per-device group.<br>- Each operation MUST enforce its required authorization policy (read, write, admin, or approvals). |
| **REQ-004** | Initial Presence Snapshot | Upon successful connection and group assignment, the Gateway MUST push a presence snapshot to the newly connected client. | - The presence snapshot MUST include the current list of connected operators and nodes.<br>- The snapshot MUST arrive before the client sends any requests. |
| **REQ-005** | Agent Streaming | When an operator invokes the agent with a message, the Gateway MUST stream agent events back to the caller as they are produced. | - The stream MUST have a clear start and end lifecycle.<br>- The stream MUST include assistant text events as the model produces output.<br>- The stream MUST include tool execution events (start and end) when the agent invokes tools.<br>- IF the agent runtime encounters an error mid-stream, the Gateway MUST emit an error event and terminate the stream. |
| **REQ-006** | Agent Identity from Mind | The Gateway MUST load the agent's personality and system message from the configured mind directory on the local disk. | - The system message MUST include content from SOUL.md.<br>- The system message MUST include content from any agent definition files in the mind.<br>- IF the mind directory is missing or invalid, the Gateway MUST refuse to start. |
| **REQ-007** | Session Management | The Gateway MUST support creating, listing, previewing, resetting, and deleting agent sessions. | - An operator MUST be able to list all sessions.<br>- An operator MUST be able to preview a specific session's history.<br>- An operator MUST be able to reset or delete a session. |
| **REQ-008** | Chat Operations | The Gateway MUST support retrieving chat history and aborting an in-progress agent response. | - An operator MUST be able to retrieve the full chat history for a session.<br>- An operator MUST be able to abort an active agent response, and the stream MUST terminate. |
| **REQ-009** | Node Invocation | The Gateway MUST allow the agent to invoke a specific registered node to perform a task and return a result. | - The invoke request MUST be delivered only to the targeted node (not broadcast).<br>- The node MUST be able to return a result through the Gateway.<br>- IF the targeted node is not connected, the Gateway MUST return an error. |
| **REQ-010** | Device Pairing | The Gateway MUST support a pairing workflow for new devices. | - A new device MUST present a public key during the pairing request.<br>- All operators MUST be notified of the pairing request.<br>- An operator MUST be able to approve or reject the request.<br>- On approval, the device MUST receive credentials for future authentication.<br>- Subsequent connections from a paired device MUST use a signed challenge for authentication. |
| **REQ-011** | Exec Approvals | The Gateway MUST support an approval workflow for privileged agent actions. | - When the agent requires approval, all operators MUST receive an approval request event.<br>- An operator MUST be able to approve or deny the request.<br>- The resolution MUST be broadcast to all operators. |
| **REQ-012** | Mind Validation | The Gateway MUST expose an operation to validate the current mind directory structure. | - Validation MUST check for the presence of SOUL.md and the working-memory directory.<br>- The result MUST indicate pass/fail with specific error details on failure. |
| **REQ-013** | Mind File Access | The Gateway MUST expose an operation to read files from the mind directory. | - File reads MUST be restricted to paths within the mind directory (no path traversal).<br>- Requests for paths outside the mind directory MUST be rejected. |
| **REQ-014** | Model Listing | The Gateway MUST expose an operation to list available models. | - The response MUST include all models the agent runtime can use. |
| **REQ-015** | Configuration | The Gateway MUST expose operations to read and update runtime configuration. | - Any authenticated operator MUST be able to read the current configuration.<br>- Only operators with admin-level authorization MUST be able to update configuration. |
| **REQ-016** | Node Registration | The Gateway MUST allow authenticated node clients to register themselves as available for invocations. | - A registered node MUST appear in the node list.<br>- A disconnected node MUST be removed from the available node list. |
| **REQ-017** | Automatic Reconnection | The Gateway MUST support automatic client reconnection with state recovery. | - IF a client is temporarily disconnected, the transport layer MUST attempt reconnection automatically.<br>- On reconnection, the client MUST be reassigned to its previous groups. |
| **REQ-018** | Graceful Shutdown | The Gateway MUST shut down cleanly, notifying clients and completing in-flight work. | - All connected clients MUST receive a shutdown notification before the Gateway terminates connections.<br>- The Gateway MUST stop accepting new connections.<br>- Active operations MUST be allowed to complete within a configurable timeout before connections are closed. |
| **REQ-019** | Loopback Bypass | The Gateway MAY allow unauthenticated connections from the local machine when loopback bypass is enabled. | - IF loopback bypass is enabled, connections originating from the local machine MAY be accepted without a token.<br>- IF loopback bypass is disabled, all connections MUST present a valid token regardless of origin. |
| **REQ-020** | Canvas Rendering | The Gateway MUST allow the agent to push interactive applications to node screens and relay user interactions back to the agent. | - The agent MUST be able to instruct a specific node to display an interactive canvas.<br>- The Gateway MUST serve canvas assets via time-limited, per-node capability tokens (minimum 144 bits, 10-minute sliding expiry).<br>- User interactions within the canvas MUST be relayed back to the agent as input events.<br>- The agent MUST be able to instruct a node to close an active canvas. |
| **REQ-021** | Skills Discovery & Invocation | The Gateway MUST expose operations for clients to discover and invoke the agent's available skills. | - Clients MUST be able to retrieve a list of registered skills (bundled and workspace-defined).<br>- Skill invocations through the protocol MUST return results to the caller. |

### 3.3 Edge Cases & Error Handling

*   **Invalid or expired token:** The Gateway MUST reject the connection and return an authentication error. The client MUST NOT receive any events or be able to invoke any operations.
*   **Mind directory missing or corrupt:** The Gateway MUST refuse to start and MUST log a descriptive error indicating which validation check failed.
*   **Agent runtime fails to start:** The Gateway MUST report the failure via the readiness probe (non-200 status) and MUST log the error. The Gateway process itself MUST remain alive so that the liveness probe continues to respond.
*   **Agent streaming error:** IF the agent runtime encounters an error mid-stream, the Gateway MUST emit a lifecycle-error event and terminate the stream. The client MUST NOT be left waiting indefinitely.
*   **Node invocation timeout:** IF a targeted node does not respond within the configured timeout, the Gateway MUST return a timeout error to the caller.
*   **Concurrent abort:** IF an operator aborts an agent response while the stream is active, the Gateway MUST terminate the stream and emit a lifecycle-end event.
*   **Path traversal on mind file read:** IF a client requests a file path that resolves outside the mind directory, the Gateway MUST reject the request and MUST NOT disclose the filesystem structure.

## 4. Non-Functional Requirements (Constraints)
*Rule: All constraints must be quantifiable and measurable.*

*   **Performance:** The Gateway MUST deliver agent streaming events to the client within 100ms of the event being produced by the agent runtime (excluding network latency).
*   **Scalability:** The Gateway MUST support a configurable number of concurrent operator connections and node connections without degradation of streaming performance.
*   **Security & Compliance:** All connections MUST be authenticated. Operations MUST be authorized based on the client's role and scopes. Gateway tokens MUST NOT be logged in plaintext. Path-traversal protections on mind file reads MUST prevent access to files outside the mind directory.
*   **Platform / Environment:** The Gateway MUST run on Windows, macOS, and Linux. Clients MUST be implementable on any platform that supports the chosen transport protocol.
*   **Availability:** The Gateway MUST bind to a configurable host and port (default: `127.0.0.1:18789`). The Gateway MUST expose a health check operation that returns current status.

## 5. User Experience (UX) & Design

*   **Design Assets:** Not applicable — the Gateway Protocol is a machine-to-machine interface, not a user-facing UI.
*   **Prototypes:** Client libraries and reference CLI implementation will serve as the interaction model.
*   **Copy & Messaging:** Event type names and error messages MUST be descriptive and consistent. Error responses MUST include a machine-readable error code and a human-readable message.

## 6. Out of Scope (Anti-Goals)
*Rule: Explicitly state what we are NOT building to prevent scope creep.*

*   Multi-mind (multi-agent) support within a single Gateway instance is out of scope.
*   Inter-node communication (node A invoking node B through the Gateway) is out of scope.
*   Channel adapters (WhatsApp, Telegram, Discord) are out of scope for the protocol layer — they are a separate concern.
*   Scheduled/cron-based agent runs are out of scope.
*   A REST HTTP API alongside the real-time protocol is out of scope for this specification — the HTTP surface will implement the [OpenResponses](https://www.openresponses.org/specification) spec and is covered separately in [gateway-http-surface.md](gateway-http-surface.md).
*   The specific persistence mechanism for sessions (file-based vs. database) is an implementation detail and out of scope for this product specification.

## 7. Dependencies & Assumptions

### 7.1 Dependencies

*   **MsClaw.Core library:** The Gateway depends on MsClaw.Core for mind validation, identity loading, mind file reading, and Copilot client creation.
*   **GitHub Copilot SDK:** The agent runtime depends on the Copilot SDK for model inference, session management, and tool execution.
*   **GitHub Copilot CLI:** The Copilot SDK requires the `copilot` CLI binary to be installed and available on PATH.

### 7.2 Assumptions

*   We assume one Gateway process per host — there is no requirement for multi-instance coordination.
*   We assume the mind directory is on local disk accessible to the Gateway process.
*   We assume all clients will implement the typed client contract (receiving pushed events) rather than relying on polling-only patterns.
*   We assume the operator-facing authorization model uses scope-based policies (read, write, admin, approvals) and the node role is a distinct authorization category.
