# Product Specification: Gateway Channels

**Document Owner(s):** MsClaw Team  
**Status:** Draft  
**Target Release:** TBD  
**Link to Technical Spec:** [msclaw-channels.md](msclaw-channels.md)  

## Version History
*Rule: No silent mutations. All changes after baseline must be recorded here.*

| Version | Date | Author | Description of Changes |
| :--- | :--- | :--- | :--- |
| 1.0 | 2026-03-03 | MsClaw Team | Initial Draft — derived from technical channels spec |
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

MsClaw agents currently communicate only through the Gateway's direct protocol (SignalR operators and device nodes). Users who want to reach the agent from everyday messaging platforms — WhatsApp, Telegram, Slack, Discord, Signal, iMessage, Email, Matrix, or IRC — have no supported path. Each platform has its own authentication model, message format, text-length limit, media handling, and group-conversation semantics. Without a standardized integration layer, every platform would require a bespoke bridge with duplicated normalization, policy enforcement, delivery tracking, and lifecycle management.

### 1.2 Business Value

- **Meet users where they already are:** Allowing the agent to respond on platforms users already use daily eliminates adoption friction and removes the requirement for a dedicated client application.
- **Single operational surface:** Operators manage all external channels — status, start/stop, health — from the same Gateway dashboard they already use for direct connections.
- **Extensibility:** A uniform adapter model means adding a new platform requires only building a new adapter against the same contract. No pipeline or Gateway changes are needed.
- **Reliability by default:** A centralized delivery queue with retry and dead-letter tracking ensures agent replies are not silently lost, reducing missed-message support incidents.

### 1.3 Success Metrics (KPIs)

*   **Metric 1:** Every supported channel MUST be independently startable, stoppable, and health-checkable without affecting the Gateway or any other channel.
*   **Metric 2:** An inbound message from any supported channel MUST reach the agent and produce a reply delivered back to the originating platform within a single round-trip — no out-of-band steps.
*   **Metric 3:** Outbound delivery failures MUST be retried up to the configured limit, and permanently failed deliveries MUST be surfaced to operators within 60 seconds of final failure.
*   **Metric 4:** A new channel adapter MUST be addable using only the published adapter contract — no modifications to the inbound pipeline, outbound pipeline, or Gateway core.

## 2. Target Audience & User Personas

| Persona Name | Description | Key Needs for this Feature |
| :--- | :--- | :--- |
| **End User** | A person who messages the agent from an external platform (WhatsApp, Telegram, Slack, etc.) | Needs to send messages and receive agent replies on their preferred platform with no additional software or accounts. |
| **Operator** | A human administrator managing the Gateway via the operator dashboard | Needs to view channel health, start/stop individual channels at runtime, configure access policies, and receive alerts on delivery failures. |
| **Channel Developer** | A developer building a new channel adapter for an unsupported platform | Needs a well-defined adapter contract to implement, with clear lifecycle, inbound, outbound, policy, and configuration responsibilities. |

## 3. Functional Requirements (The "What")
*Rule: Use unique identifiers (e.g., REQ-001) for traceability.*

### 3.1 Core User Flows

1.  **Inbound Message Flow:** An end user sends a message on an external platform. The channel adapter receives the platform event, normalizes it into a canonical message, evaluates DM/group access policies, resolves or creates an agent session for the conversation, submits the message to the agent, collects the agent's reply, formats the reply for the target platform, and delivers it back to the user.
2.  **Outbound Delivery Flow:** The agent produces a reply. The outbound pipeline formats the reply into the target platform's native format, splits it into chunks if the reply exceeds the platform's text-length limit, enqueues each chunk for delivery, attempts delivery, and retries on failure. On permanent failure, the delivery is moved to a dead-letter log and operators are notified.
3.  **Operator Channel Management Flow:** An operator views the status of all channels. The operator starts, stops, or restarts a specific channel account. The system confirms the action and pushes updated status to all connected operators.
4.  **Group Conversation Flow:** A message arrives in a group chat on an external platform. The system checks whether the agent was explicitly mentioned (or whether mention-requirement is disabled for that channel). If the mention requirement is met (or not enforced), the message proceeds through the inbound pipeline. If not, the message is silently ignored.
5.  **Policy Denial Flow:** A message arrives from a user who is not authorized under the channel's access policy. The system evaluates the policy and, depending on configuration, silently drops the message, replies with a denial notice, or escalates the message to an operator for manual review.

### 3.2 Feature Requirements & Acceptance Criteria

| ID | Feature | Description | Acceptance Criteria (Pass/Fail) |
| :--- | :--- | :--- | :--- |
| **REQ-001** | Channel Adapter Lifecycle | Each channel adapter MUST support independent start, stop, and health-check operations. | - An adapter MUST transition through defined states: Stopped → Starting → Connected (or Failed).<br>- Stopping one adapter MUST NOT affect any other adapter or the Gateway. |
| **REQ-002** | Automatic Reconnection | A channel adapter that loses its connection MUST attempt automatic reconnection with exponential backoff. | - Backoff intervals MUST start at 1 second and cap at 5 minutes.<br>- Each reconnection attempt MUST emit a status-change event to operators.<br>- IF maximum retries are exceeded, the adapter MUST transition to Failed state. |
| **REQ-003** | Inbound Message Normalization | Every channel adapter MUST normalize inbound platform messages into a single canonical message format before submitting to the agent pipeline. | - The canonical message MUST include: source channel, account, conversation identifier, sender identity, text content (if any), media attachments (if any), group flag, mention flag, and timestamp.<br>- Messages from all supported channels MUST use the same canonical format. |
| **REQ-004** | Session-per-Conversation | The system MUST maintain one agent session per unique combination of channel, account, and conversation. | - A new conversation MUST create a new agent session.<br>- Subsequent messages in the same conversation MUST resume the existing session.<br>- For DM conversations, the conversation identifier MUST be the sender's platform user ID.<br>- For group conversations, the conversation identifier MUST be the group/room ID. |
| **REQ-005** | Conversation Concurrency | The system MUST process only one agent invocation per conversation at a time. | - IF a message arrives while the agent is processing a prior message for the same conversation, the new message MUST be queued.<br>- Queued messages MUST be submitted in arrival order once the prior invocation completes. |
| **REQ-006** | DM Access Policy | Each channel account MUST enforce a configurable DM access policy. | - Policy "open": any user MUST be allowed.<br>- Policy "allowlist": only users on the allow list MUST be allowed; others MUST be denied.<br>- Policy "pairing": unpaired users MUST trigger a pairing approval flow; paired users MUST be allowed.<br>- Policy "disabled": all inbound DMs MUST be rejected. |
| **REQ-007** | Group Access Policy | Each channel account MUST enforce a configurable group access policy. | - IF mention-requirement is enabled (default), the agent MUST respond only to messages that explicitly mention it (@-mention, reply-to-agent, or agent name in text).<br>- IF mention-requirement is disabled, the agent MUST respond to every message in the group. |
| **REQ-008** | Deny List | Each channel account MUST support a deny list of senders. | - Messages from deny-listed senders MUST be rejected before any other policy evaluation.<br>- The deny list MUST take precedence over the allow list. |
| **REQ-009** | Policy Deny Actions | When a message is denied by policy, the system MUST perform one of three configurable actions. | - "Drop": the message MUST be silently discarded with no reply to the sender.<br>- "Reply": the system MUST send a denial message to the sender on the originating platform.<br>- "Escalate": the message MUST be forwarded to operators for manual review. |
| **REQ-010** | Outbound Reply Formatting | The outbound pipeline MUST convert the agent's markdown reply into the target platform's native format. | - Each channel MUST receive replies in its platform-specific format (e.g., WhatsApp formatting, Telegram HTML/MarkdownV2, Slack block-kit markup, plain text for Signal).<br>- Formatting conversion MUST NOT corrupt or lose the semantic content of the reply. |
| **REQ-011** | Text Chunking | IF an agent reply exceeds the target platform's maximum text length, the outbound pipeline MUST split the reply into multiple messages. | - Chunks MUST respect platform-specific character limits (e.g., 4,096 for Telegram, 2,000 for Discord, 65,536 for WhatsApp).<br>- Splits MUST occur on paragraph boundaries where possible.<br>- All chunks MUST be delivered in order. |
| **REQ-012** | Delivery Queue | All outbound replies MUST pass through a delivery queue before being sent to the external platform. | - Each delivery MUST be tracked with a unique delivery identifier.<br>- Successful deliveries MUST be acknowledged and record the platform-assigned message ID.<br>- The queue MUST be drainable on shutdown (see REQ-017). |
| **REQ-013** | Delivery Retry | Failed outbound deliveries MUST be retried with exponential backoff. | - The system MUST retry up to a configurable maximum number of attempts (default: 3).<br>- Backoff intervals MUST increase between attempts (default: 1s, 5s, 15s).<br>- IF all attempts fail, the delivery MUST be moved to a dead-letter log. |
| **REQ-014** | Dead-Letter Notification | Permanently failed deliveries MUST be surfaced to operators. | - A delivery-failed event MUST be pushed to all connected operators.<br>- The event MUST include the channel, account, conversation, error description, and attempt count. |
| **REQ-015** | Media Attachments (Inbound) | Channel adapters MUST normalize inbound media attachments (images, files, audio, video) into the canonical message format. | - Each attachment MUST include its MIME type and a resolvable URL or path.<br>- File name and size MUST be included when the source platform provides them. |
| **REQ-016** | Media Attachments (Outbound) | The outbound pipeline MUST support delivering media attachments to the target platform. | - Media deliveries MUST follow the same delivery queue and retry logic as text replies (REQ-012, REQ-013).<br>- IF the target platform does not support the media type, the system MUST fall back to delivering a text link. |
| **REQ-017** | Graceful Shutdown | On Gateway shutdown, the channel system MUST shut down cleanly. | - All running adapters MUST be stopped in parallel.<br>- The outbound delivery queue MUST be drained within a configurable timeout before connections close.<br>- A final status event MUST be pushed to operators showing all channels stopped. |
| **REQ-018** | Operator Channel Status | Operators MUST be able to view the current status of all channels and individual channel accounts. | - The status MUST include: channel identifier, account, connection state, last message received time, last message sent time, and pending delivery count.<br>- Status changes MUST be pushed to operators in real time. |
| **REQ-019** | Operator Channel Control | Operators with administrative authorization MUST be able to start, stop, and restart individual channel accounts at runtime. | - Start, stop, and restart operations MUST apply to a single channel account without affecting others.<br>- The system MUST push an updated status event to all operators after each control action. |
| **REQ-020** | Multi-Account Support | Each channel MUST support multiple configured accounts (e.g., two Slack workspaces, three Telegram bots). | - Each account MUST have its own credentials, access policy, and independent lifecycle.<br>- Accounts within the same channel MUST be startable and stoppable independently. |
| **REQ-021** | Channel Health Check | Each channel adapter MUST expose a health-check operation. | - The health check MUST report the current connection state.<br>- The health check MUST report error details when the adapter is in a degraded or failed state.<br>- The health check MUST be invokable by the channel manager without side effects. |
| **REQ-022** | Channel Capability Declaration | Each channel adapter MUST declare its supported capabilities (text, media types, threads, reactions, mentions, read receipts, etc.). | - Declared capabilities MUST be queryable by operators and other system components.<br>- The outbound pipeline MUST NOT attempt to deliver content types the target channel does not support. |
| **REQ-023** | Configuration Validation | The system MUST validate each channel account's configuration before starting the adapter. | - IF validation fails, the adapter MUST NOT start and MUST report specific validation errors.<br>- Validation MUST check for required credentials and well-formed settings. |
| **REQ-024** | Secrets Separation | Channel credentials (tokens, passwords, API keys) MUST NOT be stored in plain-text configuration files in production environments. | - The system MUST support loading credentials from environment variables and secret managers.<br>- Configuration files MAY contain non-secret settings (policies, feature flags, text limits). |
| **REQ-025** | Group Intro Hint | When the agent enters a group conversation for the first time, the system MAY append a configurable introduction hint to the agent's system context. | - The hint MUST be configurable per channel account.<br>- IF no hint is configured, no additional context MUST be appended. |
| **REQ-026** | Supported Channels | The system MUST support channel adapters for the following platforms: WhatsApp, Telegram, Slack, Discord, Signal, iMessage, Email, Matrix, IRC, and WebChat. | - Each platform MUST have a registered channel definition with its identifier, display name, capabilities, and text-length limit.<br>- The WebChat channel MUST route through the Gateway's existing operator connection rather than an external platform. |
| **REQ-027** | Channel Registry | The system MUST maintain a static catalog of all known channel types. | - The catalog MUST be queryable by channel identifier.<br>- Each entry MUST include: identifier, display name, description, declared capabilities, and display order. |
| **REQ-028** | Platform Rate-Limit Compliance | Each channel adapter MUST respect rate limits imposed by the external platform. | - IF the platform returns a rate-limit signal (e.g., retry-after), the adapter MUST back off for the indicated duration before retrying.<br>- Rate-limited deliveries MUST NOT be counted as permanent failures. |

### 3.3 Edge Cases & Error Handling

*   **Channel start failure:** IF a channel adapter fails to start (e.g., invalid credentials, unreachable platform), the system MUST mark it as Failed, schedule a retry with exponential backoff, and push a status-change event to operators. Other channels MUST NOT be affected.
*   **Message from unknown sender under pairing policy:** IF a message arrives from a sender who has not completed pairing and the channel's DM policy is "pairing", the system MUST initiate the pairing approval flow and MUST NOT submit the message to the agent until pairing is approved.
*   **Agent processing error:** IF the agent encounters an error while processing an inbound channel message, the system MUST NOT leave the sender waiting indefinitely. An error reply SHOULD be sent back through the outbound pipeline.
*   **Platform-unreachable during delivery:** IF the external platform is unreachable when attempting delivery, the delivery MUST be retried per REQ-013. IF the channel adapter itself transitions to Reconnecting or Failed, queued deliveries MUST be held until the adapter reconnects or the delivery times out.
*   **Oversized media attachment:** IF an inbound media attachment exceeds a configurable size limit, the system SHOULD reject or truncate the attachment and MUST NOT crash the inbound pipeline.
*   **Simultaneous messages to a busy session:** IF multiple messages arrive for the same conversation while the agent is processing, additional messages MUST be queued (per REQ-005) and MUST NOT be dropped.
*   **Deny-listed sender in a group:** IF a deny-listed sender posts in a group conversation, the message MUST be rejected even if other group members are allowed.
*   **Gateway shutdown with pending deliveries:** IF the delivery queue has pending items when shutdown begins, the system MUST attempt to drain pending deliveries within the configured timeout. Deliveries that cannot be completed MUST be logged.
*   **Configuration hot-reload:** IF channel configuration changes at runtime, the system SHOULD detect the change and start or stop adapters accordingly without requiring a full Gateway restart.

## 4. Non-Functional Requirements (Constraints)
*Rule: All constraints must be quantifiable and measurable.*

*   **Performance:** Inbound message normalization and policy evaluation MUST complete within 200 milliseconds (excluding external platform latency and agent processing time).
*   **Scalability:** The system MUST support at least 10 concurrently active channel accounts across different platforms without degradation of message processing throughput.
*   **Reliability:** Outbound delivery success rate MUST be ≥ 99.5% for messages where the external platform is reachable, measured over any rolling 24-hour period.
*   **Security & Compliance:** Channel credentials MUST be stored using secret management (not plain-text config) in production. Access policy evaluation MUST occur before any message reaches the agent pipeline. Deny-list checks MUST precede allow-list checks.
*   **Isolation:** A failure in one channel adapter (crash, hang, exception) MUST NOT propagate to other adapters or to the Gateway process.
*   **Platform / Environment:** The channel system MUST run on Windows, macOS, and Linux, consistent with the Gateway's supported platforms.

## 5. User Experience (UX) & Design

*   **Design Assets:** Not applicable — the channel system is a system integration layer. The user-facing UI is the external messaging platform itself (WhatsApp, Telegram, Slack, etc.). Users interact with the agent through their existing platform client.
*   **Prototypes:** Not applicable. End users experience the agent as a contact or bot on their chosen platform.
*   **Operator Experience:** Channel status, events, and control actions are surfaced through the existing operator dashboard (Gateway hub). No new operator-facing UI is introduced by this specification. Status-change events, message-received notifications, delivery confirmations, and delivery-failure alerts MUST use the existing operator event model.
*   **Copy & Messaging:** Policy denial messages sent to end users MUST be configurable per channel account. Error messages surfaced to operators MUST include the channel identifier, account identifier, and a machine-readable error description.

## 6. Out of Scope (Anti-Goals)
*Rule: Explicitly state what we are NOT building to prevent scope creep.*

*   Channel-specific agent tools (e.g., "pin message in Telegram", "create Slack channel") are out of scope for this specification.
*   A persistent (crash-recoverable) delivery queue is out of scope for v1. The delivery queue will be in-memory; a persistent backing store MAY be added in a future version.
*   Running channel adapters as out-of-process sidecars is out of scope. All adapters run within the Gateway process.
*   Scheduled or cron-based outbound messaging (agent-initiated messages without an inbound trigger) is out of scope.
*   End-to-end encryption management within the channel system is out of scope. Encryption is the responsibility of the external platform and its transport.
*   A dedicated channel administration UI is out of scope. Channel management is performed through the existing operator dashboard.
*   Bridging messages between channels (e.g., forwarding a Telegram message to Slack) is out of scope.

## 7. Dependencies & Assumptions

### 7.1 Dependencies

*   **MsClaw Gateway:** The channel system runs within the Gateway process and depends on the Gateway for agent session management, operator hub, and presence events. The Gateway must be operational before channels can start.
*   **Gateway Protocol:** Channel status events and operator control actions extend the existing Gateway protocol. The protocol spec ([msclaw-gateway-protocol.md](msclaw-gateway-protocol.md)) MUST be updated to include channel-related operations and events.
*   **External Platform Availability:** Each channel adapter depends on the availability of its respective external platform (Telegram Bot API, Slack API, Discord Gateway, WhatsApp Cloud API, etc.). Platform outages are outside MsClaw's control.
*   **External Runtimes for Select Channels:** Signal and iMessage adapters depend on external bridge processes (signal-cli, BlueBubbles) being installed and available on the host.

### 7.2 Assumptions

*   We assume one Gateway process per host. Multi-instance channel coordination (e.g., ensuring only one instance polls Telegram) is not required.
*   We assume external platform APIs will remain backward-compatible for the duration of a release cycle. Breaking API changes from platforms will require adapter updates.
*   We assume the in-memory delivery queue is acceptable for v1, acknowledging that in-flight deliveries will be lost on an unclean Gateway shutdown.
*   We assume channel credential management (obtaining bot tokens, registering webhooks, completing OAuth flows) is performed by the operator outside of MsClaw. The system consumes pre-provisioned credentials.
*   We assume the WebChat channel reuses the Gateway's existing SignalR transport and authentication. It does not require a separate external platform connection.
