# Copilot Instructions for MsClaw

## Build & Test

```bash
# Build
dotnet build src/MsClaw.slnx

# Run all unit tests
dotnet test src/MsClaw.Core.Tests/MsClaw.Core.Tests.csproj --nologo

# Run a single test by name
dotnet test src/MsClaw.Core.Tests/MsClaw.Core.Tests.csproj --nologo --filter "FullyQualifiedName~MindReaderTests.ReadFileAsync_ExistingFile_ReturnsContent"

# Run integration tests (requires Copilot CLI on PATH)
dotnet test src/MsClaw.Integration.Tests/MsClaw.Integration.Tests.csproj --nologo

# Code coverage (built-in, no extra packages)
dotnet test src/MsClaw.Core.Tests/MsClaw.Core.Tests.csproj --collect:"Code Coverage;Format=cobertura"
```

The solution file is `src/MsClaw.slnx` (not at repo root).

## Architecture

MsClaw.Core is a class library (NuGet package) for building AI agents with persistent personalities using the GitHub Copilot SDK. It is **not** a web app or framework — it provides primitives that consumers compose.

The core concept is a **mind**: a directory on disk that defines an agent's personality and memory. The library has two concerns:

**Mind management** (`Mind/`):
- `MindScaffold` — creates the directory structure for a new mind
- `MindValidator` — checks a mind has required files (`SOUL.md`, `.working-memory/`)
- `MindReader` — reads files and lists directories within a mind, with path-traversal protection
- `IdentityLoader` — assembles `SOUL.md` + `.github/agents/*.agent.md` files into a single system message, stripping YAML frontmatter from agent files
- `EmbeddedResources` (internal) — reads `Mind/Templates/` embedded resources for scaffolding

**Client factory** (`Client/`):
- `MsClawClientFactory` — creates a configured `CopilotClient` (from GitHub.Copilot.SDK) pointed at a mind directory
- `CliLocator` — finds the `copilot` CLI binary on PATH, preferring `.exe` then `.cmd` on Windows

Every public type has a corresponding interface (`IMindScaffold`, `IMindValidator`, `IMindReader`, `IIdentityLoader`). Interfaces live alongside their implementations.

## Mind Directory Structure

Required:
- `SOUL.md` — personality, mission, boundaries
- `.working-memory/` — persistent memory (`memory.md`, `rules.md`, `log.md`)

Optional:
- `.github/agents/` and `.github/skills/`
- `domains/`, `initiatives/`, `expertise/`, `inbox/`, `Archive/`

The canonical memory directory is `.working-memory/` — do not rename it.

## Conventions

- **Target framework**: net10.0
- **Nullable**: enabled project-wide, do not suppress warnings without justification
- **Namespace**: all types use `MsClaw.Core` (flat namespace, no sub-namespaces)
- **Commit messages**: conventional commits — `feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`
- **InternalsVisibleTo**: `MsClaw.Core.Tests` can access internal types (e.g., `EmbeddedResources`)
- **Test framework**: xUnit with `TempMindFixture` for creating disposable mind directories in tests
- **Test naming**: `MethodName_Scenario_ExpectedBehavior`
- **CopilotClient is a singleton** — it spawns a CLI child process. Use the SDK's `CreateSessionAsync`/`ResumeSessionAsync` for session lifecycle.
- **Testing with `--mind`**: always use `~/src/ernist` as the test mind directory
