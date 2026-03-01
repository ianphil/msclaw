# GitHub Copilot SDK Capabilities Reference

## Purpose
This reference summarizes what the **GitHub Copilot SDK** can do, with emphasis on the .NET MVP context for MsClaw.

## Official Sources
- Copilot SDK repo: https://github.com/github/copilot-sdk
- Copilot CLI ACP server docs: https://docs.github.com/en/copilot/reference/acp-server
- Copilot CLI overview: https://docs.github.com/en/copilot/concepts/agents/about-copilot-cli

## Core Architecture
The SDK is a programmable wrapper over the same runtime used by Copilot CLI.

```text
Your App -> SDK Client -> JSON-RPC -> Copilot CLI (server mode)
```

Key implications:
- The CLI is still required and must be installed.
- The SDK can manage CLI server lifecycle for you.
- You can also connect to an externally started ACP server.

## SDK Availability
Official SDKs are available for:
- TypeScript/Node: `@github/copilot-sdk`
- Python: `github-copilot-sdk`
- Go: `github.com/github/copilot-sdk/go`
- .NET: `GitHub.Copilot.SDK`

## Capabilities (What You Can Build)
- Multi-turn agent sessions with persisted conversational state (app-managed persistence model).
- Programmatic prompt/response workflows from your app.
- Tool-enabled agent actions (filesystem, git, web, and configurable tool access).
- Model selection at runtime (via models exposed by Copilot CLI).
- Custom agents, skills, and tools layered by your host application.
- Streaming/event-driven response handling (SDK-specific APIs).
- Integration into APIs, bots, editors, and automation workflows.

## ACP / Server Mode Details
Copilot CLI can run as an ACP server:
- `copilot --acp --stdio` (recommended for local process integrations)
- `copilot --acp --port 3000` (TCP mode)

Use cases called out in official docs:
- IDE/editor integration
- CI/CD automation
- Custom frontends
- Multi-agent coordination

## Authentication and Access Modes
Supported patterns (per SDK docs):
- Signed-in Copilot CLI user context
- Token via env vars (`COPILOT_GITHUB_TOKEN`, `GH_TOKEN`, `GITHUB_TOKEN`)
- OAuth GitHub App token flow
- BYOK (Bring Your Own Key) for supported providers

## Billing and Lifecycle Notes
- SDK is in **Technical Preview**.
- Usage follows Copilot request/quota model unless using BYOK.
- Behavior and APIs may evolve during preview.

## Practical Guidance for MsClaw MVP
Given the MVP spec (`.aidocs/mvp-spec.md`), the SDK can directly support:
1. Hosting an ASP.NET `/chat` endpoint that forwards session history to Copilot.
2. Supplying a composed system message (SOUL + operating instructions).
3. Maintaining app-side JSON session persistence across restarts.
4. Exposing read-only mind tools (`read_file`, `list_directory`) to the agent.

## Constraints to Design Around
- Requires Copilot CLI installation and auth in runtime environment.
- SDK/CLI preview status implies defensive error handling and upgrade tolerance.
- Tool permissions should be intentionally scoped in production scenarios.

## Quick Start Links
- SDK getting started: https://github.com/github/copilot-sdk/blob/main/docs/getting-started.md
- SDK auth docs: https://github.com/github/copilot-sdk/blob/main/docs/auth/index.md
- SDK BYOK docs: https://github.com/github/copilot-sdk/blob/main/docs/auth/byok.md
