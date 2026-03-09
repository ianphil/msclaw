# Cron System Conformance Research

**Date**: 2026-03-09
**Spec Version Reviewed**: [gateway-cron.md v1.0](../../specs/gateway-cron.md)
**Plan Version**: plan.md (this feature)

## Summary

The implementation plan covers 13 of 18 spec requirements fully. Five requirements (REQ-003, REQ-005, REQ-006, REQ-015, and REQ-013 partially) are deferred due to dependencies on systems not yet built (heartbeat, channels) or scoped as future enhancements. All deferred requirements have explicit migration paths documented. Two capabilities not in the spec (`CommandPayload` and `preloadToolNames`) are added based on operational needs.

## Conformance Analysis

### 1. Schedule Types (REQ-001)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| One-shot timestamp | `OneShot` schedule with `DateTimeOffset` | One-shot fires at specified time, no repeat | CONFORMANT |
| Fixed interval | `FixedInterval` schedule with `IntervalMs` | Every N milliseconds | CONFORMANT |
| Cron expression | `CronExpression` schedule parsed by Cronos | 5-field or 6-field | CONFORMANT |
| Timezone support | IANA timezone via Cronos `TZoneInfo` | IANA timezone names | CONFORMANT |

### 2. Job Persistence (REQ-002)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Disk persistence | `~/.msclaw/cron/jobs.json` | Persisted to disk, survive restarts | CONFORMANT |
| Atomic writes | Write-temp-then-rename | Before change is committed | CONFORMANT |
| Human-inspectable | JSON with `WriteIndented` | Loadable without gateway running | CONFORMANT |
| Resume on restart | Hot-reload on each timer tick | Enabled jobs resume scheduling | CONFORMANT |

### 3. Main Session Jobs (REQ-003)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Enqueue system event | Deferred | MUST enqueue system event in main session | DEFERRED |
| Non-empty message text | Deferred | Message text MUST be non-empty | DEFERRED |
| Heartbeat wake option | Deferred | MAY wake heartbeat | DEFERRED |

**Recommendation**: Deferred to heartbeat feature. The `JobPayload` discriminated union is extensible — adding a `MainSessionPayload` variant and `MainSessionJobExecutor` requires zero engine changes.

### 4. Isolated Session Jobs (REQ-004)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Fresh session per execution | `PromptJobExecutor` creates new session via `SessionPool` | New session, no prior history | CONFORMANT |
| Prompt as user message | Job prompt sent via `session.SendAsync` | Configured prompt as user message | CONFORMANT |
| Model/reasoning override | `PromptPayload.Model` field (optional) | MAY override default model | CONFORMANT |
| Independent from main session | Separate `SessionPool` key (`cron:{jobId}:{runId}`) | Independent from main session | CONFORMANT |

### 5. Heartbeat Wake Modes (REQ-005)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| "Now" mode | Deferred | MUST attempt immediate wake | DEFERRED |
| "Next heartbeat" mode | Deferred | Wait for next cycle | DEFERRED |
| Busy fallback | Deferred | Wait up to timeout, then on-demand | DEFERRED |

**Recommendation**: Deferred with REQ-003. Depends on heartbeat system.

### 6. Delivery Modes (REQ-006)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Announce to channel | Deferred | Output delivered to channel | DEFERRED |
| Webhook POST | Deferred | Output POSTed to URL | DEFERRED |
| None mode | Implicit — no external delivery, output to SignalR only | Internal-only execution | PARTIAL |

**Recommendation**: Deferred to channel system feature. Agent can use MCPorter tools directly in `PromptPayload` prompts to achieve announce behavior. `CronRunResult` captures output for all payload types, enabling future delivery routing.

### 7. Job Lifecycle (REQ-007)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Enabled/disabled/running states | `CronJobStatus` enum: Enabled, Disabled, Running | Three lifecycle states | CONFORMANT |
| Only enabled jobs scheduled | Engine filters by status on each tick | Only enabled considered | CONFORMANT |
| Running prevents concurrent | Engine checks running state before dispatch | MUST NOT schedule concurrent | CONFORMANT |
| Disabled retains config | Disable sets status, preserves all fields | Retain config and history | CONFORMANT |

### 8. Job Management Operations (REQ-008)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Create | `cron_create` tool | Create with schedule, prompt, delivery | CONFORMANT |
| List | `cron_list` tool with state/schedule/last/next run | List with current state | CONFORMANT |
| Enable/disable | `cron_pause` / `cron_resume` tools | Enable/disable immediately | CONFORMANT |
| Delete | `cron_delete` tool | Remove from store, cancel pending | CONFORMANT |
| Get | `cron_get` tool | Not explicitly required but useful | ADDITION |
| Update | `cron_update` tool | Not explicitly required but useful | ADDITION |

### 9. Concurrency Control (REQ-009)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Configurable limit | Default 1, configurable | Default 1, configurable | CONFORMANT |
| Queuing when full | Due jobs queued until slot available | MUST queue | CONFORMANT |

### 10. One-Shot Completion (REQ-010)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Finalize on success | Job disabled after success | Deleted or disabled (configurable) | CONFORMANT |
| Retry on transient error | Configurable max (default: 3) + exponential backoff | Up to configurable max | CONFORMANT |
| Disable on permanent error | Immediate disable | Disabled immediately | CONFORMANT |

### 11. Recurring Error Handling (REQ-011)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Exponential backoff | 30s, 1m, 5m, 15m, 60m steps | Configurable intervals | CONFORMANT |
| Reset on success | Backoff counter reset | MUST reset after success | CONFORMANT |
| Never permanently disabled | Recurring jobs remain enabled | Errors do not disable | CONFORMANT |

### 12. Transient Error Detection (REQ-012)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Rate limits/timeouts/5xx = transient | Classified as transient | MUST be transient | CONFORMANT |
| Auth failures/invalid config = permanent | Classified as permanent | MUST be permanent | CONFORMANT |

### 13. Stagger (REQ-013)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Stagger window | Configurable, default 0–5 minutes | SHOULD spread across window | CONFORMANT |
| Deterministic offset | Hash-based per job ID | Same job always same offset | CONFORMANT |

### 14. Run History (REQ-014)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Record per execution | Job ID, run ID, start, end, outcome, error, duration | Required fields | CONFORMANT |
| Disk persistence | Per-job JSON at `~/.msclaw/cron/history/{jobId}.json` | Persisted to disk | CONFORMANT |
| Auto-pruning | 2MB/2000 lines configurable | Automatic pruning | CONFORMANT |

### 15. Session Retention (REQ-015)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| 24-hour default retention | Deferred — relies on SessionPool lifecycle | 24-hour default | DEFERRED |
| Periodic cleanup | Not implemented in this feature | Sessions older than retention deleted | DEFERRED |

**Recommendation**: `SessionPool` already has reap timeout support. Cron sessions will be reaped by the pool's existing timeout (default 30 minutes). Explicit 24-hour retention configurable per cron-created sessions can be added as a follow-up.

### 16. Failure Alerts (REQ-016)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Alert on failure | Published to SignalR hub via `ReceiveEvent` | Deliver alert through configured channel | PARTIAL |
| Alert content | Job name, failure reason, retry status | Required content | CONFORMANT |

**Note**: Full channel delivery deferred with REQ-006. SignalR push covers connected clients.

### 17. Hot Reload (REQ-017)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Reload each tick | Job store re-read on each 2-second tick | MUST reload on each timer cycle | CONFORMANT |
| Manual edits picked up | Yes, on next startup or tick | Picked up on next startup | CONFORMANT |

### 18. Minimum Refire Gap (REQ-018)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| 2-second minimum | `PeriodicTimer` with 2-second period | At least 2 seconds | CONFORMANT |

## New Features in Spec (Not in Plan)

All spec requirements are addressed — either implemented or explicitly deferred with migration paths.

## Additions Not in Spec

1. **`CommandPayload`**: Shell command execution without LLM session. Useful for deterministic tasks (scripts, backups, git operations). Not in the spec but adds significant value at low complexity.
2. **`preloadToolNames`**: Optional field on `PromptPayload` to pre-expand specific tools in the isolated session, avoiding the `expand_tools` round-trip.
3. **`cron_get` and `cron_update` tools**: Not explicitly required by spec but natural extensions of CRUD operations.

## Recommendations

### Critical Updates
None — all critical spec requirements are addressed or explicitly deferred with rationale.

### Minor Updates
1. Add `SessionPool` reap timeout configuration for cron-created sessions to support REQ-015 retention.
2. Consider adding `lastSuccessAtUtc` to `CronJob` for operational visibility beyond `lastRunAtUtc`.

### Future Enhancements
1. `MainSessionPayload` + `MainSessionJobExecutor` when heartbeat system ships.
2. Delivery routing (Announce/Webhook) when channel adapters mature.
3. Extended session retention policies for cron-created sessions.

## Sources

- [specs/gateway-cron.md](../../specs/gateway-cron.md) — Product specification v1.0
- [backlog/plans/20260309-cron-system.md](../20260309-cron-system.md) — Quick plan
- [backlog/plans/_completed/003-tool-bridge/](../_completed/003-tool-bridge/) — IToolProvider pattern reference

## Conclusion

The implementation plan achieves strong conformance with the cron spec, covering 13 of 18 requirements fully. The 5 deferred requirements (REQ-003, REQ-005, REQ-006, REQ-015 partially, and REQ-016 partially) all depend on systems not yet built. The `JobPayload` discriminated union and `ICronJobExecutor` pattern ensure these can be added later with zero engine changes. The two additions (`CommandPayload` and `preloadToolNames`) extend the spec's capabilities for common operational scenarios.
