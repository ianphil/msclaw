# Product Specification: Mind System

**Document Owner(s):** MsClaw Team  
**Status:** Draft  
**Target Release:** TBD  
**Link to Technical Spec:** [msclaw-mind.md](msclaw-mind.md)  

## Version History
*Rule: No silent mutations. All changes after baseline must be recorded here.*

| Version | Date | Author | Description of Changes |
| :--- | :--- | :--- | :--- |
| 1.0 | 2026-03-03 | MsClaw Team | Initial Draft — derived from technical mind spec |
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

MsClaw agents need a persistent identity, knowledge base, and memory that survives across sessions and is editable by both humans and the agent. Without a defined structure, agent personality lives in ad-hoc configuration files, knowledge is scattered, and there is no separation between what the user shares and what the agent observes. The [OpenClaw project](https://github.com/openclaw/openclaw) stored personality in a single JSON config with no formal validation, no knowledge organization, no path protection on file reads, and no structured memory — leading to fragile setups that couldn't be version-controlled, shared, or composed.

MsClaw needs a file-backed system where one directory on disk is the single source of truth for an agent's personality, knowledge, and memory — with clear structure, validation, and security guarantees.

### 1.2 Business Value

- **Portable identity:** A mind is a directory. It can be copied, version-controlled with git, and shared across machines — no database or cloud service required.
- **Composable personality:** Multiple instruction files contribute to a single agent identity, enabling modular personality assembly without monolithic configuration.
- **Structured knowledge:** The IDEA storage model (Initiatives, Domains, Expertise, Archive) gives every piece of user knowledge one canonical home, eliminating duplication and enabling cross-referencing.
- **Persistent memory:** The working-memory subsystem gives agents continuity across sessions — the agent remembers what it learned, the mistakes it made, and the context it needs without the user repeating themselves.
- **Safe by default:** Path-traversal protection ensures the mind directory is a hard security boundary, preventing agents from reading or exposing files outside the mind.

### 1.3 Success Metrics (KPIs)

*   **Metric 1:** A newly scaffolded mind MUST pass validation with zero errors and be usable for agent conversation within a single bootstrap workflow — no manual file editing required.
*   **Metric 2:** Identity assembly from the same mind directory MUST produce identical output on every invocation (deterministic assembly).
*   **Metric 3:** 100% of path-traversal attempts (relative paths resolving outside the mind root) MUST be blocked — no file outside the mind directory is ever readable.
*   **Metric 4:** An agent MUST be able to read its working memory at session start and write observations during and after a session without data loss or corruption of prior entries.

## 2. Target Audience & User Personas

| Persona Name | Description | Key Needs for this Feature |
| :--- | :--- | :--- |
| **Agent Operator** | A human user who creates, configures, and maintains an agent's personality and knowledge | Needs to scaffold a new mind, customize identity files, organize knowledge into IDEA buckets, and validate the mind structure — all using a text editor and the filesystem. |
| **MsClaw Agent** | The AI agent that reads its identity and memory from the mind at runtime | Needs to read its personality (SOUL.md + agent files), access its knowledge base (IDEA buckets), read and write working memory, and maintain cross-references between knowledge items. |
| **Gateway Consumer** | A host application (Gateway, CLI, or test harness) that composes mind primitives to bring an agent to life | Needs to validate the mind, assemble identity, create a configured client, and expose mind operations to the agent through defined capabilities. |

## 3. Functional Requirements (The "What")
*Rule: Use unique identifiers (e.g., REQ-001) for traceability.*

### 3.1 Core User Flows

1.  **Scaffold a New Mind:** An operator creates a new mind directory. The system generates the required directory structure, seed files, and empty working-memory files. The directory is ready for the bootstrap workflow.
2.  **Bootstrap an Agent:** An operator starts a conversation with a newly scaffolded agent. The agent guides the operator through three phases — identity customization, agent file creation, and memory seeding — then removes the setup guide. The mind is ready for normal operation.
3.  **Validate a Mind:** An operator or consumer checks whether a mind directory has the required structure. The system reports errors (missing required elements), warnings (missing optional elements), and discovered elements.
4.  **Assemble Agent Identity:** A consumer loads the agent's system message from the mind. The system reads the core identity file and any agent instruction files, strips metadata headers, and concatenates them into a single string in a deterministic order.
5.  **Read Mind Files:** The agent or consumer reads a file or lists a directory within the mind. The system resolves the path, verifies it stays within the mind boundary, and returns the content.
6.  **Manage Knowledge (IDEA):** During conversation, the agent classifies user-shared knowledge into the appropriate IDEA bucket (Initiatives, Domains, Expertise) and writes it to disk. Completed or abandoned initiatives are moved to Archive. Cross-references between items use wiki-links.
7.  **Manage Working Memory:** At session start, the agent reads its curated memory and learned rules. During the session, the agent appends observations to the log. Periodically, the agent consolidates the log into curated memory. On mistakes, the agent appends a rule.

### 3.2 Feature Requirements & Acceptance Criteria

| ID | Feature | Description | Acceptance Criteria (Pass/Fail) |
| :--- | :--- | :--- | :--- |
| **REQ-001** | Required Mind Structure | A valid mind MUST contain a core identity file and a working-memory directory at the mind root. | - A mind directory containing both required elements MUST pass validation with zero errors.<br>- A mind directory missing either required element MUST fail validation with a specific error identifying the missing element. |
| **REQ-002** | Optional Mind Structure | The system MUST detect the presence or absence of optional directories (IDEA buckets, agent instruction files, skill definitions, inbox). | - Each missing optional directory MUST produce a warning in the validation result.<br>- Each present optional directory MUST appear in the list of discovered elements.<br>- Missing optional directories MUST NOT cause validation failure. |
| **REQ-003** | Validation Result Categories | Validation MUST categorize findings into three groups: errors (missing required elements), warnings (missing optional elements), and found (discovered elements). | - A mind with all required elements present MUST report zero errors.<br>- A mind missing optional elements MUST report one warning per missing element.<br>- Every discovered file or directory MUST appear in the found list. |
| **REQ-004** | Mind Scaffolding | The system MUST create a complete mind directory structure with all required and optional directories, plus seed files, from built-in templates. | - After scaffolding, the mind MUST pass validation with zero errors.<br>- The scaffolded mind MUST contain the core identity file, the setup guide, working-memory directory with three empty files (curated memory, rules, log), IDEA bucket directories, agent and skill directories, and inbox. |
| **REQ-005** | Non-Empty Directory Guard | Scaffolding MUST refuse to overwrite an existing non-empty directory. | - IF the target directory contains any files or subdirectories, scaffolding MUST fail with an error.<br>- IF the target directory does not exist or is empty, scaffolding MUST succeed. |
| **REQ-006** | Identity Assembly — Core Identity | The system MUST include the full content of the core identity file as the first content in the assembled system message. | - The assembled output MUST begin with the exact content of the core identity file.<br>- IF the core identity file is missing, assembly MUST fail with an error. |
| **REQ-007** | Identity Assembly — Agent Files | The system MUST discover and include agent instruction files from the designated agents directory, sorted alphabetically by filename. | - Each agent instruction file MUST appear in the assembled output after the core identity content.<br>- Files MUST be separated by horizontal rule markers.<br>- Files MUST be included in alphabetical order by filename.<br>- IF no agent instruction files exist, the assembled output MUST contain only the core identity content. |
| **REQ-008** | Identity Assembly — Metadata Stripping | The system MUST strip YAML frontmatter from agent instruction files before including them in the assembled output. | - A frontmatter block (delimited header at the start of the file) MUST be removed from the included content.<br>- Only the first frontmatter block MUST be stripped.<br>- Files without frontmatter MUST be included as-is. |
| **REQ-009** | Identity Assembly — Determinism | The same mind directory MUST always produce the same assembled system message. | - Two consecutive assembly operations on an unchanged mind MUST produce byte-identical output. |
| **REQ-010** | Identity Assembly — Safety Preservation | The assembled identity MUST be delivered to the agent runtime in a mode that preserves built-in safety guardrails. | - The system message MUST be appended to the runtime's default safety message, not replace it. |
| **REQ-011** | Path-Traversal Protection | All file reads within the mind MUST be validated to ensure the resolved path stays within the mind root directory. | - A relative path that resolves to a location inside the mind root MUST be allowed.<br>- A relative path containing traversal segments (e.g., `../`) that resolves outside the mind root MUST be rejected.<br>- An absolute path MUST be treated as relative to the mind root (leading separator stripped).<br>- Rejection MUST NOT disclose filesystem structure outside the mind. |
| **REQ-012** | File Reading | The system MUST allow reading the content of any file within the mind boundary. | - A request for an existing file within the mind MUST return its content.<br>- A request for a non-existent file MUST return a not-found error. |
| **REQ-013** | Directory Listing | The system MUST allow listing entries in any directory within the mind boundary. | - A request for an existing directory within the mind MUST return relative paths of its entries.<br>- A request for the mind root MUST return its top-level entries. |
| **REQ-014** | Git Sync | The system MAY optionally synchronize the mind from a git remote before reads. | - IF git sync is enabled and the mind is a git repository with remote changes, the system MUST update the local copy using fast-forward merge only before performing the read.<br>- IF the remote has diverged (fast-forward not possible), the system MUST continue with the local copy without error.<br>- IF the mind is not a git repository, the system MUST continue without error.<br>- IF git sync is disabled (default), no git operations MUST occur. |
| **REQ-015** | IDEA Bucket — Initiatives | The mind MUST support a bucket for active projects with a defined end state. | - Each initiative MUST be stored in its own subdirectory containing a main note and a next-actions file.<br>- Completed or abandoned initiatives MUST be movable to the Archive bucket. |
| **REQ-016** | IDEA Bucket — Domains | The mind MUST support a bucket for recurring areas with no end date. | - Each domain MUST be stored in its own subdirectory.<br>- Domain content MUST be allowed to grow and evolve over time. |
| **REQ-017** | IDEA Bucket — Expertise | The mind MUST support a bucket for reference material, patterns, and learnings. | - Expertise items MUST be stored as individual files within the expertise directory. |
| **REQ-018** | IDEA Bucket — Archive | The mind MUST support a bucket for completed or abandoned work. | - Items moved to Archive MUST remain readable and searchable.<br>- Archive MUST accept items from any other IDEA bucket. |
| **REQ-019** | Cross-Linking | Knowledge items across IDEA buckets MUST support cross-references using wiki-links. | - Each fact MUST have exactly one canonical home in the mind.<br>- References from other locations MUST use wiki-link syntax.<br>- When a canonical source moves between buckets, its inbound links SHOULD be updated. |
| **REQ-020** | Working Memory — Curated Reference | The mind MUST support a curated long-term reference file in working memory that the agent reads at every session start to orient itself. | - The file MUST be read before the first user message is processed in each session.<br>- The file MUST be updated only during consolidation, never mid-task.<br>- IF the file does not exist, the system MUST proceed without error. |
| **REQ-021** | Working Memory — Chronological Log | The mind MUST support an append-only chronological log in working memory for raw observations. | - New entries MUST be appended; existing entries MUST NOT be modified or deleted.<br>- IF the file does not exist on first write, it MUST be created.<br>- Long sessions (over 30 minutes) SHOULD include periodic observation entries. |
| **REQ-022** | Working Memory — Learned Rules | The mind MUST support a rules file in working memory where the agent records one-liner lessons from mistakes. | - New rules MUST be appended; existing rules MUST NOT be removed.<br>- The file MUST be read at every session start.<br>- IF the file does not exist on first write, it MUST be created. |
| **REQ-023** | Working Memory — Consolidation | The agent MUST periodically consolidate the chronological log into the curated reference file. | - Consolidation SHOULD be triggered after approximately 14 days elapsed or approximately 150 lines accumulated in the log.<br>- Consolidation MUST NOT occur mid-task — only during natural breaks.<br>- After consolidation, the log MUST be trimmed and the curated reference MUST reflect the agent's current understanding. |
| **REQ-024** | Knowledge vs. Observation Separation | The system MUST enforce a separation between user-shared knowledge (IDEA) and agent-observed information (working memory). | - When a user shares a fact, the agent MUST write it to the appropriate IDEA bucket.<br>- When the agent notices a pattern, makes a mistake, or records a session observation, it MUST write to working memory.<br>- Working memory is agent-private; IDEA is user-visible. |
| **REQ-025** | Bootstrap — Phase 1: Identity | The bootstrap workflow MUST guide the operator through customizing the core identity file via interactive conversation. | - The agent MUST ask about name, personality type, mission, boundaries, and tone.<br>- On completion, the core identity file MUST be updated to reflect the operator's answers. |
| **REQ-026** | Bootstrap — Phase 2: Agent File | The bootstrap workflow MUST guide the operator through creating an agent instruction file. | - The agent MUST ask about primary role, domain context, and operational principles.<br>- On completion, a new agent instruction file MUST exist in the agents directory. |
| **REQ-027** | Bootstrap — Phase 3: Memory Seeding | The bootstrap workflow MUST auto-populate working memory from the bootstrap conversation. | - The curated reference file MUST contain context and conventions from the bootstrap.<br>- The log file MUST contain an entry recording the bootstrap session.<br>- The setup guide file MUST be deleted after this phase completes. |
| **REQ-028** | IDEA Auto-Creation | IF an IDEA bucket directory does not exist when the agent needs to write knowledge to it, the system MUST create it automatically. | - Writing to a non-existent IDEA bucket directory MUST succeed after auto-creating the directory.<br>- The auto-created directory MUST appear in subsequent directory listings. |
| **REQ-029** | Client Creation from Mind | The system MUST support creating a configured agent runtime client pointed at a specific mind directory. | - The created client MUST use the identity assembled from the specified mind.<br>- The system MUST locate the required CLI binary on the system PATH.<br>- On Windows, the system MUST prefer platform-specific executable extensions when searching for the CLI binary. |
| **REQ-030** | Inbox | The mind MAY include an inbox directory for incoming items that the agent triages into IDEA buckets. | - Items placed in the inbox MUST be readable by the agent.<br>- The agent SHOULD triage inbox items into the appropriate IDEA bucket during conversation. |

### 3.3 Edge Cases & Error Handling

*   **Mind root does not exist:** Validation MUST report an error. Consumers MUST refuse to start if validation fails.
*   **Core identity file missing:** Validation MUST report a specific error. Identity assembly MUST fail. The agent MUST NOT start without a core identity.
*   **Working-memory directory missing:** Validation MUST report a specific error. The mind is invalid without it.
*   **Working-memory files missing (memory.md, rules.md, log.md):** The agent MUST treat a missing file as empty on read and create it on first write. This is expected in a freshly scaffolded mind before the first session.
*   **Path traversal attempt:** The system MUST reject the request and MUST NOT disclose any information about the filesystem outside the mind root.
*   **Scaffolding into non-empty directory:** The system MUST reject the operation to prevent accidental overwrite of an existing mind.
*   **Git sync with diverged history:** The system MUST continue with the local copy. Fast-forward-only merge prevents unexpected conflict resolution in the mind directory.
*   **Git sync on non-git directory:** The system MUST continue without error. Git operations fail silently when the mind is not a git repository.
*   **Agent instruction file with malformed frontmatter:** IF the file does not begin with a valid frontmatter delimiter on line 1, the file MUST be included as-is (no stripping attempted).
*   **Concurrent writes to working memory:** The system SHOULD ensure that append operations to the log do not corrupt existing entries.

## 4. Non-Functional Requirements (Constraints)
*Rule: All constraints must be quantifiable and measurable.*

*   **Performance:** Identity assembly MUST complete within 500ms for a mind containing up to 20 agent instruction files. Validation MUST complete within 200ms for a mind with the full optional directory structure.
*   **Scalability:** The mind system MUST support IDEA buckets containing up to 1,000 files each without degradation of file listing or reading performance.
*   **Security & Compliance:** All file reads MUST be constrained to the mind root directory via path-traversal protection. Path resolution MUST use ordinal (locale-independent) string comparison. The mind root is a hard security boundary — no read operation MAY escape it.
*   **Platform / Environment:** The mind system MUST operate on Windows, macOS, and Linux. Path handling MUST normalize platform-specific separators before validation. The system MUST require the GitHub Copilot CLI binary to be installed and available on the system PATH.
*   **Determinism:** Identity assembly MUST be deterministic — the same mind directory MUST always produce the same output, regardless of platform or invocation order.
*   **Compatibility:** The system MUST target .NET 10.0 or later.

## 5. User Experience (UX) & Design

*   **Design Assets:** Not applicable — the mind is a directory on disk, not a graphical interface.
*   **Interaction Model:** The mind is a collection of plain-text Markdown files in a well-defined directory structure. Operators create, edit, and organize files using any text editor or file manager. The agent reads and writes through defined capabilities exposed by the host application. There is no proprietary file format — all content is human-readable Markdown.
*   **Discoverability:** After scaffolding, the mind contains seed files with placeholder content that communicates the purpose of each file and directory. The bootstrap workflow guides the operator through initial setup via interactive conversation, replacing placeholder content with personalized identity and knowledge.
*   **Copy & Messaging:** Validation results MUST use descriptive messages that identify the specific element that is missing or found (e.g., "SOUL.md not found at mind root" rather than "validation failed"). Error messages on path-traversal rejection MUST NOT reveal filesystem paths outside the mind.

## 6. Out of Scope (Anti-Goals)
*Rule: Explicitly state what we are NOT building to prevent scope creep.*

*   Multi-mind (multi-agent) support within a single host application is out of scope.
*   Remote minds (loading a mind from a URL instead of a local path) are out of scope.
*   Mind export/import as a portable archive (zip, tarball) is out of scope.
*   Encryption of working-memory files at rest is out of scope.
*   Full-text search indexing across IDEA buckets and working memory is out of scope.
*   A link graph builder or query engine for wiki-link cross-references is out of scope.
*   Automatic archive policies (auto-archiving stale initiatives after inactivity) are out of scope.
*   Mind schema versioning and migration tooling are out of scope.
*   Duplicate detection or normalization enforcement by the system is out of scope — normalization is a convention followed by the agent through its instructions, not enforced by the runtime.
*   The specific persistence mechanism for sessions (file-based vs. database) is out of scope for this specification.

## 7. Dependencies & Assumptions

### 7.1 Dependencies

*   **GitHub Copilot SDK:** The agent runtime depends on the Copilot SDK for model inference, session management, and tool execution.
*   **GitHub Copilot CLI:** The Copilot SDK requires the `copilot` CLI binary to be installed and available on PATH. The mind system's client creation depends on locating this binary.
*   **Host Application (Gateway or CLI):** The mind system provides primitives (scaffolding, validation, reading, identity assembly). A host application MUST compose these primitives to bring an agent to life.
*   **Filesystem Access:** The mind directory MUST be on local disk accessible to the host process. File read/write operations are direct filesystem I/O.

### 7.2 Assumptions

*   We assume one mind directory per agent — there is no requirement for a single mind to serve multiple agents.
*   We assume the mind directory is on local disk accessible to the host process.
*   We assume operators are comfortable editing Markdown files in a text editor to customize their agent's personality and knowledge.
*   We assume git is available on the system PATH when git sync is enabled, but git is not required for core mind functionality.
*   We assume working-memory consolidation thresholds (approximately 14 days or approximately 150 log lines) are guidelines enforced by the agent's instructions, not by the runtime.
*   We assume the agent, not the runtime, is responsible for maintaining wiki-link integrity when moving items between IDEA buckets.
