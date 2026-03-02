# Microsoft Claw

A [GitHub Copilot Extension](https://docs.github.com/en/copilot/building-copilot-extensions) that gives your AI agent a persistent identity — a **mind**.

MsClaw loads a mind directory containing a `SOUL.md` identity file, agent definitions, and working memory, then serves it as a Copilot agent through the GitHub Copilot Runtime API.  
Phase 2 adds a plugin runtime so tools, hooks, services, commands, and HTTP routes can be registered via extensions.

## Quick Start

```bash
# Clone and build
git clone https://github.com/ianphil/msclaw.git
cd msclaw
dotnet build

# Start with an existing mind
dotnet run --project src/MsClaw -- --mind /path/to/your/mind

# Or scaffold a new mind from templates
dotnet run --project src/MsClaw -- --new-mind /path/to/new/mind
```

The server starts on `http://localhost:5050`.

## CLI Arguments

| Argument | Description |
|---|---|
| `--mind <path>` | Load an existing mind directory |
| `--new-mind <path>` | Scaffold a new mind from embedded templates, then load it |
| `--reset-config` | Clear saved config and exit |
| _(none)_ | Auto-discover a mind via config or convention paths |

`--mind` and `--new-mind` cannot be used together.

## Mind Directory Structure

A valid mind directory requires:

```
my-mind/
├── SOUL.md                        # Required — core identity file
├── .working-memory/               # Required — agent memory directory
├── extensions/                    # Optional — external plugin artifacts
├── extensions.lock.json           # Optional — pinned extension versions
├── .gitignore                     # Includes extensions/ by default on scaffolded minds
├── .github/
│   └── agents/
│       └── *.agent.md             # Optional — agent definition files
├── bootstrap.md                   # Optional — triggers bootstrap conversation on first run
├── Archive/
├── domains/
├── expertise/
├── inbox/
└── initiatives/
```

- **`SOUL.md`** — Defines who the agent is. Loaded as the foundation of the system message.
- **`.working-memory/`** — Persistent memory the agent reads and writes across sessions.
- **`.github/agents/*.agent.md`** — Agent files appended to the system message (YAML frontmatter stripped).
- **`bootstrap.md`** — If present, its content is prepended to the system message to guide a 3-phase bootstrap conversation (Identity → Agent File → Memory).

## Mind Discovery

When no `--mind` or `--new-mind` flag is provided, MsClaw searches for a mind in this order:

1. Cached config (`~/.msclaw/config.json`)
2. Current working directory
3. `~/.msclaw/mind`

The first valid mind found is used. The resolved path is persisted to config for future runs.

## API Endpoints

| Method | Path | Description |
|---|---|---|
| `GET` | `/health` | Health check — returns `{"status":"ok"}` |
| `GET` | `/extensions` | List currently loaded extensions |
| `POST` | `/session/new` | Create a new chat session |
| `POST` | `/command` | Execute a slash command without invoking the LLM |
| `POST` | `/chat` | Send a message and get a response |

### POST /chat

```json
{
  "message": "Hello, who are you?"
}
```

Returns:

```json
{
  "response": "...",
  "sessionId": "..."
}
```

## Configuration

MsClaw persists the active mind root to `~/.msclaw/config.json`:

```json
{
  "MindRoot": "/path/to/mind",
  "LastUsed": "2026-03-01T00:00:00Z",
  "DisabledExtensions": ["extension-id-to-disable"]
}
```

Use `--reset-config` to clear it.

## Extension System

### Discovery and loading

External extensions are discovered from:

1. `{appRoot}/extensions/`
2. `{mindRoot}/extensions/` (wins on duplicate IDs)

Each extension folder must include a `plugin.json` manifest:

```json
{
  "id": "hello-world",
  "name": "Hello World Extension",
  "version": "1.0.0",
  "entryAssembly": "runtime/HelloWorld.Extension.dll",
  "entryType": "HelloWorld.Extension.HelloWorldExtension"
}
```

### Runtime commands

- `POST /command` with `{"message":"/extensions"}` shows loaded extensions
- `POST /command` with `{"message":"/reload"}` reloads external extensions without restarting MsClaw

### Manual e2e test

See `.aidocs/e2e-extension-test.md` for a full smoke test that exercises all five extension capability paths.  
Reference sample extension repo: `https://github.com/ipdelete/hello-world-extension`.

## Development

```bash
# Build
dotnet build

# Run tests
dotnet test

# Build quiet, test quiet
dotnet build -v q && dotnet test --no-build -v n
```

### Project Structure

```
MsClaw.sln
├── src/MsClaw/                    # Main application
│   ├── Core/                      # Bootstrap, discovery, validation, identity
│   ├── Models/                    # Request/response and config models
│   └── Templates/                 # Embedded SOUL.md and bootstrap.md templates
├── tests/MsClaw.Tests/            # xUnit tests
│   ├── Core/                      # Unit tests
│   ├── Integration/               # Integration tests
│   └── TestHelpers/               # Test fixtures
└── docs/                          # Flow diagrams
```

## Tech Stack

- .NET 9 / ASP.NET Core Minimal APIs
- [GitHub Copilot SDK](https://www.nuget.org/packages/GitHub.Copilot.SDK) (`0.1.29`)
- xUnit for testing

## License

Apache 2.0 — see [LICENSE](LICENSE) for details.
