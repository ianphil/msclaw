# Bootstrap Plan — Phase 1: Mind Discovery

**Revision:** 4.0 (2026-03-01)

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
1. Scaffolds directory structure from templates (SOUL.md, `.working-memory/`, IDEA folders)
2. Drops a `bootstrap.md` file into the mind — this is the bootstrap marker
3. Serves the API

When the first `/chat` request arrives and `bootstrap.md` exists, the agent enters bootstrap mode — a multi-turn conversation that walks through identity creation (from the "Building an Agent with Attitude" guide). The agent customizes its own SOUL.md, seeds `.working-memory/`, and builds its personality through conversation.

Once bootstrap completes, `bootstrap.md` is removed. The mind is live.

### Subsequent Runs
```
msclaw
```
Uses cached mind root from last successful run. No flags needed.

---

## What Gets Scaffolded

```
{mindRoot}/
  SOUL.md              ← OpenClaw template (verbatim)
  bootstrap.md         ← Bootstrap marker + instructions for the agent
  .working-memory/
    memory.md          ← Curated long-term memory
    rules.md           ← Mistake journal
    log.md             ← Raw chronological observations
  domains/
  initiatives/
  expertise/
  inbox/
  Archive/
```

The SOUL.md template comes from the [OpenClaw reference](https://raw.githubusercontent.com/openclaw/openclaw/0f72000c96deaf385fc217811f29166ec8f2d815/docs/reference/templates/SOUL.md) verbatim.

---

## Bootstrap Mode (LLM-based UX)

When `/chat` is called and `bootstrap.md` exists in the mind root, the agent knows it's unbootstrapped. The `bootstrap.md` file contains the walkthrough instructions — the agent uses them to drive a multi-turn conversation:

1. **Identity** — Name, personality, mission, boundaries, tone → writes SOUL.md
2. **Memory** — Seeds `.working-memory/` files with initial context
3. **Agent File** — Creates `.github/agents/{name}.agent.md` in the host repo
4. **Retrieval** — Configures search tools and rules
5. **First Skill** — Creates `.github/skills/{name}/SKILL.md` in the host repo
6. **Knowledge** — Organizes IDEA folders

The user chats naturally. The agent drives the structure. When done, `bootstrap.md` is deleted and the mind is fully operational.

---

## Server Behavior

- `--mind <path>` — validate, persist, serve
- `--new-mind <path>` — create new mind, persist, serve (bootstrap.md present until first chat completes it)
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
- Config persistence (`~/.msclaw/config.json` — remember last mind root)
- Bootstrap orchestrator (coordinates the detect → validate → create → persist → serve flow)

**Bootstrap detection (in the API):**
- On `/chat`: check for `bootstrap.md` in mind root
- If present: agent enters bootstrap mode, uses `bootstrap.md` as instructions
- On bootstrap completion: agent deletes `bootstrap.md`

---

## Open Decisions

**D1: Failure behavior** — If no mind found, exit with error (recommended) or enter degraded mode?

**D2: Config location** — `~/.msclaw/config.json` (user-global) or `{project}/.msclaw.local.json` (project-local)?

**D3: Discovery priority** — Explicit → cached → convention-based discovery (recommended).

**D4: Template storage** — Embedded resource in assembly (recommended) or fetch from URL at creation time?
