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
**What:** Phase 1 (Mind Discovery) decomposition approved. Bootstrap orchestration is first-class concern. Implements 8 tasks (T1-T8) with 5 core interfaces.

### Interfaces Approved
1. **IMindValidator** — Structure validation (SOUL.md, .working-memory/, IDEA folders)
2. **IMindDiscovery** — Convention-based search (., ~/.msclaw/mind, ~/src/miss-moneypenny, cached)
3. **IMindScaffold** — Starter structure generation (OpenClaw SOUL.md template verbatim)
4. **IBootstrapOrchestrator** — Full bootstrap flow coordination (detect → configure → validate → persist → start)
5. **IConfigurationPersistence** — Config save/load (~/.msclaw/config.json, thread-safe)

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
**Status:** Waiting on D1-D4 decisions from Ian before implementation

### Blocking Decisions (Require Ian Input)
- **D1:** Interactive prompts vs fail-fast (Rec: fail-fast Phase 1)
- **D2:** Config storage (Rec: ~/.msclaw/config.json)
- **D3:** Discovery priority (Rec: explicit → cached → discovery)
- **D4:** Template source (DECIDED: OpenClaw reference verbatim)

**Rationale:** Phase 1 transforms MsClaw from hardwired instance to framework. Bootstrap is the seam — without it, MsClaw is tied to one person. With it, anyone can run `dotnet run` and get a working agent.

---
