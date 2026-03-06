# Plan: Gateway MVP Plumbing

## Summary

Create `MsClaw.Gateway` — a CLI + ASP.NET Core daemon in a single binary (`msclaw`). System.CommandLine owns the command tree; Spectre.Console renders output. The `start` command builds and runs an ASP.NET Core `WebApplication` with a `GatewayHostedService` that manages the CopilotClient lifecycle. Standalone commands (`mind validate`, `mind scaffold`) perform quick operations and exit. The gateway binds to `127.0.0.1:18789` by default, exposes `/healthz` and a SignalR hub at `/gateway`.

## Architecture

```
┌──────────────────────────────────────────────────────┐
│                    msclaw (binary)                    │
│                                                      │
│  ┌────────────────────────────────────────────────┐  │
│  │          System.CommandLine Root                │  │
│  │  ┌──────────┐  ┌──────────────────────────┐    │  │
│  │  │  start   │  │  mind                    │    │  │
│  │  │ --mind   │  │  ├── validate <path>     │    │  │
│  │  │ --new-mind│  │  └── scaffold <path>     │    │  │
│  │  └─────┬────┘  └──────────┬───────────────┘    │  │
│  └────────┼──────────────────┼────────────────────┘  │
│           │                  │                        │
│    ┌──────▼──────┐    ┌──────▼──────┐                │
│    │ Build and   │    │ Call Core   │                │
│    │ run ASP.NET │    │ + Spectre   │                │
│    │ WebApp      │    │ render +    │                │
│    │             │    │ exit        │                │
│    └──────┬──────┘    └─────────────┘                │
│           │                                          │
│  ┌────────▼──────────────────────────────────────┐   │
│  │           ASP.NET Core WebApplication          │   │
│  │                                                │   │
│  │  Endpoints:                                    │   │
│  │  ├── GET /healthz          (health probe)      │   │
│  │  └── /gateway              (SignalR hub)       │   │
│  │                                                │   │
│  │  Hosted Services:                              │   │
│  │  └── GatewayHostedService                      │   │
│  │       ├── Validate mind                        │   │
│  │       ├── Load identity                        │   │
│  │       ├── Start CopilotClient                  │   │
│  │       └── Signal readiness                     │   │
│  │                                                │   │
│  │  DI Container:                                 │   │
│  │  ├── IMindValidator    → MindValidator         │   │
│  │  ├── IMindScaffold     → MindScaffold          │   │
│  │  ├── IIdentityLoader   → IdentityLoader        │   │
│  │  ├── IMindReader       → MindReader            │   │
│  │  ├── GatewayOptions    (from config)           │   │
│  │  └── CopilotClient     (from factory, singleton)│  │
│  └────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────┘
```

### Component Responsibilities

| Component | Role | Integrates With |
|-----------|------|-----------------|
| Program.cs | Root command definition, subcommand registration | System.CommandLine |
| StartCommand | Builds WebApplication, configures DI/Kestrel/endpoints, runs host | ASP.NET Core, GatewayOptions |
| GatewayHostedService | Validates mind, loads identity, starts CopilotClient, exposes readiness | MsClaw.Core services |
| GatewayOptions | Configuration POCO (MindPath, Host, Port) | IConfiguration, CLI args |
| MindValidateCommand | Calls MindValidator, renders result with Spectre, exits | MindValidator, Spectre.Console |
| MindScaffoldCommand | Calls MindScaffold, renders result with Spectre, exits | MindScaffold, Spectre.Console |
| GatewayHub | Empty SignalR hub (methods added by EPIC-03) | ASP.NET Core SignalR |
| /healthz | Minimal endpoint querying GatewayHostedService.IsReady | GatewayHostedService |

### Data Flow: `msclaw start --mind ~/src/ernist`

```
CLI Parse                    ASP.NET Core Host
─────────                    ─────────────────
  │                                │
  ├─ Parse --mind ~/src/ernist     │
  ├─ Resolve to absolute path      │
  ├─ Bind to GatewayOptions        │
  │                                │
  ├─ Build WebApplication ────────►│
  │                                ├─ Register DI services
  │                                ├─ Configure Kestrel (127.0.0.1:18789)
  │                                ├─ Map /healthz
  │                                ├─ Map /gateway (SignalR)
  │                                ├─ Start host
  │                                │
  │                    GatewayHostedService.StartAsync()
  │                                ├─ Validate mind (MindValidator)
  │                                │   ├─ Errors? → log, set readiness=failed
  │                                │   └─ OK? → continue
  │                                ├─ Load identity (IdentityLoader)
  │                                ├─ Create CopilotClient (MsClawClientFactory)
  │                                ├─ Start CopilotClient
  │                                │   ├─ Failure? → log, set readiness=failed
  │                                │   └─ OK? → set readiness=true
  │                                │
  │                                ├─ /healthz → 200 { "status": "Healthy" }
  │                                └─ Waiting for shutdown signal...
```

## File Structure

```
src/
├── MsClaw.Gateway/
│   ├── MsClaw.Gateway.csproj          NEW: ASP.NET Core tool project
│   ├── Program.cs                     NEW: System.CommandLine root, command registration
│   ├── GatewayOptions.cs              NEW: Configuration POCO
│   ├── Hosting/
│   │   └── GatewayHostedService.cs    NEW: Mind → identity → client lifecycle
│   ├── Commands/
│   │   ├── StartCommand.cs            NEW: Builds WebApplication, runs daemon
│   │   └── Mind/
│   │       ├── ValidateCommand.cs     NEW: msclaw mind validate
│   │       └── ScaffoldCommand.cs     NEW: msclaw mind scaffold
│   ├── Hubs/
│   │   └── GatewayHub.cs             NEW: Empty SignalR hub
│   └── appsettings.json               NEW: Default config
├── MsClaw.Gateway.Tests/
│   ├── MsClaw.Gateway.Tests.csproj    NEW: xUnit test project
│   ├── GatewayOptionsTests.cs         NEW: Config binding tests
│   ├── GatewayHostedServiceTests.cs   NEW: Lifecycle tests (mocked deps)
│   ├── Commands/
│   │   ├── StartCommandTests.cs       NEW: DI wiring tests
│   │   └── Mind/
│   │       ├── ValidateCommandTests.cs NEW: Validation output tests
│   │       └── ScaffoldCommandTests.cs NEW: Scaffold output tests
│   └── Helpers/
│       └── TestHelpers.cs             NEW: Shared test utilities
├── MsClaw.Core/                        UNCHANGED
├── MsClaw.Core.Tests/                  UNCHANGED
├── MsClaw.Integration.Tests/           UNCHANGED
└── MsClaw.slnx                         MODIFY: Add Gateway + Gateway.Tests projects
```

## Critical: System.CommandLine + ASP.NET Core Coexistence

**Problem**: System.CommandLine has its own host builder pattern. ASP.NET Core has its own. They must not conflict.

**Solution**: System.CommandLine owns the CLI entry point and command parsing. The `StartCommand` handler builds the `WebApplication` independently — it does not use System.CommandLine's hosting integration. Quick commands (`mind validate`, `mind scaffold`) create their own DI container with just the MsClaw.Core services they need, bypassing ASP.NET Core entirely.

## Implementation Phases

Overview only — detailed tasks in `tasks.md`.

| Phase | Name | Description |
|-------|------|-------------|
| 1 | Project Skeleton | Create projects, csproj files, solution references, empty Program.cs |
| 2 | Configuration & DI | GatewayOptions, DI registration, config binding |
| 3 | CLI Commands | System.CommandLine root, start, mind validate, mind scaffold |
| 4 | Hosted Service & Endpoints | GatewayHostedService, /healthz, GatewayHub |
| 5 | Tests | Unit tests for all components |

## Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Command framework | System.CommandLine | Microsoft-official, middleware pipeline, testable, aligns with `git`/`gh` model |
| Rendering library | Spectre.Console | Rich terminal output (trees, spinners, colors), cross-platform, .NET native |
| Client lifecycle | Eager start in HostedService | Fail fast — operator sees errors immediately, not on first request |
| Host binding | Kestrel via `WebApplication` | Standard ASP.NET Core; configurable, lightweight |
| Hub transport | SignalR (WebSocket + fallback) | Bidirectional, built into ASP.NET Core, aligns with EPIC-03 protocol |
| Quick commands DI | Standalone ServiceCollection | Avoids spinning up ASP.NET Core for simple operations |
| Health format | JSON `{ "status": "..." }` | Follows ASP.NET Core health check convention |
| `--new-mind` on start | Scaffold then validate then start | Enables single-command onboarding |

## Configuration Example

**appsettings.json:**
```json
{
  "Gateway": {
    "Host": "127.0.0.1",
    "Port": 18789
  }
}
```

**CLI overrides:**
```bash
msclaw start --mind ~/src/ernist
msclaw start --mind ~/src/ernist --host 0.0.0.0 --port 9000
msclaw start --new-mind ~/src/my-agent
```

## New Files

| File | Purpose |
|------|---------|
| `src/MsClaw.Gateway/MsClaw.Gateway.csproj` | ASP.NET Core tool project referencing Core + System.CommandLine + Spectre.Console |
| `src/MsClaw.Gateway/Program.cs` | CLI entry point with command tree registration |
| `src/MsClaw.Gateway/GatewayOptions.cs` | Configuration POCO for mind path, host, port |
| `src/MsClaw.Gateway/Hosting/GatewayHostedService.cs` | IHostedService managing mind validation + client lifecycle |
| `src/MsClaw.Gateway/Commands/StartCommand.cs` | Builds and runs the ASP.NET Core WebApplication |
| `src/MsClaw.Gateway/Commands/Mind/ValidateCommand.cs` | Validates a mind with Spectre output |
| `src/MsClaw.Gateway/Commands/Mind/ScaffoldCommand.cs` | Scaffolds a mind with Spectre output |
| `src/MsClaw.Gateway/Hubs/GatewayHub.cs` | Empty SignalR hub |
| `src/MsClaw.Gateway/appsettings.json` | Default configuration |
| `src/MsClaw.Gateway.Tests/MsClaw.Gateway.Tests.csproj` | xUnit test project |
| `src/MsClaw.Gateway.Tests/*.cs` | Unit test files |

## Files to Modify

| File | Change |
|------|--------|
| `src/MsClaw.slnx` | Add MsClaw.Gateway and MsClaw.Gateway.Tests projects |

## Verification

1. `dotnet build src/MsClaw.slnx --nologo` — builds all projects including gateway
2. `dotnet test src/MsClaw.Gateway.Tests/MsClaw.Gateway.Tests.csproj --nologo` — all gateway unit tests pass
3. `dotnet test src/MsClaw.slnx --nologo` — full solution test suite passes
4. `msclaw mind validate ~/src/ernist` — validates the test mind
5. `msclaw start --mind ~/src/ernist` — gateway starts and `/healthz` returns 200

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| System.CommandLine + WebApplication conflict | StartCommand builds WebApp independently; no shared host builder |
| CopilotClient blocks HostedService.StartAsync | Run client creation in background task, signal readiness when done |
| Spectre.Console in CI/non-interactive | Spectre auto-detects; tests use testable output |
| System.CommandLine is still in preview | Pin to specific version; it's Microsoft-maintained and widely used |

## Limitations (MVP)

1. No authentication — loopback-only is the security boundary (EPIC-03 adds tokens)
2. No session management — CopilotClient is started but sessions are EPIC-02 scope
3. SignalR hub is empty — methods added by EPIC-03
4. No OpenAI-compatible API — EPIC-07 scope
5. No integration tests — would require a running Copilot CLI

## References

- [Gateway Spec](../../specs/gateway.md)
- [Gateway MVP Plumbing Quick Plan](../20260305-gateway-mvp-plumbing.md)
- [System.CommandLine Docs](https://learn.microsoft.com/dotnet/standard/commandline/)
- [Spectre.Console Docs](https://spectreconsole.net/)
- [ASP.NET Core SignalR](https://learn.microsoft.com/aspnet/core/signalr/)
