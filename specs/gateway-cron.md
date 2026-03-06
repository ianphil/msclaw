# Product Specification: Cron System

**Document Owner(s):** MsClaw Team  
**Status:** Draft  
**Document Level:** Feature  
**Target Release:** TBD  
**Link to Technical Spec:** [Added later by Engineering]  

## Version History
*Rule: No silent mutations. All changes after baseline must be recorded here.*

| Version | Date | Author | Description of Changes |
| :--- | :--- | :--- | :--- |
| 1.0 | 2026-03-04 | MsClaw Team | Initial Draft — derived from OpenClaw cron system analysis |
| | | | |

---

> **⚠️ Author Guidelines (Read Before Writing)**
> *   **Focus on the "What" and "Why".** Absolutely NO technical implementation details (the "How"). No database schemas, API JSON payloads, or code snippets.
> *   **No Subjective Language.** Ban words like *fast, seamless, modern, intuitive, or robust*. Use empirical, verifiable metrics.
> *   **Testability.** Every requirement must be written so QA can translate it into a definitive Pass/Fail test.
> *   **Terminology.** Use RFC 2119 keywords: **MUST**, **SHOULD**, **MAY**.

---

## 1. Executive Summary & Problem Statement

### 1.1 The Problem (The "Why")

An always-on agent needs the ability to perform work on a schedule — sending reminders, running periodic checks, publishing reports, and executing time-sensitive tasks without waiting for a human prompt. Without a cron system, every agent action requires a human to initiate it. Scheduled behaviors like "remind me about the standup in 20 minutes" or "check the deployment status every hour" are impossible.

The [OpenClaw project](https://github.com/openclaw/openclaw) solved this with an in-process cron service supporting cron expressions, fixed intervals, and one-shot timestamps, with jobs executing either in the main session (via system events + heartbeat wake) or in isolated sessions with delivery to channels or webhooks. MsClaw needs the same scheduled execution capability, integrated with the agent runtime and heartbeat system.

### 1.2 Business Value

- **Scheduled agent autonomy:** The agent can perform recurring work (daily summaries, periodic inbox checks, status reports) without human initiation.
- **Time-sensitive actions:** One-shot reminders and deadline-triggered tasks execute at the specified time, not "whenever the user next messages."
- **Multi-delivery output:** Scheduled jobs can deliver results to channels (WhatsApp, Slack), webhooks, or the main session — reaching the operator wherever they are.
- **Composable with heartbeat:** Main-session jobs queue events that the heartbeat processes, enabling scheduled work to flow through the agent's primary conversation with full context.

### 1.3 Success Metrics (KPIs)

*   **Metric 1:** A recurring cron job MUST fire within 5 seconds of its scheduled time under normal gateway load.
*   **Metric 2:** A one-shot job MUST execute at most once — no duplicate executions on retry or restart.
*   **Metric 3:** Job state MUST survive gateway restarts — enabled jobs MUST resume scheduling on the next startup without operator intervention.

## 2. Target Audience & User Personas

| Persona Name | Description | Key Needs for this Feature |
| :--- | :--- | :--- |
| **Operator** | The person who creates, manages, and receives output from scheduled jobs | Needs to schedule reminders, recurring tasks, and automated checks via conversation with the agent. Needs to list, enable, disable, and delete jobs. |
| **MsClaw Agent** | The AI agent that creates jobs on behalf of the operator and executes them | Needs to create jobs from natural language requests ("remind me in 20 minutes"), execute job prompts in isolated sessions, and deliver results. |
| **External System** | A webhook consumer that receives scheduled job output | Needs to receive job results as structured payloads at a configured URL. |

## 3. Functional Requirements (The "What")
*Rule: Use unique identifiers (e.g., REQ-001) for traceability.*

### 3.1 Core User Flows

1.  **Operator Schedules a Reminder:** The operator tells the agent "remind me about the standup in 20 minutes." The agent creates a one-shot job with the appropriate timestamp and message. When the time arrives, the cron system fires the job, the agent delivers the reminder, and the job is completed.
2.  **Operator Creates a Recurring Job:** The operator tells the agent "check my inbox every morning at 9am." The agent creates a recurring job with a cron expression. Every day at 9am, the cron system fires the job, the agent runs the check in an isolated session, and delivers the result to the configured channel.
3.  **Operator Manages Jobs:** The operator asks the agent to list all scheduled jobs. The agent returns the job list with schedule, status, and next run time. The operator can then enable, disable, or delete specific jobs.
4.  **Job Delivers to Channel:** A recurring job fires, the agent produces a summary, and the cron system delivers it to the configured channel (e.g., WhatsApp). The operator receives the message in their messaging app.

### 3.2 Feature Requirements & Acceptance Criteria

| ID | Feature | Description | Acceptance Criteria (Pass/Fail) |
| :--- | :--- | :--- | :--- |
| **REQ-001** | Schedule Types | The cron system MUST support three schedule types: one-shot (specific timestamp), fixed interval (every N milliseconds), and cron expression (5-field or 6-field with timezone). | - A one-shot job MUST fire at the specified timestamp and not repeat.<br>- An interval job MUST fire repeatedly at the specified interval.<br>- A cron expression job MUST fire according to the parsed expression.<br>- Cron expressions MUST support timezone specification (IANA timezone names). |
| **REQ-002** | Job Persistence | Job definitions and state MUST be persisted to disk and survive gateway restarts. | - After gateway restart, all enabled jobs MUST resume scheduling from their persisted state.<br>- Job state changes (enabled/disabled, last run, next run) MUST be written to disk before the change is considered committed.<br>- The job store MUST be loadable without the gateway running (human-inspectable). |
| **REQ-003** | Main Session Jobs | The cron system MUST support jobs that enqueue a system event into the main agent session. | - The system event MUST contain the job's configured message text.<br>- The message text MUST be non-empty.<br>- After enqueueing, the job MAY wake the heartbeat immediately or defer to the next scheduled heartbeat cycle (configurable per job). |
| **REQ-004** | Isolated Session Jobs | The cron system MUST support jobs that run a full agent turn in a fresh, isolated session. | - Each execution MUST create a new session with no prior conversation history.<br>- The job's configured prompt MUST be sent as the user message.<br>- The job MAY override the default model and reasoning level.<br>- The isolated session MUST be independent from the main session. |
| **REQ-005** | Heartbeat Wake Modes | Main session jobs MUST support configurable wake behavior to control when the heartbeat processes the queued event. | - "Now" mode: The cron system MUST attempt to wake the heartbeat immediately after enqueueing the event.<br>- "Next heartbeat" mode: The event MUST wait for the next scheduled heartbeat cycle.<br>- IF "now" mode is selected and the main session is busy, the system MUST wait up to a configurable timeout (default: 2 minutes), then fall back to an on-demand heartbeat wake request. |
| **REQ-006** | Delivery Modes | Isolated session jobs MUST support configurable delivery of their output. | - "Announce" mode: The output MUST be delivered to a specified channel (e.g., WhatsApp, Slack, Telegram).<br>- "Webhook" mode: The output MUST be POSTed to a configured URL.<br>- "None" mode: The output MUST NOT be delivered externally (internal-only execution).<br>- IF delivery mode is "announce" and the output is non-empty, a summary MAY be posted back to the main session. |
| **REQ-007** | Job Lifecycle | Each job MUST have a lifecycle state: enabled, disabled, or running. | - Only enabled jobs MUST be considered for scheduling.<br>- A running job MUST NOT be scheduled for a concurrent execution.<br>- Disabled jobs MUST retain their configuration and history for re-enabling. |
| **REQ-008** | Job Management Operations | The gateway MUST expose operations to create, list, enable, disable, and delete jobs. | - An operator (or agent acting on operator's behalf) MUST be able to create a new job with schedule, prompt, and delivery configuration.<br>- Listing MUST return all jobs with their current state, schedule, last run time, and next scheduled run time.<br>- Enable/disable MUST take effect immediately without gateway restart.<br>- Deletion MUST remove the job from the store and cancel any pending execution. |
| **REQ-009** | Concurrency Control | The cron system MUST enforce a configurable limit on the number of jobs executing simultaneously. | - The default concurrency limit MUST be 1 (serial execution).<br>- Jobs that exceed the concurrency limit MUST be queued for execution when a slot becomes available.<br>- The concurrency limit MUST be configurable. |
| **REQ-010** | One-Shot Job Completion | A one-shot job MUST be finalized after successful execution. | - On success, the job MUST be either deleted or disabled (configurable per job).<br>- On transient failure, the job MUST retry up to a configurable maximum (default: 3 retries) with exponential backoff.<br>- On permanent failure, the job MUST be disabled immediately. |
| **REQ-011** | Recurring Job Error Handling | A recurring job MUST remain enabled after failure and apply backoff before the next run. | - On any error, the next run MUST be delayed by exponential backoff (configurable intervals, e.g., 30s, 1m, 5m, 15m, 60m).<br>- The backoff MUST reset after a successful run.<br>- The job MUST remain enabled — errors do not permanently disable recurring jobs. |
| **REQ-012** | Transient Error Detection | The cron system MUST distinguish transient errors (retryable) from permanent errors. | - Rate limits, network timeouts, and server errors MUST be classified as transient.<br>- Authentication failures and invalid configuration MUST be classified as permanent.<br>- Only transient errors MUST trigger retry behavior for one-shot jobs. |
| **REQ-013** | Stagger for Recurring Jobs | Recurring jobs scheduled at common times (e.g., top of the hour) SHOULD be staggered to avoid load spikes. | - Jobs with identical cron expressions SHOULD be spread across a configurable stagger window (default: 0–5 minutes).<br>- The stagger offset MUST be deterministic per job (same job always gets the same offset). |
| **REQ-014** | Run History | The cron system MUST maintain a history of job executions. | - Each execution MUST record: job ID, start time, end time, outcome (success/failure), and error message if failed.<br>- History MUST be persisted to disk.<br>- History MUST be automatically pruned by configurable size and line limits to prevent unbounded growth. |
| **REQ-015** | Isolated Session Retention | Isolated sessions created by cron jobs MUST be pruned after a configurable retention period. | - The default retention period MUST be 24 hours.<br>- Sessions older than the retention period MUST be deleted during periodic cleanup.<br>- Cleanup MUST NOT interfere with actively running jobs. |
| **REQ-016** | Failure Alerts | The cron system MUST notify the operator when a job fails. | - On job failure, the system MUST deliver an alert through the configured alert channel or enqueue a system event in the main session.<br>- The alert MUST include the job name, failure reason, and retry status. |
| **REQ-017** | Hot Reload | The cron system MUST reload the job store from disk on each timer cycle. | - Manual edits to the job store file (while the gateway is stopped) MUST be picked up on the next startup.<br>- The system SHOULD support reloading without restart for configuration changes made through the management operations. |
| **REQ-018** | Minimum Refire Gap | The cron system MUST enforce a minimum interval between consecutive timer evaluations to prevent tight loops. | - The minimum gap MUST be at least 2 seconds.<br>- IF a job completes and the next job is due immediately, the system MUST wait at least the minimum gap before firing. |

### 3.3 Edge Cases & Error Handling

*   **Gateway restart with overdue jobs:** IF a job's scheduled time passed while the gateway was stopped, the job MUST fire on the next timer evaluation after startup — not skip to the next future schedule.
*   **Job store file corrupted:** The cron system MUST log a descriptive error and refuse to start the scheduler. It MUST NOT silently discard jobs.
*   **Isolated session model unavailable:** IF the model specified by a job override is unavailable, the error MUST be classified as transient and retried.
*   **Webhook delivery failure:** IF the webhook endpoint is unreachable, the error MUST be classified as transient. The job's retry policy applies.
*   **Channel delivery failure:** IF the announce target channel is not connected, the delivery MUST be retried on the next execution or logged for operator review.
*   **Clock skew or jump:** IF the system clock jumps forward, overdue jobs MUST fire on the next evaluation. IF the clock jumps backward, the system MUST NOT fire jobs that are no longer due.
*   **Concurrent job store modification:** The job store MUST NOT be corrupted by concurrent reads and writes. Persisting MUST be atomic (write-then-rename or equivalent).

## 4. Non-Functional Requirements (Constraints)
*Rule: All constraints must be quantifiable and measurable.*

*   **Performance:** The timer evaluation (scanning for due jobs) MUST complete within 100ms for a job store containing up to 500 jobs. Individual job execution time is bounded by the agent runtime's run timeout.
*   **Scalability:** The cron system MUST support up to 500 scheduled jobs without degradation of timer accuracy.
*   **Reliability:** Job state MUST be persisted to disk. Gateway restart MUST NOT lose job definitions or disable enabled jobs. The timer MUST self-correct for clock drift by re-evaluating at most every 60 seconds.
*   **Storage:** Run history MUST be bounded by configurable limits (default: 2MB per job, 2000 lines per job). Pruning MUST be automatic.
*   **Platform / Environment:** The cron system MUST operate on Windows, macOS, and Linux. Timezone handling MUST use IANA timezone names.

## 5. User Experience (UX) & Design

*   **Design Assets:** Not applicable — the cron system is an internal subsystem. Operators interact with it through conversation with the agent.
*   **Interaction Model:** Operators create and manage jobs by talking to the agent in natural language. The agent translates requests into job definitions. Job output reaches operators through their configured delivery channel or the main conversation.
*   **Copy & Messaging:** Job failure alerts MUST include the job name and a human-readable reason. Job listing output MUST include schedule, status, last run, and next run in a readable format.

## 6. Out of Scope (Anti-Goals)
*Rule: Explicitly state what we are NOT building to prevent scope creep.*

*   A visual job management UI (calendar view, drag-and-drop scheduling) is out of scope — job management is conversational.
*   Job chaining or DAG-style dependencies (job B runs after job A completes) is out of scope for v1.
*   Distributed job execution across multiple gateway instances is out of scope — one cron service per gateway.
*   Job templates or a marketplace of pre-built scheduled tasks are out of scope.
*   Sub-second scheduling precision is out of scope — the minimum refire gap is 2 seconds.
*   Persisting isolated session conversation history beyond the retention window is out of scope.

## 7. Dependencies & Assumptions

### 7.1 Dependencies

*   **Agent Runtime ([gateway-agent-runtime.md](gateway-agent-runtime.md)):** Isolated session jobs run full agent turns through the runtime. Main session jobs enqueue events processed by the runtime during heartbeat cycles.
*   **Heartbeat System ([gateway-heartbeat.md](gateway-heartbeat.md)):** Main session jobs use the heartbeat's on-demand wake mechanism to trigger processing of queued system events.
*   **Channel System ([gateway-channels.md](gateway-channels.md)):** Jobs with "announce" delivery mode route output through channel adapters.
*   **Mind System ([gateway-mind.md](gateway-mind.md)):** Isolated session jobs run with the agent's identity loaded from the mind. Job prompts may reference mind knowledge.

### 7.2 Assumptions

*   We assume one cron service per gateway process — no multi-instance coordination.
*   We assume the operator creates jobs via conversation with the agent (the agent has a bundled skill for job management), not through a separate UI.
*   We assume serial execution (concurrency = 1) is sufficient for most deployments — parallel job execution is configurable but not the default.
*   We assume the job store is small enough to fit in memory (up to 500 jobs) and can be fully loaded on each timer cycle.
*   We assume the `croner` library (or equivalent) is available for cron expression parsing with timezone support.
*   We assume transient error patterns (rate limits, timeouts, 5xx) are identifiable by the runtime and can be communicated to the cron system.
