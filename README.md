# MsClaw.Core

A .NET class library for building AI agents with persistent personalities. Powered by the [GitHub Copilot SDK](https://www.nuget.org/packages/GitHub.Copilot.SDK).

Scaffold a **mind** — a directory that defines who your agent is — and get a configured `CopilotClient` pointed at it. That's it. No web server, no framework, no opinions about how you talk to your agent.

## Install

```powershell
dotnet add package MsClaw.Core
```

## Usage

```csharp
using MsClaw.Core;

// Scaffold a new mind
var scaffold = new MindScaffold();
scaffold.Scaffold("/path/to/my-agent");

// Create a CopilotClient pointed at the mind
var client = MsClawClientFactory.Create("/path/to/my-agent");

// Load the mind's identity for session creation
var identity = new IdentityLoader();
var systemMessage = await identity.LoadSystemMessageAsync("/path/to/my-agent");

// Create a session with the mind's personality
var session = await client.CreateSessionAsync(new SessionConfig
{
    SystemMessage = new SystemMessageConfig
    {
        Mode = SystemMessageMode.Replace,
        Content = systemMessage
    }
});

// Chat
var response = await session.SendAndWaitAsync(new MessageOptions { Prompt = "Hello!" });
```

## What is a mind?

A mind is a directory that defines who your agent is and what it remembers:

- **`SOUL.md`** — Personality, mission, and boundaries
- **`.working-memory/`** — Persistent memory the agent reads and writes across sessions
- **`.github/agents/`** — Agent instruction files

## API

| Type | What it does |
|------|-------------|
| `MindScaffold` | `Scaffold(path)` — creates the mind directory structure |
| `MindValidator` | `Validate(path)` — checks a mind has required files |
| `MindReader` | `ReadFileAsync`, `ListDirectoryAsync` — reads mind contents |
| `IdentityLoader` | `LoadSystemMessageAsync(mindRoot)` — assembles SOUL.md + agents into a system message |
| `MsClawClientFactory` | `Create(mindRoot)` — returns a configured `CopilotClient` |
| `CliLocator` | `ResolveCopilotCliPath()` — finds the Copilot CLI binary |

## License

Apache 2.0 — see [LICENSE](LICENSE) for details.
