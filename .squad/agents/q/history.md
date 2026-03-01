# Project Context

- **Owner:** Ian Philpot
- **Project:** MsClaw — a .NET agent framework that hosts AI agents with personality (SOUL.md), working memory, and modular IDEA-based knowledge structure. MVP is complete.
- **Stack:** .NET 9, C#, ASP.NET Core, Azure OpenAI
- **Created:** 2026-03-01

## Key Files

- `src/MsClaw/` — main project
- `MsClaw.sln` — solution file
- `.aidocs/roadmap.md` — post-MVP roadmap (3 phases: Bootstrap, Extensions, Gateway)

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-03-01 — Bootstrap Plan Published

**Context:** Authored comprehensive bootstrap plan at `.aidocs/bootstrap-plan.md` from Phase 1 decomposition.

**What it captures:**
- Overview, current state, target state (4 success criteria)
- 5 core interfaces (IMindValidator, IMindDiscovery, IMindScaffold, IBootstrapOrchestrator, IConfigurationPersistence)
- 8 implementation tasks (T1-T8) with owners, dependencies, and deliverables
- 4 decisions requiring Ian's input (D1-D4) with Q's recommendations
- Team directives (OpenClaw SOUL.md template must be verbatim)
- Open questions deferred to Phase 2+
- Timeline (29 hours, 3-4 days parallel)

**Format:** Human-readable planning document, not agent output. Suitable for Ian and any future contributor. No code blocks for full interface definitions — architecture described via responsibilities and key contracts.

**Key directives preserved:**
- SOUL.md template: OpenClaw reference URL, verbatim (per 2026-03-01T03:37:44Z directive)
- Bootstrap is first-class, separate from runtime (blocking mode, fail-fast)
- Validation returns structured result (errors/warnings/found), not just pass/fail
- Discovery uses conventions: explicit → cached → search order
- Configuration persists in `~/.msclaw/config.json` (user-specific, multi-project)

---

### 2026-03-01 — Phase 1 Decomposition

**Context:** Analyzed roadmap Phase 1 (Bootstrap / Mind Discovery) against current codebase. MVP has MindReader service reading from hardcoded `appsettings.json` path. No validation, discovery, or scaffolding.

**Architecture decisions:**
1. **Bootstrap is first-class** — Separate from runtime services. Runs before Kestrel starts, validates before accepting requests. Blocking mode preferred over degraded/503 mode.
2. **Five clear interfaces** — IMindValidator (structure checks), IMindDiscovery (convention search), IMindScaffold (generate structure), IBootstrapOrchestrator (coordinate flow), IConfigurationPersistence (cache resolution).
3. **Validation is structured** — Return `MindValidationResult` with errors/warnings/found structure, not just pass/fail. Allows progressive enhancement.
4. **Discovery uses conventions** — Search order: explicit CLI → cached config → `.` → `~/.msclaw/mind` → `~/src/miss-moneypenny`. Filters via validator.
5. **Scaffold templates hardcoded** — SOUL.md template embedded in code for Phase 1. Consider resource files if templates grow.

**Task breakdown pattern:**
- 8 tasks (T1-T8) with explicit dependencies
- Felix owns service logic (validator, scaffold, persistence, CLI parsing)
- Vesper owns systems integration (discovery, orchestrator, Program.cs, filesystem)
- Pair on E2E testing to validate full flow
- Estimated 29 hours total (3-4 days parallel work)

**Decisions punted to Ian:**
- Interactive vs fail-fast on missing config
- Config storage location (`~/.msclaw/` vs project-local)
- Discovery priority order edge cases
- Scaffold template sourcing (hardcode vs embed vs reference)

**Key files:**
- `.squad/decisions/inbox/q-phase1-decomposition.md` — full decomposition
- `.aidocs/roadmap.md` — Phase 1 requirements
- `src/MsClaw/Core/MindReader.cs` — existing mind file access (no validation)
- `src/MsClaw/Models/MsClawOptions.cs` — current config model (hardcoded MindRoot)

**Patterns established:**
- Orchestrator pattern for multi-step flows (bootstrap, future: extension loading, gateway routing)
- Convention-over-configuration for discovery (search well-known locations before prompting)
- Explicit validation phase before runtime (fail fast, clear errors)
- Configuration persistence in user home directory (survives project moves)

---

### 2026-03-01 — Bootstrap Plan Reassessment (Rev 2.0)

**Context:** Ian shared the "Building an Agent with Attitude" guide — a 6-phase interactive walkthrough that redefines bootstrap from "scaffold dirs" to "guided agent-building workshop."

**Key shifts:**
1. **`.working-memory/` is the memory system** with defined file purposes: memory.md (curated long-term), rules.md (mistake journal), log.md (raw chronological). Not empty placeholders.
2. **Host repo vs mind separation** — `.github/agents/` and `.github/skills/` live in the HOST REPO, not the mind. The mind is identity + knowledge; the host is operational config.
3. **SOUL.md customization is interactive** — Template is the starting point, but 5 questions personalize it. Phase 1a drops template verbatim; Phase 1b adds the conversation.
4. **Scope split: Phase 1a (automated) + 1b (interactive)** — 1a delivers the roadmap's 4 success criteria with the full directory structure. 1b layers the guided 6-phase experience on top.

**Artifacts updated:**
- `.aidocs/bootstrap-plan.md` → Rev 2.1 (corrected `.ainotes/` back to `.working-memory/`)
- `.aidocs/bootstrap-guide-reference.md` → Local copy of the guide
- `.squad/decisions/inbox/q-bootstrap-reassessment.md` → Formal decision record

**Impact on tasks:**
- T3 (validator): Checks `.working-memory/` as required dir, sub-files as warnings
- T5 (scaffold): `.working-memory/` with purpose-seeded files, no `.github/` dirs
- T6 (orchestrator): Design for extensible mode system (`IBootstrapMode`) to support Phase 1b `--guided` flag
- New decision D5: Phase 1a/1b scope split needs Ian's sign-off

---

### 2026-03-01 — `.working-memory/` Naming Correction

**Context:** User directed that `.working-memory/` is the canonical memory directory name, NOT `.ainotes/`. Rev 2.0 incorrectly adopted `.ainotes/` from the guide — but the roadmap naming is authoritative.

**What changed:** All plan and squad files corrected. Bootstrap plan updated to Rev 2.1.

**Learning:** User directive: `.working-memory/` is canonical, not `.ainotes/`. Roadmap naming wins. The guide's memory file semantics (memory.md, rules.md, log.md) still apply — they just live in `.working-memory/`, not `.ainotes/`.

---

### 2026-03-01 — Bootstrap Spec v5.0 Implementation Plan

**Context:** Read `.aidocs/bootstrap-spec.md` (Rev 5.0) and created concrete implementation plan for the squad.

**Decisions resolved:**
- D1: Fail-fast on missing mind (exit with error, no degraded mode)
- D2: Config at `~/.msclaw/config.json` (user-global)
- D3: Discovery order: explicit CLI → cached config → convention search

**Plan structure:**
- 12 tasks (T1–T12) across Felix (backend), Vesper (systems), Natalya (tests)
- 6 new interfaces: IMindValidator, IMindDiscovery, IMindScaffold, IConfigPersistence, IBootstrapOrchestrator, IIdentityLoader
- 3 new models: MindValidationResult, MsClawConfig, BootstrapResult
- 19 new files, 4 modified files (including new test project)
- 4-day schedule with Day 1 fully parallel (3 tracks)
- Key integration point: T9 (Program.cs) wires everything together

**Key architecture decisions in this plan:**
- BootstrapOrchestrator runs before Kestrel starts — resolves mind root synchronously
- IdentityLoader replaces the private LoadAgentInstructionsAsync in CopilotRuntimeClient — composes SOUL.md + agent files
- Bootstrap detection is in /chat endpoint — checks for bootstrap.md presence
- MindScaffold copies templates from embedded resources, not from filesystem paths
- MindValidator returns structured result (errors/warnings/found), not bool

**Artifact:** `.squad/decisions/inbox/q-bootstrap-spec-plan.md`
