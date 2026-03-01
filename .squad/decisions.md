# Decisions

> Team decisions that affect everyone. Append-only. Scribe maintains this file.

---

## 2026-03-01T03:37:44Z: SOUL.md Template Source — OpenClaw Reference

**By:** Ian Philpot (via Copilot)  
**What:** The SOUL.md scaffold template must use the OpenClaw reference template from `https://raw.githubusercontent.com/openclaw/openclaw/0f72000c96deaf385fc217811f29166ec8f2d815/docs/reference/templates/SOUL.md` — do not generate a custom template. Use the upstream source **verbatim**.  
**Why:** User request — captured for team memory. Affects T5 (Mind Scaffold) implementation.  
**Impact:** Phase 1 task T5 (Mind Scaffold) must fetch and store the OpenClaw template exactly as-is, with no modifications or custom alternatives.

---

## 2026-03-01T03:38:30Z: Phase 1 Bootstrap — Architecture & Tasks

**By:** Q (Lead / Architect)  
**What:** Phase 1 (Mind Discovery) decomposition. Bootstrap orchestration is first-class concern. Implements 8 tasks (T1-T8) with 5 core interfaces.

### Interfaces
1. **IMindValidator** — Structure validation
2. **IMindDiscovery** — Convention-based search
3. **IMindScaffold** — Starter structure generation (OpenClaw SOUL.md template verbatim)
4. **IBootstrapOrchestrator** — Full bootstrap flow coordination
5. **IConfigurationPersistence** — Config save/load

### Task Breakdown
- **T1** (Felix): CLI arg parsing — 2h
- **T2** (Felix): Config persistence — 3h
- **T3** (Felix): Mind validator — 4h
- **T4** (Vesper): Mind discovery — 3h
- **T5** (Felix): Mind scaffold (OpenClaw template) — 4h
- **T6** (Vesper): Bootstrap orchestrator — 6h
- **T7** (Vesper): Program.cs integration — 3h
- **T8** (Felix+Vesper): E2E testing — 4h

**Total effort:** ~29 hours (3-4 days parallel)

### Blocking Decisions (Require Ian Input)
- **D1:** Interactive prompts vs fail-fast
- **D2:** Config storage location
- **D3:** Discovery priority
- **D4:** Template source (DECIDED: OpenClaw reference verbatim)

---

## 2026-03-01T03:44:07Z: Bootstrap Plan Reassessment — Phase 1a/1b Split & Memory System Update

**By:** Q (Lead / Architect)  
**Trigger:** Ian shared "Building an Agent with Attitude" guide (directive 2026-03-01T03:44:07Z)  
**What:** Proposed Phase 1a/1b split based on guide insights:

### Key Discoveries from Guide

1. **`.working-memory/` is the canonical memory system** with defined file purposes:
   - `memory.md` — Curated long-term memory, read every session
   - `rules.md` — Mistake journal, one-liners that compound
   - `log.md` — Raw chronological, append-only
   - Consolidation every ~14 days

2. **Mind ≠ Host Repo:**
   - Mind = SOUL.md + .working-memory/ + IDEA folders (domains/, initiatives/, expertise/, inbox/, Archive/)
   - Host repo = .github/agents/{name}.agent.md + .github/skills/{name}/SKILL.md
   - Scaffold creates the mind; guide adds host repo awareness

3. **Interactive walkthrough is a UX layer** on top of existing services (validator, scaffold, persistence)

### Phase 1a — Automated Infrastructure (Current Plan, Tasks T1–T8)
- Delivers roadmap's 4 success criteria
- Scaffold creates full mind structure with `.working-memory/` (memory.md, rules.md, log.md pre-seeded)
- Validator checks `.working-memory/` as required directory
- Orchestrator supports `--mind-root`, `--scaffold`, `--interactive`, `--reset-config`
- SOUL.md template dropped verbatim (no interactive customization yet)
- No host repo awareness

### Phase 1b — Interactive Walkthrough (New Plan Needed)
- Implements 6-phase guided experience from guide
- Adds `--guided` flag to orchestrator
- Interactive SOUL.md customization (5 identity questions)
- Host repo agent file and skill creation
- Retrieval/search configuration
- Conversational UX with confirm-at-each-step

### Task Updates Affecting Phase 1a

| Task | Change | Reason |
|------|--------|--------|
| T3 (Validator) | Check `.working-memory/` as required dir, check sub-files as warnings | Roadmap defines `.working-memory/` as memory system |
| T5 (Scaffold) | Create `.working-memory/` with purpose-seeded files, remove `.github/` scaffolding | Mind ≠ host repo; files have defined purposes |
| T6 (Orchestrator) | Design extensible mode system for Phase 1b `--guided` | Avoid rewriting when walkthrough is added |

### Decision: D5 — Phase 1a/1b Split

**Q's recommendation:** Ship Phase 1a first, Phase 1b follows immediately after.

**Rationale:** Phase 1a is buildable now — task breakdown ready, dependencies clear, roadmap's 4 success criteria all automated. Phase 1b requires UX design for conversational flow, host repo detection logic, and SOUL.md customization engine — all benefit from having working scaffold to test against.

**Status:** Pending Ian's sign-off.

---

## 2026-03-01T03:44:07Z: User Directive — Bootstrap Guide Alignment

**By:** Ian Philpot (via Copilot)  
**What:** The mind bootstrap flow should follow the "Building an Agent with Attitude" guide (https://raw.githubusercontent.com/ianphil/public-notes/refs/heads/main/expertise/agent-craft/building-an-agent-with-attitude.md). This is a 6-phase interactive walkthrough.  
**Why:** User request — this is the design intent for Phase 1. Expands scaffold concept from "create empty dirs" to "interactive agent-building workshop."  
**Impact:** Affects Phase 1b design (future), validates Phase 1a foundation with `.working-memory/` as memory system.

---

## 2026-03-01T03:52:41Z: User Directive — `.working-memory/` Naming is Canonical

**By:** Ian Philpot (via Copilot)  
**What:** Do NOT rename `.working-memory/` to `.ainotes/`. The "Building an Agent with Attitude" guide uses `.ainotes/` for its own pattern, but MsClaw's roadmap (`.aidocs/roadmap.md`) naming is authoritative. The guide's memory file semantics (memory.md, rules.md, log.md with defined purposes) still apply — they just live in `.working-memory/`.  
**Why:** User request — corrects Q's reassessment (Rev 2.0) which incorrectly proposed renaming to `.ainotes/` based on the external guide.  
**Impact:** All plan and squad files updated to Rev 2.1. Validator, scaffold, and all references now use `.working-memory/` as canonical.

---

## 2026-03-01T05:27:05Z: User Directive — Team Model Specification

**By:** Ian Philpot (via Copilot)  
**What:** Felix, Vesper, and Natalya should use gpt-5.3-codex model for implementation work. Q uses claude-opus-4.6 for architecture/planning.  
**Why:** User request — captured for team memory. Specifies model selection for parallel team execution.  
**Impact:** All implementation agents spawned with gpt-5.3-codex. Architectural decisions use Opus 4.6.
