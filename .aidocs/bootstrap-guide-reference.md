# Building an Agent with Attitude

> **Source:** Ian Philpot's reference guide for the mind bootstrap experience.
> **Captured:** 2026-03-01 — saved locally per Q's bootstrap reassessment.
> **Original:** `https://raw.githubusercontent.com/ianphil/public-notes/refs/heads/main/expertise/agent-craft/building-an-agent-with-attitude.md`

A bootstrap guide for GitHub Copilot. Give this document to an agent and it will walk you through setting up a persistent, personable AI assistant — complete with identity, memory, retrieval, and operational skills.

## The Six Phases:

### Phase 1: Identity — SOUL.md
- Start from OpenClaw template (already captured as directive)
- Strip YAML frontmatter
- Ask 5 questions interactively: Name, Personality, Mission, Boundaries, Tone calibration
- Customize template based on answers
- Read back and confirm

### Phase 2: Operating Instructions — The Agent File
- `.github/agents/{agent-name}.agent.md` with frontmatter
- Ask: Role, Domain context, Tools/integrations
- References SOUL.md, .ainotes/ memory system
- Operational principles, memory section, retrieval, session discipline, handover

### Phase 3: Memory System — .ainotes/
- `.ainotes/memory.md` — curated long-term (read every session, only updated during consolidation)
- `.ainotes/rules.md` — mistake journal, one-liners that compound
- `.ainotes/log.md` — raw chronological observations, append-only
- Consolidation every ~14 days

### Phase 4: Retrieval & Search
- Search tools configuration
- "Always check before acting" rules

### Phase 5: First Skill
- `.github/skills/{skill-name}/SKILL.md`
- Reusable workflow in markdown

### Phase 6: Knowledge Structure
- Folder structure: domains/, initiatives/, expertise/, inbox/, Archive/
- This IS the IDEA structure from the roadmap
