# AI Notes — Log

## 2026-03-01
- bootstrap: Consolidated bootstrap-as-conversation.md into Templates/bootstrap.md (3 phases: Identity, Agent File, Memory — dropped phases 4-6)
- bootstrap: Vendored OpenClaw SOUL.md template at pinned commit 0f72000c into Templates/SOUL.md (YAML frontmatter stripped)
- bootstrap: Renamed bootstrap-plan.md → bootstrap-spec.md — it's a spec, not a plan
- architecture: bootstrap.md in scaffolded mind drives the LLM conversation; agent deletes it when done
- architecture: IdentityLoader needs upgrade to compose SOUL.md + .github/agents/*.agent.md into system message
- convention: `.working-memory/` is the canonical memory directory name for MsClaw minds
- documentation: Created comprehensive code walkthrough (docs/msclaw-walkthrough.md) using Showboat with real verified code snippets covering all 12 major components (boot, discovery, validation, loading, sessions, runtime client, scaffolding, persistence, models, orchestration, request flow, architecture summary)

## 2026-03-02
- cleanup: Merged claude/code-review-sqMq7 branch — removed all hardcoded miss-moneypenny refs from discovery, defaults, config, and README
- architecture: BootstrapOrchestrator now silently skips unknown CLI args to avoid breaking ASP.NET Core host flags (--urls, --environment, etc.)
- architecture: MindValidator early-returns when root directory is missing — prevents cascading errors for SOUL.md/.working-memory
- architecture: Program.cs reuses bootstrap-time instances as DI singletons instead of creating duplicates
- tooling: `showboat verify` detects stale code blocks in walkthrough docs — used it to find and fix 5 blocks after the code review merge
- refactor: Replaced custom session management (SessionManager, ISessionManager, SessionState, SessionMessage) with Copilot SDK built-in sessions (CreateSessionAsync, ResumeSessionAsync, SendAndWaitAsync, InfiniteSessionConfig)
- architecture: CopilotClient must be registered as singleton — spawning per-request is like starting a new database per query
- architecture: SDK's InfiniteSessionConfig auto-compacts at 80% context utilization — eliminates manual context window management
- anti-pattern: BuildPrompt pattern (stuffing full conversation as text blob) replaced — SDK maintains proper user/assistant turn history natively
- architecture: System message (SOUL.md + agents) is set once at session creation, not re-sent per message
- spec: Distilled phase2-design.md (extension system) into a clean extension-spec.md — separated "what/why" (spec) from "how" (design with code examples); resolved all 4 open questions and integrated answers inline
- architecture: Extension system uses two-tier discovery — `{appRoot}/extensions` for core, `{mindRoot}/extensions` for mind-local; mind-root can override core by matching extension ID
- architecture: ExtensionManager loads core extensions first (in-process), then external extensions from DLL assemblies with manifest-driven dependency ordering using SemVer range checks
- architecture: ISessionControl interface decouples extension reload from session lifecycle — allows warm reload of external extensions without app restart
- architecture: CopilotRuntimeClient collects registered tools from all extensions into SessionConfig.Tools at session creation; hooks fire async for session/message/bootstrap events
- convention: Extension manifests use `plugin.json` with id, name, version, entryPoint, and optional dependencies map
- convention: Mind scaffolding now creates `extensions/` dir, `extensions.lock.json`, and adds extension paths to mind-local `.gitignore`
- convention: MsClawConfig.DisabledExtensions holds a list of extension IDs to skip during loading
- documentation: Created comprehensive extension-developer-guide.md — hands-on walkthrough for extension developers covering full IExtension lifecycle, tool/command/hook registration, configuration, testing, and common patterns; included ASP.NET limitation (HTTP routes frozen after startup) with documented workaround
- docs: Removed duplicate "Extension System: A New Architecture" section from msclaw-walkthrough.md (merge artifact from Showboat generation)
- phase2-review: Marked item 6 (HTTP route reload limitation) as complete with documentation link
