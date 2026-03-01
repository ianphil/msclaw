# AI Notes — Log

## 2026-03-01
- bootstrap: Consolidated bootstrap-as-conversation.md into Templates/bootstrap.md (3 phases: Identity, Agent File, Memory — dropped phases 4-6)
- bootstrap: Vendored OpenClaw SOUL.md template at pinned commit 0f72000c into Templates/SOUL.md (YAML frontmatter stripped)
- bootstrap: Renamed bootstrap-plan.md → bootstrap-spec.md — it's a spec, not a plan
- architecture: bootstrap.md in scaffolded mind drives the LLM conversation; agent deletes it when done
- architecture: IdentityLoader needs upgrade to compose SOUL.md + .github/agents/*.agent.md into system message
- convention: `.working-memory/` is the canonical memory directory name for MsClaw minds
