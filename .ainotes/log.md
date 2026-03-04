# AI Notes — Log

## 2026-03-04
- sdk-analysis: Copilot SDK covers 17 of 23 agent runtime REQs out of the box (9 direct, 6 thin wrappers, 2 more discovered during source review)
- sdk-analysis: SDK's ToolsApi.ListAsync() calls "tools.list" RPC — covers REQ-023 (skills discovery) for free
- sdk-analysis: SDK's SendAndWaitAsync has built-in timeout but does NOT abort the run — thin wrapper needed to call AbortAsync on TimeoutException
- sdk-analysis: CLI agent harness discovers .github/skills/ automatically when Cwd points to mind root — REQ-013 is free
- sdk-analysis: SDK tool model is static per session (set at CreateSessionAsync) — RegisterTools is internal. Node-provided tools (REQ-014) require dynamic registry we must build
- sdk-analysis: SDK ConnectionState is binary (Connected/Error) — no degraded concept. Must build Starting→Ready→Degraded→Stopped state machine
- sdk-analysis: SDK has zero concurrency control — no per-caller or global throttle. Must build both (REQ-006, REQ-007)
- mind-model: MindReader.cs removed exit-code error handling from sync process — sync failures now silently succeed
