# Bootstrap Plan — Phase 1: Mind Discovery

**Document:** Clean comprehensive bootstrap plan for Phase 1  
**Owner:** Q (Lead / Architect)  
**Audience:** Ian Philpot, Felix (backend dev), Vesper (systems dev)  
**Date:** 2026-03-01

---

## Overview

Phase 1 transforms MsClaw from a hardwired instance into a framework. Today, `MIND_ROOT` is set via `appsettings.json`, pointing to Ian's local `miss-moneypenny` directory. No first-run experience, no validation beyond "file not found," and no scaffolding.

**Phase 1 goal:** Anyone can `dotnet run` MsClaw on their machine and get a working agent — detection of missing configuration, validation of mind structure, scaffolding of starter templates, and persistence of resolved choices.

**Why this matters:** This is the seam between "instance" and "framework." Without proper bootstrap, MsClaw is tied to one person and one setup. With it, it becomes composable, deployable, and extensible.

---

## Current State

### What Exists
- `MindReader` service reads files from configured path
- `MsClawOptions` bound from `appsettings.json`
- Basic path traversal protection
- Hardcoded mind root: `"../miss-moneypenny"`

### What's Missing
1. **First-run detection** — no way to detect missing/invalid configuration
2. **Mind validation** — no check for required structure (SOUL.md, .working-memory/, IDEA folders)
3. **Convention-based discovery** — no fallback locations (current dir, ~/.msclaw/mind, development conventions)
4. **Scaffolding** — no way to generate starter structure for new agents
5. **Configuration persistence** — no mechanism to save resolved mind root
6. **Bootstrap orchestration** — no coordinator for the full "detect → configure → validate → persist → start" flow
7. **CLI argument support** — no `--mind-root` or `--scaffold` flags
8. **Interactive prompts** — no user interaction during bootstrap

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

**Responsibilities:**
- Verify required structure exists: `SOUL.md`, `.working-memory/`
- Report optional structure: IDEA folders (domains/, initiatives/, expertise/, inbox/, Archive/)
- Return structured result with errors, warnings, and discovered structure

**Key contract:**
```
Validate(mindRoot: string) → MindValidationResult
  - IsValid: bool
  - Errors: List<string>       // Blocking issues (SOUL.md missing, etc.)
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
- Create directory structure (SOUL.md, .working-memory/, IDEA folders, Archive/)
- Embed SOUL.md template (from OpenClaw reference, verbatim)
- Handle errors (existing directory, permission issues)
- Validate scaffold result before reporting success

**Generated structure:**
```
{mindRoot}/
  SOUL.md                  ← Template from OpenClaw reference (verbatim)
  .working-memory/
    memory.md              ← Empty placeholder
    rules.md               ← Empty placeholder
    log.md                 ← Empty placeholder
  domains/
  initiatives/
  expertise/
  inbox/
  Archive/
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

**Key contract:**
```
RunAsync(args: string[], cancellationToken: CancellationToken) → BootstrapResult
  - Success: bool
  - MindRoot?: string      // Resolved absolute path
  - Error?: string
  - Mode: BootstrapMode    // Discovered, Scaffolded, Configured, Cached
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
- Errors: missing SOUL.md, missing .working-memory/
- Warnings: empty SOUL.md, no IDEA folders found
- Discovers which IDEA folders exist (domains/, initiatives/, expertise/, inbox/, Archive/)
- Returns `MindValidationResult` with structured findings

**Tests:**
- Valid mind → IsValid = true, no errors
- Missing SOUL.md → IsValid = false, error reported
- Missing .working-memory/ → IsValid = false, error reported
- Empty SOUL.md → IsValid = true, warning reported
- No IDEA folders → IsValid = true, warning reported

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
- Creates directories and empty files (.working-memory/memory.md, etc.)
- Validates scaffold result through validator before returning success
- Handles existing directory (error), permission issues (error), missing parent directory (create or error)

**Tests:**
- Scaffold into empty directory → success, all files created
- Scaffold into existing directory → error
- Validate scaffolded structure → passes validator (SOUL.md, .working-memory/ present)
- AgentName substitution works correctly

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

**Flow implementation:**
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

## Open Questions (Deferred to Phase 2+)

These are intentionally **not** part of Phase 1:

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

1. **Ian decides D1-D4** — required before implementation starts
2. **Q reviews decisions with team** → move decomposition to `.squad/decisions/` once approved
3. **Felix starts T1, T2** — in parallel
4. **Vesper starts T4 discovery** — once T3 (validator) is ready for integration
5. **Daily standups** — track progress, adjust estimates
6. **Pair on T8** — E2E testing validates entire flow

---

## Success Checklist

- [ ] All 4 roadmap success criteria pass (fresh install, explicit path, scaffold, cached)
- [ ] No hardcoded paths in `appsettings.json` (ships with `MindRoot: null` or missing)
- [ ] `README.md` includes bootstrap instructions for new users
- [ ] Felix and Vesper can run MsClaw on their machines without Ian's hands-on help
- [ ] SOUL.md template is OpenClaw reference (verbatim, per directive)
- [ ] Validation errors are structured (errors/warnings/found structure)
- [ ] Configuration persists and loads correctly
- [ ] CLI args work: `--mind-root`, `--scaffold`, `--interactive`, `--reset-config`

---

**Document Status:** Ready for Ian's input on D1-D4.  
**Author:** Q (Lead / Architect)  
**Date:** 2026-03-01  
**Revision:** 1.0
