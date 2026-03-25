---
title: "Genesis — Mind Starter Template Repository"
status: open
priority: high
created: 2026-03-10
related: 20260310-cron-copilot-extension.md
---

# Genesis — Mind Starter Template Repository

## Summary

Create a GitHub template repository called `genesis` that provides a batteries-included mind starter kit. A user clones it, runs `copilot`, answers two questions (character + role), and walks away with a fully configured personal AI agent — complete with personality, working memory, skills, and the cron scheduling extension pre-installed.

## Motivation

Today, bootstrapping a mind requires either running a multi-step PowerShell command or knowing the MsClaw scaffolding internals. The genesis template reduces this to:

```
git clone https://github.com/ianphil/genesis ~/src/my-agent
cd ~/src/my-agent
copilot
```

The template ships everything the agent needs to operate — directory structure, memory files, skills, extensions, and the genesis conversation flow. The only missing pieces are the personal parts: SOUL.md (personality) and the agent file (role). Genesis creates those through conversation on first launch.

This is an agent distribution mechanism. Every mind built from genesis inherits the same infrastructure. Extensions like cron become standard capabilities, not optional add-ons.

## What Ships in the Template

```
genesis/
├── .github/
│   ├── copilot-instructions.md       ← genesis file (temporary, replaced during bootstrap)
│   ├── agents/                       ← empty, created during genesis conversation
│   ├── skills/
│   │   ├── commit/SKILL.md           ← git commit skill
│   │   ├── capture/SKILL.md          ← quick-capture to inbox skill
│   │   └── daily-report/SKILL.md     ← daily summary skill
│   └── extensions/
│       └── cron/                     ← pre-built cron scheduling extension
│           ├── extension.mjs
│           ├── package.json
│           ├── node_modules/         ← vendored (croner)
│           ├── tools/
│           │   ├── crud.mjs
│           │   ├── lifecycle.mjs
│           │   └── engine-control.mjs
│           ├── engine/
│           │   └── main.mjs
│           └── lib/
│               ├── store.mjs
│               ├── identity.mjs
│               ├── scheduler.mjs
│               ├── lifecycle.mjs
│               ├── executor.mjs
│               ├── prompt-executor.mjs
│               ├── errors.mjs
│               ├── history.mjs
│               └── stagger.mjs
├── .working-memory/
│   ├── memory.md                     ← template header, seeded during genesis
│   ├── rules.md                      ← template header
│   └── log.md                        ← template header, first entry added during genesis
├── domains/                          ← empty
├── initiatives/                      ← empty
├── expertise/                        ← empty
├── inbox/                            ← empty
├── Archive/                          ← empty (capital A)
├── .gitignore                        ← includes .github/extensions/cron/data/
├── LICENSE
└── README.md                         ← explains what this is, links to MsClaw docs
```

### What's NOT in the template

- `SOUL.md` — created during genesis conversation (personality)
- `.github/agents/*.agent.md` — created during genesis conversation (role)
- `bootstrap.md` — the genesis instructions live in `.github/copilot-instructions.md` instead, so Copilot reads them automatically on launch

## The Genesis Conversation Flow

The genesis file (`.github/copilot-instructions.md`) drives a 5-phase interactive bootstrap:

### Phase 1: Identity — Two Questions

1. **Character selection** — *"Pick a fictional character whose voice and personality you want this agent to embody."* Examples: Jarvis, Alfred, Wednesday, Cortana, TARS. This sets the tone for SOUL.md.
2. **Role definition** — *"What should this agent do?"* Examples: Chief of Staff, PM, Research Assistant, Writer, Life Manager. This determines the agent file content.

### Phase 2: Generate SOUL.md

- Research the character's voice, mannerisms, and philosophy
- Write SOUL.md with: personality, mission, core truths, boundaries, vibe, continuity section
- Confirm with operator before writing

### Phase 3: Generate Agent File

- Create `.github/agents/{agent-name}.agent.md` with YAML frontmatter
- Include: role description, workflow method, operational principles, memory strategy, retrieval tactics, session handover protocol
- Opening line MUST state: *"Read SOUL.md at the repository root. That is your personality, your voice, your character."*

### Phase 4: Seed Working Memory

- **memory.md** — Architecture overview (IDEA method, three-file memory, extensions available), user context (~30 lines)
- **rules.md** — Header only (rules are learned through experience, not pre-loaded)
- **log.md** — First entry: records the bootstrap event (character chosen, role defined, files generated)

### Phase 5: Cleanup and Handoff

- Replace `.github/copilot-instructions.md` with the permanent version (IDEA taxonomy, session start sequence, memory file purposes, skills guide, extension reference)
- The genesis file is gone — the mind is live 🟢
- Log the bootstrap completion in `log.md`

## Git Remote and Ownership

After clone, `origin` points to the genesis template repo. The bootstrap conversation should handle this:

1. Detect that origin points to the template repo
2. Ask: *"Where should this mind live? Give me a GitHub username or org."*
3. Use `gh repo create <user>/my-agent --private --source .` to create a new repo
4. Update origin to point to the user's new repo
5. Initial commit with the bootstrapped mind
6. Push

If `gh` CLI isn't available, provide manual instructions. The user owns their mind from the start.

Alternatively, the repo can be configured as a **GitHub template repository**. Users click "Use this template" on GitHub, get a fresh repo in their account, then clone. No remote dance needed. Both paths should work.

## Vendoring Strategy

### node_modules (croner)

Vendor `node_modules/` in the template. Rationale:
- Single small dependency (~50KB)
- Zero-friction first experience — no post-clone `npm install` step
- The cron extension must work immediately when Copilot discovers it
- Trade-off: slightly larger repo, manual updates. Acceptable for v1.

### Skills

Skills are markdown files (SKILL.md) — no dependencies, no vendoring needed. They ship as-is.

## .gitignore

```gitignore
# Extension runtime data (not version-controlled)
.github/extensions/cron/data/

# OS
.DS_Store
Thumbs.db

# IDE
.vscode/
.idea/
*.swp
```

Working memory IS version-controlled — it's part of the mind's persistent state. IDEA directories are version-controlled — they're the mind's knowledge base.

## Permanent copilot-instructions.md

After genesis completes, the instructions file is replaced with the permanent version containing:

- **IDEA taxonomy reference** — what goes where (initiatives, domains, expertise, archive)
- **Session start sequence** — read memory.md, read rules.md, orient
- **Memory file purposes** — memory.md (curated reference), rules.md (one-liner lessons), log.md (append-only journal)
- **Skills guide** — available skills and how to invoke them
- **Extension reference** — cron extension capabilities, tool names
- **Knowledge vs observation boundary** — IDEA directories for shared knowledge, .working-memory for agent observations

## Relationship to MsClaw

Genesis is **independent of MsClaw** as a .NET project. It doesn't require the MsClaw library, Gateway, or any .NET tooling. It uses:

- The Copilot CLI (installed separately)
- The `@github/copilot-sdk` (auto-resolved by the CLI for extensions)
- Node.js (for the cron extension engine)

MsClaw's embedded templates (`src/MsClaw.Core/Mind/Templates/`) are the canonical reference for file content, but genesis is a standalone repo. The two may diverge — genesis is the user-facing distribution, MsClaw is the developer library.

If MsClaw's `MindScaffold` is ever updated, the genesis template should be updated to match. But there's no runtime dependency.

## Open Questions

- Should genesis include additional extensions beyond cron? (e.g., a web research extension, a file watcher)
- Should the permanent `copilot-instructions.md` reference the cron extension tools by name, or keep it generic?
- Should the README explain the mind concept for newcomers, or link to external docs?
- Should genesis support multiple agent files (e.g., a primary agent + a code review agent), or start with just one?
- How to handle template updates — should there be a `genesis update` skill that pulls latest extension code from the template repo?

## Tasks

### Phase 1: Repository Setup
- [ ] Create `ianphil/genesis` repo on GitHub, mark as template repository
- [ ] Set up directory structure (IDEA dirs, .working-memory, .github/agents, .github/skills)
- [ ] Create .gitignore with extension data exclusions
- [ ] Write README.md explaining what genesis is and the two-command setup
- [ ] Add LICENSE

### Phase 2: Genesis Conversation File
- [ ] Write `.github/copilot-instructions.md` (genesis file) with the 5-phase bootstrap flow
- [ ] Include character examples and role examples for guided selection
- [ ] Include SOUL.md writing instructions with required sections (personality, mission, core truths, boundaries, vibe)
- [ ] Include agent file writing instructions with YAML frontmatter template and required sections
- [ ] Include working memory seeding instructions
- [ ] Include self-replacement instructions (genesis → permanent instructions)

### Phase 3: Permanent Instructions
- [ ] Write the permanent `copilot-instructions.md` that replaces genesis after bootstrap
- [ ] Include IDEA taxonomy reference table
- [ ] Include session start sequence
- [ ] Include memory file documentation
- [ ] Include skills reference
- [ ] Include cron extension reference (tool names, capabilities)

### Phase 4: Skills
- [ ] Port commit skill (SKILL.md) from MsClaw templates
- [ ] Port capture skill (SKILL.md) from MsClaw templates
- [ ] Port daily-report skill (SKILL.md) from MsClaw templates
- [ ] Verify skills work in standalone mind (no MsClaw dependency)

### Phase 5: Cron Extension
- [ ] Build cron extension per the cron extension plan (20260310-cron-copilot-extension.md)
- [ ] Vendor node_modules (croner) into the extension directory
- [ ] Verify extension loads and tools register when Copilot starts in the genesis repo
- [ ] Test prompt job execution with mind identity (SOUL.md + agent files → system message)

### Phase 6: Git Remote Handling
- [ ] Add remote detection to genesis conversation (check if origin points to template)
- [ ] Add `gh repo create` flow for automatic repo setup
- [ ] Add manual fallback instructions for users without `gh` CLI
- [ ] Test both paths: template repo "Use this template" + direct clone

### Phase 7: End-to-End Validation
- [ ] Fresh clone → `copilot` → complete genesis conversation → verify SOUL.md created
- [ ] Verify agent file created with correct frontmatter and opening line
- [ ] Verify working memory seeded correctly
- [ ] Verify genesis file replaced with permanent instructions
- [ ] Verify cron extension loads and `cron_create` works post-bootstrap
- [ ] Verify prompt job uses the newly created mind personality
- [ ] Test on Windows and macOS
