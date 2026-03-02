# MS-Claw

A personal agent runtime powered by the [GitHub Copilot SDK](https://www.nuget.org/packages/GitHub.Copilot.SDK). Give your AI agent a persistent personality — a **mind** — with memory that grows across conversations.

## Get Started

```powershell
npm install -g @github/copilot
winget install Microsoft.DotNet.SDK.9
dotnet tool install -g MsClaw
msclaw --new-mind ~/my-agent
```

👉 **[Full Getting Started Guide](https://blog.ianp.io/msclaw/getting-started.html)** — install prerequisites, scaffold a mind, bootstrap a personality, have your first conversation.

## What is a mind?

A mind is a directory that defines who your agent is and what it remembers:

- **`SOUL.md`** — Personality, mission, and boundaries
- **`.working-memory/`** — Persistent memory the agent reads and writes across sessions
- **`.github/agents/`** — Agent instruction files

MsClaw loads the mind, serves it as an HTTP API, and the agent accumulates knowledge over time.

## CLI

```powershell
msclaw                        # Use last-used mind from config
msclaw --mind ~/my-agent      # Load a specific mind
msclaw --new-mind ~/my-agent  # Scaffold a new mind and start
msclaw --reset-config         # Clear saved config
```

## Documentation

- [Getting Started](https://blog.ianp.io/msclaw/getting-started.html)
- [MsClaw Walkthrough](https://blog.ianp.io/msclaw/msclaw-walkthrough.html)
- [Extension Developer Guide](https://blog.ianp.io/msclaw/extension-developer-guide.html)
- [NuGet Package](https://www.nuget.org/packages/MsClaw)

## License

Apache 2.0 — see [LICENSE](LICENSE) for details.
