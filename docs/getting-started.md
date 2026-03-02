# Getting Started with MsClaw

MsClaw is a runtime that gives your AI agent a persistent identity — a **mind**. This guide walks you through setting up your first mind and having your first conversation.

## Prerequisites

- A [GitHub Copilot](https://github.com/features/copilot) subscription
- [Copilot CLI](https://www.npmjs.com/package/@github/copilot) installed via npm
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [PowerShell 7](https://learn.microsoft.com/powershell/)

```powershell
npm install -g @github/copilot
winget install Microsoft.DotNet.SDK.9
winget install Microsoft.PowerShell
```

## Install

```powershell
dotnet tool install -g MsClaw
```

## Scaffold a new mind

Run MsClaw with `--new-mind` to create a fresh mind directory:

```powershell
msclaw --new-mind ~/my-agent
```

This creates the mind structure at `~/my-agent/`:

```
my-agent/
├── SOUL.md               # Identity file (template — you'll customize this)
├── bootstrap.md          # Triggers the bootstrap conversation
├── .working-memory/      # Persistent memory the agent reads/writes
│   ├── memory.md         # Long-term reference
│   ├── rules.md          # Lessons learned
│   └── log.md            # Chronological observations
├── .github/
│   ├── agents/           # Agent instruction files (created during bootstrap)
│   └── skills/           # Agent skills
├── domains/              # Domain knowledge
├── initiatives/          # Active initiatives
├── expertise/            # Expertise areas
├── inbox/                # Incoming items
├── Archive/              # Archived material
├── extensions/           # For plugins (empty for now)
├── extensions.lock.json  # Pinned extension versions
└── .gitignore            # Excludes extensions/
```

MsClaw validates the mind, saves its location to `~/.msclaw/config.json`, and starts the server on **http://localhost:5000**.

## Bootstrap your agent

Because `bootstrap.md` exists in a new mind, MsClaw enters bootstrap mode. The agent will walk you through three phases to build its personality:

1. **Identity** — Name, personality, mission, boundaries → customizes `SOUL.md`
2. **Agent file** — Role, domain, tools → creates `.github/agents/{name}.agent.md`
3. **Memory** — Seeds `.working-memory/` with initial context from the conversation

The easiest way to have this conversation is with the chat script below. The agent will ask you questions one at a time — answer naturally, it offers sensible defaults if you're unsure.

## Chat script (experimental)

A lightweight terminal chat client that connects to a running MsClaw instance. This is experimental and will be replaced eventually, but it works for quick conversations today.

Browse the source: [scripts/chat.ps1](https://github.com/ianphil/msclaw/blob/master/scripts/chat.ps1)

```powershell
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/ianphil/msclaw/master/scripts/chat.ps1" -OutFile chat.ps1
.\chat.ps1
```

Connects to `http://localhost:5000/chat`, shows an animated spinner while waiting, and maintains session state across messages. Type `quit` to exit.

## After bootstrap

Once all three phases are complete, the agent deletes `bootstrap.md` and the mind is live. You now have:

- `SOUL.md` — Your agent's personality and voice
- `.github/agents/{name}.agent.md` — Operational instructions
- `.working-memory/memory.md` — Long-term reference (consolidation target)
- `.working-memory/rules.md` — Lessons learned from mistakes
- `.working-memory/log.md` — Chronological observations

## Running again

MsClaw remembers your mind. Just run:

```powershell
msclaw
```

It reads `~/.msclaw/config.json` to find the last-used mind and starts serving. To switch minds, pass `--mind` with a different path:

```powershell
msclaw --mind ~/another-agent
```

## What's next

- After 2-3 sessions, memory accumulates and the agent gets noticeably better
- When mistakes happen, the agent adds rules to `.working-memory/rules.md`
- After ~2 weeks, do the first memory consolidation (log → memory)
- Explore the [Extension Developer Guide](extension-developer-guide.md) to add tools and capabilities

## API Reference

MsClaw exposes a simple HTTP API. You can use `Invoke-RestMethod`, the VS Code REST Client extension, or any HTTP tool.

| Method | Path | Description |
|---|---|---|
| `GET` | `/health` | Health check |
| `POST` | `/session/new` | Create a new chat session |
| `POST` | `/chat` | Send a message and get a response |
| `POST` | `/command` | Execute a slash command (e.g., `/extensions`, `/reload`) |
| `GET` | `/extensions` | List loaded extensions |

### PowerShell

```powershell
Invoke-RestMethod -Uri http://localhost:5000/chat -Method POST -ContentType 'application/json' -Body '{"message": "Hello!"}'
```

### VS Code REST Client

Create a file called `requests.http`:

```http
@host = http://localhost:5000

### Health check
GET {{host}}/health

### Chat
POST {{host}}/chat
Content-Type: application/json

{
  "message": "Hello!"
}
```

Click "Send Request" above each block to interact with the agent.
