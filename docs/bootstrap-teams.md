# Bootstrap Teams Channel (via MCPorter)

How to connect an MsClaw agent to Microsoft Teams using MCPorter's daemon mode and the Agency CLI. Once complete, tools like `ListTeams`, `ListChats`, `SendMessage`, etc. are available to the MsClaw channel adapter through MCPorter's keep-alive daemon.

## Prerequisites

| Requirement | Why |
|---|---|
| [Agency CLI](https://aka.ms/agency) on PATH | Provides the Teams MCP server (`agency mcp teams`) |
| [MCPorter](https://github.com/steipete/mcporter) (`npx mcporter`) | Keeps the MCP server alive as a daemon |
| Node.js 18+ | Required by MCPorter |
| Microsoft Entra ID session | Agency's Teams proxy authenticates as you — sign in first via `agency mcp teams` or your browser |

Verify both tools are available:

```bash
agency --version
npx mcporter --version
```

---

## 1 — Register the Teams MCP server in MCPorter

Add a `teams` server entry that tells MCPorter to spawn `agency mcp teams` over stdio:

```bash
npx mcporter config add teams \
  --command "agency" \
  --arg mcp \
  --arg teams \
  --description "Microsoft Teams MCP server via Agency" \
  --scope home
```

This writes the entry to `~/.mcporter/mcporter.json`. Use `--scope project` instead if you want a repo-local config at `./config/mcporter.json`.

### Set keep-alive lifecycle

MCPorter only manages servers in its daemon if they are marked `keep-alive`. The CLI doesn't expose a `--lifecycle` flag, so edit the config directly:

**`~/.mcporter/mcporter.json`** (after the `config add`):

```json
{
  "mcpServers": {
    "teams": {
      "command": "agency",
      "args": ["mcp", "teams"],
      "description": "Microsoft Teams MCP server via Agency",
      "lifecycle": "keep-alive"
    }
  }
}
```

The key addition is `"lifecycle": "keep-alive"`. Without it, MCPorter treats the server as ephemeral (spawned per-call and torn down after).

---

## 2 — Start the MCPorter daemon

```bash
npx mcporter daemon start --log
```

Expected output:

```
Daemon started for 1 server(s).
```

The `--log` flag writes daemon activity to `~/.mcporter/daemon/daemon-<hash>.log` for troubleshooting.

---

## 3 — Verify

### Daemon status

```bash
npx mcporter daemon status
```

Expected:

```
Daemon pid 3504 — socket: \\.\pipe\mcporter-daemon-<hash>
Log file: C:\Users\<you>\.mcporter\daemon\daemon-<hash>.log
- teams: idle
```

The `teams: idle` line confirms the server is connected and waiting for calls.

### List available tools

```bash
npx mcporter list teams --schema
```

This prints every tool the Teams MCP server exposes (with parameter schemas). Key tools for the channel adapter:

| Tool | Description |
|---|---|
| `ListTeams` | List Teams workspaces the user is a member of |
| `ListChats` | List chats by metadata (topic, participants) |
| `ListChatMessages` | Retrieve messages from a chat or channel |
| `SendChatMessage` | Send a message to a chat or channel |
| `GetMyProfile` | Look up the authenticated user's profile and ID |

### Smoke-test a call

```bash
npx mcporter call teams.GetMyProfile
```

If auth is working, this returns your Microsoft 365 profile. If you see an auth error, run `agency mcp teams` interactively once to complete the Entra ID sign-in flow, then restart the daemon.

---

## 4 — Daemon lifecycle

| Action | Command |
|---|---|
| Start | `npx mcporter daemon start` |
| Start with logging | `npx mcporter daemon start --log` |
| Status | `npx mcporter daemon status` |
| Restart | `npx mcporter daemon restart` |
| Stop | `npx mcporter daemon stop` |

The daemon auto-manages the `agency mcp teams` child process. If the process crashes, the daemon restarts it on the next call.

---

## How MsClaw uses this

The MsClaw Teams channel adapter (`TeamsChannelAdapter`) polls the daemon on a timer:

```
MsClaw Channel Adapter
  → HTTP/IPC call to mcporter daemon
    → mcporter forwards to agency mcp teams (stdio)
      → Agency proxies to Microsoft Graph (your Entra ID token)
```

The adapter never touches the Teams API or Graph tokens directly — MCPorter and Agency handle all of that. Swapping Teams for a different platform (Slack, Discord) means registering a different MCP server; the adapter pattern stays the same.

---

## Troubleshooting

| Problem | Fix |
|---|---|
| `Daemon is not running` | Run `npx mcporter daemon start` |
| `teams: disconnected` | Check `agency mcp teams` works standalone; re-auth if needed |
| Auth errors on tool calls | Run `agency mcp teams` interactively to complete Entra ID sign-in, then `npx mcporter daemon restart` |
| Server not listed in daemon | Confirm `"lifecycle": "keep-alive"` is in `~/.mcporter/mcporter.json` |
| Daemon log location | `~/.mcporter/daemon/daemon-<hash>.log` (or pass `--log-file <path>`) |
