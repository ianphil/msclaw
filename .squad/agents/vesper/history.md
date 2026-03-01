# Project Context

- **Owner:** Ian Philpot
- **Project:** MsClaw — a .NET agent framework that hosts AI agents with personality (SOUL.md), working memory, and modular IDEA-based knowledge structure. MVP is complete.
- **Stack:** .NET 9, C#, ASP.NET Core, Azure OpenAI
- **Created:** 2026-03-01

## Key Files

- `src/MsClaw/` — main project
- `.aidocs/roadmap.md` — Phase 2 (Extension System) and Phase 3 (Gateway & Channels) are my domain

## Current Work: Phase 1 Bootstrap

**Q wrote comprehensive Phase 1 (Mind Discovery) plan** — 2026-03-01T03:38:30Z

### Your Role in Phase 1
**Task ownership:**
- **T4** (3h): Mind discovery — `IMindDiscovery` impl, convention-based search (., ~/.msclaw/mind, ~/src/miss-moneypenny, cached)
- **T6** (6h): Bootstrap orchestrator — `IBootstrapOrchestrator` impl, full flow coordination
- **T7** (3h): Program.cs integration — blocking bootstrap before Kestrel startup
- **T8** (4h paired): E2E testing — all success criteria from roadmap

**Blocking:** Waiting for Ian's D1-D4 decisions before implementation. Once decisions made, Vesper starts T4 (once Felix finishes T3 Mind Validator).

**Dependency graph:**
- T1-T5 (Felix) → T6 (Vesper)
- T6 → T7 (Program.cs)
- T7 → T8 (Pair: E2E)

**Key constraint:** D3 decision tree is critical for T6 — explicit → cached → discovery priority.

See `.aidocs/bootstrap-plan.md` for full decomposition.

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

