# Bootstrap Spec — Phase 1: Mind Discovery

**Revision:** 5.0 (2026-03-01)

---

## The Workflow

Two paths in, one bootstrap experience:

### Path A: Existing Mind
```
msclaw --mind ~/path/to/mind
```
Validates the mind structure, serves the API. Done.

### Path B: New Mind
```
msclaw --new-mind /path/to/new/mind
```
1. Scaffolds directory structure (SOUL.md template, `.working-memory/`, `.github/`, IDEA folders)
2. Drops a `bootstrap.md` file into the mind — this is the bootstrap marker
3. Serves the API

When the first `/chat` request arrives and `bootstrap.md` exists, the agent enters bootstrap mode — a multi-turn conversation that gives the agent its identity. The conversation is driven by the instructions in `bootstrap.md`, defined in `src/MsClaw/Templates/bootstrap.md`.

Once bootstrap completes, `bootstrap.md` is removed. The mind is live.

### Subsequent Runs
```
msclaw
```
Uses cached mind root from last successful run. No flags needed.

---

## What Gets Scaffolded

Scaffold creates the **structure**. The bootstrap conversation fills it with **identity and content**.

```
{mindRoot}/
  SOUL.md              ← OpenClaw template (verbatim, customized during bootstrap)
  bootstrap.md         ← Bootstrap marker + conversation instructions for the agent
  .working-memory/
    memory.md          ← Curated long-term memory (empty starter)
    rules.md           ← Mistake journal (empty starter)
    log.md             ← Raw chronological observations (empty starter)
  .github/
    agents/            ← Empty (agent file created during bootstrap conversation)
    skills/            ← Empty (skills added later through normal usage)
  domains/
  initiatives/
  expertise/
  inbox/
  Archive/
```

### Template Files

Both template files live in `src/MsClaw/Templates/` and are embedded resources in the assembly. No network fetch at runtime.

| File | Source | Purpose |
|------|--------|---------|
| `src/MsClaw/Templates/SOUL.md` | Vendored from [OpenClaw](https://raw.githubusercontent.com/openclaw/openclaw/0f72000c96deaf385fc217811f29166ec8f2d815/docs/reference/templates/SOUL.md) (pinned commit `0f72000c`, YAML frontmatter stripped) | Copied verbatim into `{mindRoot}/SOUL.md` during scaffold. Customized by the agent during bootstrap Phase 1. |
| `src/MsClaw/Templates/bootstrap.md` | 3-phase bootstrap conversation guide (Identity → Agent File → Memory) | Copied into `{mindRoot}/bootstrap.md` during scaffold. The agent reads it to drive the bootstrap conversation, then deletes it when done. |

During `--new-mind` scaffold, the scaffolder copies both files from the embedded resources into the new mind directory. Everything else (empty dirs, starter memory files) is generated in code.

---

## Bootstrap Conversation (3 Phases)

When `/chat` is called and `bootstrap.md` exists in the mind root, the agent enters bootstrap mode. The conversation covers three phases:

### Phase 1: Identity — SOUL.md

The agent asks questions (one at a time) to customize the scaffolded SOUL.md:
- **Name** — what the agent should be called
- **Personality** — voice and style (dry, warm, blunt, playful, or custom)
- **Mission** — one-sentence purpose
- **Boundaries** — hard limits specific to the user's work
- **Tone calibration** — always/never say, pet peeves, style notes

The agent modifies the template based on answers, reads it back for confirmation.

### Phase 2: Agent File — `.github/agents/{name}.agent.md`

The agent asks:
- **Role** — primary job (knowledge management, code review, coordination, etc.)
- **Domain context** — project or area the agent works in
- **Tools and integrations** — what's available (ADO, GitHub, CLIs, etc.)

Generates the agent file with: operating instructions, role description, method, operational principles, memory protocol, retrieval habits, session handover discipline.

### Phase 3: Memory — `.working-memory/`

No questions needed. The agent seeds the three memory files with initial context gathered from the conversation so far:
- `memory.md` — initial context, conventions, active work
- `rules.md` — empty (rules accumulate through usage)
- `log.md` — bootstrap entry with date and summary

After Phase 3, the agent deletes `bootstrap.md`. The mind is live.

**What comes later (not part of bootstrap):** Retrieval configuration, skills, and IDEA folder organization happen organically through normal agent usage.

---

## IdentityLoader Changes

The current `IdentityLoader` reads only `SOUL.md`. Post-bootstrap, the agent also has `.github/agents/{name}.agent.md` with operational instructions.

`IdentityLoader` must compose both into the system message:
1. Read `SOUL.md` → personality and voice
2. Discover and read `.github/agents/*.agent.md` → operational instructions
3. Compose into a single system message sent to the Copilot SDK

---

## Server Behavior

- `--mind <path>` — validate, persist, serve
- `--new-mind <path>` — scaffold mind, persist, serve (bootstrap.md present until bootstrap conversation completes)
- No flags — use cached mind root, or fail with usage instructions
- `--reset-config` — clear cached config
- No `--interactive` or `--guided` flags. The server is non-interactive at startup. Bootstrap is an LLM conversation, not a CLI wizard.

---

## What Needs Building

**Infrastructure (before API starts):**
- CLI argument parsing (`--mind`, `--new-mind`, `--reset-config`)
- Mind validation (check structure completeness)
- Mind discovery (convention-based fallback locations)
- Mind scaffolding (generate structure from templates, include `bootstrap.md`)
- Vendor SOUL.md template into repo as embedded resource
- Config persistence (`~/.msclaw/config.json` — remember last mind root)
- Bootstrap orchestrator (coordinates the detect → validate → create → persist → serve flow)

**IdentityLoader upgrade:**
- Discover and read `.github/agents/*.agent.md` in addition to `SOUL.md`
- Compose both into the system message

**Bootstrap detection (in the API):**
- On `/chat`: check for `bootstrap.md` in mind root
- If present: agent enters bootstrap mode, uses `bootstrap.md` as conversation instructions
- On bootstrap completion: agent deletes `bootstrap.md`

---

## Resolved Decisions

**D4: Template storage** — Embedded resource in assembly. Templates vendored at `src/MsClaw/Templates/`. ✅

---

## Open Decisions

**D1: Failure behavior** — If no mind found, exit with error (recommended) or enter degraded mode?

**D2: Config location** — `~/.msclaw/config.json` (user-global) or `{project}/.msclaw.local.json` (project-local)?

**D3: Discovery priority** — Explicit → cached → convention-based discovery (recommended).
