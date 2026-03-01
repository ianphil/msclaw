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

- **`.working-memory/` is the canonical memory directory name** — MsClaw roadmap (`.aidocs/roadmap.md`) uses `.working-memory/` as authoritative. The "Building an Agent with Attitude" guide uses `.ainotes/` for its own pattern, but MsClaw's naming is the source of truth. Memory file semantics (memory.md, rules.md, log.md) still apply per guide, just in `.working-memory/`. This affects how minds are discovered and validated. **Validator (T3)** checks .working-memory/ as required directory; **discovery (T4)** should understand this structure as the canonical memory location for any mind. Bootstrap plan updated to Rev 2.1.

- **Mind ≠ Host Repo** — Mind is SOUL.md + .working-memory/ + IDEA folders. Host repo is .github/agents/{name}.agent.md + .github/skills/{name}/SKILL.md. Phase 1a creates mind; Phase 1b (future) adds host repo interaction. **Impact for T6:** Orchestrator should design mode system to support future `--guided` flag (Phase 1b) without rewriting. Don't add host repo awareness in Phase 1a.

- **Phase 1a/1b split approved (D5)** — Ship Phase 1a (automated infrastructure, T1–T8) first, Phase 1b (interactive 6-phase guide walkthrough) follows. T6 (Bootstrap Orchestrator) is the seam that enables extensibility for Phase 1b modes. Design for mode extensibility early.

<!-- Append new learnings below. Each entry is something lasting about the project. -->

- **Template scaffolding now depends on embedded resources from `Templates/`** — `MsClaw.csproj` includes `Templates\**\*` as `EmbeddedResource`, and `Core/EmbeddedResources.ReadTemplate(fileName)` resolves resources as `MsClaw.Templates.{fileName}` with a clear exception if missing. This removes runtime filesystem assumptions for template retrieval and gives downstream scaffold/orchestrator work a stable, assembly-backed template access path.

- **Mind discovery order is fixed and validator-gated** — Discovery checks cached config first, then `Directory.GetCurrentDirectory()`, then `~/.msclaw/mind`, then `~/src/miss-moneypenny`; each candidate must exist on disk and pass `IMindValidator.Validate(...).IsValid` before selection.
- **Bootstrap orchestrator exit signaling uses nullable result instead of process termination** — `IBootstrapOrchestrator.Run` now returns `BootstrapResult?`, and `--reset-config` returns `null` after clearing config so `Program.cs` can own process exit behavior; this keeps orchestration testable while preserving fail-fast semantics for invalid/bootstrap-missing states.
- **Startup now resolves `MindRoot` before DI build and runtime composes identity via `IIdentityLoader`** — `Program.cs` runs `BootstrapOrchestrator` pre-`builder.Build()`, binds config first, then overrides only `MsClawOptions.MindRoot` from bootstrap result; `CopilotRuntimeClient` now loads system message through `IIdentityLoader` and prepends `bootstrap.md` when present to signal bootstrap mode in `/chat`.
