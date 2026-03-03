# Product Specification: Skills System

**Document Owner(s):** MsClaw Team  
**Status:** Draft  
**Target Release:** TBD  
**Link to Technical Spec:** [msclaw-skills.md](msclaw-skills.md)  

## Version History
*Rule: No silent mutations. All changes after baseline must be recorded here.*

| Version | Date | Author | Description of Changes |
| :--- | :--- | :--- | :--- |
| 1.0 | 2026-03-03 | MsClaw Team | Initial Draft — derived from technical skills spec |
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

MsClaw agents can reason about many topics, but they cannot *act* beyond what the underlying model runtime provides. Without an extensibility mechanism, every new capability — reading memory, searching the web, capturing a photo from a device — must be hard-coded into the gateway. This creates a bottleneck: mind authors cannot give their agents new abilities without modifying and redeploying the gateway itself.

The [OpenClaw project](https://github.com/openclaw/openclaw) solved this with a three-tier skill model (bundled, managed, workspace), but that implementation is JavaScript-specific and code-first. MsClaw needs a declarative, descriptor-driven skill system that lets mind authors extend agent capabilities by placing files in the mind directory — no code changes, no redeployment.

### 1.2 Business Value

- **Author autonomy:** Mind authors can add, modify, and remove agent capabilities without gateway changes — reducing time-to-capability from a release cycle to a file edit.
- **Device integration:** Skills that require hardware (camera, screen, location) are transparently routed to connected device nodes, enabling the agent to interact with the physical world.
- **Ecosystem growth:** A standard skill descriptor format enables a future marketplace of community-contributed skills, installable with a single command.
- **Operational safety:** Dependency checking and execution approval gates ensure skills only run when their prerequisites are met and privileged actions are explicitly authorized.

### 1.3 Success Metrics (KPIs)

*   **Metric 1:** A mind author MUST be able to add a new agent capability by placing a descriptor file in the mind directory — the skill MUST be available on the next session without a gateway restart.
*   **Metric 2:** All v1 bundled skills (memory read/write, mind list/read, web search, camera capture, screen record, location) MUST be discoverable and invocable through the agent.
*   **Metric 3:** 100% of skill invocations requiring a device node MUST be routed to a node with the matching capability, or return an error if no matching node is connected.
*   **Metric 4:** 100% of skill invocations MUST complete or time out within the configured timeout period — no invocation MAY run indefinitely.

## 2. Target Audience & User Personas

| Persona Name | Description | Key Needs for this Feature |
| :--- | :--- | :--- |
| **Mind Author** | A person designing an agent's personality, knowledge, and capabilities by authoring files in a mind directory | Needs to define new skills declaratively, see them discovered automatically, and control what the agent can do — without modifying gateway code. |
| **Operator** | A human user interacting with the agent via CLI, web UI, or desktop app | Needs to inspect available skills, trigger re-discovery, test skills directly, and approve privileged skill executions. |
| **Node (Device)** | An automated device endpoint (iOS, macOS, headless) registered with the Gateway | Needs to receive skill invocation requests for capabilities it offers (camera, screen, location) and return results. |

## 3. Functional Requirements (The "What")
*Rule: Use unique identifiers (e.g., REQ-001) for traceability.*

### 3.1 Core User Flows

1.  **Add a Workspace Skill:** A mind author creates a skill descriptor file in the mind's skills directory. On the next agent session, the gateway discovers the descriptor, validates it, checks its requirements, and registers the skill. The agent can then invoke the skill when relevant.
2.  **Agent Invokes a Local Skill:** During a conversation, the agent decides to use a skill (e.g., read memory). The gateway resolves the skill by name, executes it locally, and returns the result to the agent. The operator sees start and end events in the streaming response.
3.  **Agent Invokes a Node-Routed Skill:** The agent invokes a skill that requires a device capability (e.g., camera). The gateway identifies a connected node with that capability, sends the invocation request to the node, receives the result, and returns it to the agent.
4.  **Operator Inspects Skills:** An operator requests the list of registered skills. The gateway returns each skill's name, description, version, source tier, status, and tags.
5.  **Operator Triggers Re-Discovery:** An operator with admin privileges triggers skill re-discovery. The gateway re-scans all skill sources, validates descriptors, and updates the registry. All connected clients receive a notification of the change.
6.  **Approval-Gated Skill Execution:** The agent invokes a skill that requires operator approval. The gateway pauses execution and pushes an approval request to all operators. An operator approves or denies. If approved, execution proceeds; if denied, an error is returned to the agent.

### 3.2 Feature Requirements & Acceptance Criteria

| ID | Feature | Description | Acceptance Criteria (Pass/Fail) |
| :--- | :--- | :--- | :--- |
| **REQ-001** | Declarative Skill Descriptors | Skills MUST be defined via declarative descriptor files, not compiled into the gateway. Each descriptor MUST declare a unique name, a natural-language description, a version, input parameters, and an execution mode. | - A skill with all required fields MUST be accepted during discovery.<br>- A skill missing any required field (name, description, parameters, execution mode) MUST be rejected. |
| **REQ-002** | Three-Tier Skill Sourcing | The system MUST support three tiers of skill sources: bundled (shipped with the gateway), workspace (defined in the mind directory), and managed (installed from external registries). | - Bundled skills MUST be present on every gateway startup.<br>- Workspace skills MUST be loaded from the mind directory.<br>- Managed skills MUST be loaded from the shared skills directory (future — see REQ-005). |
| **REQ-003** | Bundled Skills | The gateway MUST ship with built-in skills covering: memory read, memory write, mind directory listing, mind file read, web search, camera capture, screen recording, and device location. | - Each bundled skill listed above MUST appear in the skill registry on startup.<br>- Bundled skills MUST NOT be removable or overridable by workspace or managed skills. |
| **REQ-004** | Workspace Skills | The system MUST discover skill descriptors from the mind directory (primary and alternative skill directories) each time a session is created. | - A valid descriptor placed in the mind's skills directory MUST be registered on the next session creation.<br>- A descriptor placed in the alternative skills directory MUST be registered if no same-named descriptor exists in the primary directory.<br>- Discovery MUST NOT require a gateway restart. |
| **REQ-005** | Managed Skills (Future) | The system SHOULD support installing skills from external sources (version control repositories, package registries, local paths) into a shared directory available to all minds. | - Managed skills are out of scope for v1.<br>- The discovery pipeline MUST reserve the lowest priority tier for managed skills so the feature can be added without disrupting existing tiers. |
| **REQ-006** | Priority-Based Discovery | Skills MUST be discovered and merged in strict priority order: bundled (highest), workspace, managed (lowest). | - IF a bundled skill and a workspace skill share the same name, the bundled skill MUST be kept and the workspace skill MUST be skipped.<br>- IF a workspace skill and a managed skill share the same name, the workspace skill MUST be kept. |
| **REQ-007** | Name Collision Handling | When multiple skills from different tiers share the same name, the system MUST keep the higher-priority skill, skip the lower-priority duplicate, and log a warning. | - The duplicate MUST NOT appear in the registry.<br>- A warning message identifying the collision MUST be logged. |
| **REQ-008** | Descriptor Validation | The system MUST validate every skill descriptor during discovery according to defined validation rules. | - A descriptor with a missing or malformed name MUST be rejected (skill skipped).<br>- A descriptor with a missing description MUST be rejected.<br>- A descriptor with invalid input parameter definitions MUST be rejected.<br>- A descriptor with a missing or unrecognized execution mode MUST be rejected.<br>- A descriptor with an invalid version MUST be accepted but assigned a default version, with a warning logged. |
| **REQ-009** | Dependency Checking | Skills MUST be able to declare requirements (external binaries, device capabilities, operating system constraints). The system MUST check these requirements during discovery. | - IF a required binary is not found on the host, the skill MUST be registered with degraded status.<br>- IF a required device capability has no connected node, the skill MUST be registered but marked unavailable until a matching node connects.<br>- IF an operating system constraint is not met, the skill MUST be skipped. |
| **REQ-010** | Skill Status Tracking | Each registered skill MUST have a status indicating its readiness: ready (all requirements met), degraded (missing optional prerequisites), or unavailable (missing required device capability). | - A skill with all requirements satisfied MUST have status "ready".<br>- A skill with a missing binary MUST have status "degraded" and MUST include a reason.<br>- A skill requiring a device capability with no matching node connected MUST have status "unavailable". |
| **REQ-011** | Hot Discovery | Workspace skills MUST be discovered from disk each time a new agent session is created, without requiring a gateway restart. | - Adding a descriptor file to the mind's skills directory and creating a new session MUST result in the new skill appearing in the registry.<br>- Removing a descriptor file and creating a new session MUST result in the skill no longer appearing. |
| **REQ-012** | Multiple Execution Modes | The system MUST support multiple execution modes for skills: in-process execution, shell command execution, script execution, device node routing, and external HTTP endpoint invocation. | - A skill configured for in-process execution MUST execute within the gateway process.<br>- A skill configured for shell command execution MUST spawn a host process.<br>- A skill configured for script execution MUST invoke the declared interpreter with the script file.<br>- A skill configured for node routing MUST be delivered to a connected device node.<br>- A skill configured for HTTP endpoint invocation MUST make an outbound HTTP request. |
| **REQ-013** | Node-Routed Skill Invocation | Skills requiring device capabilities MUST be routed to a connected node that advertises the matching capability. | - The invocation request MUST be delivered only to a node with the required capability.<br>- IF no node with the required capability is connected, the system MUST return an error immediately.<br>- The node's result MUST be returned to the agent as the skill output. |
| **REQ-014** | Node Target Selection Policies | The system MUST support policies for selecting which node receives a node-routed invocation: any available node, a preferred node (with fallback), or a specific named device. | - Policy "any": the system MUST select any connected node with the required capability.<br>- Policy "preferred": the system MUST use the preferred node if connected, otherwise fall back to any matching node.<br>- Policy "specific": the system MUST route to the named device. IF that device is not connected, the system MUST return an error. |
| **REQ-015** | Invocation Event Streaming | Every skill invocation MUST produce a start event and an end event in the agent's streaming response. | - The start event MUST include the skill name, source tier, and whether the skill requires a device node.<br>- The end event MUST include the skill name, execution duration, and success or failure status. |
| **REQ-016** | Execution Approval | Skills MAY declare that operator approval is required before execution. When declared, the gateway MUST pause execution and request operator approval. | - IF a skill requires approval, the gateway MUST push an approval request to all connected operators before executing.<br>- IF an operator approves, the skill MUST execute.<br>- IF an operator denies, the skill MUST NOT execute and the agent MUST receive an error. |
| **REQ-017** | Timeout Enforcement | All skill invocations MUST enforce a configurable timeout. The default timeout for local skills and the default timeout for node-routed skills MUST be independently configurable. | - A local skill exceeding its timeout MUST be terminated and return a timeout error.<br>- A node-routed skill exceeding its timeout MUST return a timeout error.<br>- No skill invocation MAY run indefinitely. |
| **REQ-018** | Skill Listing | Operators MUST be able to retrieve the list of all registered skills, including each skill's name, description, version, source tier, status, and tags. | - The response MUST include every skill in the registry.<br>- Each entry MUST contain all listed metadata fields. |
| **REQ-019** | Skill Detail Inspection | Operators MUST be able to retrieve the full descriptor of any registered skill by name. | - IF the skill exists, the full descriptor MUST be returned.<br>- IF the skill does not exist, the system MUST return a "not found" response. |
| **REQ-020** | Manual Re-Discovery | Operators with admin privileges MUST be able to trigger re-discovery of workspace and managed skills on demand. | - Re-discovery MUST re-scan all non-bundled skill sources.<br>- The result MUST include the total skill count broken down by tier, plus any errors and warnings from validation.<br>- Bundled skills MUST NOT be affected by re-discovery. |
| **REQ-021** | Direct Skill Invocation | Operators with admin privileges MUST be able to invoke a skill directly (bypassing the agent model) for testing and debugging. | - The operator MUST specify the skill name and input parameters.<br>- The system MUST execute the skill and return the result directly to the operator. |
| **REQ-022** | Skill Change Notifications | When the skill registry changes (discovery, status change), all connected clients MUST receive a notification. | - The notification MUST include the reason for the change and the updated skill list.<br>- The notification MUST be pushed without the client requesting it. |
| **REQ-023** | Path Traversal Protection | Skills that access the filesystem MUST be restricted to paths within the mind directory. Requests for paths outside the mind directory MUST be rejected. | - A skill invocation with a path that resolves within the mind directory MUST succeed.<br>- A skill invocation with a path that resolves outside the mind directory (e.g., via `../`) MUST be rejected.<br>- The error response MUST NOT disclose the host filesystem structure. |
| **REQ-024** | Environment Variable Allowlisting | Skill descriptors that reference environment variables MUST only have access to explicitly allowlisted variables. | - A reference to an allowlisted variable MUST resolve to its value.<br>- A reference to a non-allowlisted variable MUST NOT resolve and MUST produce an error or empty value. |
| **REQ-025** | Argument Injection Prevention | When executing external commands, arguments MUST be passed directly without shell expansion to prevent injection attacks. | - Input containing shell metacharacters (e.g., `; rm -rf /`) MUST be treated as literal text, not interpreted by a shell. |
| **REQ-026** | Skill Disabling | Operators MUST be able to disable specific skills by name via configuration. Disabled skills MUST NOT appear in the registry or be invocable. | - A skill listed in the disabled configuration MUST NOT be registered during discovery.<br>- A disabled skill MUST NOT be available to the agent. |
| **REQ-027** | Configurable Approval Policy | The system MUST support a global approval policy that can override per-skill approval settings. | - IF the global policy requires approval for all skills, every skill invocation MUST require operator approval regardless of the skill's own setting.<br>- IF the global policy defers to per-skill settings, only skills that individually declare approval MUST require it. |
| **REQ-028** | Context-Aware Parameterization | Skill descriptors MUST support referencing contextual values (mind directory location, working memory location, skill input parameters, allowlisted environment variables) in their execution configuration. | - A reference to the mind directory location MUST resolve to the correct absolute path.<br>- A reference to a skill input parameter MUST resolve to the value provided by the agent at invocation time. |

### 3.3 Edge Cases & Error Handling

*   **Descriptor with missing required fields:** The system MUST skip the invalid skill, log a descriptive error identifying the missing field(s), and continue discovering remaining skills.
*   **Name collision across tiers:** The system MUST keep the higher-priority skill, skip the lower-priority duplicate, and log a warning identifying both the kept and skipped skill and their tiers.
*   **Required binary not on host PATH:** The skill MUST be registered with "degraded" status and a reason message. The agent MAY still attempt to invoke the skill, but the invocation MUST fail with a descriptive error.
*   **Node-routed skill with no matching node connected:** The skill MUST be registered with "unavailable" status. IF the agent invokes the skill, the system MUST return an error indicating no node with the required capability is available.
*   **Node becomes available after session creation:** The skill status SHOULD update from "unavailable" to "ready" when a node with the matching capability connects, and all clients MUST be notified of the status change.
*   **Skill invocation timeout:** The system MUST terminate the invocation and return a timeout error to the agent. For shell command and script executions, the spawned process MUST be killed.
*   **Approval request with no operators connected:** IF a skill requires approval and no operators are connected, the system MUST return an error to the agent indicating that approval cannot be obtained.
*   **Concurrent re-discovery during active session:** Re-discovery MUST NOT affect skills already registered on active sessions. New skill registrations MUST only apply to sessions created after re-discovery completes.
*   **HTTP skill targeting non-HTTPS endpoint:** The system MUST reject HTTP requests to non-localhost URLs that do not use HTTPS.
*   **Skill descriptor referencing non-allowlisted environment variable:** The system MUST NOT resolve the variable and MUST treat the reference as an error.

## 4. Non-Functional Requirements (Constraints)
*Rule: All constraints must be quantifiable and measurable.*

*   **Performance:** Skill discovery for a mind directory containing up to 50 workspace skills MUST complete within 2 seconds. Individual skill invocation overhead (excluding handler execution time) MUST be under 50ms.
*   **Scalability:** The skill registry MUST support up to 200 concurrently registered skills (across all tiers) without degradation of discovery or invocation performance.
*   **Security & Compliance:** All filesystem access from skills MUST be constrained to the mind directory via path-traversal protection. Environment variable access MUST be limited to an explicit allowlist. External command execution MUST NOT use shell expansion. HTTPS MUST be required for non-localhost HTTP skill endpoints.
*   **Reliability:** Every skill invocation MUST enforce a timeout. No invocation MAY run indefinitely. Failure of a single skill during discovery MUST NOT prevent other skills from being registered.
*   **Platform / Environment:** The skill system MUST function on Windows, macOS, and Linux. Skills declaring platform constraints MUST be skipped on non-matching platforms.

## 5. User Experience (UX) & Design

*   **Design Assets:** Not applicable — skills are configured via declarative descriptor files placed in the mind directory and (in the future) installed from managed-skill registries. There is no GUI. The user interacts with the skill system through file editing (creating and modifying descriptor files) and agent conversations (the agent invokes skills automatically based on context).
*   **Prototypes:** Not applicable — the interaction model is file-based authoring and conversational agent use.
*   **Copy & Messaging:** Validation errors and warnings during skill discovery MUST use descriptive, actionable messages identifying the skill name, the specific validation failure, and the corrective action. Skill status reasons (degraded, unavailable) MUST clearly state what prerequisite is missing.

## 6. Out of Scope (Anti-Goals)
*Rule: Explicitly state what we are NOT building to prevent scope creep.*

*   Managed skill registry, install, and update commands are out of scope for v1. The discovery pipeline reserves the managed tier but the installation mechanism is future work.
*   Composite skills (skills that orchestrate other skills) are out of scope.
*   A skill marketplace or community sharing registry is out of scope.
*   Container-based sandboxing for untrusted skills is out of scope.
*   Skill analytics (invocation frequency, latency tracking, failure rate dashboards) are out of scope.
*   A dedicated skill testing framework or unit test harness is out of scope.
*   Hot reload via filesystem watcher (automatic re-discovery when files change) is out of scope — discovery is per-session or on-demand via manual re-discovery.
*   Skill-level permissions beyond the approval gate (e.g., read-only vs. write vs. network access categories) are out of scope.
*   Streaming output from long-running skills is out of scope — skills return a single result.
*   An index file listing multiple skills in a single descriptor is out of scope — each skill requires its own descriptor file.

## 7. Dependencies & Assumptions

### 7.1 Dependencies

*   **MsClaw Gateway:** The skill system is a subsystem of the Gateway. It depends on the Gateway's session lifecycle, event streaming, and device node infrastructure.
*   **Gateway Protocol:** Skill listing, inspection, re-discovery, and direct invocation operations depend on the Gateway Protocol for client-server communication (see [gateway-protocol.md](gateway-protocol.md)).
*   **Exec Approval Flow:** Approval-gated skills depend on the Gateway's existing exec approval workflow for operator approval and denial.
*   **Device Node Infrastructure:** Node-routed skills depend on the Gateway's device pairing, node registration, and node invocation infrastructure.
*   **MsClaw.Core Library:** Filesystem skills (memory and mind access) depend on MsClaw.Core's path-traversal protection and mind reader capabilities.
*   **GitHub Copilot SDK:** Skill registration as agent tools depends on the Copilot SDK's session configuration and tool invocation lifecycle.

### 7.2 Assumptions

*   We assume the mind directory is on local disk accessible to the gateway process.
*   We assume one gateway process per host — there is no requirement for skill registry synchronization across multiple gateway instances.
*   We assume mind authors are trusted to place skill descriptors in the mind directory. Untrusted skill execution (e.g., from managed registries) will require additional sandboxing in a future release.
*   We assume the gateway process has sufficient host permissions to spawn child processes for shell command and script skill execution modes.
*   We assume that the number of workspace skills per mind will not exceed 50 in typical usage.
