# Bootstrap Plan — Phase 1: Mind Discovery

**Document:** Clean comprehensive bootstrap plan for Phase 1  
**Owner:** Q (Lead / Architect)  
**Audience:** Ian Philpot, Felix (backend dev), Vesper (systems dev)  
**Date:** 2026-03-01  
**Revision:** 2.0 (2026-03-01 — reassessed per "Building an Agent with Attitude" guide)

---

> **⚠️ Revision 2.0 — What Changed**
>
> Ian shared the "Building an Agent with Attitude" guide (saved at `.aidocs/bootstrap-guide-reference.md`), a 6-phase interactive walkthrough that redefines what "bootstrap" means. Key shifts:
>
> 1. **Bootstrap is a guided workshop, not just scaffolding.** The scaffold (T5) creates structure; the orchestrator (T6) walks the user through a conversational 6-phase experience.
> 2. **`.ainotes/` replaces `.working-memory/`.** Same files (memory.md, rules.md, log.md) but with defined purposes: curated long-term memory, mistake journal, raw chronological log. Consolidation every ~14 days.
> 3. **Host repo vs mind separation.** The agent file (`.github/agents/`) and skills (`.github/skills/`) live in the HOST REPO, not in the mind directory. The mind holds identity + knowledge; the host holds operational config.
> 4. **SOUL.md is customized interactively.** The OpenClaw template is the starting point, but Phase 1 of the guide asks 5 questions (Name, Personality, Mission, Boundaries, Tone) and customizes the template.
> 5. **Phase 1 scope split.** Automated infrastructure (Phase 1a) ships first. The interactive walkthrough (Phase 1b) layers on top. See Scope Decision below.

---

## Overview

Phase 1 transforms MsClaw from a hardwired instance into a framework. Today, `MIND_ROOT` is set via `appsettings.json`, pointing to Ian's local `miss-moneypenny` directory. No first-run experience, no validation beyond "file not found," and no scaffolding.

**Phase 1 goal:** Anyone can `dotnet run` MsClaw on their machine and get a working agent — detection of missing configuration, validation of mind structure, scaffolding of starter templates, persistence of resolved choices, and (in Phase 1b) a guided interactive experience that builds a personalized agent from scratch.

**Why this matters:** This is the seam between "instance" and "framework." Without proper bootstrap, MsClaw is tied to one person and one setup. With it, it becomes composable, deployable, and extensible.

---

## Scope Decision: Phase 1a vs 1b

**Q's recommendation:** Split Phase 1 into two increments.

### Phase 1a — Automated Infrastructure (this plan, T1–T8)
Everything the guide needs as a foundation: detect, validate, scaffold, discover, persist. The scaffold creates the full mind structure from the guide. The orchestrator supports `--scaffold` and `--mind-root` as non-interactive paths.

### Phase 1b — Interactive Walkthrough (follow-up plan)
The guided 6-phase experience from the "Building an Agent with Attitude" guide. This layers on top of Phase 1a:
- Phase 1 (Identity): Interactive SOUL.md customization (5 questions)
- Phase 2 (Agent File): `.github/agents/{name}.agent.md` in the HOST REPO
- Phase 3 (Memory): `.ainotes/` structure (already scaffolded by 1a)
- Phase 4 (Retrieval): Search tool configuration
- Phase 5 (First Skill): `.github/skills/{name}/SKILL.md` in the HOST REPO
- Phase 6 (Knowledge): IDEA folders (already scaffolded by 1a)

**Rationale:** The automated parts are the foundation. You can't run the interactive workshop without the ability to create and validate structure. Phase 1a delivers the roadmap's 4 success criteria. Phase 1b adds the guided experience on top — same services, new UX layer. Shipping 1a first means Felix and Vesper can test against real mind structures while the interactive walkthrough is designed.

**What Phase 1b adds to the codebase:**
- Interactive SOUL.md customization (template + 5 questions → personalized output)
- Host repo awareness (write `.github/agents/`, `.github/skills/` outside the mind)
- Retrieval/search configuration step
- The walkthrough orchestrator (6 phases, conversational, confirm-at-each-step)

---

## Current State

### What Exists
- `MindReader` service reads files from configured path
- `MsClawOptions` bound from `appsettings.json`
- Basic path traversal protection
- Hardcoded mind root: `"../miss-moneypenny"`

### What's Missing
1. **First-run detection** — no way to detect missing/invalid configuration
2. **Mind validation** — no check for required structure (SOUL.md, .ainotes/, IDEA folders)
3. **Convention-based discovery** — no fallback locations (current dir, ~/.msclaw/mind, development conventions)
4. **Scaffolding** — no way to generate starter structure for new agents
5. **Configuration persistence** — no mechanism to save resolved mind root
6. **Bootstrap orchestration** — no coordinator for the full "detect → configure → validate → persist → start" flow
7. **CLI argument support** — no `--mind-root` or `--scaffold` flags
8. **Interactive prompts** — no user interaction during bootstrap
9. **Memory system** — no `.ainotes/` structure with defined file purposes (Phase 1b: interactive seeding)

---

## Target State

### Success Criteria (from Roadmap)

When Phase 1 is complete:

1. ✅ **Fresh install, no config:** `dotnet run` detects no mind, offers to scaffold or configure
2. ✅ **Explicit mind root:** `dotnet run --mind-root ~/src/miss-moneypenny` validates and starts serving
3. ✅ **Scaffold new:** `dotnet run --scaffold ~/src/my-new-agent` creates IDEA structure and starts serving
4. ✅ **Subsequent runs:** After first successful run, `dotnet run` remembers the mind root (no re-prompt)

### Additional Outcomes

- No hardcoded paths in `appsettings.json` (ships with `MindRoot: null`)
- `README.md` includes bootstrap instructions for new users
- Felix and Vesper can run MsClaw on their machines without Ian's hands-on help
- Validation errors are structured (errors/warnings/found structure) for progressive enhancement

---

## Architecture

### Five Core Interfaces

#### 1. **IMindValidator**

Checks mind structure completeness and reports findings.

> **⚠️ Rev 2.0 change:** Validates `.ainotes/` instead of `.working-memory/`. The `.ainotes/` directory is the memory system from the "Building an Agent with Attitude" guide.

**Responsibilities:**
- Verify required structure exists: `SOUL.md`, `.ainotes/` (with memory.md, rules.md, log.md)
- Report optional structure: IDEA folders (domains/, initiatives/, expertise/, inbox/, Archive/)
- Return structured result with errors, warnings, and discovered structure

> **⚠️ Rev 2.0 change:** `.working-memory/` → `.ainotes/`. The guide defines `.ainotes/` as the memory system directory with specific file purposes: `memory.md` (curated long-term, read every session), `rules.md` (mistake journal, one-liners), `log.md` (raw chronological, append-only).

**Key contract:**
```
Validate(mindRoot: string) → MindValidationResult
  - IsValid: bool
  - Errors: List<string>       // Blocking issues (SOUL.md missing, .ainotes/ missing, etc.)
  - Warnings: List<string>     // Non-blocking (empty SOUL.md, no IDEA folders)
  - Found: MindStructure       // What was discovered
```

**Usage:** Filter discovery results; verify scaffold outcomes; report to user.

---

#### 2. **IMindDiscovery**

Locate minds via convention before asking the user.

**Responsibilities:**
- Search well-known locations in priority order
- Filter results through validator (only return valid minds)
- Fall back gracefully if no minds found

**Search order:**
1. Current working directory (`.`) if valid mind structure exists
2. `~/.msclaw/mind` (standard user config location)
3. `~/src/miss-moneypenny` (development convention)
4. Cached config path from last run (fast path)

**Key contract:**
```
DiscoverMinds() → List<string>  // Absolute paths in priority order
```

**Usage:** First step in bootstrap; if discovery succeeds, validate and persist.

---

#### 3. **IMindScaffold**

Generate starter mind structure with templates.

**Responsibilities:**
- Create directory structure (SOUL.md, .ainotes/, IDEA folders, Archive/)
- Embed SOUL.md template (from OpenClaw reference, verbatim)
- Handle errors (existing directory, permission issues)
- Validate scaffold result before reporting success

> **⚠️ Rev 2.0 change:** `.working-memory/` → `.ainotes/`. The scaffold creates `.ainotes/` with seeded files that have header comments explaining their purpose. The `.github/agents/` and `.github/skills/` directories are NOT part of the mind scaffold — they belong to the host repo and are created by the interactive walkthrough (Phase 1b).

**Generated structure:**
```
{mindRoot}/
  SOUL.md                  ← Template from OpenClaw reference (verbatim)
  .ainotes/
    memory.md              ← Curated long-term memory (read every session)
    rules.md               ← Mistake journal, one-liners that compound
    log.md                 ← Raw chronological observations, append-only
  domains/
  initiatives/
  expertise/
  inbox/
  Archive/
```

**NOT in mind scaffold (host repo concerns, Phase 1b):**
```
{hostRepo}/
  .github/agents/{name}.agent.md   ← Operating instructions (Phase 1b)
  .github/skills/{name}/SKILL.md   ← Reusable workflows (Phase 1b)
```

**Key contract:**
```
Scaffold(mindRoot: string, agentName?: string) → ScaffoldResult
  - Success: bool
  - Error?: string
  - CreatedFiles: List<string>
```

**Usage:** When user requests `--scaffold` or when interactive mode offers generation.

---

#### 4. **IBootstrapOrchestrator**

Coordinate the full bootstrap flow.

**Responsibilities:**
- Parse CLI arguments
- Execute decision tree (cache → explicit → scaffold → discover → prompt → fail)
- Log each step
- Persist successful resolution
- Return resolved mind root or clear error

> **⚠️ Rev 2.0 change:** The orchestrator now has two modes. Phase 1a implements the automated flow (the decision tree below). Phase 1b adds a `--guided` mode that runs the 6-phase interactive walkthrough from the "Building an Agent with Attitude" guide. The guided mode calls the same services (scaffold, validator, persistence) but adds conversational SOUL.md customization, host repo setup, and skill creation.

**Key contract:**
```
RunAsync(args: string[], cancellationToken: CancellationToken) → BootstrapResult
  - Success: bool
  - MindRoot?: string      // Resolved absolute path
  - Error?: string
  - Mode: BootstrapMode    // Discovered, Scaffolded, Configured, Cached, Guided (Phase 1b)
```

**Decision tree:**
1. **Load cached config** — if exists and validates → return (fast path)
2. **Parse CLI args**
   - If `--scaffold <path>` → scaffold → validate → persist → return
   - If `--mind-root <path>` → validate → persist → return
3. **Discovery** → find first valid mind → persist → return
4. **Interactive mode** (if stdin is TTY)
   - If `--interactive` flag or no other option → prompt user
5. **Fail** — if all above exhausted and non-interactive → error with clear message

**Usage:** Entry point in Program.cs; runs before Kestrel startup (blocking mode, fail-fast).

---

#### 5. **IConfigurationPersistence**

Save and load bootstrap configuration.

**Responsibilities:**
- Persist mind root for subsequent runs
- Load cached configuration with validation
- Handle missing files, corruption, permission issues
- Thread-safe writes

**Storage:**
- Location: `~/.msclaw/config.json`
- Format: JSON with `mindRoot` and `lastValidated` timestamp
- Graceful degradation: missing file returns `null`, corrupt file returns `null` with warning log

**Key contract:**
```
SaveMindRoot(mindRoot: string) → void
LoadMindRoot() → string?
Clear() → void
```

**Usage:** Persistence layer for orchestrator; enables fast subsequent runs.

---

## Implementation Tasks

### Dependency Graph

```
T1 (CLI parsing) ──┬──→ T2 (Config persistence)
                   ├──→ T3 (Mind validator)
                   ├──→ T4 (Mind discovery)
                   └──→ T5 (Mind scaffold)

T2, T3, T4, T5 ──→ T6 (Bootstrap orchestrator)
T6 ──→ T7 (Program.cs integration)
T7 ──→ T8 (End-to-end testing)
```

---

### T1: CLI Argument Parsing
**Owner:** Felix (backend dev)  
**Effort:** 2 hours  
**Dependencies:** None

Add CLI argument support to Program.cs using `Microsoft.Extensions.Configuration.CommandLine` or manual parsing:
- `--mind-root <path>` — explicit mind location
- `--scaffold <path>` — generate new mind at path
- `--interactive` — prompt user if configuration missing
- `--reset-config` — clear cached configuration

**Deliverable:** CLI args available in Program.cs before DI setup; args structure passed to orchestrator.

---

### T2: Configuration Persistence
**Owner:** Felix (backend dev)  
**Effort:** 3 hours  
**Dependencies:** None

Implement `IConfigurationPersistence`:
- `JsonConfigurationPersistence` class
- Stores in `~/.msclaw/config.json`
- Handles missing directory (create it), invalid JSON (return null), file corruption (log warning, return null)
- Thread-safe writes (lock or retry pattern)

**Tests:**
- Save mind root → load → verify path matches
- Load with missing file → returns null
- Load with corrupted JSON → returns null and logs warning

**Deliverable:** Working persistence layer; directory is created on first write.

---

### T3: Mind Validator
**Owner:** Felix (backend dev)  
**Effort:** 4 hours  
**Dependencies:** None

Implement `IMindValidator`:
- `MindValidator` class checks directory structure
- Errors: missing SOUL.md, missing .ainotes/
- Warnings: empty SOUL.md, no IDEA folders found, missing .ainotes/ sub-files (memory.md, rules.md, log.md)
- Discovers which IDEA folders exist (domains/, initiatives/, expertise/, inbox/, Archive/)
- Returns `MindValidationResult` with structured findings

> **⚠️ Rev 2.0 change:** `.working-memory/` → `.ainotes/`. The validator checks for `.ainotes/` as a required directory and its three files as warnings if missing.

**Tests:**
- Valid mind → IsValid = true, no errors
- Missing SOUL.md → IsValid = false, error reported
- Missing .ainotes/ → IsValid = false, error reported
- Empty SOUL.md → IsValid = true, warning reported
- No IDEA folders → IsValid = true, warning reported
- .ainotes/ exists but missing sub-files → IsValid = true, warnings reported

**Deliverable:** Structured validation service; used by discovery and scaffold verification.

---

### T4: Mind Discovery
**Owner:** Vesper (systems dev)  
**Effort:** 3 hours  
**Dependencies:** T3 (validator for filtering results)

Implement `IMindDiscovery`:
- `MindDiscovery` class searches standard locations
- Search order: `.`, `~/.msclaw/mind`, `~/src/miss-moneypenny`, cached config path
- Filters results through validator (only returns valid minds)
- Returns list in priority order; empty list if no valid minds found
- Handles path expansion (`~` → user home) via `Path.GetFullPath(Environment.ExpandEnvironmentVariables(path))`

**Tests:**
- Mock filesystem with valid mind in `~/.msclaw/mind` → discovered at correct priority
- Multiple valid minds → returned in correct priority order
- No valid minds → returns empty list

**Deliverable:** Convention-based mind location; integration point for validator.

---

### T5: Mind Scaffold
**Owner:** Felix (backend dev)  
**Effort:** 4 hours  
**Dependencies:** T3 (validator to verify scaffold result)

Implement `IMindScaffold`:
- `MindScaffold` class generates starter structure
- **SOUL.md template:** Fetch from OpenClaw reference URL verbatim: `https://raw.githubusercontent.com/openclaw/openclaw/0f72000c96deaf385fc217811f29166ec8f2d815/docs/reference/templates/SOUL.md`
  - Store as embedded resource or hardcoded string in code
  - Replace `{AgentName}` placeholder if provided (from CLI `--scaffold` or interactive prompt)
- Creates `.ainotes/` with seeded files: memory.md (header: "Curated long-term memory"), rules.md (header: "Mistake journal"), log.md (header: "Raw chronological observations")
- Creates IDEA folders: domains/, initiatives/, expertise/, inbox/, Archive/
- Validates scaffold result through validator before returning success
- Handles existing directory (error), permission issues (error), missing parent directory (create or error)

> **⚠️ Rev 2.0 changes:**
> - `.working-memory/` → `.ainotes/` with purpose-seeded files (not empty placeholders)
> - `.github/agents/` and `.github/skills/` are NOT scaffolded here — they're host repo concerns (Phase 1b)
> - The interactive SOUL.md customization (5 questions) is Phase 1b — scaffold drops the template verbatim

**Tests:**
- Scaffold into empty directory → success, all files created
- Scaffold into existing directory → error
- Validate scaffolded structure → passes validator (SOUL.md, .ainotes/ present)
- AgentName substitution works correctly
- .ainotes/ files have purpose headers, not empty

**Deliverable:** Self-contained mind generator; used by orchestrator.

---

### T6: Bootstrap Orchestrator
**Owner:** Vesper (systems dev)  
**Effort:** 6 hours  
**Dependencies:** T1, T2, T3, T4, T5

Implement `IBootstrapOrchestrator`:
- `BootstrapOrchestrator` class coordinates flow
- Implements decision tree from interface contract
- Logs each step (cache hit, discovery, validation, scaffold, persist)
- Handles interactive prompts (if `--interactive` flag set or stdin is TTY)
- Persists successful resolution through `IConfigurationPersistence`

> **⚠️ Rev 2.0 change:** Phase 1a implements the automated decision tree below. The `--guided` flag and 6-phase walkthrough (interactive SOUL.md customization, host repo agent file creation, skill creation) are Phase 1b. The orchestrator should be designed with an extensible mode system so Phase 1b plugs in cleanly — e.g., `IBootstrapMode` strategy pattern rather than hardcoded switch.

**Flow implementation (Phase 1a — automated):**
1. Load cached config → validate → return if valid (fast path)
2. Parse CLI args → if explicit path provided → validate → persist → return
3. Parse CLI args → if scaffold requested → scaffold → validate → persist → return
4. Discovery → validate first found → persist → return
5. If all fail and interactive mode → prompt user (mind root or scaffold new)
6. If all fail and non-interactive → error with clear message

**Tests:**
- Cached valid mind → returns immediately (mode = Cached)
- `--mind-root` with valid path → validates and returns (mode = Configured)
- `--scaffold` with new path → generates structure and returns (mode = Scaffolded)
- Discovery finds valid mind → returns first found (mode = Discovered)
- All fail, non-interactive → error with instructions

**Deliverable:** Complete bootstrap coordinator; ready for Program.cs integration.

---

### T7: Program.cs Integration
**Owner:** Vesper (systems dev)  
**Effort:** 3 hours  
**Dependencies:** T6

Modify Program.cs to run bootstrap before service setup:
1. Inject `IBootstrapOrchestrator`
2. Call `BootstrapOrchestrator.RunAsync(args)` before creating `WebApplication.CreateBuilder`
3. If bootstrap fails → log error → `Environment.Exit(1)`
4. If bootstrap succeeds → set `MsClawOptions.MindRoot` from result
5. Proceed with existing DI/service startup

**Key design decision:** Bootstrap is **blocking** — runs before Kestrel starts, prevents degraded startup. Simpler failure mode, clearer error messages for users.

**Deliverable:** Bootstrap runs before any service startup; MsClawOptions.MindRoot is guaranteed valid.

---

### T8: End-to-End Testing
**Owner:** Felix + Vesper (pair programming)  
**Effort:** 4 hours  
**Dependencies:** T7

Test all four success criteria from the roadmap in isolated environment (separate temp directory per test, no pollution of developer's `~/.msclaw`):

**Test 1: Fresh install, no config**
```bash
rm -rf ~/.msclaw
dotnet run --interactive
# Expected: Prompt for mind root or offer to scaffold
# User selects scaffold → generates structure → starts serving
```

**Test 2: Explicit mind root**
```bash
dotnet run --mind-root ~/src/miss-moneypenny
# Expected: Validates mind, persists choice, starts serving
```

**Test 3: Scaffold new mind**
```bash
dotnet run --scaffold ~/src/my-new-agent
# Expected: Creates complete structure, starts serving
# Verify: SOUL.md from OpenClaw reference, all directories present
```

**Test 4: Subsequent run uses cache**
```bash
dotnet run
# Expected: Uses cached mind root, no prompts, immediate startup
```

**Test 5: Reset config**
```bash
dotnet run --reset-config --interactive
# Expected: Clears cache, re-prompts for mind root
```

**Deliverable:** All 4 success criteria passing; E2E test suite ready for CI/CD.

---

## Task Ownership Summary

### Felix (Backend Dev)
- **T1:** CLI argument parsing
- **T2:** Configuration persistence (save/load)
- **T3:** Mind validator (structure checks)
- **T5:** Mind scaffold (generate starter files)
- **T8:** E2E testing (pair with Vesper)

**Why Felix:** Service implementations, business logic, validation rules, unit testing — all backend domain.

### Vesper (Systems Dev)
- **T4:** Mind discovery (filesystem interaction, conventions)
- **T6:** Bootstrap orchestrator (system lifecycle, flow coordination)
- **T7:** Program.cs integration (startup ordering)
- **T8:** E2E testing (pair with Felix)

**Why Vesper:** Filesystem conventions, environment variable expansion, startup ordering, system-level integration — all systems domain.

---

## Decisions Requiring Ian's Input

These 4 decisions are **blocking** — they need Ian's call before implementation. Q has included recommendations for each.

### D1: Interactive Prompts vs Fail-Fast

**Question:** If bootstrap fails and stdin is not a TTY (e.g., running in Docker, systemd), should MsClaw:
- **(a) Exit with error immediately** — clear failure, user sees what went wrong
- **(b) Enter degraded mode** — health endpoint returns 503, `/chat` returns "not configured"
- **(c) Accept configuration via API** — POST `/admin/configure` endpoint

**Q's recommendation:** **(a) for Phase 1, (c) for Phase 2+ if hosting becomes an issue.**

**Rationale:** Phase 1 MVP should be simple. Fail-fast is clear. If MsClaw gets containerized for production, we can add (c).

---

### D2: Configuration Storage Location

**Question:** Where to persist configuration?
- **(a) `~/.msclaw/config.json`** — user-specific, survives project moves, supports multiple projects
- **(b) `{project_root}/.msclaw.local.json`** — project-specific, gitignored, tied to deployment
- **(c) `appsettings.json`** — inline with existing config, auto-generated

**Q's recommendation:** **(a) for multi-project use, (b) if MsClaw is tied to a single deployment.**

**Rationale:** (a) is more flexible (user has one mind, use it everywhere). (b) is cleaner if each project/deployment has its own agent.

---

### D3: Discovery Priority Order

**Question:** Should discovery prioritize:
- **(a) Closest to execution context** — `.`, then `~/.msclaw/mind`
- **(b) Cached config first** — fast path, then search
- **(c) Explicit config always wins** — even if invalid, don't fall back

**Q's recommendation:** **Explicit → cached → discovery.** Explicit config should validate; if invalid, fail immediately (don't fall back).

**Rationale:** Explicit intent should be respected. Caching is a fast path. Discovery is fallback.

---

### D4: Scaffold Template Source

**Question:** SOUL.md template — where to get it?
- **(a) Hardcoded in `MindScaffold`** — self-contained, no external dependencies
- **(b) Copy from reference template** — `https://raw.githubusercontent.com/openclaw/openclaw/...`
- **(c) Embed as resource file** — in assembly, maintainable if templates grow

**Q's recommendation:** **(b) Fetch from OpenClaw reference verbatim per [2026-03-01T03:37:44Z directive](./decisions/inbox/copilot-directive-20260301T033744Z.md).** Store locally as hardcoded string or embedded resource.

**Rationale:** Ian explicitly requested the upstream template verbatim. Store locally to avoid network dependency on every scaffold.

---

## Open Questions (Deferred to Phase 1b / Phase 2+)

These are intentionally **not** part of Phase 1a:

### Deferred to Phase 1b (Interactive Walkthrough)
5. **SOUL.md customization** — The guide's 5 questions (Name, Personality, Mission, Boundaries, Tone) that personalize the template. Phase 1a drops the template verbatim.
6. **Host repo agent file** — `.github/agents/{name}.agent.md` creation. Lives in host repo, not mind. Needs host repo detection.
7. **Host repo skill creation** — `.github/skills/{name}/SKILL.md`. Same host repo concern.
8. **Retrieval/search configuration** — Phase 4 of the guide. What tools, what rules.
9. **Guided walkthrough orchestration** — The `--guided` flag, 6-phase conversational flow, confirm-at-each-step UX.
10. **`.ainotes/` consolidation** — The guide specifies ~14 day consolidation cycles. This is an operational concern, not bootstrap.

### Deferred to Phase 2+

1. **Git integration** — Should scaffold initialize a git repo in the new mind directory?
2. **Validation caching** — Should validator results be cached (e.g., only re-validate every 5 minutes)?
3. **Mind versioning** — Should minds have a version schema that MsClaw checks?
4. **Multiple minds** — Should MsClaw support switching between minds without restarting?

**Recommendation:** Defer all to Phase 2+. Phase 1 is about getting to "anyone can run MsClaw." These add complexity.

---

## Team Directives

These are binding decisions from the team, captured for future reference:

### Directive: OpenClaw SOUL.md Template (2026-03-01T03:37:44Z)

> The SOUL.md scaffold template must use the OpenClaw reference template from `https://raw.githubusercontent.com/openclaw/openclaw/0f72000c96deaf385fc217811f29166ec8f2d815/docs/reference/templates/SOUL.md` — do not generate a custom template. Use the upstream source **verbatim**.

**Impact:** T5 (Mind Scaffold) must fetch and store the OpenClaw template exactly as-is, with no modifications or custom alternatives.

---

## Timeline and Effort

**Estimated total:** ~29 hours  
- **Felix:** 13 hours (T1, T2, T3, T5, T8)
- **Vesper:** 12 hours (T4, T6, T7, T8)
- **Pair:** 4 hours (T8 E2E)

**Target completion:** 3-4 days with parallel work  
- **Days 1-2:** Felix and Vesper work in parallel (T1-T5)
- **Day 3:** T6 integration, T7 startup
- **Day 4:** T8 E2E testing, validation

---

## Next Steps

1. **Ian decides D1-D5** — D1-D4 required before implementation; D5 (1a/1b split) confirms scope
2. **Q reviews decisions with team** → move decomposition to `.squad/decisions/` once approved
3. **Felix starts T1, T2** — in parallel
4. **Vesper starts T4 discovery** — once T3 (validator) is ready for integration
5. **Daily standups** — track progress, adjust estimates
6. **Pair on T8** — E2E testing validates entire flow
7. **Q plans Phase 1b** — interactive walkthrough design after 1a scope is confirmed

---

## Success Checklist

- [ ] All 4 roadmap success criteria pass (fresh install, explicit path, scaffold, cached)
- [ ] No hardcoded paths in `appsettings.json` (ships with `MindRoot: null` or missing)
- [ ] `README.md` includes bootstrap instructions for new users
- [ ] Felix and Vesper can run MsClaw on their machines without Ian's hands-on help
- [ ] SOUL.md template is OpenClaw reference (verbatim, per directive)
- [ ] Validation errors are structured (errors/warnings/found structure)
- [ ] `.ainotes/` scaffolded with memory.md, rules.md, log.md (purpose-seeded, not empty)
- [ ] Validator checks for `.ainotes/` (not `.working-memory/`)
- [ ] Configuration persists and loads correctly
- [ ] CLI args work: `--mind-root`, `--scaffold`, `--interactive`, `--reset-config`

---

**Document Status:** Ready for Ian's input on D1-D4. Scope decision (1a/1b split) needs Ian's sign-off.  
**Author:** Q (Lead / Architect)  
**Date:** 2026-03-01  
**Revision:** 2.0 — Reassessed per "Building an Agent with Attitude" guide
