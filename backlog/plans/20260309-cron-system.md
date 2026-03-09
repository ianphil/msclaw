---
title: "Cron System — Scheduled Agent Autonomy"
status: open
priority: high
created: 2026-03-09
---

# Cron System — Scheduled Agent Autonomy

## Summary

Implement a timer-based cron engine and expose it as an `IToolProvider` on the gateway's tool bridge. The agent gains `cron_create`, `cron_list`, `cron_get`, `cron_update`, `cron_delete`, `cron_pause`, and `cron_resume` tools to self-program recurring and one-shot work.

## Motivation

The agent can only act when a human prompts it. Scheduled behaviors — "remind me in 20 minutes," "check my inbox every morning at 9am," "monitor the Engineering channel every 30 minutes" — are impossible without a cron system. The spec (`specs/gateway-cron.md`) defines 18 requirements; the tool bridge (`IToolProvider` from feature 003) provides the registration surface. This plan connects them.

## Proposal

### Goals

- Cron tool provider registers 7 tools (CRUD + pause/resume) via `IToolProvider`
- Timer-based engine evaluates due jobs every 2s, fires within 5s of schedule
- Jobs persist to `~/.msclaw/cron/` as human-inspectable JSON, survive restarts
- Job execution creates isolated sessions via `SessionPool` with full tool surface
- Run history recorded per job, auto-pruned by configurable limits
- Cron output published to SignalR hub for chat UI / canvas subscribers

### Non-Goals

- Main-session jobs via heartbeat wake (deferred — heartbeat system not yet built)
- Channel delivery modes ("announce" to WhatsApp/Slack) — agent uses MCPorter tools directly
- Job chaining / DAG dependencies (spec explicitly excludes for v1)
- Visual job management UI

## Design

The cron system has four layers:

**1. `CronToolProvider : IToolProvider`** — Discovers 7 `AIFunction` tools using `AIFunctionFactory.Create`. Tool names follow snake_case convention: `cron_create`, `cron_list`, etc. Handlers delegate to the cron engine. Tier: `Bundled`, all tools `AlwaysVisible = true` (cron is always available).

**2. `CronEngine`** — Singleton `IHostedService`. Owns a `PeriodicTimer` (2s tick per REQ-018). Each tick: load job store, find due jobs, respect concurrency limit (default 1, REQ-009), dispatch to the appropriate executor via `ICronJobExecutor`. Manages job lifecycle states (enabled/disabled/running per REQ-007). Handles overdue-on-startup (REQ edge case), exponential backoff on recurring job errors (REQ-011), and one-shot retry/finalization (REQ-010).

**3. Job Payload Model (extensible)** — `CronJob` carries a discriminated `JobPayload` instead of a bare `prompt` string. The engine selects the executor based on payload type:

| Payload Type | Executor | Description |
|---|---|---|
| `PromptPayload` | `PromptJobExecutor` | Creates isolated LLM session, sends prompt, agent reasons + calls tools. Prompt engineering covers hybrid CALL+REASON use cases — the agent follows deterministic instructions and reasons when told to. |
| `CommandPayload` | `CommandJobExecutor` | Runs a shell command / script on the host via `Process.Start()`, captures stdout/stderr. No LLM session, no token cost. For deterministic work that doesn't need reasoning. |

Adding a new job type later requires only a new `ICronJobExecutor` implementation and a new payload variant — zero engine changes.

**4. `ICronJobExecutor` + executors** — `ICronJobExecutor` defines `Task<CronRunResult> ExecuteAsync(CronJob job, string runId, CancellationToken ct)`. The engine resolves the right executor from DI based on `job.Payload` type. `PromptJobExecutor` creates an isolated session via `SessionPool.GetOrCreateAsync("cron:{jobId}:{runId}", factory)`. Factory builds `SessionConfig` with system message + tools from `IToolCatalog.GetDefaultTools()` plus any `preloadToolNames` the job specifies. Sends the job prompt, waits for `SessionIdleEvent`, returns `CronRunResult`. `CommandJobExecutor` runs `Process.Start()`, captures stdout/stderr, returns `CronRunResult`. The engine records run history and publishes output to `IHubContext<GatewayHub, IGatewayHubClient>`.

**Output model**: `CronRunResult` is payload-agnostic — contains `Content` (string), `Outcome` (success/failure), `ErrorMessage`, `DurationMs`. All executor types produce the same result shape, so the engine's history recording and SignalR publishing work identically regardless of how the job ran.

**Persistence**: `CronJobStore` reads/writes `~/.msclaw/cron/jobs.json`. Atomic write (write-temp-then-rename per REQ edge case). Hot-reload on each timer tick (REQ-017). Run history in `~/.msclaw/cron/history/{jobId}.json` with 2MB/2000-line pruning (REQ-014).

**Schedule parsing**: Use `Cronos` NuGet package for cron expression parsing with timezone support (IANA names per REQ-001). Fixed intervals and one-shot timestamps handled with simple `DateTimeOffset` math.

## Tasks

- [ ] **CronJob model + store**: Define `CronJob` record (id, name, schedule type, cron expr / interval / timestamp, payload discriminator, timezone, status, backoff state). `JobPayload` is a base type with `PromptPayload` and `CommandPayload`. `PromptPayload` has `Prompt` + optional `PreloadToolNames`. `CommandPayload` has `Command` + optional `Arguments` + optional `WorkingDirectory`. Implement `CronJobStore` with JSON persistence at `~/.msclaw/cron/`, atomic writes, load-on-tick. JSON serialization uses a `type` discriminator for polymorphic payload deserialization.
- [ ] **ICronJobExecutor + PromptJobExecutor**: Define `ICronJobExecutor` with `Task<CronRunResult> ExecuteAsync(CronJob, string runId, CancellationToken)`. `CronRunResult` is payload-agnostic (Content, Outcome, ErrorMessage, DurationMs). `PromptJobExecutor` creates isolated session via `SessionPool`, configures tools from `IToolCatalog`, sends prompt, collects output. Disposes session after completion.
- [ ] **CommandJobExecutor**: Runs `Process.Start()` with the command payload, captures stdout/stderr, enforces a configurable timeout (default: 5 min), returns `CronRunResult`. No LLM session involved.
- [ ] **CronEngine hosted service**: `IHostedService` with `PeriodicTimer` (2s). Tick evaluates due jobs, enforces concurrency limit, resolves executor from DI by payload type, dispatches. Records run history and publishes output to SignalR hub. Handles overdue-on-startup, stagger (REQ-013), minimum refire gap (REQ-018).
- [ ] **Run history**: `CronRunHistory` with per-job JSON files, auto-pruning by size/line limits (REQ-014). Records start, end, outcome, error message.
- [ ] **CronToolProvider**: Implements `IToolProvider` (Bundled tier, AlwaysVisible). 7 `AIFunction` tools via `AIFunctionFactory.Create` delegating to `CronEngine` / `CronJobStore`.
- [ ] **Error handling + backoff**: Transient vs. permanent error classification (REQ-012). Exponential backoff for recurring jobs (REQ-011). One-shot retry with configurable max (REQ-010). Failure alerts to main session (REQ-016).
- [ ] **Unit tests**: CronJobStore persistence round-trip, CronEngine tick evaluation + concurrency, CronToolProvider discovery, backoff math, schedule parsing, CommandJobExecutor process capture + timeout.

## Open Questions

- Should cron jobs be able to specify `preloadToolNames` at creation (so the isolated session starts with specific MCPorter tools pre-expanded, avoiding the `expand_tools` round-trip)? The MCPorter plan's Q2 suggests yes. This lives on `PromptPayload` specifically.
- Cronos vs. NCrontab vs. hand-rolled for cron parsing — Cronos has timezone support and is well-maintained. Confirm it works on net10.0.
- `CommandPayload` security: should commands run in a sandboxed process, or trust the operator? Likely trust (single-user gateway), but worth noting for security review. Configurable timeout (default 5 min) prevents runaway processes.
