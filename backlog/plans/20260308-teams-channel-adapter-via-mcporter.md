---
title: "MCPorter Tool Bridge & Cron-as-Tools"
status: open
priority: high
created: 2026-03-08
revised: 2026-03-08
---

# MCPorter Tool Bridge & Cron-as-Tools

## Summary

Register MCPorter's MCP servers (Teams, email, calendar, etc.) and Gateway cron operations as native tools on the Copilot SDK session. The agent gains direct access to external services and the ability to schedule its own recurring tasks — all through tool calls. No custom channel adapters, no poll loops, no hardcoded integrations.

## Motivation

MsClaw agents currently have no way to interact with external platforms (Teams, email, calendar) or schedule autonomous work. The original plan called for a dedicated Teams channel adapter with poll timers, session-per-conversation management, and inbound/outbound flows — a significant amount of Gateway-side orchestration code.

The insight: MCPorter already abstracts MCP servers behind a uniform `mcporter call server.tool key=value` interface. The Copilot SDK already supports custom tools via `SessionConfig.Tools` and `AIFunctionFactory`. Instead of building orchestration in C#, we bridge MCPorter's tool surface into the SDK and let the agent orchestrate via natural language reasoning.

Add cron operations as tools on the same session, and the agent can self-program: "check my Teams every 30 minutes and DM me a summary" becomes a tool call to `cron.Create` with a prompt that uses `teams.ListChannelMessages` and `teams.PostMessage`. The Gateway becomes a thin runtime — host the agent, provide the tool surface, run the scheduler.

## Proposal

### Goals

- Agent has native tool access to every MCP server registered in MCPorter (Teams, email, calendar, etc.)
- Agent can create, list, update, and delete cron jobs through tool calls
- Cron jobs fire by creating agent sessions with the full tool surface, executing the job's prompt
- Gateway manages MCPorter daemon lifecycle (config, start, stop, health)
- Adding a new channel (Slack, Discord) = registering one more MCP server in MCPorter config — the agent discovers the tools automatically

### Non-Goals

- Building per-platform channel adapters (the agent uses tools directly)
- Real-time inbound message push from external platforms (the agent polls via cron when it decides to)
- Full implementation of gateway-channels.md delivery queue / dead-letter / retry (those remain future work for delivery reliability)
- Multi-mind routing (one mind per Gateway instance)

## Design

### Architecture

```
Agent Session (Copilot SDK)
  ├── MCPorter Tools (dynamically discovered)
  │     ├── teams.ListChats, teams.PostMessage, teams.SearchTeamsMessages, ...
  │     ├── mail.ListMessages, mail.SendMessage, ...
  │     ├── calendar.GetEvents, calendar.CreateEvent, ...
  │     └── (any MCP server registered in mcporter config)
  │           ↓ tool handler
  │         mcporter call <server>.<tool> key=value  (subprocess)
  │           ↓
  │         MCPorter Daemon (keeps MCP connections alive)
  │           ↓
  │         MCP Server (e.g., agency mcp teams — Entra ID auth)
  │           ↓
  │         Platform API (Microsoft Graph, etc.)
  │
  └── Cron Tools (Gateway-native)
        ├── cron.Create   — schedule a job (cron expr + prompt)
        ├── cron.List     — show active jobs
        ├── cron.Get      — job details + run history
        ├── cron.Update   — change schedule or prompt
        ├── cron.Delete   — remove a job
        └── cron.Pause / cron.Resume
              ↓ when job fires
            Gateway creates isolated session with full tool surface
              → sends job prompt as message
              → agent executes (calling mcporter tools, cron tools, etc.)
              → output published to Gateway protocol (SignalR hub)
                → chat UI renders as notification/toast
                → canvas nodes receive push events
                → future subscribers (webhooks, mobile push, etc.)
```

### Key Design Decisions

**Tools all the way down.**  
Every capability the agent has — Teams, email, calendar, scheduling — is a tool on its session. The agent decides what to do and when. No Gateway-side orchestration decides "poll Teams every 30s" — the agent creates that cron job itself if it wants to. This aligns with the "Never Rewrite What You've Already Imported" principle: the SDK orchestrates tool calls, MCPorter wraps MCP servers, we just bridge them.

**MCPorter as the universal MCP tool provider.**  
MCPorter's `list --schema --json` command returns every registered server's tools with full JSON Schema parameter definitions. The Gateway reads this at startup, dynamically generates `AIFunction` instances for each tool, and registers them on every session. Adding Slack, Discord, or any MCP server means adding one entry to `~/.mcporter/mcporter.json` — the agent gets the tools on its next session.

**Tool handler = subprocess call.**  
Each tool handler executes `mcporter call {server}.{tool} key=value`, parses the JSON stdout, and returns it to the SDK. For a 30-second poll cycle or a send-message call, the ~100ms subprocess overhead is negligible. The MCPorter daemon keeps the underlying MCP server connections warm, so there's no cold-start per call.

**Cron as first-class tools, not just config.**  
The cron system from `specs/gateway-cron.md` is exposed as tools on the session. When the agent calls `cron.Create`, it provides a schedule and a prompt. When the job fires, the Gateway creates an isolated session (per REQ-004 of the cron spec) with the full tool surface and sends the prompt. The agent in that session can call any tool — including creating more cron jobs. This makes the cron system composable and agent-driven.

**Gateway manages MCPorter lifecycle.**  
The Gateway owns the MCPorter config file and daemon process:
- On startup: ensures `~/.mcporter/mcporter.json` has the configured MCP servers, starts the daemon if not running
- On shutdown: optionally stops the daemon (configurable — may want it to outlive the Gateway)
- Health checks: verifies daemon is running and servers are connected before marking readiness
- Config updates: when the operator adds a new MCP server through the Gateway, it writes the entry to mcporter config and restarts the daemon

**Isolated sessions for cron jobs.**  
Each cron job execution gets a fresh session via `SessionPool.GetOrCreateAsync` with a caller key like `cron:{jobId}:{runId}`. The session has the system message (SOUL.md + agent files) and the full tool surface. This aligns with the cron spec's REQ-004 (isolated session jobs) and prevents cron execution from polluting the operator's conversation history.

**Server-initiated publish to Gateway protocol.**  
When a cron job produces output, the Gateway publishes it to the SignalR hub via `IHubContext<GatewayHub, IGatewayHubClient>`. The hub is the pub/sub backbone — the cron executor doesn't know or care who's listening. Subscribers consume what they care about:
- **Chat UI** — renders the event as a toast notification ("Skippy found something in Engineering > General")
- **Canvas nodes** — receive push events (REQ-011/REQ-012 of gateway-canvas.md) to update dashboards, summary cards, or any active canvas surface
- **Future subscribers** — webhooks, mobile push, another agent's session

The cron job's prompt can include delivery intent ("summarize and push to my canvas dashboard" vs. "summarize and DM me on Teams") — the agent decides the delivery path through tool calls, and the hub routes it to all connected subscribers.

### Example: Agent Self-Programs a Teams Monitor

```
Operator: "Monitor the Engineering General channel every 30 minutes
           and DM me a summary of anything important."

Agent reasoning:
  1. I need to find the team and channel IDs
  2. I need to find the operator's chat ID for DMs
  3. I need to create a cron job that reads and summarizes

Agent tool calls:
  → teams.GetMyProfile()
    ← { id: "abc-123", displayName: "Ian", upn: "ian@contoso.com" }

  → teams.ListTeams(userId: "abc-123")
    ← [{ id: "team-456", displayName: "Engineering" }, ...]

  → teams.ListChannels(teamId: "team-456")
    ← [{ id: "channel-789", displayName: "General" }, ...]

  → teams.ListChats(userUpns: ["ian@contoso.com"])
    ← [{ id: "chat-me", chatType: "oneOnOne" }, ...]

  → cron.Create(
      name: "eng-general-summary",
      schedule: "*/30 * * * *",
      prompt: "Read the last 30 minutes of messages from Teams channel
               channel-789 in team team-456 using teams.ListChannelMessages.
               Summarize anything important. If there's something noteworthy,
               DM me a summary using teams.PostMessage to chat chat-me.
               If nothing interesting, do nothing."
    )
    ← { jobId: "job-001", nextRun: "2026-03-08T21:30:00Z" }

Agent response:
  "Done. I'll check Engineering > General every 30 minutes and DM you
   if anything important comes up. You can say 'show my cron jobs'
   to see the schedule or 'stop monitoring' to cancel it."
```

When the cron job fires at 21:30:
1. Gateway creates isolated session with full tool surface
2. Sends the job's prompt
3. Agent calls `teams.ListChannelMessages(teamId: "team-456", channelId: "channel-789", ...)`
4. Agent reasons about the messages
5. If noteworthy: calls `teams.PostMessage(chatId: "chat-me", content: "Summary: ...")`
6. Output published to Gateway protocol (SignalR hub) — chat UI shows a toast, canvas nodes receive push events

### Component Details

#### MCPorter Tool Bridge

**Discovery phase** (Gateway startup):
1. Execute `mcporter list --schema --json`
2. Parse the JSON response — each server has a `tools[]` array with `name`, `description`, `inputSchema`
3. For each tool, create an `AIFunction` via `AIFunctionFactory.Create`:
   - Function name: `{server}.{tool}` (e.g., `teams.PostMessage`)
   - Description: from mcporter schema
   - Parameters: derived from `inputSchema` (JSON Schema → C# delegate parameters)
   - Handler: executes `mcporter call {server}.{tool} key=value`, returns parsed JSON

**Execution phase** (tool call from agent):
1. SDK invokes the `AIFunction` handler with the agent's arguments
2. Handler serializes arguments to `key=value` pairs
3. Executes `mcporter call teams.PostMessage chatId=... content=...`
4. Parses stdout JSON
5. Returns result to SDK → agent receives tool output

**Refresh**: Re-discover tools when mcporter config changes (daemon restart, new server added).

#### Cron Tool Surface

Six tools registered on every session:

| Tool | Parameters | Returns |
|---|---|---|
| `cron.Create` | `name`, `schedule` (cron expr or interval), `prompt`, `timezone?` | `{ jobId, nextRun }` |
| `cron.List` | (none) | `[{ jobId, name, schedule, status, nextRun, lastRun }]` |
| `cron.Get` | `jobId` | `{ jobId, name, schedule, status, nextRun, lastRun, history[] }` |
| `cron.Update` | `jobId`, `schedule?`, `prompt?`, `name?` | `{ jobId, nextRun }` |
| `cron.Delete` | `jobId` | `{ deleted: true }` |
| `cron.Pause` / `cron.Resume` | `jobId` | `{ jobId, status }` |

**Job persistence**: Jobs are stored on disk (per cron spec REQ-002) so they survive Gateway restarts. Format TBD — likely JSON in `~/.msclaw/cron/` or within the mind's `.working-memory/`.

**Job execution flow**:
1. Cron timer fires (evaluated every second, ≤100ms per spec REQ-012)
2. Gateway creates isolated session: `SessionPool.GetOrCreateAsync("cron:{jobId}:{runId}", factory)`
3. Factory creates `SessionConfig` with `Streaming = true`, system message, and full tool surface (mcporter tools + cron tools)
4. Sends the job's `prompt` via `session.SendAsync(new MessageOptions { Prompt = job.Prompt })`
5. Subscribes to session events; waits for `SessionIdleEvent`
6. Publishes output to Gateway protocol (SignalR hub) — subscribers (chat UI, canvas nodes, etc.) consume it
7. Records run result in job history (per cron spec REQ-014)
8. Disposes session (or let SessionPool reap it)

#### MCPorter Lifecycle Management

The Gateway fully owns the MCPorter lifecycle — from first-run zero-state through steady-state operation. MCPorter config (`~/.mcporter/mcporter.json`) is the single source of truth for which MCP servers exist; the Gateway and the agent both read and write it.

**Three startup states:**

| State | What exists | Gateway action |
|---|---|---|
| **Zero-state** | No `~/.mcporter/mcporter.json`, possibly no mcporter installed | Create config dir + empty config file. Start daemon (no servers yet — daemon runs idle). Agent or operator adds servers later via tool calls. |
| **Config exists, daemon stopped** | Config with server entries, daemon not running | Read config, start daemon, discover tools, register on sessions. Normal restart path. |
| **Config exists, daemon running** | Daemon already running (started externally or by previous Gateway run) | Adopt the running daemon. Verify health, discover tools, register on sessions. Never force-stop a daemon you didn't start. |

**Startup sequence** (handles all three states):
1. Ensure `~/.mcporter/` directory exists
2. If `~/.mcporter/mcporter.json` missing, create it: `{ "mcpServers": {}, "imports": [] }`
3. Read config — note which servers are registered
4. Check daemon status via `mcporter daemon status`
5. If not running, start it via `mcporter daemon start`
6. If running, adopt it (verify PID is healthy)
7. Discover tools via `mcporter list --schema --json` (returns empty tools if no servers configured — that's fine)
8. Register discovered tools on session factory
9. Mark readiness — even with zero MCPorter tools, Gateway is ready (cron tools and basic agent functionality still work)

**Runtime health loop** (periodic, e.g., every 60s):
- `mcporter daemon status` — is it running? are servers connected?
- If daemon died: restart, re-discover tools, invalidate sessions with stale tool surfaces
- If a server shows `disconnected` or `error`: log warning, keep other servers' tools active
- If all servers healthy: no-op

**Configuration management**:
- Gateway reads `~/.mcporter/mcporter.json` on startup and after mutations
- Writes go through `McPorterConfigManager` which:
  - Adds/removes/updates `mcpServers` entries
  - Always sets `"lifecycle": "keep-alive"` on new entries (daemon-managed)
  - After any write: `mcporter daemon restart` → re-discover tools → refresh tool registry
- Both the agent (via `mcporter.AddServer` tool) and future admin surfaces use the same manager

**Shutdown**:
- Default: leave daemon running (it may serve other consumers, and surviving Gateway restarts is the expected case)
- Optional: stop daemon with Gateway (configurable for single-user setups)
- Gateway only stops daemons it started (tracks "did I start this?" flag)

**Graceful degradation**:
- If mcporter is not installed (npx fails): Gateway starts without MCPorter tools. Logs a warning. Agent can still use cron tools and respond to prompts — just no external service access.
- If daemon won't start: same degradation. Tools unavailable, everything else works.
- If a single server fails auth: that server's tools return errors the agent can reason about. Other servers unaffected.

#### MCPorter Management Tools

The agent can manage MCP server registrations through tool calls. These tools modify `~/.mcporter/mcporter.json` and trigger daemon restarts + tool re-discovery.

| Tool | Parameters | Returns | What it does |
|---|---|---|---|
| `mcporter.ListServers` | (none) | `[{ name, status, transport, toolCount }]` | List registered MCP servers and their health |
| `mcporter.AddServer` | `name`, `command`, `args[]`, `description?` | `{ name, status, tools[] }` | Add a server entry, restart daemon, discover its tools |
| `mcporter.RemoveServer` | `name` | `{ removed: true }` | Remove a server entry, restart daemon, remove its tools from registry |
| `mcporter.ServerStatus` | `name?` | `{ name, status, lastUsed, tools[] }` | Detailed status for one or all servers |

**Example: Agent connects to Slack at operator's request**

```
Operator: "Can you also connect to our Slack workspace?"

Agent tool calls:
  → mcporter.AddServer(
      name: "slack",
      command: "npx",
      args: ["-y", "@anthropic/slack-mcp-server"],
      description: "Slack workspace via MCP"
    )
    ← { name: "slack", status: "connected", tools: ["ListChannels", "PostMessage", ...] }

Agent response:
  "Done — I now have access to Slack. I can list channels, read messages,
   and post replies. Want me to set up monitoring on any Slack channels?"
```

Behind the scenes:
1. `McPorterConfigManager` writes the entry to `~/.mcporter/mcporter.json` with `"lifecycle": "keep-alive"`
2. Runs `mcporter daemon restart`
3. Runs `mcporter list --schema --json` to discover the new server's tools
4. `ToolRegistry` adds the new `AIFunction` instances
5. New sessions get the expanded tool surface; existing sessions keep their original tools until they expire and are recreated

### Injection Point

Tools are injected in `AgentMessageService.GetOrCreateSessionAsync`, where `SessionConfig` is currently built:

```csharp
private Task<IGatewaySession> GetOrCreateSessionAsync(
    string callerKey,
    CancellationToken cancellationToken)
{
    return sessionPool.GetOrCreateAsync(callerKey, async ct =>
    {
        var sessionConfig = new SessionConfig
        {
            Streaming = true,
            Tools = toolRegistry.GetAllTools()  // ← MCPorter + Cron tools
        };

        if (string.IsNullOrWhiteSpace(hostedService.SystemMessage) is false)
        {
            sessionConfig.SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = hostedService.SystemMessage
            };
        }

        return await client.CreateSessionAsync(sessionConfig, ct);
    }, cancellationToken);
}
```

A `IToolRegistry` service holds the discovered MCPorter tools and the cron tools. `AgentMessageService` (and the cron job executor) both pull from it when building `SessionConfig`.

## Tasks

- [ ] **MCPorter tool bridge — discovery**: Create `McPorterToolProvider` that executes `mcporter list --schema --json`, parses the response, and generates `AIFunction` instances for each tool. Each function's handler shells out to `mcporter call {server}.{tool} key=value` and returns parsed JSON. Supports re-discovery (refresh) when servers change.
- [ ] **MCPorter management tools**: Create `McPorterManagementToolProvider` that exposes `mcporter.ListServers`, `mcporter.AddServer`, `mcporter.RemoveServer`, `mcporter.ServerStatus` as `AIFunction` instances. Handlers delegate to `McPorterConfigManager` and trigger daemon restart + tool re-discovery.
- [ ] **MCPorter config manager**: Create `McPorterConfigManager` that reads/writes `~/.mcporter/mcporter.json` — create from zero-state, add/remove/update MCP server entries, always set `"lifecycle": "keep-alive"`. Gateway is sole writer; file watcher detects external changes (manual `mcporter config add`) and triggers re-discovery. Used by daemon service, management tools, and future admin surface.
- [ ] **MCPorter daemon lifecycle service**: Create `McPorterDaemonService : IHostedService` that owns the full daemon lifecycle — zero-state config creation, daemon start/adopt, periodic health checks (60s), restart on crash, tool re-discovery on restart. Tracks whether it started the daemon to decide shutdown behavior.
- [ ] **Tool registry with lazy loading**: Create `IToolRegistry` / `ToolRegistry` that holds all discovered tools (MCPorter, MCPorter management, cron) but only exposes bootstrap meta-tools on new sessions: `tool_registry_search(query)` and `tool_registry_load(tools[])`. `tool_registry_load` resumes the session via `ResumeSessionAsync` with the requested tools added. Supports hot-refresh when providers change. Injected into `AgentMessageService` and cron executor.
- [ ] **Session config injection**: Modify `AgentMessageService.GetOrCreateSessionAsync` to populate `SessionConfig.Tools` with bootstrap tools from `IToolRegistry` (meta-tools + cron tools). Full MCPorter tool surface loaded on-demand via `tool_registry_load`.
- [ ] **Cron tool surface**: Create `CronToolProvider` that exposes `cron.Create`, `cron.List`, `cron.Get`, `cron.Update`, `cron.Delete`, `cron.Pause`, `cron.Resume` as `AIFunction` instances. Handlers delegate to the cron engine.
- [ ] **Cron engine**: Create `CronEngine` — timer-based job scheduler with disk persistence at `~/.msclaw/cron/`. Jobs store hybrid plans with CALL steps (deterministic tool invocations) and REASON steps (LLM judgment checkpoints). When a job fires, creates an isolated session with full tool surface and executes the plan. Records run history.
- [ ] **Cron job executor**: Create `CronJobExecutor` that creates an isolated session via `SessionPool`, configures it with tools from `IToolRegistry`, sends the job prompt, collects output, and publishes to the Gateway protocol (SignalR hub) via `IHubContext`. Subscribers (chat UI, canvas nodes, etc.) consume events independently.
- [ ] **Gateway protocol publish for cron output**: Wire `IHubContext<GatewayHub, IGatewayHubClient>` into the cron job executor to publish agent responses from cron jobs to the SignalR hub. Chat UI renders as notifications/toasts; canvas nodes receive push events (REQ-011/REQ-012 of gateway-canvas.md).
- [ ] **Integration test — zero-state startup**: Gateway starts with no `~/.mcporter/mcporter.json`. Verify config is created, daemon starts (idle), Gateway reaches ready state with cron tools but no MCPorter tools.
- [ ] **Integration test — MCPorter tool bridge**: Test with mock `mcporter` subprocess returning canned tool schemas and call results. Verify tools are discovered and callable through the SDK session.
- [ ] **Integration test — server add via tool call**: Agent calls `mcporter.AddServer` → verify config updated → daemon restarted → new tools appear in registry → next session has expanded tool surface.
- [ ] **Integration test — cron round-trip**: Create a cron job via tool call → verify it fires → verify isolated session executes with tools → verify output pushed to SignalR.

## Resolved Decisions

| # | Question | Decision | Rationale |
|---|---|---|---|
| Q1 | Tool naming convention | `teams_post_message` (full snake_case) | SDK tests use snake_case; LLM function-calling APIs (OpenAI, Anthropic) reject dots. MCPorter's `PostMessage` becomes `post_message` with server prefix. |
| Q2 | Tool count limits | Lazy tool loading via meta-tools | Session starts with `tool_registry_search` and `tool_registry_load`. Agent discovers and loads only the tools it needs. `tool_registry_load` resumes the session via `ResumeSessionAsync` with the expanded tool set (preserves conversation history). **NOTE (from 003 impl):** ResumeSessionAsync must NOT be called from inside a tool handler — it deadlocks the CLI JSON-RPC. Use deferred sync instead: tool handler adds to in-memory list, `SyncToolsIfNeededAsync` calls ResumeSession before the next `SendAsync`. See `.aidocs/invariants/no-resume-session-in-tool-handler.md`. Also consider adding a `preloadToolNames` parameter to `GetOrCreateSessionAsync` so cron jobs can pre-load specific tools at session creation without expand_tools. |
| Q3 | Cron job prompt vs. plan | Hybrid tool plan with CALL + REASON steps | CALL steps are deterministic tool invocations (no LLM). REASON steps send results to the agent for judgment. Cost-efficient: only pay for LLM at reasoning checkpoints. |
| Q4 | Cron state location | `~/.msclaw/cron/` | Gateway infrastructure, separate from mind. Agent queries its schedule via `cron.List` tool — no filesystem access needed. |
| Q5 | Tool refresh after server add | Eventual consistency | New sessions pick up updated tool registry. Existing sessions keep their tools until they expire. No force-expire, no mid-session updates. |
| Q6 | Error propagation | Structured JSON errors | `{ "error": "auth_expired", "server": "teams", "message": "...", "suggestion": "..." }`. Agent can reason about errors and inform operator. |
| Q7 | Rate limiting | Let failures propagate | Agent sees rate-limit errors and backs off. No proactive throttling — we don't know each server's limits. |
| Q8 | Config file contention | Gateway is sole writer + file watcher | Gateway writes `~/.mcporter/mcporter.json`. File watcher detects external changes (e.g., manual `mcporter config add`) and triggers re-discovery. |
| Q9 | Server auth bootstrapping | Agent surfaces auth URL to operator via SignalR hub | When a new server needs OAuth, the agent publishes a notification to the hub with the auth URL. Operator clicks to authenticate. Works for both desktop and headless Gateways. |
| Q10 | npx vs. global install | `npx mcporter` (auto-downloads, always latest) | Zero-config, works today. Pin version later when integration stabilizes. |

## Open Questions

_(None remaining — all resolved above.)_

## References

- `specs/gateway-cron.md` — cron system requirements
- `specs/gateway-channels.md` — channels system requirements (partially superseded by this approach)
- `specs/gateway-heartbeat.md` — heartbeat system (cron jobs can implement heartbeat-like behavior)
- `docs/bootstrap-teams.md` — MCPorter daemon setup walkthrough
- [MCPorter repo](https://github.com/steipete/mcporter) — daemon source, protocol docs
- [GitHub Copilot SDK](https://www.nuget.org/packages/GitHub.Copilot.SDK) — SessionConfig.Tools, AIFunctionFactory
- `.github/instructions/copilot-sdk-csharp.instructions.md` — SDK tool registration patterns
