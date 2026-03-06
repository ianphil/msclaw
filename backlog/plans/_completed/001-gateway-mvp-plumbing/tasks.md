# Gateway MVP Plumbing Tasks (TDD)

## TDD Approach

All implementation follows strict Red-Green-Refactor:
1. **RED**: Write failing test first
2. **GREEN**: Write minimal code to pass test
3. **REFACTOR**: Clean up while keeping tests green

### Two Test Layers

| Layer | Purpose | When to Run |
|-------|---------|-------------|
| **Unit Tests** | Implementation TDD (Red-Green-Refactor) | During implementation |
| **Spec Tests** | Intent-based acceptance validation | After all phases complete |

## User Story Mapping

| Story | spec.md Reference | Spec Tests |
|-------|-------------------|------------|
| Operator starts gateway | FR-2, FR-3 | GatewayHostedService lifecycle, Health endpoint |
| Operator scaffolds + starts | FR-1.4 | Start command --new-mind |
| Mind author validates | FR-1.5 | Mind validate command |
| Mind author scaffolds | FR-1.6 | Mind scaffold command |
| Developer queries health | FR-2.4 | Health endpoint readiness |
| Developer connects SignalR | FR-2.4 | SignalR hub mapped |

## Dependencies

```
Phase 1 (Skeleton) ──► Phase 2 (Config & DI) ──► Phase 3 (Commands)
                                                        │
                                                        ├──► Phase 4 (Hosted Service & Endpoints)
                                                        │
                                                        └──► Phase 5 (Mind Commands)
```

## Phase 1: Project Skeleton

### Project Setup
- [x] T001 [IMPL] Create `MsClaw.Gateway.csproj` — ASP.NET Core Exe, net10.0, references MsClaw.Core + System.CommandLine + Spectre.Console + SignalR
- [x] T002 [IMPL] Create `MsClaw.Gateway.Tests.csproj` — xUnit test project referencing MsClaw.Gateway
- [x] T003 [IMPL] Update `MsClaw.slnx` to include both new projects
- [x] T004 [IMPL] Create minimal `Program.cs` with empty root command (build verification)
- [x] T005 [IMPL] Verify `dotnet build src/MsClaw.slnx --nologo` succeeds with zero errors

## Phase 2: Configuration & Options

### GatewayOptions
- [x] T006 [TEST] Write tests for GatewayOptions defaults (Host=127.0.0.1, Port=18789)
- [x] T007 [IMPL] Create `GatewayOptions.cs` with MindPath, Host, Port properties
- [x] T008 [TEST] Write tests for GatewayState enum values (Starting, Validating, Ready, Failed, Stopping, Stopped)
- [x] T009 [IMPL] Create `GatewayState.cs` enum

## Phase 3: CLI Commands

### Command Tree
- [x] T010 [TEST] Write test that root command has "start" and "mind" subcommands registered
- [x] T011 [IMPL] Create `Program.cs` with System.CommandLine root command, register start and mind subcommands
- [x] T012 [TEST] Write test that start command has --mind and --new-mind options
- [x] T013 [IMPL] Create `StartCommand.cs` with --mind and --new-mind option definitions
- [x] T014 [TEST] Write test that mind validate command has a path argument
- [x] T015 [IMPL] Create `ValidateCommand.cs` with path argument definition
- [x] T016 [TEST] Write test that mind scaffold command has a path argument
- [x] T017 [IMPL] Create `ScaffoldCommand.cs` with path argument definition

## Phase 4: Hosted Service & Endpoints

### GatewayHostedService
- [x] T018 [TEST] Write test that GatewayHostedService validates mind on StartAsync and sets Ready when valid
- [x] T019 [TEST] Write test that GatewayHostedService sets Failed state when mind validation has errors
- [x] T020 [TEST] Write test that GatewayHostedService loads identity after successful validation
- [x] T021 [TEST] Write test that GatewayHostedService disposes CopilotClient on StopAsync
- [x] T022 [IMPL] Create `GatewayHostedService.cs` — full lifecycle implementation with IMindValidator, IIdentityLoader, MsClawClientFactory
- [x] T023 [IMPL] Create `IGatewayHostedService.cs` interface with State, Error, IsReady properties

### Health Endpoint
- [x] T024 [TEST] Write test that /healthz returns 200 with Healthy status when service is ready
- [x] T025 [TEST] Write test that /healthz returns 503 with Unhealthy status when service is not ready
- [x] T026 [IMPL] Wire /healthz endpoint in StartCommand using GatewayHostedService readiness

### SignalR Hub
- [x] T027 [TEST] Write test that GatewayHub extends Hub
- [x] T028 [IMPL] Create `GatewayHub.cs` — empty Hub subclass
- [x] T029 [IMPL] Map GatewayHub at `/gateway` route in StartCommand

### DI Wiring
- [x] T030 [TEST] Write test that StartCommand registers IMindValidator, IIdentityLoader, IMindScaffold, GatewayHostedService in DI
- [x] T031 [IMPL] Wire full DI registration in StartCommand (MsClaw.Core services + GatewayOptions + hosted service)

## Phase 5: Mind Commands (Spectre Rendering)

### Validate Command
- [x] T032 [TEST] Write test that ValidateCommand calls MindValidator and returns exit code 0 for valid mind
- [x] T033 [TEST] Write test that ValidateCommand returns exit code 1 for invalid mind
- [x] T034 [IMPL] Implement ValidateCommand handler with Spectre.Console tree rendering

### Scaffold Command
- [x] T035 [TEST] Write test that ScaffoldCommand calls MindScaffold.Scaffold for the given path
- [x] T036 [IMPL] Implement ScaffoldCommand handler with Spectre.Console output

### Start Command Handler
- [x] T037 [TEST] Write test that StartCommand with --new-mind calls MindScaffold before building host
- [x] T038 [IMPL] Implement StartCommand handler — build WebApplication, wire DI, configure Kestrel, map endpoints, run host

## Final Validation

After all implementation phases are complete:

- [x] `dotnet build src/MsClaw.slnx --nologo` passes with zero warnings
- [x] `dotnet test src/MsClaw.Gateway.Tests/MsClaw.Gateway.Tests.csproj --nologo` passes
- [x] `dotnet test src/MsClaw.slnx --nologo` — full solution passes
- [x] Run spec tests with `/spec-tests` skill using `specs/tests/001-gateway-mvp-plumbing.md`
- [x] All spec tests pass → feature complete

## Task Summary

| Phase | Tasks | [TEST] | [IMPL] |
|-------|-------|--------|--------|
| Phase 1: Project Skeleton | T001-T005 | 0 | 5 |
| Phase 2: Configuration | T006-T009 | 2 | 2 |
| Phase 3: CLI Commands | T010-T017 | 4 | 4 |
| Phase 4: Hosted Service & Endpoints | T018-T031 | 8 | 6 |
| Phase 5: Mind Commands | T032-T038 | 4 | 3 |
| **Total** | **38** | **18** | **20** |
