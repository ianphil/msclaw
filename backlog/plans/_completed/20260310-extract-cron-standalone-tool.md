---
title: "Extract Cron System into Standalone Class Library and dotnet Tool"
status: superseded
superseded_by: 20260310-cron-copilot-extension.md
priority: medium
created: 2026-03-10
---

# Extract Cron System into Standalone Class Library and dotnet Tool

## Summary

Refactor the cron scheduling engine out of MsClaw.Gateway into a reusable `MsClaw.Cron` class library, then build a `MsClaw.Cron.Tui` console app published as a standalone `dotnet tool` on NuGet.

## Motivation

The cron system is one of the most valuable pieces built in MsClaw — a complete scheduling engine with persistence, retry, concurrency control, and multiple schedule types. Currently it's locked inside Gateway, meaning you need the full Gateway stack to use it. Extracting it lets anyone schedule and run command jobs from the terminal with zero LLM dependency, and Gateway can still consume the library for its prompt-job and SignalR integration.

## Proposal

### Goals

- Standalone `MsClaw.Cron` class library with no Gateway or SignalR dependencies (only Cronos + Microsoft.Extensions.Hosting)
- `MsClaw.Cron.Tui` published as a `dotnet tool` with System.CommandLine for CLI UX
- Gateway continues working by referencing `MsClaw.Cron` and registering its own executors/sinks
- Existing test coverage migrates cleanly

### Non-Goals

- Prompt job execution in the TUI (stays Gateway-only for now; command jobs only in standalone)
- Distributed / multi-process cron execution
- Job chaining or DAG dependencies
- Breaking changes to Gateway's user-facing cron behavior

## Design

**MsClaw.Cron** (class library, NuGet package):
- All models, interfaces, engine, store, lifecycle, schedule calculator, stagger calculator, command executor, error classifier
- `ICronOutputSink` and `ICronJobExecutor` remain interfaces — consumers provide implementations
- Ships a `ConsoleCronOutputSink` for terminal use
- Single external dependency: Cronos
- Storage path configurable, defaults to `~/.msclaw/cron/`

**MsClaw.Cron.Tui** (dotnet tool, NuGet package):
- System.CommandLine CLI: `msclaw-cron list | create | get | pause | resume | delete | update | run`
- `run` command starts CronEngine in foreground with ConsoleCronOutputSink
- `create` supports `--command`, `--cron`, `--interval`, `--at` flags
- Package as `dotnet tool` via `<PackAsTool>true</PackAsTool>`

**MsClaw.Gateway** (consumer):
- References MsClaw.Cron
- Registers `PromptJobExecutor` and `SignalRCronOutputSink` as its own implementations
- `CronToolProvider` stays in Gateway (ToolBridge-specific) or moves to class lib as reusable helper

**Shared storage**: TUI and Gateway share `~/.msclaw/cron/` by default so jobs created from either surface are visible everywhere. File-level locking in CronJobStore already handles concurrent access.

## Tasks

- [ ] Create `MsClaw.Cron` project, move core types (models, interfaces, engine, store, calculators, command executor, error classifier)
- [ ] Add `ConsoleCronOutputSink` to class lib for terminal output
- [ ] Create `MsClaw.Cron.Tui` project with System.CommandLine, wire up all 8 commands
- [ ] Update MsClaw.Gateway to reference MsClaw.Cron, keep PromptJobExecutor and SignalR sink in Gateway
- [ ] Migrate tests: core tests → MsClaw.Cron.Tests, Gateway-specific tests stay in Gateway.Tests
- [ ] Configure MsClaw.Cron.Tui as a dotnet tool (`PackAsTool`, tool command name, NuGet metadata)
- [ ] Verify end-to-end: `msclaw-cron create` + `msclaw-cron run` executes a command job with console output

## Open Questions

- Tool command name: `msclaw-cron` or just `cron`? (probably `msclaw-cron` to avoid collisions)
- Should `CronToolProvider` move to the class lib as a reusable helper, or stay Gateway-only?
- Shared storage path vs isolated — defaulting shared for now, revisit if conflicts arise
