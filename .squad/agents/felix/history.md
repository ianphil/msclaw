# Project Context

- **Owner:** Ian Philpot
- **Project:** MsClaw — a .NET agent framework that hosts AI agents with personality (SOUL.md), working memory, and modular IDEA-based knowledge structure. MVP is complete.
- **Stack:** .NET 9, C#, ASP.NET Core, Azure OpenAI
- **Created:** 2026-03-01

## Key Files

- `src/MsClaw/` — main project
- `src/MsClaw/Program.cs` — startup and DI
- `src/MsClaw/Core/` — core services
- `src/MsClaw/Models/` — data models

## Current Work: Phase 1 Bootstrap

**Q wrote comprehensive Phase 1 (Mind Discovery) plan** — 2026-03-01T03:38:30Z

### Your Role in Phase 1
**Task ownership:**
- **T1** (2h): CLI argument parsing — `--mind-root`, `--scaffold`, `--interactive`, `--reset-config`
- **T2** (3h): Configuration persistence — `IConfigurationPersistence` impl, `~/.msclaw/config.json`
- **T3** (4h): Mind validator — `IMindValidator` impl, structure checks (SOUL.md, .working-memory/)
- **T5** (4h): Mind scaffold — `IMindScaffold` impl, OpenClaw SOUL.md template verbatim
- **T8** (4h paired): E2E testing — all success criteria from roadmap

**Blocking:** Waiting for Ian's D1-D4 decisions before implementation. Once decisions made, Felix starts T1 & T2 in parallel.

**Dependency:** T1-T5 feed into T6 (Vesper's Bootstrap Orchestrator). All tests validate via IMindValidator before success.

**Key directive:** SOUL.md scaffold template must use OpenClaw reference `https://raw.githubusercontent.com/openclaw/openclaw/0f72000c96deaf385fc217811f29166ec8f2d815/docs/reference/templates/SOUL.md` — verbatim, no modifications.

See `.aidocs/bootstrap-plan.md` for full decomposition.

## Learnings

- **`.working-memory/` is the canonical memory directory name** — MsClaw roadmap (`.aidocs/roadmap.md`) uses `.working-memory/` as authoritative. The "Building an Agent with Attitude" guide uses `.ainotes/` for its own pattern, but MsClaw's naming is the source of truth. Memory file semantics (memory.md, rules.md, log.md) still apply per guide, just in `.working-memory/`. **Validator (T3)** checks .working-memory/ as required directory. **Scaffold (T5)** creates .working-memory/ with all three files purpose-seeded. Bootstrap plan updated to Rev 2.1.

- **Mind ≠ Host Repo** — Mind is SOUL.md + .working-memory/ + IDEA folders (domains/, initiatives/, expertise/, inbox/, Archive/). Host repo is .github/agents/{name}.agent.md + .github/skills/{name}/SKILL.md. Phase 1a creates mind only; Phase 1b (future) adds host repo awareness. **Impact for T5:** Don't scaffold .github/ directories — those are host repo concerns added later.

- **Phase 1a/1b split approved (D5)** — Ship Phase 1a (automated infrastructure, all 8 tasks) first, then Phase 1b (interactive 6-phase guide walkthrough). Phase 1a doesn't need --guided flag yet; T6 orchestrator should be extensible enough to add it without rewrite.

<!-- Append new learnings below. Each entry is something lasting about the project. -->

- **Session management refactor (2026-03-02)** — Implemented SDK-native session management: deleted SessionManager, ISessionManager, SessionState, SessionMessage; rewrote ICopilotRuntimeClient and CopilotRuntimeClient with CreateSessionAsync/SendMessageAsync pattern; modified ChatRequest, MsClawOptions, Program.cs endpoints. Registered CopilotClient singleton with InfiniteSessions enabled. Build: 0 errors, 0 warnings.

- **Core bootstrap service behavior landed (T3/T4/T6/T7)** — Implemented synchronous `MindValidator` structure checks with error/warning/found classification, user-global config persistence at `~/.msclaw/config.json`, embedded-template scaffold creation for new minds, and identity composition (`SOUL.md` + `.github/agents/*.agent.md`) with YAML frontmatter stripping.

- **Session management refactor completed (2026-03-02)** — Removed manual session persistence (`SessionManager`, `SessionState`, `SessionMessage`) and replaced with SDK-native session handling. `CopilotClient` is now a singleton registered in DI. `CopilotRuntimeClient` uses `CreateSessionAsync` / `ResumeSessionAsync` / `SendAndWaitAsync` pattern. Enabled `InfiniteSessions` for automatic context compaction. The SDK owns conversation state; HTTP layer only routes session IDs. Deleted 4 files, rewrote 2 interfaces/implementations, modified 3 models. Build succeeds with zero warnings.

- **Phase 1 review hardening (2026-03-02)** — `CopilotRuntimeClient` now caches `CopilotSession` instances in a `ConcurrentDictionary` and reuses them per `sessionId` instead of calling `ResumeSessionAsync` per message, preventing SDK `_sessions` growth in long-running hosts. `ICopilotRuntimeClient` no longer extends `IAsyncDisposable` (and no longer has a no-op `DisposeAsync`), aligning contract with actual ownership/lifecycle in DI. `ConfigPersistence.Load` now treats malformed `~/.msclaw/config.json` as recoverable by catching `JsonException` and returning `null` so bootstrap discovery can fall through.

- **Phase 1 review fix coverage (2026-03-02T01:51Z)** — Natalya added 5 comprehensive tests to validate Felix's fixes: 3 config corruption tests (null bytes, invalid UTF-8, truncated JSON), 1 interface contract test (no IAsyncDisposable on ICopilotRuntimeClient), 1 integration scope test (documents session caching constraint). All 56 tests pass (51 existing + 5 new). Build: 0 errors, 6.4s.

