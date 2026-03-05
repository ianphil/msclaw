# Specification: Gateway MVP Plumbing

## Overview

### Problem Statement

MsClaw.Core provides mind management primitives and CopilotClient creation as a class library. There is no process to host these capabilities — no CLI entry point, no daemon, no networking. The gateway spec defines 9 epics of functionality, but none can be implemented without a running process to live in. Without this plumbing, minds remain inert files on disk with no way to start, validate, or connect to them from external clients.

### Solution Summary

Create `MsClaw.Gateway` — a single binary (`msclaw`) that is both a CLI tool and an ASP.NET Core daemon. The `start` command boots the daemon: validate mind → load identity → start CopilotClient → listen for connections. Standalone commands (`mind validate`, `mind scaffold`) perform quick operations and exit. The project provides the hosting chassis so each epic can be implemented independently against a running gateway.

### Business Value

| Benefit | Impact |
|---------|--------|
| Unblocks all 9 gateway epics | Every epic requires a running process — this provides it |
| Gives minds a CLI surface | Operators can scaffold and validate minds from the command line |
| Establishes the daemon pattern | Future epics add subcommands and endpoints to existing structure |
| Provides health observability | Operators and orchestrators can query readiness state |

## User Stories

### Operator

**As an operator**, I want to start the gateway pointing at a mind directory, so that the agent is running and accepting connections.

**Acceptance Criteria:**
- `msclaw start --mind <path>` validates the mind, loads identity, starts the CopilotClient, and begins listening on `127.0.0.1:18789`
- The command fails with a descriptive error if the mind directory is invalid
- The command fails with a descriptive error if the Copilot CLI is missing or unauthenticated

**As an operator**, I want to scaffold a new mind and start the gateway in one command, so that I can get a fresh agent running quickly.

**Acceptance Criteria:**
- `msclaw start --new-mind <path>` creates the mind directory structure, then starts the gateway
- If the directory already exists and is a valid mind, the scaffold step is skipped

### Mind Author

**As a mind author**, I want to validate a mind directory from the CLI, so that I can check my work without starting the full gateway.

**Acceptance Criteria:**
- `msclaw mind validate <path>` checks the directory and prints a structured result (errors, warnings, found items)
- Exit code is non-zero if there are validation errors
- Output is human-readable with visual hierarchy (tree, colors)

**As a mind author**, I want to scaffold a new mind from the CLI, so that I get the correct directory structure without memorizing it.

**Acceptance Criteria:**
- `msclaw mind scaffold <path>` creates the full mind directory structure
- Output confirms what was created
- Command fails with a descriptive error if the target already exists as a non-empty directory

### Developer (Integrator)

**As a developer**, I want to query the gateway's health endpoint, so that I can verify it is running and ready before sending requests.

**Acceptance Criteria:**
- `GET /healthz` returns 200 when the gateway is fully initialized (CopilotClient started)
- `GET /healthz` returns 503 when the gateway is still initializing or the CopilotClient failed to start
- Response body includes a status field

**As a developer**, I want the gateway to expose a SignalR hub, so that future epics can add real-time methods to it.

**Acceptance Criteria:**
- A SignalR hub is mapped at `/gateway`
- The hub accepts connections but has no application-level methods yet
- The hub is available as soon as the WebApplication starts (does not wait for CopilotClient readiness)

## Functional Requirements

### FR-1: CLI Command Tree

| Requirement | Description |
|-------------|-------------|
| FR-1.1 | The binary MUST be named `msclaw` |
| FR-1.2 | The root command MUST use System.CommandLine for parsing |
| FR-1.3 | `msclaw start --mind <path>` MUST boot the ASP.NET Core daemon |
| FR-1.4 | `msclaw start --new-mind <path>` MUST scaffold then boot |
| FR-1.5 | `msclaw mind validate <path>` MUST validate and exit |
| FR-1.6 | `msclaw mind scaffold <path>` MUST scaffold and exit |
| FR-1.7 | `--mind` and `--new-mind` MUST accept absolute or relative paths |

### FR-2: Daemon Hosting

| Requirement | Description |
|-------------|-------------|
| FR-2.1 | The `start` command MUST build and run an ASP.NET Core `WebApplication` |
| FR-2.2 | The host MUST bind to a configurable address (default `127.0.0.1:18789`) |
| FR-2.3 | The host MUST register `GatewayHostedService` for lifecycle management |
| FR-2.4 | The host MUST map `/healthz` and `/gateway` (SignalR) endpoints |
| FR-2.5 | The host MUST support graceful shutdown via SIGINT/SIGTERM/Ctrl+C |

### FR-3: Gateway Hosted Service

| Requirement | Description |
|-------------|-------------|
| FR-3.1 | On start, the service MUST validate the mind directory |
| FR-3.2 | On start, the service MUST load the identity (system message) |
| FR-3.3 | On start, the service MUST create and start the CopilotClient |
| FR-3.4 | The service MUST expose a readiness state queryable by the health endpoint |
| FR-3.5 | On stop, the service MUST dispose the CopilotClient |
| FR-3.6 | If validation fails, the service MUST set readiness to failed and log errors |
| FR-3.7 | If CopilotClient fails to start, the service MUST set readiness to failed and log errors |

### FR-4: Configuration

| Requirement | Description |
|-------------|-------------|
| FR-4.1 | `GatewayOptions` MUST include MindPath, Host, and Port |
| FR-4.2 | CLI arguments MUST override appsettings.json values |
| FR-4.3 | Default host MUST be `127.0.0.1` |
| FR-4.4 | Default port MUST be `18789` |

## Non-Functional Requirements

### Performance

| Requirement | Target |
|-------------|--------|
| Health probe response time | < 200ms |
| Gateway startup to ready (excluding CopilotClient) | < 2 seconds |

### Security

| Requirement | Target |
|-------------|--------|
| Default bind address | Loopback only (`127.0.0.1`) |
| Authentication | None (loopback-only baseline; EPIC-03 adds token auth) |

### Reliability

| Requirement | Target |
|-------------|--------|
| Gateway process must stay alive even if CopilotClient fails | Health endpoint returns 503, process remains running |

## Scope

### In Scope

- MsClaw.Gateway project (ASP.NET Core + System.CommandLine)
- `msclaw start`, `msclaw mind validate`, `msclaw mind scaffold` commands
- GatewayHostedService with CopilotClient lifecycle
- GatewayOptions configuration binding
- /healthz endpoint
- Empty GatewayHub (SignalR)
- Spectre.Console rendering for mind commands
- MsClaw.Gateway.Tests with unit tests
- Solution file updates

### Out of Scope

- Any epic's feature-level requirements (EPIC-01 through EPIC-09)
- Token-based authentication (EPIC-03)
- Channel adapters, cron, heartbeat, canvas, skills infrastructure
- Integration tests requiring a running Copilot CLI
- OpenAI-compatible HTTP endpoints (EPIC-07)
- Session management (EPIC-02)

### Future Considerations

- Each epic adds subcommands and endpoints to this structure
- StartCommand will gain `--port`, `--host` CLI options
- GatewayHostedService will gain session management responsibility

## Success Criteria

| Metric | Target | Measurement |
|--------|--------|-------------|
| `msclaw start --mind <path>` boots | Gateway reaches ready state | Health endpoint returns 200 |
| `msclaw mind validate <path>` works | Prints validation tree and exits | Non-zero exit on invalid mind |
| `msclaw mind scaffold <path>` works | Creates directory structure | Valid mind after scaffold |
| All unit tests pass | 100% pass rate | `dotnet test` on gateway test project |
| Solution builds cleanly | Zero warnings | `dotnet build src/MsClaw.slnx` |

## Assumptions

1. System.CommandLine is stable enough for production CLI (it is the Microsoft-maintained command-line parser).
2. The Copilot SDK's `CopilotClient` can be created and started within an `IHostedService.StartAsync` call.
3. SignalR can be mapped before any hub methods are registered — empty hubs accept connections.
4. Spectre.Console and System.CommandLine coexist without conflicts.

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| System.CommandLine conflicts with ASP.NET Core host builder | Low | High | StartCommand builds WebApplication independently of System.CommandLine's host |
| CopilotClient startup blocks too long in HostedService | Medium | Medium | Use background startup with readiness flag; don't block host startup |
| Spectre.Console ANSI escapes break in non-interactive terminals | Low | Low | Spectre auto-detects terminal capabilities |

## Glossary

| Term | Definition |
|------|------------|
| Mind | A directory on disk containing an agent's personality, knowledge, and memory |
| Gateway | The MsClaw daemon process that hosts a mind and exposes it via network endpoints |
| SOUL.md | The root identity file in a mind that defines personality, mission, and boundaries |
| Hosted Service | An ASP.NET Core `IHostedService` that manages long-running background work |
| CopilotClient | The GitHub Copilot SDK client that spawns a CLI child process for model inference |
