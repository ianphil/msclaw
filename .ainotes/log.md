# AI Notes — Log

## 2026-03-01
- bootstrap: Consolidated bootstrap-as-conversation.md into Templates/bootstrap.md (3 phases: Identity, Agent File, Memory — dropped phases 4-6)
- bootstrap: Vendored OpenClaw SOUL.md template at pinned commit 0f72000c into Templates/SOUL.md (YAML frontmatter stripped)
- bootstrap: Renamed bootstrap-plan.md → bootstrap-spec.md — it's a spec, not a plan
- architecture: bootstrap.md in scaffolded mind drives the LLM conversation; agent deletes it when done
- architecture: IdentityLoader needs upgrade to compose SOUL.md + .github/agents/*.agent.md into system message
- convention: `.working-memory/` is the canonical memory directory name for MsClaw minds
- documentation: Created comprehensive code walkthrough (docs/msclaw-walkthrough.md) using Showboat with real verified code snippets covering all 12 major components (boot, discovery, validation, loading, sessions, runtime client, scaffolding, persistence, models, orchestration, request flow, architecture summary)

## 2026-03-02
- cleanup: Merged claude/code-review-sqMq7 branch — removed all hardcoded miss-moneypenny refs from discovery, defaults, config, and README
- architecture: BootstrapOrchestrator now silently skips unknown CLI args to avoid breaking ASP.NET Core host flags (--urls, --environment, etc.)
- architecture: MindValidator early-returns when root directory is missing — prevents cascading errors for SOUL.md/.working-memory
- architecture: Program.cs reuses bootstrap-time instances as DI singletons instead of creating duplicates
- tooling: `showboat verify` detects stale code blocks in walkthrough docs — used it to find and fix 5 blocks after the code review merge
