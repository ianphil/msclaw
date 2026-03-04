# Product Specification: Agent Runtime

**Document Owner(s):** MsClaw Team  
**Status:** Draft  
**Document Level:** Feature  
**Target Release:** TBD  
**Link to Technical Spec:** [msclaw-agent-runtime.md](msclaw-agent-runtime.md)  

## Version History
*Rule: No silent mutations. All changes after baseline must be recorded here.*

| Version | Date | Author | Description of Changes |
| :--- | :--- | :--- | :--- |
| 1.0 | 2026-03-03 | MsClaw Team | Initial Draft — derived from technical agent runtime spec |
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

MsClaw needs an embedded agent engine that accepts a user message, loads the agent's identity from its mind, delegates inference to a model, executes tools, and streams the reply back to the caller. Without a defined runtime, every consumer (SignalR hub, HTTP endpoint, channel adapter) would implement its own inference loop, tool dispatch, and session bookkeeping — leading to duplicated logic, inconsistent streaming behavior, and no shared concurrency or error-recovery story.

The [OpenClaw project](https://github.com/openclaw/openclaw) solved a similar problem with a bespoke TypeScript runner (`pi-embedded-runner`), but that approach required hand-rolled inference loops, manual session persistence, custom tool-abort wiring, and ad-hoc concurrency control. MsClaw needs a runtime that delegates inference and tool execution to the Copilot SDK while providing a uniform event stream, concurrency management, and extensible tool registration.

### 1.2 Business Value

- **Single execution surface:** One runtime serves all delivery channels (SignalR, HTTP SSE, channel adapters), eliminating duplicated agent logic.
- **Streaming by default:** Every agent response is a sequence of events — lifecycle markers, text deltas, tool signals — enabling real-time UX across all clients without polling.
- **Extensible tool model:** Bundled skills, workspace skills, and node-provided capabilities are all registered through one mechanism, making the agent's abilities composable without code changes.
- **Concurrency safety:** Per-caller and global concurrency controls prevent runaway resource consumption and conflicting simultaneous runs.
- **Mind-backed identity:** The agent's personality is loaded from the mind directory, meaning identity changes require editing files — not redeploying code.

### 1.3 Success Metrics (KPIs)

*   **Metric 1:** An operator MUST be able to send a message and receive a streamed agent response through any delivery channel using only the runtime's event stream — no channel-specific agent logic.
*   **Metric 2:** The runtime MUST enforce concurrency limits such that no caller can have more than one active agent run at a time, and global concurrency MUST NOT exceed the configured maximum.
*   **Metric 3:** Tools from all three sources (bundled, workspace, node) MUST be discoverable and invocable by the agent within a single session without additional configuration per source.

## 2. Target Audience & User Personas

| Persona Name | Description | Key Needs for this Feature |
| :--- | :--- | :--- |
| **Operator** | A human user conversing with the agent via any connected client (CLI, web, desktop) | Needs to send messages, receive streamed responses in real time, abort in-progress runs, and manage sessions (list, reset, delete). |
| **Gateway Consumer** | A delivery channel (SignalR hub, HTTP endpoint, channel adapter) that routes messages to the agent | Needs a single entry point to submit a message and consume a uniform event stream, without managing inference, tools, or sessions directly. |
| **Mind Author** | A human who defines the agent's personality, workspace skills, and knowledge | Needs the runtime to load identity from the mind directory, discover workspace skills, and make bundled tools available — all without code changes. |
| **Node (Device)** | An automated device endpoint registered with the Gateway | Needs its capabilities to be registered as tools the agent can invoke, and needs invocation requests routed to it when the agent calls those tools. |

## 3. Functional Requirements (The "What")
*Rule: Use unique identifiers (e.g., REQ-001) for traceability.*

### 3.1 Core User Flows

1.  **Agent Conversation (Streaming):** An operator sends a message. The runtime resolves or creates a session, assembles context from the mind, sends the message to the model, and streams back a sequence of events — lifecycle start, assistant text deltas, tool execution signals, final message, lifecycle end.
2.  **Tool Invocation:** During inference, the model requests a tool execution. The runtime dispatches the call to the appropriate handler (bundled, workspace, or node), returns the result to the model, and emits tool start/end events to the caller.
3.  **Session Management:** An operator lists sessions, previews a session's history, resets a session (clearing history for a fresh start), or deletes a session entirely.
4.  **Abort:** An operator aborts an in-progress agent run. The runtime cancels the active inference, terminates the event stream, and releases the concurrency slot.
5.  **Identity Reload:** An operator or automated trigger reloads the agent's identity from the mind without restarting the Gateway. New sessions use the updated identity.

### 3.2 Feature Requirements & Acceptance Criteria

| ID | Feature | Description | Acceptance Criteria (Pass/Fail) |
| :--- | :--- | :--- | :--- |
| **REQ-001** | Single Agent Runtime | The Gateway MUST host exactly one agent runtime instance that serves all delivery channels. | - All connected delivery channels (SignalR, HTTP SSE, channel adapters) MUST submit messages through the same runtime.<br>- The runtime MUST NOT be instantiated more than once per Gateway process. |
| **REQ-002** | Mind-Backed Identity | The runtime MUST load the agent's system message from the configured mind directory at startup. | - The system message MUST include content from SOUL.md and any agent instruction files in the mind.<br>- The system message MUST be delivered to the model in append mode, preserving built-in safety guardrails.<br>- IF the mind directory is missing or invalid, the runtime MUST refuse to start. |
| **REQ-003** | Session-Per-Caller | Each caller MUST be assigned an independent session identified by a caller key. | - A new caller key MUST result in a new session being created.<br>- A returning caller key MUST resume the existing session.<br>- Sessions for different caller keys MUST be independent (no shared conversation history). |
| **REQ-004** | Streaming Event Output | All agent output MUST be emitted as a uniform sequence of events with defined lifecycle, assistant, reasoning, and tool streams. | - Every agent run MUST begin with a lifecycle-start event and end with a lifecycle-end event.<br>- Assistant text MUST be emitted as incremental delta events as the model produces output.<br>- A final complete assistant message event MUST be emitted regardless of streaming mode.<br>- Tool invocations MUST emit start and end events with tool name and result. |
| **REQ-005** | Event Ordering | Events within an agent run MUST be sequenced with a monotonically increasing sequence number. | - Each event MUST carry a sequence number greater than the previous event in the same run.<br>- No two events in the same run MAY share a sequence number. |
| **REQ-006** | Per-Caller Concurrency | The runtime MUST enforce that only one agent run is active per caller key at a time. | - IF a second run request arrives for a caller with an active run, the runtime MUST handle it according to the configured concurrency mode (reject, queue, or replace).<br>- In reject mode, the second request MUST be refused.<br>- In queue mode, the second request MUST execute after the active run completes.<br>- In replace mode, the active run MUST be aborted before the new run starts. |
| **REQ-007** | Global Concurrency Limit | The runtime MUST enforce a configurable maximum number of concurrent active runs across all callers. | - Requests beyond the global limit MUST be queued with a configurable timeout.<br>- IF the queue timeout expires, the request MUST be rejected. |
| **REQ-008** | Abort Support | The runtime MUST allow an operator to abort an in-progress agent run. | - An abort request MUST cancel the active inference and terminate the event stream.<br>- The event stream MUST emit a lifecycle-abort event before closing.<br>- The concurrency slot MUST be released after abort. |
| **REQ-009** | Session Listing | The runtime MUST allow operators to list all tracked sessions. | - The list MUST include each session's caller key, creation time, last-used time, and active status. |
| **REQ-010** | Session Deletion | The runtime MUST allow operators to delete a session by caller key. | - After deletion, the caller key MUST be treated as a new caller on the next request.<br>- IF the session has an active run, the run MUST be aborted before deletion. |
| **REQ-011** | Session Reset | The runtime MUST allow operators to reset a session, clearing its history while preserving the caller key mapping. | - After reset, the next message MUST start a fresh conversation with no prior history. |
| **REQ-012** | Bundled Tools | The runtime MUST register built-in tools for reading and writing working memory, listing mind contents, and reading mind files. | - The agent MUST be able to read any file in working memory via a bundled tool.<br>- The agent MUST be able to write to any file in working memory via a bundled tool.<br>- The agent MUST be able to list directories and read files within the mind boundary via bundled tools.<br>- Path-traversal protection on mind reads MUST be enforced (delegated to the mind system). |
| **REQ-013** | Workspace Skills | The runtime MUST discover and register skills defined in the mind directory as agent-invocable tools. | - Skills in the designated skill directories MUST be discovered at startup.<br>- Each discovered skill MUST be registered as an invocable tool available to the agent.<br>- IF a skill's declared requirements are not met (e.g., missing binary), the skill MUST be excluded and the failure logged. |
| **REQ-014** | Node-Provided Tools | The runtime MUST register capabilities provided by connected nodes as agent-invocable tools. | - When a node connects and registers capabilities, those capabilities MUST become available as tools for the agent.<br>- When a node disconnects, its capabilities MUST be removed from the available tool set.<br>- Tool invocations for node capabilities MUST be routed to the specific node that registered them. |
| **REQ-015** | Identity Reload | The runtime MUST support reloading the agent's identity from the mind without restarting the Gateway. | - After reload, new sessions MUST use the updated system message.<br>- Existing active sessions MUST continue with their original system message until they complete. |
| **REQ-016** | Hot Reload (Optional) | The runtime MAY support automatic identity reload when mind files change on disk. | - IF hot reload is enabled, changes to SOUL.md or agent instruction files MUST trigger an identity reload without operator intervention.<br>- IF hot reload is disabled (default), mind file changes MUST NOT take effect until manual reload or Gateway restart. |
| **REQ-017** | Runtime State | The runtime MUST report its current lifecycle state (starting, ready, degraded, stopped). | - During initialization, the state MUST be "starting".<br>- After successful initialization, the state MUST be "ready".<br>- IF a non-fatal issue occurs (e.g., partial skill load failure, failed connectivity check), the state MUST be "degraded".<br>- After shutdown, the state MUST be "stopped". |
| **REQ-018** | Degraded Mode | When in degraded state, the runtime MUST continue accepting requests with reduced capability. | - Existing active runs MUST continue to completion.<br>- New runs MUST be accepted but MAY have reduced tool availability.<br>- The runtime MUST report the degradation reason to connected operators via presence events. |
| **REQ-019** | Run Timeout | The runtime MUST enforce a configurable maximum wall-clock time for a single agent run. | - IF a run exceeds the configured timeout, the runtime MUST abort it and emit a lifecycle-error event.<br>- The concurrency slot MUST be released after timeout. |
| **REQ-020** | Model Selection | The runtime MUST support a configurable default model and per-request model overrides. | - IF the caller specifies a model, the runtime MUST use that model for the session.<br>- IF the caller does not specify a model, the runtime MUST use the configured default. |
| **REQ-021** | File Attachments | The runtime MUST support including file attachments with agent messages. | - Attached files MUST be forwarded to the model as part of the message context.<br>- Each attachment MUST include a file path and an optional display name. |
| **REQ-022** | Caller Context | The runtime MUST support injecting caller metadata (channel name, device ID, display name) into the agent's prompt context. | - Caller context MUST be available to the agent as part of the request context.<br>- The agent MUST be able to use caller context to tailor its response (e.g., identifying which channel the request came from). |
| **REQ-023** | Skills Discovery | The runtime MUST expose an operation for clients to discover the agent's available skills. | - The response MUST include all registered tools from all sources (bundled, workspace, node).<br>- The response MUST distinguish between tool sources. |

### 3.3 Edge Cases & Error Handling

*   **Mind directory missing or corrupt:** The runtime MUST refuse to start. The Gateway MUST log a descriptive error and report non-ready status via health probes.
*   **Agent runtime fails to start (CLI not found):** The Gateway MUST refuse to start and MUST log that the required CLI binary was not found on PATH.
*   **Connectivity check fails at startup:** The runtime MUST start in degraded state. The first incoming request MUST trigger a retry. IF the retry succeeds, the state MUST transition to ready.
*   **Model inference error mid-stream:** The runtime MUST emit a lifecycle-error event, terminate the stream, and release the concurrency slot. The client MUST NOT be left waiting indefinitely.
*   **Tool execution failure:** The error MUST be returned to the model as the tool result. The model decides whether to retry, use an alternative, or inform the user. The runtime MUST emit a tool-error event.
*   **CLI child process crash:** The runtime MUST mark all existing sessions as stale. The next request for each caller MUST create a fresh session. The runtime MUST notify operators of the crash via a presence event.
*   **Abort during tool execution:** The runtime MUST cancel the tool execution, emit a lifecycle-abort event, and release the concurrency slot.
*   **Workspace skill with unmet requirements:** The skill MUST be excluded from the tool set. The runtime MUST log the failure and enter degraded state if any skills fail to load.
*   **Node disconnects during tool invocation:** The runtime MUST return a timeout or unavailable error to the model as the tool result. The node's capabilities MUST be unregistered.

## 4. Non-Functional Requirements (Constraints)
*Rule: All constraints must be quantifiable and measurable.*

*   **Performance:** Agent streaming events MUST be forwarded to the caller within 100ms of being produced by the model (excluding network latency). Identity assembly at startup MUST complete within 500ms for a mind containing up to 20 agent instruction files.
*   **Scalability:** The runtime MUST support a configurable number of concurrent active sessions (default: 10) without degradation of streaming event delivery.
*   **Security & Compliance:** The system message MUST be delivered in append mode, preserving built-in safety guardrails. Working-memory writes MUST be constrained to the working-memory directory. Mind reads MUST enforce path-traversal protection (delegated to the mind system).
*   **Platform / Environment:** The runtime MUST operate on Windows, macOS, and Linux. The runtime MUST require the GitHub Copilot CLI binary on the system PATH.
*   **Availability:** The runtime MUST expose its lifecycle state (starting, ready, degraded, stopped) for health-check consumption. A degraded runtime MUST continue serving requests with reduced capability rather than refusing all traffic.
*   **Compatibility:** The runtime MUST target .NET 10.0 or later.

## 5. User Experience (UX) & Design

*   **Design Assets:** Not applicable — the agent runtime is an internal engine, not a user-facing interface.
*   **Interaction Model:** Delivery channels (SignalR, HTTP SSE, channel adapters) submit messages to the runtime and consume a uniform event stream. The runtime is the single execution surface behind all agent interactions. No channel implements its own inference loop.
*   **Copy & Messaging:** Lifecycle events MUST use descriptive type names (`start`, `end`, `error`, `abort`). Error events MUST include a human-readable message. Tool events MUST include the tool name and result summary.

## 6. Out of Scope (Anti-Goals)
*Rule: Explicitly state what we are NOT building to prevent scope creep.*

*   Persistent session storage across Gateway restarts is out of scope for v1 (sessions are ephemeral).
*   Loop detection (ping-pong or no-progress patterns) is out of scope for v1 — evaluate after initial usage.
*   Multi-model routing (skills requesting a different model than the session default) is out of scope.
*   Sandbox execution for untrusted workspace skills is out of scope.
*   Conversation branching (forking a session's history) is out of scope.
*   Content filtering or PII redaction middleware in the event pipeline is out of scope.
*   The specific serialization format for events (JSON polymorphic vs. discriminated union) is an implementation detail and out of scope for this product specification.
*   The specific persistence mechanism for sessions (file-based vs. database) is an implementation detail and out of scope for this product specification.

## 7. Dependencies & Assumptions

### 7.1 Dependencies

*   **Mind System ([gateway-mind.md](gateway-mind.md)):** The runtime depends on the mind system for identity loading (SOUL.md + agent files), mind validation, mind file reading, and working-memory access. A valid mind MUST exist before the runtime can start.
*   **MsClaw.Core library:** The runtime uses MsClaw.Core primitives — `MindValidator`, `IdentityLoader`, `MindReader`, and `MsClawClientFactory`.
*   **GitHub Copilot SDK:** The runtime depends on the Copilot SDK for model inference, session management, tool execution, and streaming event delivery.
*   **GitHub Copilot CLI:** The Copilot SDK requires the `copilot` CLI binary to be installed and available on PATH.
*   **Gateway Protocol ([gateway-protocol.md](gateway-protocol.md)):** The protocol specification defines how delivery channels connect, authenticate, and consume the event stream produced by the runtime.

### 7.2 Assumptions

*   We assume one agent runtime per Gateway process — there is no requirement for multi-agent hosting.
*   We assume the mind directory is on local disk accessible to the Gateway process.
*   We assume sessions are ephemeral in v1 — Gateway restart clears all sessions, and CLI child process crash loses session state.
*   We assume the Copilot SDK manages model inference, tool dispatch, and retry logic internally — the runtime does not implement its own inference loop.
*   We assume concurrency defaults (10 max concurrent sessions, reject mode) are sufficient for single-operator use and will be tuned based on usage patterns.
*   We assume workspace skill definitions follow a declarative format that can be parsed and validated at startup without executing the skill.
