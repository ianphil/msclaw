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

<!-- Append new learnings below. Each entry is something lasting about the project. -->

