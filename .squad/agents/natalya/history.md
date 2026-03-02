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
- 2026-03-01: T11 unit tests now cover MindValidator, ConfigPersistence, MindDiscovery, MindScaffold, and IdentityLoader contract behavior including filesystem edge cases and frontmatter composition.
- 2026-03-01: Added T12 integration coverage for real BootstrapOrchestrator and IdentityLoader with filesystem-backed scenarios, including config reset, invalid args, and frontmatter stripping; full suite now passes 33/33.
- 2026-03-02: Session management refactor testing complete — added ChatRequestTests (6 tests) and MsClawOptionsTests (7 tests) to validate model changes. Total test suite now at 47 passing tests.
- 2026-03-02: CopilotRuntimeClient SDK integration layer cannot be unit tested due to sealed CopilotClient class and process-based state. Documented testing boundary in CopilotRuntimeClientIntegrationScopeTests.cs — integration tests with real CLI are needed for that layer.
- 2026-03-02: SessionStore property removed from MsClawOptions as part of session refactor — SDK now owns persistence via InfiniteSessions. Test validates the removal via reflection to catch accidental reintroduction.
