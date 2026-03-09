# MsClaw Documentation

A [GitHub Copilot Extension](https://docs.github.com/en/copilot/building-copilot-extensions) that gives your AI agent a persistent personality — a **mind**.

## Getting Started

```bash
dotnet tool install -g MsClaw
msclaw mind scaffold /path/to/my-mind
msclaw auth login
msclaw start --mind /path/to/my-mind
```

👉 **[Setup Guide](setup.md)** — prerequisites, installation, and building from source.

## Architecture

MsClaw is a single binary (`msclaw`) that functions as both a CLI tool and an ASP.NET Core daemon:

```
msclaw (binary)
├── CLI Commands
│   ├── start --mind <path>     → boots the gateway daemon
│   ├── mind scaffold <path>    → creates a new mind directory
│   ├── mind validate <path>    → validates mind structure
│   └── auth login              → device code auth flow
│
├── Gateway Daemon (127.0.0.1:18789)
│   ├── SignalR Hub             → real-time streaming conversations
│   ├── OpenResponses API       → POST /v1/responses (stateless HTTP)
│   ├── Chat UI                 → browser-based interface at /
│   ├── Tool Bridge             → pluggable tool provider system
│   └── Health Probes           → /health, /health/ready
│
└── Optional
    ├── Dev Tunnel (--tunnel)   → remote HTTPS access
    └── Teams (via MCPorter)    → agent reachable from Teams
```

**Projects:**

| Project | Purpose |
|---------|---------|
| `MsClaw.Core` | Mind management library — validation, scaffolding, identity loading, file reading |
| `MsClaw.Gateway` | ASP.NET Core daemon + CLI — the main binary |
| `MsClaw.Tunnel` | Dev Tunnel integration for remote access |

## Guides

- **[Setup Guide](setup.md)** — Prerequisites, installation, building from source
- **[Gateway Quickstart](gateway-quickstart.md)** — Manual testing walkthrough: scaffold → start → health checks → chat → API → SignalR
- **[Cron System](cron-system.md)** — Scheduled agent autonomy: reminders, recurring jobs, command tasks
- **[Tools Developer Guide](tools-dev-guide.md)** — Build custom tool providers to extend agent capabilities
- **[Bootstrap Flow (Existing Mind)](bootstrap-mind-flow.md)** — What happens when you load a mind with `--mind`
- **[Bootstrap Flow (New Mind)](bootstrap-new-mind-flow.md)** — What happens when you scaffold with `--new-mind` (GENESIS flow)
- **[Bootstrap Teams](bootstrap-teams.md)** — Connect your agent to Microsoft Teams via MCPorter

## Specs

Product and protocol specifications live in `specs/`:

| Spec | Covers |
|------|--------|
| `gateway.md` | Product vision, user personas, 9 epics |
| `gateway-protocol.md` | Transport negotiation, auth, streaming, device pairing |
| `gateway-agent-runtime.md` | Runtime lifecycle, streaming events, concurrency |
| `gateway-skills.md` | Skill discovery, three-tier model, execution modes, approval gates |
| `gateway-channels.md` | Channel adapters (Teams, etc.) |
| `gateway-mind.md` | Mind model — SOUL.md, IDEA method, working memory |

## Links

- [GitHub Repository](https://github.com/ianphil/msclaw)
- [NuGet Package](https://www.nuget.org/packages/MsClaw)
