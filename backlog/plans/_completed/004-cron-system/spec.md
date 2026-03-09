# Specification: Cron System — Scheduled Agent Autonomy

## Overview

### Problem Statement

The MsClaw agent can only act when a human prompts it. Scheduled behaviors — "remind me in 20 minutes," "check my inbox every morning at 9am," "monitor the Engineering channel every 30 minutes" — are impossible without a cron system. Every agent action requires human initiation, making the gateway reactive-only.

### Solution Summary

Implement a timer-based cron engine as an `IHostedService` and expose job management as an `IToolProvider` on the gateway's tool bridge. The agent gains 7 tools (`cron_create`, `cron_list`, `cron_get`, `cron_update`, `cron_delete`, `cron_pause`, `cron_resume`) to self-program recurring and one-shot work. Jobs persist to `~/.msclaw/cron/` as human-inspectable JSON and survive restarts. Job execution runs in isolated sessions (via `PromptPayload`) or as host processes (via `CommandPayload`).

### Business Value

| Benefit | Impact |
|---------|--------|
| Scheduled agent autonomy | Agent performs recurring work (daily summaries, periodic checks) without human initiation |
| Time-sensitive actions | One-shot reminders and deadline tasks execute at the specified time |
| Deterministic task execution | Shell commands run without LLM sessions — no token cost for scripted work |
| Operator trust through transparency | Job definitions are human-inspectable JSON files on disk |
| Composable architecture | New payload types require only a new `ICronJobExecutor` — zero engine changes |

## User Stories

### Operator

**As an operator**, I want to tell the agent "remind me about the standup in 20 minutes," so that I receive a timely reminder without needing to set a separate alarm.

**Acceptance Criteria:**
- Agent creates a one-shot job with a timestamp 20 minutes from now
- The job fires within 5 seconds of the scheduled time
- The reminder is delivered to connected SignalR clients
- The job is finalized after successful delivery

**As an operator**, I want to tell the agent "check my inbox every morning at 9am," so that I receive a daily summary without asking.

**Acceptance Criteria:**
- Agent creates a recurring job with a cron expression `0 9 * * *` in the operator's timezone
- Each execution runs in an isolated session with inbox-checking tools
- Output is published to SignalR hub
- The job persists across gateway restarts

**As an operator**, I want to list, pause, resume, and delete my scheduled jobs, so that I maintain control over autonomous agent behavior.

**Acceptance Criteria:**
- `cron_list` returns all jobs with schedule, status, last run, and next run
- `cron_pause` disables a job immediately without losing configuration
- `cron_resume` re-enables a paused job
- `cron_delete` removes the job and its history

### Agent (Self-Programming)

**As the MsClaw agent**, I want to create cron jobs from natural language requests, so that I can schedule work on behalf of the operator.

**Acceptance Criteria:**
- Agent translates "every 30 minutes" to a fixed-interval schedule
- Agent translates "at 3pm EST" to a one-shot timestamp with timezone
- Agent translates "weekdays at 9am" to a cron expression `0 9 * * 1-5`
- Agent selects `PromptPayload` for tasks requiring reasoning, `CommandPayload` for deterministic scripts

## Functional Requirements

### FR-1: Schedule Types (REQ-001)

| Requirement | Description |
|-------------|-------------|
| FR-1.1 | Support one-shot schedule: a specific `DateTimeOffset` timestamp. Job fires once and finalizes. |
| FR-1.2 | Support fixed-interval schedule: every N milliseconds. Job fires repeatedly at the configured interval. |
| FR-1.3 | Support cron expression schedule: 5-field or 6-field cron expressions parsed by Cronos with IANA timezone support. |
| FR-1.4 | Cron expressions MUST support timezone specification using IANA timezone names (e.g., `America/New_York`). |

### FR-2: Job Persistence (REQ-002)

| Requirement | Description |
|-------------|-------------|
| FR-2.1 | Job definitions persist to `~/.msclaw/cron/jobs.json` as human-inspectable JSON. |
| FR-2.2 | Atomic writes: write to temp file then rename to prevent corruption. |
| FR-2.3 | All enabled jobs resume scheduling on gateway restart without operator intervention. |
| FR-2.4 | Job store is loadable and editable without the gateway running. |

### FR-3: Job Payloads

| Requirement | Description |
|-------------|-------------|
| FR-3.1 | `PromptPayload`: creates an isolated LLM session, sends a prompt, collects output. Supports optional `preloadToolNames` for pre-expanded tools. |
| FR-3.2 | `CommandPayload`: runs a shell command via `Process.Start()`, captures stdout/stderr. No LLM session, no token cost. Configurable timeout (default: 5 minutes). |
| FR-3.3 | Payload type is a polymorphic discriminated union serialized with a `type` field in JSON. |
| FR-3.4 | Adding a new payload type requires only a new `ICronJobExecutor` implementation and a new `JobPayload` variant. |

### FR-4: Job Lifecycle (REQ-007, REQ-008)

| Requirement | Description |
|-------------|-------------|
| FR-4.1 | Jobs have three lifecycle states: `enabled`, `disabled`, `running`. |
| FR-4.2 | Only enabled jobs are considered for scheduling. |
| FR-4.3 | A running job MUST NOT be scheduled for concurrent execution. |
| FR-4.4 | CRUD operations: `cron_create`, `cron_get`, `cron_update`, `cron_delete`. |
| FR-4.5 | Lifecycle operations: `cron_pause` (enabled→disabled), `cron_resume` (disabled→enabled). |
| FR-4.6 | Deletion removes the job from the store and cancels any pending execution. |

### FR-5: Concurrency Control (REQ-009)

| Requirement | Description |
|-------------|-------------|
| FR-5.1 | Configurable limit on simultaneously executing jobs (default: 1 — serial execution). |
| FR-5.2 | Jobs exceeding the concurrency limit are queued for execution when a slot becomes available. |

### FR-6: Error Handling (REQ-010, REQ-011, REQ-012)

| Requirement | Description |
|-------------|-------------|
| FR-6.1 | One-shot jobs: retry up to configurable max (default: 3) with exponential backoff on transient errors. Finalized (disabled) on permanent error. |
| FR-6.2 | Recurring jobs: exponential backoff (30s, 1m, 5m, 15m, 60m) on errors. Backoff resets after success. Never permanently disabled. |
| FR-6.3 | Transient errors: rate limits, network timeouts, server errors. Permanent errors: auth failures, invalid configuration. |
| FR-6.4 | Failure alerts published to SignalR hub with job name, failure reason, and retry status. |

### FR-7: Run History (REQ-014)

| Requirement | Description |
|-------------|-------------|
| FR-7.1 | Each execution records: job ID, run ID, start time, end time, outcome (success/failure), error message, duration. |
| FR-7.2 | History persisted in per-job JSON files at `~/.msclaw/cron/history/{jobId}.json`. |
| FR-7.3 | Automatic pruning by configurable size (default: 2MB) and line limit (default: 2000 lines). |

### FR-8: Timer Behavior (REQ-017, REQ-018)

| Requirement | Description |
|-------------|-------------|
| FR-8.1 | Engine evaluates due jobs every 2 seconds via `PeriodicTimer`. |
| FR-8.2 | Hot-reload: job store re-read from disk on each tick. |
| FR-8.3 | Overdue-on-startup: jobs whose schedule passed during downtime fire on the next tick. |
| FR-8.4 | Stagger: recurring jobs at common times spread across a configurable window (default: 0–5 minutes) with deterministic per-job offset. |

## Non-Functional Requirements

### Performance

| Requirement | Target |
|-------------|--------|
| Timer evaluation latency | < 100ms for up to 500 jobs |
| Job fire accuracy | Within 5 seconds of scheduled time |
| Minimum refire gap | 2 seconds between consecutive evaluations |

### Reliability

| Requirement | Target |
|-------------|--------|
| Job state durability | Survives gateway restart |
| Atomic persistence | Write-temp-then-rename prevents corruption |
| Concurrent store access | No corruption from concurrent reads/writes |
| Clock jump handling | Forward jump: overdue jobs fire; backward jump: no spurious fires |

### Storage

| Requirement | Target |
|-------------|--------|
| Job store format | Human-inspectable JSON |
| History per job | 2MB / 2000 lines max, auto-pruned |
| Platform support | Windows, macOS, Linux |

## Scope

### In Scope

- Cron engine hosted service with 2-second `PeriodicTimer`
- `CronToolProvider` with 7 tools (CRUD + pause/resume)
- `CronJob` model with `PromptPayload` and `CommandPayload`
- `ICronJobExecutor` abstraction with two implementations
- JSON persistence at `~/.msclaw/cron/` with atomic writes
- Run history with automatic pruning
- Error classification (transient/permanent) and exponential backoff
- Concurrency control with configurable limit
- SignalR output publishing via `IHubContext`
- Cronos-based cron expression parsing with IANA timezone support

### Out of Scope

- Main session jobs via heartbeat wake (REQ-003, REQ-005) — heartbeat system not built
- Channel delivery modes (REQ-006 Announce/Webhook) — agent uses MCPorter tools directly
- Job chaining / DAG dependencies — spec explicitly excludes for v1
- Visual job management UI — job management is conversational
- Sub-second scheduling precision — minimum refire gap is 2 seconds
- Distributed job execution — one cron service per gateway
- Isolated session retention (REQ-015) — deferred to session pool lifecycle feature

### Future Considerations

- Main session job support when heartbeat system ships
- Channel delivery modes when MCPorter channel adapters mature
- Job templates / pre-built scheduled task library
- Session retention policies for cron-created sessions

## Success Criteria

| Metric | Target | Measurement |
|--------|--------|-------------|
| Job fire accuracy | Within 5 seconds of schedule | Automated test with known schedule |
| Restart durability | 100% job survival across restart | Create job → restart gateway → verify job still fires |
| One-shot finalization | Exactly-once execution | One-shot fires, verify disabled/deleted |
| Backoff behavior | Exponential delay on failure | Mock failure → verify increasing delays |
| History pruning | Stays under 2MB/2000 lines | Generate history → verify auto-prune |
| Tool discovery | All 7 tools in catalog | Resolve `IToolCatalog` → verify tool names |

## Assumptions

1. One cron service per gateway process — no multi-instance coordination.
2. The operator creates jobs via conversation with the agent (cron tools are bundled).
3. Serial execution (concurrency = 1) is sufficient for most deployments.
4. The job store is small enough to fit in memory (up to 500 jobs) and can be fully loaded each tick.
5. Cronos NuGet package works on net10.0 with IANA timezone support.
6. `CommandPayload` trusts the operator — single-user gateway, no sandboxing needed.
7. `PromptPayload` sessions are self-contained and don't affect the main session.

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Cronos doesn't support net10.0 | Low | High | Verify before implementation; fall back to NCrontab or hand-rolled |
| Job store file corruption on crash | Medium | High | Atomic write-temp-then-rename; refuse to start on parse error |
| Runaway `CommandPayload` process | Medium | Medium | Configurable timeout (default: 5 min); process kill on timeout |
| Clock skew causes missed/duplicate fires | Low | Medium | Re-evaluate on each tick; track `lastRunAtUtc` to prevent duplicates |
| SessionPool key collisions for cron sessions | Low | Low | Use `"cron:{jobId}:{runId}"` format — unique per execution |

## Glossary

| Term | Definition |
|------|------------|
| One-shot | A job that fires once at a specific timestamp and then finalizes |
| Fixed interval | A job that fires repeatedly every N milliseconds |
| Cron expression | A 5-field or 6-field schedule expression (e.g., `0 9 * * *`) |
| PromptPayload | Job payload that creates an isolated LLM session and sends a prompt |
| CommandPayload | Job payload that runs a shell command without an LLM session |
| Transient error | A retryable error: rate limit, timeout, server error |
| Permanent error | A non-retryable error: auth failure, invalid configuration |
| Stagger | Deterministic offset applied to recurring jobs to avoid load spikes |
