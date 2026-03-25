---
title: "Cron Scheduling as a Copilot CLI Extension"
status: open
priority: medium
created: 2026-03-10
supersedes: 20260310-extract-cron-standalone-tool.md
related: 20260310-genesis-mind-template.md
---

# Cron Scheduling as a Copilot CLI Extension

## Summary

Build a repo-scoped Copilot CLI extension that lives in the agent's mind repository, providing conversational cron job management and a detached background engine for executing both shell commands and AI prompts on a schedule. Prompt jobs run with the mind's personality (SOUL.md, agent files). Pure Node.js, no .NET dependency. Replaces the earlier plan to extract cron into a dotnet tool.

## Motivation

The cron system in MsClaw.Gateway is one of the most useful pieces built — scheduling, persistence, retry, concurrency control, multiple schedule types. But it's locked inside Gateway. The earlier plan proposed extracting it as a `dotnet tool`, which would work but requires a separate binary, System.CommandLine boilerplate, and NuGet packaging.

A Copilot CLI extension is a better fit:

- **Natural language for free** — no flag parsing, no help text. You say *"run my linter every 30 minutes"* and the agent calls `cron_create`.
- **Mind-scoped** — lives in the agent's mind repo (`.github/extensions/cron/`). Scheduled tasks are part of the agent's behavior, not a generic utility. Prompt jobs inherit the mind's personality via SOUL.md and agent files.
- **Self-contained** — one directory, pure JS, no build step, no NuGet.
- **Engine as detached process** — survives CLI exit, no window flash, no Task Scheduler.

## Architecture

### Extension layout

The extension lives in the mind repo alongside SOUL.md, working memory, and agent files. Scheduled tasks are part of the agent's identity — not a personal utility.

```
<mind-repo>/                         # e.g., ~/src/ernist/
├── SOUL.md
├── .working-memory/
├── .github/
│   ├── agents/                      # Agent files (personality, skills)
│   └── extensions/
│       └── cron/
│           ├── extension.mjs        # Entry point — registers tools and hooks with joinSession()
│           ├── package.json         # Minimal, just for npm dependencies (croner)
│           ├── node_modules/        # Vendored/installed deps
│           │
│           ├── tools/
│           │   ├── crud.mjs         # cron_create, cron_list, cron_get, cron_update, cron_delete
│           │   ├── lifecycle.mjs    # cron_pause, cron_resume
│           │   └── engine-control.mjs # cron_engine_start, cron_engine_stop, cron_engine_status
│           │
│           ├── engine/
│           │   └── main.mjs         # Standalone engine script, spawned as detached process
│           │
│           ├── lib/
│           │   ├── store.mjs        # Job store — one JSON file per job, atomic writes
│           │   ├── identity.mjs     # Identity loader — assembles SOUL.md + agent files into system message
│           │   ├── scheduler.mjs    # Schedule evaluation — cron expressions, intervals, one-shot
│           │   ├── lifecycle.mjs    # State transitions — success/failure/retry/disable
│           │   ├── executor.mjs     # Command executor — child_process with timeout
│           │   ├── prompt-executor.mjs # Prompt executor — spawns CopilotClient with --mind
│           │   ├── errors.mjs       # Error classification — transient vs permanent
│           │   ├── history.mjs      # Run history — per-job append, auto-prune
│           │   └── stagger.mjs      # Deterministic offset per job ID (SHA-256 hash)
│           │
│           └── data/
│               ├── jobs/            # One JSON file per job: {jobId}.json
│               ├── history/         # One JSON file per job: {jobId}.json (array of run records)
│               └── engine.lock      # PID lockfile for singleton engine
```

The `data/` directory should be added to `.gitignore` — job state and history are local runtime data, not version-controlled.

### How the pieces connect

```
┌──────────────────────────────────────┐
│         Copilot CLI Session          │
│                                      │
│  User: "run my tests every hour"     │
│                  │                   │
│          Agent decides to call       │
│          cron_create tool            │
└──────────┬───────────────────────────┘
           │ JSON-RPC / stdio
           ▼
┌──────────────────────────────────────┐
│      extension.mjs (forked child)    │
│                                      │
│  tools/crud.mjs → lib/store.mjs     │
│  Writes data/jobs/{id}.json          │
└──────────────────────────────────────┘

┌──────────────────────────────────────┐
│  engine/main.mjs (detached process)  │
│                                      │
│  2-second tick loop                  │
│  Reads data/jobs/*.json              │
│  Evaluates schedules via lib/        │
│  Spawns commands via lib/executor    │
│  Spawns prompts via lib/prompt-exec  │
│    (CopilotClient with --mind)       │
│  Writes data/history/{id}.json       │
│  Holds data/engine.lock with PID     │
└──────────────────────────────────────┘
```

The extension and engine share the `data/` directory. They never share a process — the extension handles CRUD through the tool interface, the engine handles execution independently.

### Concurrency model

Multiple Copilot sessions can load the extension simultaneously. Safety guarantees:

- **One file per job** — two sessions creating different jobs write to different files. No conflicts.
- **Atomic writes** — write to temp file, then `fs.renameSync()`. No partial JSON on crash.
- **Engine singleton** — `engine.lock` contains PID. Before starting, check if PID is alive (`process.kill(pid, 0)`). Stale lockfile from reboot is detected and cleaned up.
- **Active job tracking** — engine keeps a `Set<string>` of in-flight job IDs. Same job won't double-dispatch.

## Job Model

Ported from Gateway's CronJob with support for both command and prompt payloads:

### Command job example

```json
{
  "id": "a1b2c3d4",
  "name": "Run linter",
  "status": "enabled",
  "maxConcurrency": 1,
  "createdAtUtc": "2026-03-10T22:00:00.000Z",
  "createdFrom": "C:\\src\\msclaw",
  "lastRunAtUtc": null,
  "nextRunAtUtc": "2026-03-10T22:30:00.000Z",
  "schedule": {
    "type": "cron",
    "expression": "*/30 * * * *",
    "timezone": "America/Chicago"
  },
  "payload": {
    "type": "command",
    "command": "npm",
    "arguments": "run lint",
    "workingDirectory": "C:\\src\\msclaw",
    "timeoutSeconds": 300
  },
  "backoff": null
}
```

### Prompt job example

```json
{
  "id": "e5f6g7h8",
  "name": "Weekly git summary",
  "status": "enabled",
  "maxConcurrency": 1,
  "createdAtUtc": "2026-03-10T22:00:00.000Z",
  "createdFrom": "C:\\src\\msclaw",
  "lastRunAtUtc": null,
  "nextRunAtUtc": "2026-03-13T17:00:00.000Z",
  "schedule": {
    "type": "cron",
    "expression": "0 17 * * 5",
    "timezone": "America/Chicago"
  },
  "payload": {
    "type": "prompt",
    "prompt": "Summarize the git log for the past week in C:\\src\\msclaw. Focus on major changes and open threads.",
    "model": "claude-sonnet-4.5",
    "preloadToolNames": null,
    "timeoutSeconds": 120
  },
  "backoff": null
}
```

### Schedule types

| Type | Fields | Behavior |
|------|--------|----------|
| `cron` | `expression`, `timezone?` | Standard 5-field cron. Timezone defaults to UTC. Uses `croner` npm package. |
| `interval` | `intervalMs` | Fixed interval from last run. First run fires immediately. |
| `oneShot` | `fireAtUtc` | Fires once. Disables after success or max retries (3). |

### Payload types

#### Command payload (`type: "command"`)

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `type` | `"command"` | required | Discriminator |
| `command` | string | required | Executable name or path |
| `arguments` | string? | null | Command-line arguments |
| `workingDirectory` | string? | user home | Working directory for the process |
| `timeoutSeconds` | number | 300 | Kill process tree after this duration |

#### Prompt payload (`type: "prompt"`)

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `type` | `"prompt"` | required | Discriminator |
| `prompt` | string | required | The message to send to the LLM |
| `model` | string? | null | Model override (e.g., `"claude-sonnet-4.5"`, `"gpt-5"`). Uses CLI default if null. |
| `preloadToolNames` | string[]? | null | Tool names to make available during prompt execution |
| `timeoutSeconds` | number | 120 | Abort session after this duration |

Each prompt job execution consumes one premium request from the user's Copilot quota. Scheduled prompts add up — a job running every 30 minutes is ~48 requests/day.

### Backoff state

```json
{
  "consecutiveFailures": 2,
  "nextRetryAtUtc": "2026-03-10T22:05:00.000Z",
  "lastErrorMessage": "Connection timeout"
}
```

Exponential backoff steps: 30s → 1m → 5m → 15m → 1h (capped).

### Run history record

```json
{
  "runId": "uuid",
  "jobId": "a1b2c3d4",
  "startedAtUtc": "2026-03-10T22:00:00.000Z",
  "completedAtUtc": "2026-03-10T22:00:03.000Z",
  "outcome": "success",
  "errorMessage": null,
  "durationMs": 3000
}
```

Per-job history files. Auto-prune at 500 records or 1 MB.

## Tools

### CRUD tools (tools/crud.mjs)

| Tool | Description | Key Parameters |
|------|-------------|----------------|
| `cron_create` | Create a new cron job | `name`, `payloadType` (`command` or `prompt`), command fields or prompt fields, `scheduleType`, schedule fields, `timeoutSeconds?` |
| `cron_list` | List all jobs with status and next run | `status?` (filter by enabled/disabled) |
| `cron_get` | Get job details and recent run history | `jobId` |
| `cron_update` | Modify a job's schedule, payload, or name | `jobId`, plus any fields to change |
| `cron_delete` | Delete a job and its history | `jobId` |

### Lifecycle tools (tools/lifecycle.mjs)

| Tool | Description | Key Parameters |
|------|-------------|----------------|
| `cron_pause` | Disable a job (keeps definition) | `jobId` |
| `cron_resume` | Re-enable a paused job, recalculate next run | `jobId` |

### Engine control tools (tools/engine-control.mjs)

| Tool | Description | Key Parameters |
|------|-------------|----------------|
| `cron_engine_start` | Spawn engine as detached, windowless process | none |
| `cron_engine_stop` | Kill engine by PID from lockfile | none |
| `cron_engine_status` | Check if engine is running, report uptime and active jobs | none |

## Engine Design (engine/main.mjs)

Ported from Gateway's CronEngine, adapted for standalone Node.js:

- **Tick interval**: 2 seconds (`setInterval`)
- **On each tick**: read all job files from `data/jobs/`, evaluate schedules, dispatch due jobs
- **Dual executor dispatch**: checks `payload.type` — routes `"command"` to `lib/executor.mjs`, `"prompt"` to `lib/prompt-executor.mjs`
- **Command execution**: `child_process.spawn` with `timeout`, combined stdout/stderr capture
- **Prompt execution**: creates a `CopilotClient` via `@github/copilot-sdk` with the mind repo root as `Cwd` (so the CLI discovers SOUL.md and agent files), creates a session with the specified model, sends the prompt, waits for `SessionIdleEvent`, captures the response, disposes. The mind path is resolved relative to the extension directory (`../../..` from `.github/extensions/cron/`).
- **State transitions**: `lib/lifecycle.mjs` — same logic as Gateway's `CronJobLifecycle.ApplyResult()`
- **Error classification**: `lib/errors.mjs` — timeout and network errors are transient, everything else permanent
- **Stagger**: `lib/stagger.mjs` — SHA-256 hash of job ID → deterministic offset within configurable window
- **Logging**: write to stderr (not stdout) and optionally to `data/engine.log`
- **Lockfile**: write PID to `data/engine.lock` on startup, delete on clean shutdown. SIGTERM handler for graceful stop.
- **Concurrency gate**: configurable max concurrent executions (default 3), enforced via counter

### Prompt executor identity flow

**SOUL.md is NOT auto-discovered by the Copilot CLI.** The CLI picks up `.github/copilot-instructions.md` and `.github/instructions/*.instructions.md` from the `Cwd` directory automatically, but SOUL.md is a MsClaw mind concept. The engine must explicitly load the mind's identity and inject it.

The engine's `lib/identity.mjs` ports the Gateway's `IdentityLoader` logic:

1. Read `SOUL.md` from mind root
2. Glob `.github/agents/*.agent.md`, sort alphabetically
3. Strip YAML frontmatter (`---...---` block at start of each file)
4. Join all parts with `"\n\n---\n\n"` separator
5. Cache at engine startup — the identity doesn't change between job runs

The prompt executor then configures each session with both layers:

```js
// lib/prompt-executor.mjs
const client = new CopilotClient({
    Cwd: mindRoot,          // CLI discovers .github/copilot-instructions.md, etc.
    AutoStart: true,
    UseStdio: true,
});

const session = await client.CreateSessionAsync({
    Model: payload.model,
    Streaming: true,
    SystemMessage: {
        Mode: "append",      // preserves safety guardrails
        Content: cachedIdentity,  // SOUL.md + agent files assembled by lib/identity.mjs
    },
});
```

This gives the prompt job the full mind personality: SOUL.md defines who the agent is, agent files add capabilities, and standard Copilot instruction files are picked up automatically from the working directory.

### SDK access for the engine

The detached engine runs as a standalone `node` process outside the extension host. It does not get the automatic `@github/copilot-sdk` module resolution that extensions enjoy. The engine resolves the SDK from the well-known install location:

```js
// engine/main.mjs
import { homedir } from "node:os";
import { join } from "node:path";

const sdkBase = join(homedir(), ".copilot", "pkg", "universal");
// Resolve the latest version directory under sdkBase
const { CopilotClient } = await import(join(sdkBase, version, "copilot-sdk", "index.js"));
```

No environment variables, no parameters from the extension. The engine knows where the SDK lives because `~/.copilot/pkg/universal/` is the standard Copilot CLI install path.

### Engine spawn (from extension)

```js
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const extDir = dirname(fileURLToPath(import.meta.url));
const enginePath = join(extDir, "engine", "main.mjs");

const child = spawn("node", [enginePath], {
    detached: true,
    stdio: "ignore",
    windowsHide: true,       // no console window flash
});
child.unref();               // parent doesn't wait for child
```

`windowsHide: true` + `detached: true` + `stdio: "ignore"` = invisible background process on Windows.

## Dependencies

| Package | Purpose | Alternative |
|---------|---------|-------------|
| `croner` | Cron expression parsing with timezone support | `cron-parser` (less timezone support) |

Single runtime dependency. All other functionality uses Node.js built-ins (`fs`, `path`, `child_process`, `crypto`).

Install via `package.json` in the extension directory:

```json
{
  "name": "copilot-cron-extension",
  "private": true,
  "type": "module",
  "dependencies": {
    "croner": "^9.0.0"
  }
}
```

## Learnings from Gateway Implementation

The existing .NET cron system in `MsClaw.Gateway/Services/Cron/` has battle-tested patterns that must carry over. These are not suggestions — they're lessons learned.

### Prompt executor session pattern

The Gateway's `PromptJobExecutor` establishes a proven flow for running AI prompts on a schedule:

1. **Isolated session per run** — each execution creates a fresh session keyed `"cron:{jobId}:{runId}"`. Sessions are never reused across runs.
2. **Three-event completion** — subscribe to `AssistantMessageEvent` (capture latest content), `SessionErrorEvent` (capture error), and `SessionIdleEvent` (signal done). Use a `TaskCompletionSource` / `Promise` to await completion.
3. **System message append** — the Gateway appends its system message via `SystemMessageMode.Append`, preserving safety guardrails. The extension engine must do the same — load SOUL.md + agent files via `lib/identity.mjs` and inject as `SystemMessage: { mode: "append", content: ... }`. Setting `Cwd` to the mind root is NOT sufficient — SOUL.md is not auto-discovered by the CLI.
4. **Tool preloading** — prompt payloads support `preloadToolNames` so specific tools can be made available during execution. The extension should carry this forward.
5. **Model override per job** — each prompt job can specify its own model, falling back to the session default.
6. **Guaranteed cleanup** — session disposal happens in a `finally` block, always, even on error. Leaking sessions means leaking CLI child processes.

### Engine dispatch rules

The engine's tick-evaluate-dispatch cycle has specific ordering and safety checks:

1. **Due check**: `status == enabled AND nextRunAtUtc <= now AND nextRunAtUtc != null AND (backoff == null OR backoff.nextRetryAtUtc <= now) AND job not in activeJobIds`
2. **Non-blocking concurrency gate** — `WaitAsync(0)` (or equivalent). If the gate is full, skip the job — it'll be due again next tick. Never block the tick loop.
3. **Active job tracking** — `Set<string>` of in-flight job IDs prevents double-dispatch. Add before execution, remove in `finally`.
4. **Deterministic ordering** — due jobs sorted by `nextRunAtUtc` then `id` for reproducibility.
5. **Drain on shutdown** — `SIGTERM` handler must wait for in-flight executions before exit.

### Tool response shape

The Gateway's `CronToolProvider` returns both structured data AND a human-readable `Summary` string in every response. The agent uses the summary for conversation; the structured data is for programmatic use. This dual-return pattern is critical for good conversational UX.

### Job ID generation

Job IDs are derived from the job name via kebab-case conversion (`"Run Linter" → "run-linter"`), not random UUIDs. This makes IDs human-readable in conversation and predictable for repeat interactions.

### Command executor details

- `CreateNoWindow = true` / `windowsHide: true` — **critical** on Windows to avoid console flash
- Combined stdout + stderr into single output
- Process **tree** kill on timeout (not just the parent process)
- Exit code 0 = success, anything else = failure
- Timeout is classified as **transient** (retryable); invalid executable is **permanent**

### Atomic write protocol

1. Serialize to JSON string
2. Write to temp file with random suffix in same directory
3. `rename(temp, target)` — atomic on both Windows (NTFS) and Linux
4. Clean up temp file in `finally` block even if rename fails
5. Serialize flush operations behind a lock/semaphore to prevent interleaving

### Cron expression handling

- Auto-detect 5-field (standard) vs 6-field (with seconds) by counting space-separated tokens
- Timezone lookup via IANA identifier, falls back to UTC when null/empty
- The `croner` npm package supports both formats and timezone-aware evaluation

### History pruning (dual limits)

Gateway enforces **both** a record count limit (2,000) AND a byte size limit (2 MB) per job. Prune oldest first. The extension plan currently says 500 records / 1 MB — adjust as needed, but keep the dual-limit pattern.

## Non-Goals

- **Distributed / multi-process coordination** — single engine per mind
- **Job chaining or DAG dependencies**
- **Web UI or TUI** — the Copilot CLI conversation IS the UI
- **Changes to Gateway's existing cron** — Gateway keeps its own cron system independently
- **Cross-mind job management** — each mind manages its own schedules

## Relationship to Gateway

This extension and Gateway's cron are **independent systems**:

- Gateway keeps its .NET cron engine, prompt executor, SignalR sink, and tool provider
- The extension is a mind-scoped scheduler with no Gateway dependency
- They do not share storage — Gateway uses `~/.msclaw/cron/`, extension uses `<mind-repo>/.github/extensions/cron/data/`
- If Gateway is ever retired, the extension stands on its own
- Gateway's prompt executor uses session pools; the extension's prompt executor spawns fresh CopilotClient instances with the mind's personality

## Design Decisions

- **Mind-scoped, not user-scoped**: The extension lives in the mind repo because scheduled tasks are part of the agent's behavior. Prompt jobs use the mind's SOUL.md and agent files for personality. Managing schedules happens from within the mind repo context — this is where you'd naturally configure the agent.
- **Conditional auto-start**: The `onSessionStart` hook checks if jobs exist in `data/jobs/`. If yes, checks if the engine PID is alive. If not running, spawns it automatically. If no jobs exist, does nothing. First `cron_create` call also starts the engine as a side effect. The user never has to think about engine lifecycle.

## Open Questions

- Should the extension include a `cron_engine_logs` tool to tail recent engine output?
- Maximum number of concurrent jobs in the engine — default 3, or configurable via a settings file?
- Should job `createdFrom` (repo path) be used for filtering in `cron_list`, or is it just metadata?

## Tasks

### Phase 1: Foundation
- [ ] Scaffold extension directory at `<mind-repo>/.github/extensions/cron/` with `extension.mjs`
- [ ] Install `croner` dependency, create `package.json`
- [ ] Add `data/` to mind repo `.gitignore`
- [ ] Implement `lib/store.mjs` — one-file-per-job CRUD with atomic writes
- [ ] Implement `lib/identity.mjs` — read SOUL.md + .github/agents/*.agent.md, strip frontmatter, join with `---` separator, cache result
- [ ] Implement `lib/scheduler.mjs` — cron, interval, and one-shot schedule evaluation
- [ ] Wire `extension.mjs` with `joinSession()`, verify extension loads

### Phase 2: CRUD Tools
- [ ] Implement `cron_create` tool — validate inputs, generate ID, write job file, calculate first `nextRunAtUtc`
- [ ] Implement `cron_list` tool — read all job files, format as table
- [ ] Implement `cron_get` tool — read job file + recent history
- [ ] Implement `cron_update` tool — partial update with recalculated schedule
- [ ] Implement `cron_delete` tool — remove job file and history file
- [ ] Implement `cron_pause` and `cron_resume` tools

### Phase 3: Engine
- [ ] Implement `lib/executor.mjs` — spawn command, capture output, enforce timeout
- [ ] Implement `lib/prompt-executor.mjs` — create CopilotClient with mind root as Cwd, load cached identity from lib/identity.mjs, inject as SystemMessage (mode: append), create session, send prompt, await SessionIdle, capture response, dispose client
- [ ] Implement `lib/lifecycle.mjs` — state transitions, backoff calculation
- [ ] Implement `lib/errors.mjs` — transient vs permanent classification
- [ ] Implement `lib/history.mjs` — append run records, auto-prune
- [ ] Implement `engine/main.mjs` — tick loop, job evaluation, dual-dispatch (command/prompt), lockfile management
- [ ] Implement `cron_engine_start` tool — spawn detached, windowless engine process
- [ ] Implement `cron_engine_stop` tool — read lockfile, kill PID, clean up
- [ ] Implement `cron_engine_status` tool — check PID alive, report active jobs

### Phase 4: Polish
- [ ] Implement `lib/stagger.mjs` — deterministic offset per job ID
- [ ] Add `onSessionStart` hook — conditionally auto-start engine if jobs exist and engine not running
- [ ] Start engine as side effect of `cron_create` when it's the first job
- [ ] Add `createdFrom` metadata to jobs (current working directory at creation time)
- [ ] End-to-end test: create command job → start engine → verify command executes → check history
- [ ] End-to-end test: create prompt job → verify engine spawns CopilotClient with mind personality → check response in history
- [ ] Update old plan (`20260310-extract-cron-standalone-tool.md`) status to `superseded`
