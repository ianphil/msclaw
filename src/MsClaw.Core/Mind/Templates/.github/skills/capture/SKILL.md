---
description: Capture and normalize context into the mind. Use when the user shares information that needs to be stored — context dumps, 1:1 debriefs, decisions, status updates, "remember this". Triggers on capture, note this, remember, brain dump, debrief, 1:1 notes.
name: capture
---

# Capture — Mind Normalization

The mind is a normalized database. Your job is to decompose what the user said, route each piece to its canonical home, link everything together, and log your observations last.

## Workflow

### Step 1: Decompose

Parse the user's input into discrete items. A single message often contains multiple types:

> "Alex needs oncall fixed, Sam is doing well on testing, and we should create an ADO backlog for the partner program"

That's three items:
1. Task (Alex oncall) → person check-in
2. Person context (Sam testing) → person note
3. Initiative task (partner backlog) → initiative next-actions

List the items explicitly before writing anything. If there are more than 3, show the user the decomposition and confirm before proceeding.

### Step 1b: Clarify

Before routing, ask about anything ambiguous. Common things to check:

- **Decision or thought?** "I'm thinking about moving Sam to..." — is that decided, or still being explored? Decisions get committed to notes. Thoughts might just need a log entry.
- **Which entity?** "We should track this" — in which initiative? Which person's check-in?
- **Scope** — "Alex needs work" — is that a next-action for the user to assign, or should I look at the backlog and suggest something?
- **Missing context** — if a new person, team, or initiative is mentioned that doesn't exist in the mind, ask before creating.

Keep it tight — one or two focused questions, not an interrogation. If the answer is obvious from context, don't ask. The goal is to prevent misrouting, not slow things down.

### Step 2: Search

For each item, check what already exists:

```
qmd search "{person name or topic}"
```

Also check `mind-index.md` for related notes. The goal: **update existing notes, don't create duplicates.**

### Step 3: Route

Use the placement map to determine where each item goes:

| Type | Destination | What to Write |
|------|-------------|---------------|
| Person context | `domains/people/{name}/{name}.md` | Update relevant section (workstream, working style, check-ins) |
| Team dynamics | `domains/{team}/{team}.md` + people notes | Update topology, cross-team patterns, link both directions |
| Initiative update | `initiatives/{name}/{name}.md` | Update status, ownership, scope; also update `next-actions.md` if tasks |
| Technical pattern | `domains/{team}/` or `expertise/` | Create or update pattern note, link to all people involved |
| Task / action | Person check-ins or initiative `next-actions.md` | `- [ ]` item with enough context to execute without asking |
| Decision | The note it affects | Update the note directly; log entry captures the *why* |

### Step 4: Link

After placing each item, wire the connections:

- Add `[[wiki-links]]` in every note that references another entity
- People → initiatives they work on
- Initiatives → people who own them
- Domain notes → both people and initiatives
- If a new note was created, suggest adding it to `mind-index.md`

### Step 5: Log

**After** the mind is updated, write a single log entry to `.working-memory/log.md` capturing:
- What you observed about the session (energy, patterns, connections)
- NOT the knowledge itself — that's already in the mind

Bad log entry: "Alex needs oncall fixed, Sam interested in testing"
Good log entry: "Post-1:1 context dump — user in strategic mode, connecting team dynamics across testing, oncall, and partner program. Properly normalized across 6 people notes and 2 initiatives."

## Rules

- **Never skip Step 2.** Searching first prevents duplicates and surfaces context you'd otherwise miss.
- **Never put knowledge in log.md.** If it's about a person, it goes in their note. If it's about an initiative, it goes there. Log.md is for your observations only.
- **When in doubt about placement, ask.** "Should this go in Alex's note or in the testing strategy?" is a better question than guessing wrong.
- **Show your work on large captures.** If the user dumps 5+ items, show the decomposition before writing. This catches misunderstandings early.
- **Link aggressively.** A note without links is a dead end. Every entity mentioned should be a wiki-link.
