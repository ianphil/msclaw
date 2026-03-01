# Project Context

- **Owner:** Ian Philpot
- **Project:** MsClaw — a .NET agent framework that hosts AI agents with personality (SOUL.md), working memory, and modular IDEA-based knowledge structure. MVP is complete.
- **Stack:** .NET 9, C#, ASP.NET Core, Azure OpenAI
- **Created:** 2026-03-01

## Key Files

- `src/MsClaw/` — main project
- Need to discover existing test projects and coverage

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->
- 2026-03-01: Test infrastructure now includes `tests/MsClaw.Tests` with xUnit + NSubstitute on .NET 9 and a solution entry in `MsClaw.sln`.
- 2026-03-01: `TempMindFixture` is the shared helper for creating disposable mind directory structures (`valid`, `minimal`, `empty`) under temp paths for filesystem-focused tests.
