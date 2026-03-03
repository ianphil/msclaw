# Product Specification: MsClaw Gateway

**Document Owner(s):** MsClaw Team  
**Status:** Draft  
**Target Release:** v1.0  
**Link to Technical Spec:** [msclaw-gateway.md](msclaw-gateway.md) · [msclaw-gateway-protocol.md](msclaw-gateway-protocol.md)

## Version History
*Rule: No silent mutations. All changes after baseline must be recorded here.*

| Version | Date | Author | Description of Changes |
| :--- | :--- | :--- | :--- |
| 1.0 | 2026-03-03 | MsClaw Team | Initial Draft |
| 1.1 | 2026-03-03 | MsClaw Team | Reconciled with Gateway Protocol spec — aligned transport, scalability, shutdown, streaming |
| 2.0 | 2026-03-03 | MsClaw Team | Rewritten as epic-level overview. Feature-level requirements pushed down to sub-specs. All prior REQs preserved in their owning sub-spec. |
| 2.1 | 2026-03-03 | MsClaw Team | Re-conformed to product-spec template: Mind Model moved into § 1.4, section numbering aligned, Edge Cases restored as § 3.3. |
| | | | |

---

> **⚠️ Author Guidelines (Read Before Writing)**
> *   This document is the **epic-level** overview of the MsClaw Gateway. It defines the product vision, user flows, and high-level capability areas ("epics"). It does **not** contain feature-level requirements.
> *   Feature-level requirements (REQ-XXX) live in the **sub-specs** listed in the Epics table below. Each sub-spec is authoritative for its domain.
> *   **Focus on the "What" and "Why".** Absolutely NO technical implementation details (the "How"). No database schemas, API JSON payloads, or code snippets.
> *   **No Subjective Language.** Ban words like *fast, seamless, modern, intuitive, or robust*. Use empirical, verifiable metrics.
> *   **Terminology.** Use RFC 2119 keywords: **MUST**, **SHOULD**, **MAY**.

---

## 1. Executive Summary & Problem Statement

### 1.1 The Problem (The "Why")

AI agents today lack a persistent, always-on runtime that connects a defined personality ("mind") to the outside world. Users who want a personal AI agent must manually start sessions, lose context between conversations, and have no way to reach their agent from messaging platforms, devices, or external services. There is no central daemon that hosts an agent's identity, manages conversations, bridges messaging channels, and exposes hardware capabilities from personal devices — all from a single process.

### 1.2 Business Value

The MsClaw Gateway is the core product that turns MsClaw.Core (a library for building AI agent personalities) into a usable, always-on agent host. Without it, minds are inert files on disk. The Gateway unlocks:

- **Always-on agent access** — users can reach their agent from any messaging platform, device, or API client without manually starting sessions.
- **Multi-channel presence** — a single agent identity is reachable via WhatsApp, Telegram, Slack, Discord, Signal, iMessage, Email, and web chat.
- **Device integration** — personal devices (phones, laptops) become hardware extensions of the agent, providing cameras, screens, and location data.
- **Ecosystem compatibility** — any tool built for the OpenAI API can talk to a MsClaw agent with zero modification.

### 1.3 Success Metrics (KPIs)

- **Metric 1:** An operator MUST be able to start the gateway, send a message to the agent, and receive a reply within 30 seconds of first launch.
- **Metric 2:** The gateway MUST maintain a connection to at least one messaging channel for 7 consecutive days without operator intervention.
- **Metric 3:** The agent MUST persist learned information across sessions — facts written to working memory in session N MUST be retrievable in session N+1.
- **Metric 4:** An OpenAI-compatible client library (Python `openai`, JS, or curl) MUST be able to send a chat completion request to the gateway and receive a valid response without any client-side modification.

### 1.4 The Mind Model

The gateway hosts a single **mind** — a directory on disk that is the agent's entire memory, personality, and knowledge base. The mind is not a configuration file; it is a living, structured body of knowledge that the agent reads at the start of every session and writes to as it learns. Everything the agent knows lives here. The full mind system is specified in [gateway-mind.md](gateway-mind.md).

**Identity — `SOUL.md`.** The mind's root contains a single required file: `SOUL.md`. This defines who the agent is — personality, mission, voice, and boundaries. It is always the first content in the agent's system message.

**Knowledge Structure — IDEA.** All knowledge is organized into four directories: `initiatives/` (active projects), `domains/` (recurring areas), `expertise/` (learnings and patterns), and `Archive/` (completed work). The convention is **one canonical home per fact, wiki-links everywhere else**.

**Working Memory — `.working-memory/`.** The agent's private scratchpad containing three files: `memory.md` (curated reference, read at session start), `log.md` (append-only observations), and `rules.md` (one-liner lessons from mistakes).

**The Bright Line.** Knowledge shared by the user goes to IDEA directories. Observations made by the agent go to `.working-memory/`. Knowledge belongs to the mind; observations belong to the agent's private memory.

**Consolidation.** The agent MUST periodically distill durable insights from `log.md` into `memory.md` and `rules.md` during natural conversation breaks.

**Session Orientation.** At the start of every session, the agent reads `memory.md` before processing the first user message.

---

## 2. Target Audience & User Personas

| Persona Name | Description | Key Needs for this Feature |
| :--- | :--- | :--- |
| **Operator** | The person who owns and configures the agent. Interacts via CLI, Desktop app, or Web UI. | Start/stop the gateway, configure channels, monitor health, send messages, approve actions, browse the mind. |
| **End User (Messenger)** | A person who messages the agent via an external platform (WhatsApp, Telegram, Slack, etc.). | Send messages to the agent and receive replies in their native messaging app. |
| **Node Owner** | A person whose device (phone, laptop) is paired with the gateway as a hardware extension. | Pair a device, grant hardware capabilities (camera, screen, location), and view agent-controlled canvases. |
| **Developer (Integrator)** | A developer who builds on top of the agent using the OpenAI-compatible API or webhooks. | Send requests to standard API endpoints, receive streaming responses, and trigger agent actions via webhooks. |
| **Mind Author** | A person designing an agent's personality, knowledge, and capabilities by authoring files in a mind directory. | Scaffold a new mind, customize identity, organize knowledge, define workspace skills — all via text files. |
| **Channel Developer** | A developer building a new channel adapter for an unsupported messaging platform. | Implement the adapter contract to bridge a new platform without modifying the gateway core. |

## 3. Functional Requirements (The "What")

This section defines the gateway's capability areas at the epic level. Each epic maps to a sub-spec that contains the authoritative feature-level requirements (REQ-XXX), acceptance criteria, edge cases, and non-functional constraints for that domain.

### 3.1 Core User Flows

1. **Operator Launches Gateway:** The operator starts the gateway pointing at a mind directory. The system validates the mind, loads the agent's identity, and begins accepting connections. The operator receives confirmation that the gateway is ready.

2. **Operator Sends a Message:** The operator connects to the gateway, sends a text message to the agent, and receives a streamed reply. The agent's response reflects the personality defined in the mind's `SOUL.md`.

3. **External User Messages via Channel:** An external user sends a message on a supported platform (e.g., WhatsApp). The channel adapter receives the message, routes it through the agent, and delivers the agent's reply back on the same platform.

4. **Agent Uses a Node:** During a conversation, the agent decides it needs a photo. The gateway routes a capture request to a paired device with a camera. The device takes the photo and returns it to the agent as a tool result.

5. **Agent Presents a Canvas:** The agent pushes an interactive UI to a node's screen. The node renders it in a WebView. The user interacts with the canvas, and their input flows back to the agent.

6. **Developer Calls the API:** A developer sends a standard OpenAI-format chat completion request to the gateway's HTTP endpoint. The gateway processes it through the same agent runtime and returns a response in OpenAI-compatible format.

7. **Agent Captures Knowledge:** During conversation, the user shares information about a project. The agent classifies it as an initiative, writes or updates the appropriate file in `initiatives/`, and wiki-links any related domains or expertise files. The user's knowledge is now persisted in the mind.

8. **Agent Consolidates Memory:** After several sessions, the agent reviews its `log.md` entries, distills durable insights into `memory.md` and new lessons into `rules.md`, then continues the conversation with an updated internal model.

### 3.2 Epic Requirements

Each epic below defines a high-level capability the gateway MUST deliver. The **Sub-Spec** column links to the document containing the feature-level requirements, acceptance criteria, edge cases, and non-functional constraints.

| ID | Epic | Description | Sub-Spec |
| :--- | :--- | :--- | :--- |
| **EPIC-01** | Mind System | The gateway MUST host a single mind directory as the agent's identity, knowledge base, and persistent memory. This includes mind validation, scaffolding, identity assembly, IDEA knowledge storage, working memory (session orientation, log, rules, consolidation), bootstrap workflow, and path-traversal-protected file access. | [gateway-mind.md](gateway-mind.md) |
| **EPIC-02** | Gateway Protocol | The gateway MUST provide a real-time bidirectional protocol for operators and device nodes. This includes transport negotiation with automatic fallback, authentication (bearer token, device token, loopback bypass), role-based authorization (read, write, admin, approvals), presence snapshots, agent streaming, session management, chat history and abort, device pairing, node invocation, exec approvals, model listing, configuration access, and automatic reconnection. | [gateway-protocol.md](gateway-protocol.md) |
| **EPIC-03** | HTTP Surface | The gateway MUST expose stateless HTTP endpoints for health probes, OpenAI-compatible chat completions and responses APIs, webhook ingress, canvas asset serving, and SignalR transport negotiation. This includes bearer token and capability token authentication, SSE streaming, consistent error responses, and session mapping for HTTP callers. | [gateway-http-surface.md](gateway-http-surface.md) |
| **EPIC-04** | Channel System | The gateway MUST bridge external messaging platforms (WhatsApp, Telegram, Slack, Discord, Signal, iMessage, Email, Matrix, IRC, WebChat) into the agent pipeline. This includes adapter lifecycle management, inbound normalization, outbound delivery with retry and dead-letter tracking, DM and group access policies, media attachments, multi-account support, operator channel control, and platform rate-limit compliance. | [gateway-channels.md](gateway-channels.md) |
| **EPIC-05** | Skills System | The gateway MUST discover, register, and execute skills that extend the agent's capabilities. This includes three-tier sourcing (bundled, workspace, managed), declarative descriptors, priority-based discovery, multiple execution modes (in-process, shell, script, node-routed, HTTP), node target selection policies, approval gates, timeout enforcement, operator management operations, and security controls (path traversal, env allowlisting, argument injection prevention). | [gateway-skills.md](gateway-skills.md) |
| **EPIC-06** | Canvas Host | The gateway MUST allow the agent to push interactive HTML/JS applications to node screens and relay user interactions back. This includes canvas commands (present, hide, navigate, evaluate, snapshot, push events, reset), capability token lifecycle (minting, validation, sliding expiry, revocation, refresh), asset serving with path-traversal protection, user action bridge, OpenClaw compatibility, bridge injection, and live reload for development. | [gateway-canvas.md](gateway-canvas.md) |
| **EPIC-07** | Graceful Shutdown | The gateway MUST shut down cleanly without losing in-flight work. On shutdown signal, it MUST notify all connected clients, stop accepting new connections, drain delivery queues, allow active operations to complete within a configurable timeout, stop all channel adapters, and terminate the agent runtime process. | [gateway-protocol.md](gateway-protocol.md) · [gateway-channels.md](gateway-channels.md) |

### 3.3 Edge Cases & Error Handling

Each sub-spec defines its own detailed edge cases. The following are gateway-wide concerns that span multiple subsystems:

- **Mind directory missing or invalid:** The gateway MUST refuse to start and MUST log a message identifying which required files are missing. See [gateway-mind.md](gateway-mind.md).
- **Agent runtime fails to start:** The gateway MUST report the failure via the readiness probe (non-200 status) and MUST log the error. The gateway process MUST remain alive. See [gateway-protocol.md](gateway-protocol.md).
- **Channel connection failure:** A single channel failing MUST NOT prevent other channels or the core gateway from operating. The failed channel MUST retry independently. See [gateway-channels.md](gateway-channels.md).
- **Concurrent messages from same caller:** The gateway MUST enforce one active agent stream per caller key. A second request while a stream is active MUST either queue or reject with a descriptive error. See [gateway-protocol.md](gateway-protocol.md) and [gateway-http-surface.md](gateway-http-surface.md).
- **Working memory files missing:** IF `memory.md`, `log.md`, or `rules.md` do not exist when the agent reads them, it MUST proceed without error. Missing files MUST be created on first write. See [gateway-mind.md](gateway-mind.md).

## 4. Non-Functional Requirements (Constraints)

These are gateway-wide constraints. Each sub-spec defines additional domain-specific constraints.

- **Performance:** The gateway MUST respond to health probe requests within 200 milliseconds. Agent reply streaming MUST begin within 5 seconds of receiving a user message under normal load.
- **Scalability:** The gateway MUST support a configurable number of concurrent connections and sessions without degradation.
- **Security:** All remote connections MUST be authenticated. Gateway tokens MUST NOT be logged in plaintext. Capability tokens MUST be cryptographically random (minimum 144 bits). The mind reader MUST enforce path-traversal protection — requests for files outside the mind directory MUST be rejected.
- **Reliability:** The gateway MUST automatically reconnect to clients that experience transport interruptions. Channel adapters MUST retry failed connections with exponential backoff.
- **Binding:** The gateway MUST bind to a configurable host and port (default: `127.0.0.1:18789`).
- **Platform:** The gateway MUST run on Windows, macOS, and Linux. It MUST require only the .NET runtime and the GitHub Copilot CLI on PATH.

## 5. User Experience (UX) & Design

- **Design Assets:** Not applicable — the gateway is a headless daemon. Operator interaction is via CLI, Desktop app, or Web UI (separate specs).
- **Prototypes:** Not applicable.
- **Copy & Messaging:** Error messages returned by the gateway MUST be descriptive and actionable (e.g., "Mind validation failed: SOUL.md not found at /path/to/mind/SOUL.md"). Agent personality and voice are defined by the mind's `SOUL.md`, not by the gateway.

## 6. Out of Scope (Anti-Goals)

- **Multi-mind / multi-agent hosting** — the gateway hosts exactly one mind in v1. Multi-agent support is deferred.
- **mDNS / Bonjour discovery** — nodes MUST connect by explicit URL. Automatic network discovery is not included.
- **Session persistence to disk** — sessions are ephemeral and live only within the agent runtime process lifetime. Cross-restart session resumption is deferred.
- **Operator UI** — the gateway is a headless daemon. CLI, Desktop, and Web UI are separate products with their own specs.
- **Knowledge normalization enforcement** — the convention of "one canonical home per fact, links everywhere else" is followed by the agent through its instructions. The gateway does not enforce uniqueness or detect duplicates.

## 7. Dependencies & Assumptions

### 7.1 Dependencies

- **MsClaw.Core library** — the gateway depends on MsClaw.Core for mind validation, identity loading, mind reading, and client factory functionality.
- **GitHub Copilot SDK** — the agent runtime requires the `GitHub.Copilot.SDK` NuGet package for model inference, tool execution, and session management.
- **GitHub Copilot CLI** — the Copilot SDK spawns the `copilot` CLI as a child process. The CLI binary MUST be installed and available on PATH.
- **ASP.NET Core** — the gateway is built on ASP.NET Core for its SignalR hub, HTTP endpoints, authentication middleware, and hosting infrastructure.

### 7.2 Assumptions

- We assume the gateway will run as a single process on a single host (not distributed).
- We assume the Copilot CLI child process is stable enough to run for days without restart.
- We assume operators have a GitHub account with Copilot access for authentication with the Copilot SDK.
- We assume channel adapters for third-party platforms (WhatsApp, Telegram, etc.) will use unofficial or open-source libraries where official APIs are unavailable.
- We assume the mind directory is on a local filesystem accessible by the gateway process.
