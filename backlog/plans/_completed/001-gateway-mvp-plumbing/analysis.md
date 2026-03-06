# Gateway MVP Plumbing Analysis

## Executive Summary

MsClaw.Core is a class library providing mind management primitives and CopilotClient creation. It has no CLI surface, no hosting infrastructure, and no process lifecycle. The gateway spec defines 9 epics of functionality that need a running process. This feature creates that process — a CLI + ASP.NET Core daemon in a single binary (`msclaw`).

| Pattern | Integration Point |
|---------|-------------------|
| Factory pattern (MsClawClientFactory) | GatewayHostedService calls `Create()` to get CopilotClient |
| Validation pipeline (MindValidator) | StartCommand validates mind before booting host; ValidateCommand exposes it as CLI |
| Scaffolding (MindScaffold) | ScaffoldCommand exposes it as CLI; `--new-mind` flag triggers it before start |
| Identity assembly (IdentityLoader) | GatewayHostedService loads system message during startup |
| CLI resolution (CliLocator) | Used internally by MsClawClientFactory; no direct gateway usage needed |
| Path-traversal protection (MindReader) | Not consumed by gateway plumbing directly; used by future epics |

## Architecture Comparison

### Current Architecture

```
┌─────────────────────────┐
│       MsClaw.Core       │  Class library only
│  ┌───────────────────┐  │  No CLI, no process,
│  │  Mind Management  │  │  no hosting, no HTTP
│  │  Client Factory   │  │
│  └───────────────────┘  │
└─────────────────────────┘
         ▲
         │ NuGet reference
         │
┌─────────────────────────┐
│   Consumer (undefined)  │  No consumer exists yet
└─────────────────────────┘
```

### Target Architecture

```
┌──────────────────────────────────────────────────┐
│                  msclaw (binary)                  │
│  ┌────────────────────────────────────────────┐  │
│  │           System.CommandLine CLI            │  │
│  │  start │ mind validate │ mind scaffold      │  │
│  └────────────┬───────────────────────────────┘  │
│               │                                   │
│  ┌────────────▼───────────────────────────────┐  │
│  │        ASP.NET Core WebApplication          │  │
│  │  ┌──────────────┐  ┌───────────────────┐   │  │
│  │  │ /healthz     │  │ /gateway (SignalR) │   │  │
│  │  └──────────────┘  └───────────────────┘   │  │
│  │                                             │  │
│  │  ┌──────────────────────────────────────┐   │  │
│  │  │     GatewayHostedService             │   │  │
│  │  │  validate → identity → client → ready│   │  │
│  │  └──────────────────────────────────────┘   │  │
│  └─────────────────────────────────────────────┘  │
│               │                                   │
│  ┌────────────▼───────────────────────────────┐  │
│  │           MsClaw.Core (library)             │  │
│  │  MindValidator · IdentityLoader             │  │
│  │  MsClawClientFactory · MindScaffold         │  │
│  └─────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────┘
```

## Pattern Mapping

### 1. Factory Pattern → Hosted Service

**Current Implementation:**
`MsClawClientFactory.Create(mindRoot)` returns a raw `CopilotClient`. The caller manages lifecycle.

**Target Evolution:**
`GatewayHostedService` calls the factory during `StartAsync`, holds the client as a singleton, and disposes it during `StopAsync`. The client's `IAsyncDisposable` contract aligns with the hosted service lifecycle.

### 2. Validation Pipeline → CLI + Startup Guard

**Current Implementation:**
`MindValidator.Validate(mindRoot)` returns `MindValidationResult` with `Errors`, `Warnings`, and `Found` lists. Consumers inspect the result.

**Target Evolution:**
Two consumers: (a) `StartCommand` validates before building the host and refuses to start on errors; (b) `MindValidateCommand` validates and renders results as a Spectre.Console tree, then exits.

### 3. Scaffolding → CLI Command + `--new-mind` Flag

**Current Implementation:**
`MindScaffold.Scaffold(mindRoot)` creates the directory structure with embedded templates.

**Target Evolution:**
Two consumers: (a) `MindScaffoldCommand` calls `Scaffold` and renders results with Spectre; (b) `StartCommand` with `--new-mind` calls `Scaffold` before proceeding to validation.

### 4. Identity Assembly → Startup Sequence

**Current Implementation:**
`IdentityLoader.LoadSystemMessageAsync(mindRoot)` reads SOUL.md + agent files, strips frontmatter, joins with separators.

**Target Evolution:**
`GatewayHostedService` calls this during startup to build the system message. The result is stored for use when creating Copilot sessions.

## What Exists vs What's Needed

### Currently Built

| Component | Status | Notes |
|-----------|--------|-------|
| MindScaffold | ✅ | Creates full directory structure from templates |
| MindValidator | ✅ | Returns errors/warnings/found with required/optional checks |
| MindReader | ✅ | Path-traversal protected file reading, git sync |
| IdentityLoader | ✅ | Assembles SOUL.md + agent files into system message |
| MsClawClientFactory | ✅ | Creates CopilotClient with mindRoot, AutoStart, UseStdio |
| CliLocator | ✅ | Cross-platform copilot CLI resolution (exe/cmd/which) |
| TempMindFixture | ✅ | Disposable test minds for unit tests |

### Needed

| Component | Status | Source |
|-----------|--------|--------|
| MsClaw.Gateway project | ❌ | New ASP.NET Core + System.CommandLine project |
| Program.cs with command tree | ❌ | System.CommandLine root, subcommand registration |
| GatewayOptions POCO | ❌ | New config class bound from CLI args + appsettings |
| StartCommand | ❌ | Builds WebApplication, wires DI, runs host |
| GatewayHostedService | ❌ | Mind → identity → client lifecycle in IHostedService |
| MindValidateCommand | ❌ | Spectre.Console rendering of validation result |
| MindScaffoldCommand | ❌ | Spectre.Console rendering of scaffold result |
| /healthz endpoint | ❌ | Returns 200 only when hosted service signals ready |
| GatewayHub (SignalR) | ❌ | Empty hub, methods added by EPIC-03 |
| MsClaw.Gateway.Tests project | ❌ | Unit tests for config, DI, command parsing |

## Key Insights

### What Works Well

1. **Clean interfaces** — every MsClaw.Core type has a corresponding interface, making DI registration and testing straightforward.
2. **MsClawClientFactory already configures CopilotClient fully** — the gateway just needs to call `Create(mindRoot)` and manage the lifetime.
3. **MindValidator returns structured results** — `Errors`, `Warnings`, `Found` lists map directly to Spectre.Console tree rendering.
4. **TempMindFixture pattern** — gateway tests can reuse the same pattern for creating disposable mind directories.

### Gaps/Limitations

| Limitation | Solution |
|------------|----------|
| No DI registration exists for MsClaw.Core services | Gateway registers concrete types against interfaces in its DI container |
| MsClawClientFactory is static, not injectable | Register as singleton factory delegate, or call directly from hosted service |
| No configuration binding for host/port | GatewayOptions POCO with CLI arg binding via System.CommandLine + IConfiguration |
| No readiness signal contract | GatewayHostedService exposes an `IsReady` property; health endpoint queries it |
| System.CommandLine and Spectre.Console are new dependencies | Only added to the gateway project, not MsClaw.Core |
