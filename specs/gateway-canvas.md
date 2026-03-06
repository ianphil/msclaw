# Product Specification: Gateway Canvas Host

**Document Owner(s):** MsClaw Team  
**Status:** Draft  
**Target Release:** TBD  
**Link to Technical Spec:** [msclaw-canvas.md](msclaw-canvas.md)  

## Version History
*Rule: No silent mutations. All changes after baseline must be recorded here.*

| Version | Date | Author | Description of Changes |
| :--- | :--- | :--- | :--- |
| 1.0 | 2026-03-03 | MsClaw Team | Initial Draft — derived from technical canvas spec |
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

MsClaw agents need a way to present interactive visual surfaces (canvases) to device nodes and receive user interactions back from those surfaces. Without a dedicated canvas subsystem, the Gateway has no mechanism for an agent to show a UI on a node's screen, collect user input from that UI, or manage the lifecycle of the displayed content.

The [OpenClaw project](https://github.com/openclaw/openclaw) addressed this with its `canvas` and `a2ui` subsystem, but that design exposes broad endpoint surfaces, uses separate unauthenticated routes for bridge assets, and relies on conventions that do not align with MsClaw's Gateway contracts, mind-driven orchestration, or node invocation model.

### 1.2 Business Value

- **Agent-driven UI control:** Agents can present dashboards, forms, and interactive tools to device nodes without requiring a separate UI hosting system.
- **Scoped, time-limited access:** Canvas assets are served through per-node capability tokens rather than broad gateway credentials, limiting the blast radius of a compromised token.
- **Portable interaction model:** A compatibility bridge layer allows canvases originally built for OpenClaw to work under MsClaw, reducing migration effort for existing canvas applications.
- **Developer velocity:** Optional live reload during development eliminates the build-refresh cycle when iterating on canvas content.

### 1.3 Success Metrics (KPIs)

*   **Metric 1:** An agent MUST be able to present a canvas on a connected node, receive at least one user interaction from that canvas, and process it within the agent session — end to end — using only published Gateway operations.
*   **Metric 2:** A canvas capability token MUST NOT grant access to any Gateway resource outside the canvas asset surface.
*   **Metric 3:** All canvas commands defined in this specification MUST be exercisable through the existing node invocation mechanism with no out-of-band communication.

## 2. Target Audience & User Personas

| Persona Name | Description | Key Needs for this Feature |
| :--- | :--- | :--- |
| **Agent (Mind-Driven)** | An AI agent orchestrated by the Gateway, operating under a mind's personality and rules | Needs to present, navigate, hide, and evaluate content on node screens; needs to receive user interaction events from canvases to continue tool and session workflows. |
| **Node (Device)** | A device endpoint (iOS, macOS, Android, headless) registered with the Gateway, rendering canvases in a WebView | Needs to receive canvas commands, open a WebView pointed at a capability-scoped URL, bridge user actions back to the Gateway, and refresh expired capability tokens. |
| **Operator** | A human user managing the agent and its connected nodes | Needs assurance that canvas access is scoped per-node and time-limited, and that node screens are only controlled by the authorized agent. |
| **Canvas Developer** | A developer building interactive canvas applications for agents | Needs a predictable asset-serving surface, a bridge for sending user actions, and live reload during development. |

## 3. Functional Requirements (The "What")
*Rule: Use unique identifiers (e.g., REQ-001) for traceability.*

### 3.1 Core User Flows

1.  **Present Canvas:** The agent instructs the Gateway to display a canvas on a specific node. The Gateway mints or refreshes a capability token, constructs a scoped URL, and sends a present command to the node. The node opens a WebView and loads the canvas from the Gateway.
2.  **User Interaction:** A user interacts with an element in the canvas WebView. The bridge layer captures the action and sends it upstream through the node's connection to the Gateway. The Gateway routes the interaction event into the active agent session.
3.  **Canvas Navigation:** The agent instructs the Gateway to navigate an already-open canvas to a different page. The node updates the WebView without closing and reopening it.
4.  **Canvas Dismissal:** The agent instructs the Gateway to hide the canvas on a node. The node closes the WebView.
5.  **Capability Token Refresh:** A node's capability token approaches or reaches expiration. The node requests a new token from the Gateway and receives updated credentials and a new scoped URL base.
6.  **Live Reload (Development):** A developer modifies a canvas asset file on disk. The Gateway detects the change and signals connected canvas pages to reload.

### 3.2 Feature Requirements & Acceptance Criteria

| ID | Feature | Description | Acceptance Criteria (Pass/Fail) |
| :--- | :--- | :--- | :--- |
| **REQ-001** | Canvas Asset Serving | The Gateway MUST serve canvas assets from a configured directory through a capability-token-scoped URL path. | - A request with a valid capability token MUST receive the requested asset.<br>- A request with an expired or invalid token MUST be rejected.<br>- A request for a non-existent asset MUST return a not-found response.<br>- Served HTML assets MUST NOT be cached by the client (cache headers MUST prevent storage). |
| **REQ-002** | Path Traversal Protection | The Gateway MUST prevent access to files outside the configured canvas root directory. | - A request containing path traversal sequences MUST be rejected.<br>- The resolved file path MUST fall within the canvas root directory.<br>- Rejection responses MUST NOT disclose the filesystem structure. |
| **REQ-003** | Capability Token Minting | The Gateway MUST generate a per-node capability token when a canvas is first presented to a node or when a node requests a token refresh. | - Each token MUST contain at least 144 bits of entropy.<br>- Each token MUST be bound to a single connected node identity.<br>- Each token MUST have a default time-to-live of 10 minutes. |
| **REQ-004** | Capability Token Validation | The Gateway MUST validate the capability token on every canvas asset request. | - The token MUST exist and MUST NOT be expired.<br>- The token MUST be associated with an active node session.<br>- On successful validation, the token's expiration MUST be extended (sliding window). |
| **REQ-005** | Capability Token Revocation | Capability tokens MUST become invalid when their conditions are no longer met. | - A token MUST become invalid when its time-to-live expires without renewal.<br>- A token MUST become invalid when the associated node disconnects.<br>- A token MUST become invalid when the node rotates to a new token. |
| **REQ-006** | Present Command | The agent MUST be able to instruct a specific node to open and display a canvas. | - The command MUST be delivered through the existing node invocation mechanism.<br>- The command MUST include the capability-scoped URL for the canvas.<br>- The node MUST open a WebView and load the specified canvas. |
| **REQ-007** | Hide Command | The agent MUST be able to instruct a specific node to close an active canvas. | - The command MUST be delivered through the existing node invocation mechanism.<br>- The node MUST close the active WebView. |
| **REQ-008** | Navigate Command | The agent MUST be able to instruct a specific node to navigate an open canvas to a different URL. | - The command MUST be delivered through the existing node invocation mechanism.<br>- The node MUST navigate the existing WebView to the new URL without closing and reopening it. |
| **REQ-009** | Evaluate Command | The agent MUST be able to execute a script within a node's active canvas WebView and receive a result. | - The command MUST be delivered through the existing node invocation mechanism.<br>- The result MUST indicate success or failure.<br>- On failure, the result MUST include an error description. |
| **REQ-010** | Snapshot Command | The agent MUST be able to capture a screenshot of a node's active canvas. | - The command MUST be delivered through the existing node invocation mechanism.<br>- The agent MAY specify a format and quality for the captured image. |
| **REQ-011** | Push UI Event | The agent MUST be able to push a UI event payload to a node's active canvas. | - The command MUST be delivered through the existing node invocation mechanism.<br>- The canvas MUST receive the event data. |
| **REQ-012** | Push Streamed UI Events | The agent MUST be able to push multiple UI events to a node's active canvas in a single operation. | - The command MUST be delivered through the existing node invocation mechanism.<br>- All events in the batch MUST be delivered to the canvas in order. |
| **REQ-013** | Reset UI State | The agent MUST be able to reset the UI state of a node's active canvas. | - The command MUST be delivered through the existing node invocation mechanism.<br>- The canvas MUST return to its initial state after the reset. |
| **REQ-014** | User Action Bridge | User interactions within a canvas MUST be relayed from the node back to the Gateway and into the active agent session. | - The canvas MUST provide a mechanism for page content to emit user actions.<br>- User actions MUST be delivered to the Gateway through the node's connection.<br>- The Gateway MUST route user actions into the active agent session as input events.<br>- Each user action MUST carry an action identifier, action name, and timestamp. |
| **REQ-015** | OpenClaw Compatibility Bridge | The canvas SHOULD include a compatibility layer that allows canvas applications built for OpenClaw to send user actions without modification. | - Canvas pages using OpenClaw-style bridge calls MUST have their actions captured and forwarded identically to native MsClaw bridge calls. |
| **REQ-016** | Capability Token Refresh | A node MUST be able to request a new capability token from the Gateway before or after the current token expires. | - The refresh response MUST include the new token, its expiration time, and the updated scoped URL base.<br>- The previous token MUST become invalid after the new token is issued. |
| **REQ-017** | A2UI Bridge Injection | The Gateway MUST support automatic injection of the interaction bridge into served HTML canvas pages. | - When bridge injection is enabled, HTML responses MUST include the bridge without the canvas developer manually adding it.<br>- When bridge injection is disabled, HTML responses MUST be served unmodified. |
| **REQ-018** | Live Reload (Development) | The Gateway MAY support a live reload mode for canvas development. | - When live reload is enabled and a file in the canvas root changes, all connected canvas pages MUST reload.<br>- When live reload is disabled, file changes MUST NOT trigger any reload signal. |
| **REQ-019** | Canvas Feature Toggle | The Gateway MUST allow the canvas subsystem to be enabled or disabled through configuration. | - When canvas is disabled, all canvas asset requests MUST be rejected.<br>- When canvas is disabled, canvas commands MUST NOT be available to the agent. |

### 3.3 Edge Cases & Error Handling

*   **Expired capability token on asset request:** The Gateway MUST reject the request with an authentication error. The node MUST be able to request a fresh token and retry.
*   **Invalid or malformed token format:** The Gateway MUST reject the request with an authentication error. The response MUST NOT disclose details about the expected token format.
*   **Missing canvas asset:** The Gateway MUST return a not-found error. The response MUST NOT disclose the filesystem structure or canvas root path.
*   **Node disconnected during canvas command:** The Gateway MUST return an unavailable error to the agent, consistent with existing node invocation failure behavior.
*   **Script evaluation error in WebView:** The evaluate command result MUST indicate failure and MUST include the error description. The canvas MUST remain open and functional after the error.
*   **Canvas root directory not configured or missing:** The Gateway MUST reject all canvas operations and MUST log a descriptive error. The Gateway itself MUST NOT fail to start — only the canvas subsystem MUST be unavailable.
*   **Concurrent token refresh:** IF a node issues multiple refresh requests before receiving the first response, the Gateway MUST ensure only one valid token exists for that node at any time. Subsequent refreshes MUST invalidate earlier tokens.

## 4. Non-Functional Requirements (Constraints)
*Rule: All constraints must be quantifiable and measurable.*

*   **Performance:** Canvas asset responses for files under 1 MB MUST be served within 50ms of token validation (excluding network latency).
*   **Security — Token Entropy:** Capability tokens MUST contain at least 144 bits of cryptographically random data.
*   **Security — Token Comparison:** Token validation MUST use constant-time comparison to prevent timing-based attacks.
*   **Security — Token Logging:** Raw capability tokens MUST NOT appear in any log output at any log level.
*   **Security — Access Scope:** A valid capability token MUST grant access only to the canvas asset surface. It MUST NOT grant access to any other Gateway operation.
*   **Platform / Environment:** The canvas subsystem MUST function on Windows, macOS, and Linux, consistent with the Gateway's supported platforms.
*   **Scalability:** The canvas subsystem MUST support concurrent canvas sessions across all connected nodes without degrading asset-serving performance for any individual node.

## 5. User Experience (UX) & Design

*   **Design Assets:** Not applicable — the canvas host is a machine-to-machine and device interface. Canvas content is rendered within node WebViews, not through a traditional user-facing application. Visual design of individual canvas applications is the responsibility of the canvas developer, not this subsystem.
*   **Prototypes:** The interaction model is defined by the canvas command surface (present, hide, navigate, evaluate, snapshot, push events, reset) and the user action bridge. No standalone prototype applies.
*   **Copy & Messaging:** Error responses from the canvas asset surface MUST use consistent, machine-readable status codes. Human-readable error messages SHOULD be included but MUST NOT disclose internal paths, token values, or filesystem structure.

## 6. Out of Scope (Anti-Goals)
*Rule: Explicitly state what we are NOT building to prevent scope creep.*

*   Multi-mind canvas roots (serving different canvas directories for different minds within a single Gateway process) are out of scope.
*   A full browser sandboxing policy engine for canvas WebViews is out of scope.
*   Cross-node shared canvas state synchronization (one canvas surface shared across multiple nodes) is out of scope.
*   A separate, unauthenticated endpoint for bridge assets is out of scope — all bridge assets are served under the capability-token-scoped route.
*   Serving canvas assets from remote origins (external URLs) is out of scope — only Gateway-hosted assets are supported.
*   The specific persistence mechanism for capability tokens (in-memory vs. distributed store) is an implementation detail and out of scope for this product specification.

## 7. Dependencies & Assumptions

### 7.1 Dependencies

*   **MsClaw Gateway Protocol:** The canvas subsystem depends on the Gateway's existing node invocation mechanism to deliver canvas commands to nodes and receive results. See [gateway-protocol.md](gateway-protocol.md).
*   **Node Registration & Connection:** Nodes MUST be registered and connected to the Gateway before canvas commands can target them.
*   **Mind System:** The agent driving canvas interactions operates under a mind's personality and rules, loaded by the Gateway through MsClaw.Core.
*   **Node WebView Capability:** Target nodes MUST be capable of rendering a WebView to display canvas content.

### 7.2 Assumptions

*   We assume one canvas root directory per Gateway process — there is no requirement for per-mind or per-agent canvas isolation within a single process.
*   We assume canvas assets are stored on local disk accessible to the Gateway process.
*   We assume nodes have a WebView runtime available (or equivalent rendering surface) for displaying canvas HTML content.
*   We assume the existing node invocation and event model in the Gateway protocol is sufficient to carry all canvas commands and user action events without protocol-level changes.
*   We assume the OpenClaw-compatible bridge helper names MAY be deprecated in a future version, but MUST be supported in this release.
