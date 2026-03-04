# Product Specification: Heartbeat System

**Document Owner(s):** MsClaw Team  
**Status:** Draft  
**Document Level:** Feature  
**Target Release:** TBD  
**Link to Technical Spec:** [Added later by Engineering]  

## Version History
*Rule: No silent mutations. All changes after baseline must be recorded here.*

| Version | Date | Author | Description of Changes |
| :--- | :--- | :--- | :--- |
| 1.0 | 2026-03-04 | MsClaw Team | Initial Draft — derived from OpenClaw heartbeat analysis |
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

An always-on agent needs periodic self-awareness — the ability to check for pending work, review its environment, and act on standing instructions without waiting for a human to send a message. Without a heartbeat, the agent is purely reactive: it only thinks when spoken to. Incoming system events (cron job outputs, channel summaries, queued reminders) would pile up in the main session with no mechanism to process them until the next human message arrives.

The [OpenClaw project](https://github.com/openclaw/openclaw) solved this with a heartbeat runner that fires every 30 minutes, reads a `HEARTBEAT.md` checklist, and processes queued system events — suppressing delivery when nothing needs attention. MsClaw needs the same periodic awareness loop, integrated with the agent runtime and configurable per deployment.

### 1.2 Business Value

- **Proactive agent behavior:** The agent can check inboxes, calendars, notifications, and queued events on its own schedule — surfacing information before the operator asks.
- **System event processing:** Cron jobs, channel summaries, and other subsystems can queue events into the main session knowing the heartbeat will process them within a bounded interval.
- **Noise suppression:** When nothing needs attention, the heartbeat suppresses delivery entirely — the operator is never interrupted with empty status updates.
- **Configurable awareness:** Operators control how often the agent checks in and during which hours, adapting the agent's attentiveness to their schedule.

### 1.3 Success Metrics (KPIs)

*   **Metric 1:** The heartbeat MUST fire within the configured interval (default 30 minutes) of the previous heartbeat, measured over a 24-hour period, with zero missed cycles during active hours.
*   **Metric 2:** System events queued by other subsystems MUST be processed by the heartbeat within one interval of being enqueued (or immediately if an on-demand wake is requested).
*   **Metric 3:** Heartbeat cycles that produce no actionable content MUST NOT generate any delivery to the operator — zero false-positive notifications.

## 2. Target Audience & User Personas

| Persona Name | Description | Key Needs for this Feature |
| :--- | :--- | :--- |
| **Operator** | The person who owns the agent and receives its proactive messages | Needs the agent to surface important information on its own, without being asked, and suppress noise when nothing needs attention. |
| **Mind Author** | The person who defines the agent's heartbeat checklist and behavior | Needs to author a heartbeat instruction file that tells the agent what to check on each cycle. |
| **Gateway Subsystem** | Internal components (cron, channels) that queue events for the main session | Needs a mechanism to wake the heartbeat on demand so queued events are processed without waiting for the next scheduled cycle. |

## 3. Functional Requirements (The "What")
*Rule: Use unique identifiers (e.g., REQ-001) for traceability.*

### 3.1 Core User Flows

1.  **Scheduled Heartbeat Cycle:** The heartbeat interval elapses. The system triggers an agent turn in the main session. The agent reads its heartbeat instructions and any queued system events, decides what needs attention, and either delivers a message to the operator or suppresses output.
2.  **On-Demand Wake:** Another subsystem (e.g., a cron job) queues a system event and requests an immediate heartbeat. The heartbeat fires within a short coalescing window, processes the event, and delivers any output.
3.  **Operator Configures Heartbeat:** The operator sets the heartbeat interval, active hours, and delivery target. The heartbeat respects these settings on all subsequent cycles.
4.  **Nothing-to-Report Suppression:** The heartbeat fires, the agent reviews its checklist, and determines nothing needs attention. The agent responds with a suppression token. The system discards the response and delivers nothing to the operator.

### 3.2 Feature Requirements & Acceptance Criteria

| ID | Feature | Description | Acceptance Criteria (Pass/Fail) |
| :--- | :--- | :--- | :--- |
| **REQ-001** | Periodic Heartbeat | The gateway MUST run a heartbeat cycle at a configurable interval in the main agent session. | - The heartbeat MUST fire at the configured interval (default: 30 minutes).<br>- The interval MUST be configurable per deployment.<br>- Each heartbeat cycle MUST run as an agent turn in the main session with full conversation history available. |
| **REQ-002** | Heartbeat Instructions | The heartbeat MUST prompt the agent using a heartbeat instruction file from the mind directory, if one exists. | - IF a heartbeat instruction file exists in the mind, the agent MUST receive its content as the heartbeat prompt.<br>- IF no heartbeat instruction file exists, the heartbeat MUST use a default prompt that checks for queued events.<br>- The instruction file MUST be re-read from disk on each cycle (not cached). |
| **REQ-003** | System Event Processing | The heartbeat MUST process any system events queued in the main session since the last cycle. | - System events enqueued by cron jobs, channels, or other subsystems MUST be visible to the agent during the heartbeat turn.<br>- All pending system events MUST be processed in a single heartbeat cycle. |
| **REQ-004** | Suppression Token | The heartbeat MUST suppress delivery when the agent indicates nothing needs attention. | - IF the agent's response contains only the suppression token (e.g., `HEARTBEAT_OK`) and no other actionable content, the system MUST NOT deliver the response to the operator.<br>- IF the agent's response contains actionable content beyond the suppression token, the system MUST deliver the response. |
| **REQ-005** | Delivery Target | The heartbeat MUST support configurable delivery targets for actionable responses. | - The delivery target MUST be configurable to: no delivery (default), last active channel, or a specific named channel.<br>- IF the target is "no delivery," actionable responses MUST still be recorded in the session but not pushed to the operator.<br>- IF the target is a specific channel, the response MUST be delivered through that channel's adapter. |
| **REQ-006** | On-Demand Wake | The gateway MUST support waking the heartbeat on demand from other subsystems. | - A wake request MUST cause the heartbeat to fire within a short coalescing window (maximum 1 second).<br>- Multiple wake requests arriving within the coalescing window MUST be batched into a single heartbeat cycle.<br>- On-demand wakes MUST NOT reset the scheduled interval timer. |
| **REQ-007** | Wake Priority | Wake requests MUST carry a priority level to resolve conflicts when multiple requests arrive simultaneously. | - The system MUST support at least three priority levels (e.g., retry, scheduled, action).<br>- When multiple wake requests are coalesced, the highest-priority reason MUST be used. |
| **REQ-008** | Active Hours | The heartbeat MAY support restricting cycles to configured active hours. | - IF active hours are configured, the heartbeat MUST NOT fire outside the specified time window.<br>- IF active hours are not configured, the heartbeat MUST fire at all hours.<br>- On-demand wakes SHOULD still fire outside active hours (subsystem-initiated events are time-sensitive). |
| **REQ-009** | Heartbeat Enable/Disable | The operator MUST be able to enable or disable the heartbeat without restarting the gateway. | - IF disabled, no scheduled heartbeat cycles MUST fire.<br>- IF disabled, on-demand wakes MUST still be processed (system events need a path to the agent).<br>- Re-enabling MUST resume the interval timer from the current time. |
| **REQ-010** | Concurrency Guard | The heartbeat MUST NOT run concurrently with an active operator conversation in the main session. | - IF the main session has an active agent run when the heartbeat is due, the heartbeat MUST wait until the run completes.<br>- IF the main session is still busy after a configurable wait (default: 2 minutes), the heartbeat MUST defer to the next cycle or queue an on-demand wake. |
| **REQ-011** | Heartbeat State Visibility | The gateway MUST expose the heartbeat's current state to operators. | - The state MUST include: whether the heartbeat is enabled, the configured interval, last run time, and next scheduled run time.<br>- The state MUST be queryable via the gateway protocol or HTTP surface. |

### 3.3 Edge Cases & Error Handling

*   **Heartbeat instruction file missing:** The heartbeat MUST proceed with a default prompt. Missing the file MUST NOT prevent the heartbeat from firing.
*   **Agent error during heartbeat turn:** The error MUST be logged. The heartbeat MUST schedule the next cycle normally — a single failure MUST NOT disable the heartbeat.
*   **Main session busy for extended period:** IF the main session is busy for longer than the wait timeout, the heartbeat MUST NOT block indefinitely. It MUST defer and re-attempt on the next cycle.
*   **Gateway shutdown during heartbeat:** The active heartbeat turn MUST be cancelled. The heartbeat MUST stop its interval timer during graceful shutdown.
*   **Delivery target channel unavailable:** IF the configured delivery channel is not connected, the response MUST be logged but not lost. Delivery SHOULD be retried on the next heartbeat cycle if the channel reconnects.

## 4. Non-Functional Requirements (Constraints)
*Rule: All constraints must be quantifiable and measurable.*

*   **Performance:** A heartbeat cycle with no queued events and a suppression-only response MUST complete within 30 seconds (model inference time included).
*   **Scalability:** The heartbeat runs in the main session — one heartbeat per gateway instance. No multi-instance coordination is required.
*   **Reliability:** The heartbeat timer MUST self-recover from drift. IF the system clock jumps forward, the heartbeat MUST fire on the next interval boundary rather than firing multiple catch-up cycles.
*   **Platform / Environment:** The heartbeat MUST operate on Windows, macOS, and Linux. No platform-specific dependencies beyond the gateway runtime.

## 5. User Experience (UX) & Design

*   **Design Assets:** Not applicable — the heartbeat is an internal subsystem with no direct UI.
*   **Interaction Model:** The operator experiences the heartbeat as occasional proactive messages from the agent. The agent's tone and content are governed by the heartbeat instruction file in the mind, not by the heartbeat system itself.
*   **Copy & Messaging:** The suppression token (`HEARTBEAT_OK`) MUST be stripped from any delivered response. The operator MUST never see the raw token.

## 6. Out of Scope (Anti-Goals)
*Rule: Explicitly state what we are NOT building to prevent scope creep.*

*   Per-agent heartbeat configuration (multi-agent) is out of scope — the gateway hosts one agent.
*   Heartbeat analytics or dashboards (cycle history, suppression rates) are out of scope for v1.
*   Complex scheduling (cron-style heartbeat schedules) is out of scope — the heartbeat uses a fixed interval. Use the cron system for complex schedules.
*   Heartbeat-triggered tool invocations beyond what the agent decides during its turn are out of scope.
*   Automatic escalation (e.g., paging the operator if a heartbeat detects a critical issue) is out of scope.

## 7. Dependencies & Assumptions

### 7.1 Dependencies

*   **Agent Runtime ([gateway-agent-runtime.md](gateway-agent-runtime.md)):** The heartbeat runs agent turns in the main session. It depends on the runtime for session resolution, streaming, and tool access.
*   **Mind System ([gateway-mind.md](gateway-mind.md)):** The heartbeat instruction file lives in the mind directory. The mind reader is used to load it on each cycle.
*   **Cron System ([gateway-cron.md](gateway-cron.md)):** The cron system queues system events and uses the on-demand wake mechanism to trigger immediate heartbeats.
*   **Channel System ([gateway-channels.md](gateway-channels.md)):** Heartbeat delivery targets may route through channel adapters.

### 7.2 Assumptions

*   We assume the heartbeat runs in the main session (not an isolated session), giving it access to full conversation history for contextual awareness.
*   We assume 30 minutes is a reasonable default interval — operators can tune it based on their needs.
*   We assume the suppression token approach is sufficient for noise control — the agent is trusted to use it correctly based on its heartbeat instructions.
*   We assume on-demand wakes from subsystems are infrequent enough that coalescing within 1 second is sufficient.
