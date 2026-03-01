# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Architecture, design decisions, code review | Q | System design, API contracts, extension interfaces, PR reviews |
| Core .NET implementation, services, APIs | Felix | Bootstrap flow, mind validation, configuration, DI wiring |
| Extension system, plugin architecture, discovery | Vesper | IExtension, plugin loader, hook system, channel adapters |
| Tests, quality, edge cases | Natalya | Unit tests, integration tests, validation, coverage |
| Scope & priorities | Q | What to build next, trade-offs, roadmap decisions |
| Session logging | Scribe | Automatic — never needs routing |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | Q |
| `squad:q` | Architecture/design work | Q |
| `squad:felix` | Implementation work | Felix |
| `squad:vesper` | Extension/plugin work | Vesper |
| `squad:natalya` | Testing work | Natalya |

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what branch are we on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** If a feature is being built, spawn Natalya to write test cases simultaneously.
