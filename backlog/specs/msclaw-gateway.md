# MsClaw Gateway Architecture

> Central daemon — agent host, messaging hub, SignalR API server, HTTP surface.

Reference: [OpenClaw architecture](../../.ai/docs/openclaw-architecture.md) · [MsClaw protocol spec](msclaw-gateway-protocol.md)

```
                        ┌─────────────────────────────────────────────────────────────┐
                        │                   MSCLAW GATEWAY (daemon)                   │
                        │               :18789  (SignalR + HTTP)                       │
                        │                                                             │
                        │  ┌───────────────────────┐   ┌────────────────────────────┐ │
                        │  │     AGENT RUNTIME      │   │    MESSAGING CHANNELS      │ │
                        │  │   (embedded Copilot    │   │                            │ │
                        │  │    SDK client)          │   │  WhatsApp                  │ │
                        │  │                         │   │  Telegram                  │ │
                        │  │  identity loading       │   │  Slack / Discord           │ │
                        │  │  model inference        │   │  Signal / iMessage         │ │
                        │  │  tool execution ────────┼───│  Email                     │ │
                        │  │  session management     │   │  WebChat                   │ │
                        │  │  streaming replies      │   │                            │ │
                        │  └──────────┬──────────────┘   └────────────────────────────┘ │
                        │             │ tool calls                                     │
                        │  ┌──────────▼──────────────┐   ┌────────────────────────────┐ │
                        │  │        SKILLS           │   │   HTTP SURFACE             │ │
                        │  │  bundled / managed /     │   │  /health                   │ │
                        │  │  workspace skills        │   │  /v1/chat/completions      │ │
                        │  └─────────────────────────┘   │  /v1/responses              │ │
                        │                                │  /hooks/{name}              │ │
                        │                                └────────────────────────────┘ │
                        └────────┬──────────────┬──────────────────┬───────────────────┘
                                 │              │                  │            │
                        SignalR  │     SignalR  │        SignalR   │            │ disk
                                 │  role:"node" │                  │            │
                 ┌───────────────┘              │                  │   ┌────────▼────────┐
                 │                              │                  │   │  MIND (on disk)  │
                 │                              │                  │   │                  │
                 ▼                              ▼                  │   │  SOUL.md         │
  ┌──────────────────────────┐  ┌──────────────────────────┐      │   │  .working-memory/│
  │      CLIENT (operator)    │  │      NODE (device)        │     │   │  .github/agents/ │
  │                           │  │                           │     │   │  skills/         │
  │  CLI / Desktop /          │  │  macOS / iOS / Android /  │     │   └─────────────────┘
  │  Web Admin                │  │  Headless                 │     │
  │                           │  │                           │     └────────────────┐
  │  ► health, status         │  │  ► camera.*               │                     │
  │  ► send, agent            │  │  ► canvas.*               │                     ▼
  │  ► subscribe events       │  │  ► screen.record          │     ┌──────────────────────────┐
  │                           │  │  ► location.get           │     │      CLIENT (operator)    │
  │  device identity          │  │                           │     │                           │
  │  + pairing                │  │  device identity          │     │  Web UI / Automations     │
  └──────────────────────────┘  │  + pairing + caps          │     │                           │
                                 └──────────────────────────┘     │  ► send, agent            │
                                                                   │  ► subscribe events       │
                                                                   │                           │
                                                                   │  device identity           │
                                                                   │  + pairing                │
                                                                   └──────────────────────────┘
```

## Flow Summary

- **Clients** ask the Gateway to do things (send messages, run the agent, check health).
- **Gateway** runs the **Agent** internally — identity → prompt → model → tools → reply.
- **Nodes** provide hardware capabilities the Agent's tools can reach through the Gateway.
- **Channels** bridge external messaging platforms into the agent pipeline.
- All real-time connections use **ASP.NET Core SignalR** with auth middleware, role-based groups, and automatic reconnection.
- Stateless HTTP endpoints serve health probes, OpenAI-compatible APIs, and inbound webhooks.

## Key Concepts

| Component | Role | Lives Where |
|-----------|------|-------------|
| **Gateway** | Central daemon — messaging hub, agent host, SignalR + HTTP server | Single process per host |
| **Agent Runtime** | Embedded Copilot SDK client — identity loading, inference, tool execution | Inside the Gateway |
| **Channel** | Adapter that bridges an external messaging platform into the agent pipeline | Plugin within the Gateway |
| **Skill** | Reusable tool definition the agent can invoke (CLI command, script, API call) | Mind workspace or bundled |
| **Node** | Device endpoint providing hardware capabilities (camera, screen, location) | macOS / iOS / Android / headless |
| **Client** | Operator interface for control-plane actions | CLI / Desktop / Web UI |

---

## Mind Directory

The Gateway hosts a single **mind** — a directory on disk that defines the agent's
personality, memory, and extended capabilities. The mind is the source of truth for
who the agent is and what it knows.

### Structure

```
~/src/ernist/                        ← mind root (configured via MindRoot)
├── SOUL.md                          ← personality, mission, boundaries (REQUIRED)
├── .working-memory/                 ← persistent memory across sessions (REQUIRED)
│   ├── memory.md                    ←   things the agent remembers
│   ├── rules.md                     ←   learned rules and preferences
│   └── log.md                       ←   activity log
├── .github/
│   ├── agents/                      ← agent instruction files (optional)
│   │   └── *.agent.md               ←   merged into system message, frontmatter stripped
│   └── skills/                      ← skill definitions (optional)
├── domains/                         ← domain knowledge (optional)
├── initiatives/                     ← active projects and goals (optional)
├── expertise/                       ← reference material (optional)
├── skills/                          ← workspace skills (optional)
├── inbox/                           ← incoming items for the agent (optional)
└── Archive/                         ← completed/retired items (optional)
```

### Required Files

| Path | Purpose |
|------|---------|
| `SOUL.md` | The agent's core identity — personality, mission, voice, boundaries. Always the first content in the system message. |
| `.working-memory/` | Directory for persistent memory the agent reads and writes across sessions. The gateway's built-in `memory.*` skills operate on this directory. |

### Identity Assembly

The `IdentityLoader` assembles the system message from the mind directory:

1. Read `SOUL.md` as the base system message.
2. Discover `.github/agents/*.agent.md` files.
3. Strip YAML frontmatter from each agent file.
4. Concatenate everything into a single system message.
5. Pass to the Copilot SDK via `SystemMessageConfig` in append mode (preserves SDK guardrails).

### Mind at Runtime

The mind is not static — the agent reads and writes to it during conversations:

- **Reads** — `MindReader` provides path-traversal-protected access to any file in the mind.
  The agent can read domain knowledge, initiative details, or its own memory.
- **Writes** — Built-in `memory.write` skill writes to `.working-memory/`. This is how
  the agent persists things it learns across sessions.
- **Validation** — `MindValidator` checks structure on startup and can be triggered at
  runtime via a hub method. Operators see validation results (errors, warnings, found files).
- **Scaffolding** — `MindScaffold` creates a new mind directory with the required structure
  and template files. Used by setup wizards and CLI commands.

### Mind ↔ Gateway Relationship

The Gateway treats the mind as a read-mostly, write-selectively resource:

- The mind root path is configured once at startup (`MsClaw:Gateway:MindRoot`).
- The gateway validates the mind before accepting connections.
- The `CopilotClient` singleton is bound to the mind root.
- Identity is loaded once at startup and cached (restart to pick up `SOUL.md` changes).
- `.working-memory/` is the only directory the agent routinely writes to.
- All other mind files are read-only from the gateway's perspective.

---

## 1. SignalR API Server

The Gateway's primary real-time surface. Replaces OpenClaw's hand-rolled WebSocket
JSON frame dispatch with SignalR's native RPC, streaming, grouping, and reconnection.

### Responsibilities

- Transport negotiation (WebSocket preferred, SSE and long-polling fallback).
- Authentication via ASP.NET Core middleware before hub connection is established.
- Role-based group assignment — operators, nodes, and per-device targeting.
- Typed hub contracts — compile-time checked client ↔ server interfaces.
- Native streaming via `IAsyncEnumerable<AgentEvent>` for agent output.
- Automatic keepalive and reconnection with state recovery.

### Auth Model

- **Gateway token** — shared secret, passed as bearer token in header or query string.
- **Device pairing** — nodes present a public key, operator approves, subsequent connections use signed challenge.
- **Loopback bypass** — optional unauthenticated access from `127.0.0.1` for local CLI.

### Groups

| Group | Members | Receives |
|-------|---------|----------|
| `operators` | CLI, Web UI, Desktop | Presence, chat, approvals, agent events |
| `nodes` | iOS, macOS, Android, headless | Node invoke requests |
| `node:{deviceId}` | Single device | Targeted invocations for that device |

### Authorization Policies

| Policy | Required Scope | Protects |
|--------|----------------|----------|
| `OperatorRead` | `operator.read` | Health, Presence, SessionsList, ModelsList |
| `OperatorWrite` | `operator.write` | Agent, Send, Poll, ChatAbort |
| `OperatorAdmin` | `operator.admin` | ConfigSet, MindValidate |
| `OperatorApprovals` | `operator.approvals` | ExecApprovalResolve |
| `NodeRole` | `role=node` | RegisterNode, NodeInvokeResult |

> **Detailed spec:** [msclaw-gateway-protocol.md](msclaw-gateway-protocol.md) — hub contracts,
> connection lifecycle, agent streaming flow, event schemas.

---

## 2. Agent Runtime Host

The Gateway embeds a singleton `CopilotClient` (from the GitHub Copilot SDK) as
its agent runtime. MsClaw.Core provides the integration layer between the mind
on disk and the SDK.

### Responsibilities

- **Identity loading** — assemble `SOUL.md` + `.github/agents/*.agent.md` into a
  system message via `IdentityLoader`. This is the agent's personality.
- **Mind validation** — verify the mind directory has required structure
  (`SOUL.md`, `.working-memory/`) before the gateway accepts connections.
- **Session management** — map callers to Copilot SDK sessions. The `CopilotClient`
  is a singleton (one CLI child process); each conversation is a separate `CopilotSession`.
- **Model inference** — delegate prompt → completion to the Copilot SDK, which
  handles model selection, token management, and API routing.
- **Tool execution** — the SDK invokes tools defined by skills, node capabilities,
  and built-in operations. The gateway routes tool calls to the appropriate handler.
- **Streaming replies** — SDK event callbacks are adapted into the `AgentEvent`
  stream and pushed to callers via SignalR.

### Mind Integration

| MsClaw.Core Type | Gateway Usage |
|------------------|---------------|
| `MindValidator` | Validates mind structure on startup and via hub method |
| `IdentityLoader` | Assembles system message from mind files |
| `MindReader` | Reads mind files at runtime (path-traversal protected) |
| `MindScaffold` | Creates new mind directories (setup wizard) |
| `MsClawClientFactory` | Creates the singleton `CopilotClient` pointed at the mind root |

### Session Lifecycle

- One `CopilotClient` per gateway (singleton, spawns CLI child process).
- One `CopilotSession` per conversation (created or resumed per caller).
- Sessions are identified by a caller key.
- Concurrency limit — one active agent stream per caller key at a time.
- Sessions are ephemeral in v1 (live in the CLI process, not persisted to disk).

### Startup Sequence

1. Validate mind directory (`MindValidator`).
2. Load system message (`IdentityLoader`).
3. Create `CopilotClient` singleton (`MsClawClientFactory`).
4. Register as `IAsyncDisposable` in the DI container.
5. Start accepting SignalR connections.

### Graceful Shutdown

1. Stop accepting new connections.
2. Drain active agent streams (respect cancellation tokens).
3. `DisposeAsync` the `CopilotClient` (terminates CLI child process).
4. Exit.

---

## 3. Channel Hub

Channels bridge external messaging platforms into the agent pipeline. Each channel
is an adapter that normalizes inbound messages into agent requests and formats
agent replies back into platform-native responses.

### Responsibilities

- **Inbound normalization** — receive a message from an external platform (webhook,
  polling, or persistent connection) and convert it to a standard agent request.
- **Outbound formatting** — take the agent's reply and format it for the target
  platform (markdown → Slack blocks, HTML → email, etc.).
- **Lifecycle management** — start, stop, and health-monitor each channel independently.
- **DM and group policies** — control who can message the agent (allowlists,
  pairing requirements, open access).
- **Multi-account** — support multiple accounts per platform (e.g., two Slack workspaces).

### Supported Channels (Target)

| Channel | Transport | Auth Model |
|---------|-----------|------------|
| WhatsApp | Baileys (unofficial) or Cloud API | QR pairing / API token |
| Telegram | Bot API (long-polling or webhook) | Bot token |
| Slack | Socket Mode or Events API | OAuth app |
| Discord | Gateway (WebSocket) | Bot token |
| Signal | Signal CLI or libsignal | Linked device |
| iMessage | BlueBubbles or similar bridge | Local API |
| Email | IMAP/SMTP or provider API | OAuth / app password |
| Matrix | Client-Server API | Access token |
| IRC | Persistent TCP connection | SASL / NickServ |
| WebChat | Built-in, served by Gateway HTTP | Gateway token |

### Channel Adapter Contract

Each channel adapter must provide:

- **Connect** — establish the connection to the external platform.
- **Disconnect** — clean teardown.
- **Health** — report connection status (connected, degraded, disconnected).
- **Inbound handler** — receive messages, normalize, and submit to the agent pipeline.
- **Outbound handler** — format and deliver agent replies to the platform.
- **Configuration schema** — strongly-typed options for the channel.

### Message Flow

```
External Platform
        │
        ▼
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│ Channel Adapter  │────►│  Agent Pipeline   │────►│ Channel Adapter  │
│  (inbound)       │     │                    │     │  (outbound)      │
│                  │     │  normalize →        │     │                  │
│  webhook /       │     │  create session →   │     │  format reply →  │
│  polling /       │     │  send to agent →    │     │  deliver to      │
│  persistent conn │     │  collect reply      │     │  platform        │
└─────────────────┘     └──────────────────┘     └─────────────────┘
```

### Channel Lifecycle

- Channels are configured in `appsettings.json` under `MsClaw:Channels`.
- Each channel starts independently on gateway boot.
- Failed channels retry with exponential backoff.
- Channels can be started, stopped, and reloaded at runtime via hub methods.
- Channel status is included in the presence snapshot pushed to operators.

---

## 4. Skills System

Skills are reusable tool definitions that the agent can invoke during a conversation.
They extend the agent's capabilities beyond the Copilot SDK's built-in tools.

### Responsibilities

- **Discovery** — find skills in the mind workspace, bundled with the gateway, or
  installed from a registry.
- **Registration** — expose discovered skills as tools to the Copilot SDK session.
- **Invocation** — execute the skill when the agent calls it (run a CLI command,
  call an API, execute a script).
- **Dependency checking** — verify required binaries and packages are available.
- **Lifecycle** — install, update, and remove skills at runtime.

### Skill Sources

| Source | Location | Example |
|--------|----------|---------|
| **Bundled** | Shipped with the gateway binary | `health.check`, `memory.read`, `memory.write` |
| **Workspace** | Mind directory (`skills/` or `.github/skills/`) | Agent-specific tools defined by the mind author |
| **Managed** | Installed from a registry (NuGet, npm, git) | Community or third-party skills |

### Skill Definition

A skill declares:

- **Name** — unique identifier (e.g., `web.search`, `camera.capture`).
- **Description** — natural-language description for the model's tool selection.
- **Parameters** — typed input schema.
- **Requirements** — binaries, packages, or capabilities needed to execute.
- **Handler** — the executable logic (CLI command, script path, or inline delegate).

### Skill ↔ Node Relationship

Some skills require hardware capabilities provided by nodes (e.g., `camera.capture`
needs a device with a camera). The gateway's skill system and node router work
together:

1. Skill is invoked by the agent.
2. Gateway checks if the skill requires a node capability.
3. If yes, routes the invocation to an appropriate connected node.
4. Node executes and returns the result.
5. Result is passed back to the agent as the tool response.

Skills that don't require node capabilities execute locally on the gateway host.

### Built-in Skills (v1 Target)

| Skill | Description | Requires Node? |
|-------|-------------|----------------|
| `memory.read` | Read from `.working-memory/` | No |
| `memory.write` | Write to `.working-memory/` | No |
| `mind.list` | List files in the mind directory | No |
| `mind.read` | Read a file from the mind | No |
| `web.search` | Search the web | No |
| `camera.capture` | Take a photo | Yes |
| `screen.record` | Record the screen | Yes |
| `location.get` | Get device location | Yes |

---

## 5. HTTP Endpoints

The Gateway exposes a set of stateless HTTP endpoints alongside the SignalR hub.
These serve integrations that don't need real-time streaming or can't use SignalR.

### Responsibilities

- **Health probes** — load balancer and orchestrator readiness/liveness checks.
- **OpenAI-compatible APIs** — drop-in replacement endpoints so existing OpenAI
  client libraries can talk to the agent.
- **Webhooks** — inbound hooks for channel adapters, CI/CD triggers, and
  external event sources.
- **Control UI** — optional web interface for gateway administration.

### Endpoints

| Path | Method | Purpose |
|------|--------|---------|
| `/health` | GET | Liveness probe — returns 200 if the gateway process is alive |
| `/health/ready` | GET | Readiness probe — returns 200 if mind is loaded and `CopilotClient` is connected |
| `/v1/chat/completions` | POST | OpenAI Chat Completions compatible endpoint |
| `/v1/responses` | POST | OpenAI Responses API compatible endpoint |
| `/hooks/{name}` | POST | Named webhook ingress — routes to channel adapters or agent |
| `/gateway` | — | SignalR hub negotiate + transport endpoint |

### OpenAI-Compatible APIs

- Accept standard OpenAI request bodies (`messages`, `model`, `stream`, `tools`).
- Route through the same agent runtime as SignalR callers.
- Support streaming via SSE (`stream: true`) matching OpenAI's chunked response format.
- Authenticate via `Authorization: Bearer <gateway_token>`.
- Enable use of any OpenAI client library (Python `openai`, JS, curl) without modification.

### Webhook Ingress

- Each webhook has a name and optional secret for HMAC validation.
- Webhook payloads are normalized and routed to the appropriate channel adapter
  or directly to the agent.
- Configuration defines which webhooks are active and how they map to agent actions.

### Control UI

An optional web interface served by the gateway for administration:

- View gateway status, connected clients/nodes, active sessions.
- Start/stop channels, view channel health.
- Browse mind files, validate mind structure.
- Send messages to the agent (built-in WebChat).
- Served from embedded resources or a static file path.

---

## Configuration

```json
{
  "MsClaw": {
    "Gateway": {
      "BindHost": "127.0.0.1",
      "Port": 18789,
      "Token": null,
      "MindRoot": "~/src/ernist",
      "LoopbackBypass": true,
      "MaxConcurrentSessions": 10,
      "ControlUi": true
    },
    "Agent": {
      "DefaultModel": "gpt-5",
      "Streaming": true,
      "TimeoutSeconds": 600
    },
    "Channels": {
      "Telegram": { "Enabled": false, "BotToken": null },
      "Slack": { "Enabled": false, "AppToken": null },
      "Discord": { "Enabled": false, "BotToken": null },
      "WebChat": { "Enabled": true }
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

## Mapping to OpenClaw

| OpenClaw | MsClaw | Notes |
|----------|--------|-------|
| Gateway daemon (Node.js) | MsClaw Gateway (ASP.NET Core) | Same role, different runtime |
| Raw WebSocket JSON frames | ASP.NET Core SignalR | Typed RPC replaces manual dispatch |
| pi-mono agent runtime | CopilotClient (GitHub Copilot SDK) | SDK handles inference + tools |
| `connect` handshake | ASP.NET auth middleware | Auth before hub connection |
| `role: "node"` in connect frame | Claim `role=node` → group assignment | Same concept, middleware-based |
| Channel plugins (Baileys, grammY, etc.) | Channel adapters (contract-based) | Same pattern, C# interfaces |
| Skills (bundled / managed / workspace) | Skills (bundled / managed / workspace) | Same three-tier model |
| `~/.openclaw/openclaw.json` (JSON5) | `appsettings.json` (standard .NET config) | Env vars, user secrets, CLI args |
| Canvas host (`/__openclaw__/canvas/`) | Control UI (optional web admin) | Scoped to admin, not agent UI |
| `/v1/chat/completions` | `/v1/chat/completions` | Same OpenAI-compatible surface |
| `/v1/responses` | `/v1/responses` | Same OpenAI Responses API surface |
| mDNS/Bonjour discovery | Not in v1 | Nodes connect by explicit URL |

---

## Component Specs

Each subsystem will have its own detailed spec with contracts, schemas, and flows:

| Component | Spec | Status |
|-----------|------|--------|
| SignalR API Server | [msclaw-gateway-protocol.md](msclaw-gateway-protocol.md) | ✅ Draft |
| Agent Runtime Host | `msclaw-agent-runtime.md` | Planned |
| Channel Hub | [msclaw-channels.md](msclaw-channels.md) | ✅ Draft |
| Skills System | `msclaw-skills.md` | Planned |
| HTTP Endpoints | `msclaw-http-surface.md` | Planned |

## Open Questions

- Should the Gateway support multiple minds (multi-agent) in a future version?
- Should channel adapters run in-process or as sidecar processes?
- Should skills support hot-reload (add/remove without gateway restart)?
- How should the agent decide between node capabilities when multiple nodes
  offer the same capability (e.g., two devices with cameras)?
- Should the OpenAI-compatible endpoints support tool definitions passed in the
  request, or only use skills registered in the gateway?
