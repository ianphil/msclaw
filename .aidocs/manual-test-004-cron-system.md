# Manual E2E Test — Cron System (Feature 004)

## Prerequisites

- `copilot` CLI installed and on PATH
- A valid mind directory (use `~/src/ernist` for testing)
- .NET 10 SDK installed
- Authenticated with GitHub (`gh auth login`)

## Start the Gateway

```powershell
dotnet run --project src\MsClaw.Gateway -- start --mind C:\src\ernist
```

Verify the banner prints with all endpoints:

```
MSCLAW GATEWAY READY
  UI (browser)      http://127.0.0.1:18789/
  SignalR Hub       http://127.0.0.1:18789/gateway
  Health            http://127.0.0.1:18789/health
```

## Test 1 — Cron Tools Discoverable

Open `http://127.0.0.1:18789/` in a browser and send:

> What cron tools do you have? List every cron tool name you can see.

**Expected:** The response lists all 7 tools: `cron_create`, `cron_list`, `cron_get`, `cron_update`, `cron_delete`, `cron_pause`, `cron_resume`. These are `Bundled` tier with `AlwaysVisible = true`, so they appear in every session without needing `expand_tools`.

## Test 2 — Create a One-Shot Job

Send:

> Remind me about the standup in 2 minutes.

**Expected:** The agent calls `cron_create` with a `oneShot` schedule set ~2 minutes from now and a `prompt` payload. The response confirms the job was created and shows the next run time.

## Test 3 — Create a Recurring Job

Send:

> Check my inbox every morning at 9am Eastern.

**Expected:** The agent calls `cron_create` with a `cron` schedule (`0 9 * * *`), timezone `America/New_York`, and a `prompt` payload. The response confirms the job and shows the next scheduled run.

## Test 4 — List Jobs

Send:

> List all my cron jobs.

**Expected:** The agent calls `cron_list` and returns a summary of both jobs created above, showing name, ID, status (enabled), schedule, and next run time.

## Test 5 — Get Job Details

Send:

> Show me the details of the inbox check job.

**Expected:** The agent calls `cron_get` with the job ID (e.g., `check-my-inbox-every-morning-at-9am-eastern` or similar kebab-case slug) and returns full job details plus run history (empty at this point).

## Test 6 — Pause and Resume

Send:

> Pause the inbox check job.

**Expected:** The agent calls `cron_pause`, the job status changes to `disabled`. Verify with:

> List my cron jobs.

The inbox check should show `disabled`. Then:

> Resume the inbox check job.

**Expected:** Status returns to `enabled`.

## Test 7 — Delete a Job

Send:

> Delete the standup reminder.

**Expected:** The agent calls `cron_delete` and confirms the job was removed. A follow-up `cron_list` should show only the inbox check job.

## Test 8 — One-Shot Job Fires

1. Create a one-shot job that fires in 10 seconds:

> Create a one-shot cron job called "quick-test" that fires in 10 seconds with the prompt "Say hello, this is a cron test."

2. Wait ~12 seconds (2-second timer tick + execution time).

**Expected:** The gateway console shows the cron engine dispatching the job. The SignalR hub receives a `ReceiveCronResult` event with the assistant's response. The job status changes to `disabled` (finalized).

## Test 9 — Command Payload

Send:

> Create a cron job called "disk-check" that runs "df -h" every 5 minutes as a command, not a prompt.

(On Windows, use `dir` instead of `df -h`.)

**Expected:** The agent calls `cron_create` with `payloadType: "command"` and `scheduleType: "fixedInterval"` with `intervalMs: 300000`. No LLM session is created — the command runs directly via `Process.Start()`.

## Test 10 — Job Persistence Across Restart

1. Verify jobs exist with `cron_list`.
2. Stop the gateway (Ctrl+C).
3. Inspect `~/.msclaw/cron/jobs.json` — it should be human-readable JSON with all jobs.
4. Restart the gateway.
5. Send `cron_list` again.

**Expected:** All jobs survive the restart with their schedules, status, and configuration intact.

## Test 11 — Overdue-on-Startup

1. Create a recurring job that fires every 30 seconds.
2. Stop the gateway.
3. Wait 60 seconds (the job becomes overdue).
4. Restart the gateway.

**Expected:** The overdue job fires on the first engine tick after startup (within ~2 seconds).

## Test 12 — Concurrent Execution Guard

1. Create a `prompt` job with a slow prompt: "Write a 1000-word essay on software architecture."
2. While it's executing, check that a second tick doesn't dispatch the same job again.

**Expected:** The engine logs show the job dispatched once. `IsJobActive` returns true while executing. A second tick skips it.

---

## Verifying Engine Behavior (Console)

Check the gateway console for log lines at startup:

```
Cron engine started — evaluating jobs every 2 seconds
```

When a job fires:

```
Dispatching cron job 'quick-test' (run: <guid>)
Cron job 'quick-test' completed: Success (duration: 1234ms)
```

**Expected:** No exceptions in the log during normal operation.

## Verifying Disk State

```powershell
# Job definitions
cat ~/.msclaw/cron/jobs.json

# Run history for a specific job
cat ~/.msclaw/cron/history/quick-test.json
```

**Expected:** JSON is well-formatted (`WriteIndented`), fields use camelCase, and null fields are omitted.

---

## What's NOT Testable Yet

| Area | Why |
|------|-----|
| Main session jobs (REQ-003) | Heartbeat system not built |
| Channel delivery (REQ-006) | Agent uses MCPorter tools directly in prompt payload |
| Session retention (REQ-015) | Deferred to session pool lifecycle feature |
| Stagger behavior | Requires multiple jobs with identical cron expressions at scale |
| History pruning | Requires generating >2MB or >2000 records per job |
| Spec tests | Run `specs\tests\Invoke-SpecTests.ps1 specs\tests\004-cron-system.md` when copilot CLI is on PATH |
