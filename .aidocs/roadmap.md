# MsClaw — Post-MVP Roadmap

Three phases, in order. Each builds on the last.

---

## Phase 1: Bootstrap / Mind Discovery ✅

**Status:** Complete — implemented in `feature/bootstrap-spec` branch.

**Goal:** Replace the `MIND_ROOT` env var with a proper first-run experience. MsClaw becomes a framework — not just a hardwired instance.

**SDK surface:** Minimal. Bootstrap resolves mind root and composes identity *before* the SDK client exists. The composed system message feeds into `SessionConfig.SystemMessage` at session creation time.

### What Was Built

- **CLI argument parsing** — `--mind <path>`, `--new-mind <path>`, `--reset-config`
- **MindValidator** — Validates SOUL.md and .working-memory/ (errors), other IDEA dirs (warnings)
- **MindDiscovery** — Convention-based search: cached config → cwd → `~/.msclaw/mind` → `~/src/miss-moneypenny`
- **MindScaffold** — Generates new mind from embedded SOUL.md and bootstrap.md templates
- **ConfigPersistence** — Saves resolved mind root to `~/.msclaw/config.json`
- **IdentityLoader** — Composes SOUL.md + `.github/agents/*.agent.md` into system message
- **BootstrapOrchestrator** — Coordinates the full bootstrap flow before Kestrel starts
- **Bootstrap detection** — If `{mindRoot}/bootstrap.md` exists, its content is prepended to the system message

### Key Decisions

- `BootstrapOrchestrator.Run()` returns `BootstrapResult?` — null signals `--reset-config` (exit cleanly)
- SOUL.md template vendored from OpenClaw pinned commit `0f72000c`
- SOUL.md and `.working-memory/` missing = validation error; other dirs missing = warning only
- ConfigPersistence uses instance `_configPath` (not static) for test isolation

### Tests

33 tests (unit + integration), all passing.

### Success Criteria — Met

1. ✅ `dotnet run -- --mind ~/src/ernist` → validates the mind → starts serving
2. ✅ `dotnet run -- --new-mind ~/src/new-agent` → scaffolds IDEA structure → starts serving
3. ✅ Subsequent `dotnet run` remembers the mind root via `~/.msclaw/config.json`
4. ✅ No config + no discoverable mind → clear error with usage message

---

## Phase 2: Extension System 🚧

**Status:** In progress — Phase 2 foundation implemented on `feature/extension-system-phase-2`.

**Goal:** Modular capabilities via a plugin API. The gateway, channels, tools, and hooks all register as extensions — this is the seam that makes MsClaw composable.

### What Was Built

- **Extension contract + API** — `IExtension`, `ExtensionBase`, `IMsClawPluginApi`, hook/command contexts, and extension lifecycle methods
- **Capability registration surface** — Tools, hooks, services, commands, and HTTP routes
- **Extension runtime manager** — Core + external extension loading, manifest parsing, dependency ordering with SemVer range checks, and error isolation
- **Two-tier discovery** — `{appRoot}/extensions` and `{mindRoot}/extensions` with mind-root override behavior
- **Core extensions** — MindReader moved behind extension registration; runtime control extension provides `/reload` and `/extensions`
- **Runtime wiring** — `SessionConfig.Tools` from extensions and hook firing for `session:*`, `message:*`, bootstrap, and extension-loaded events
- **Command bypass endpoint** — `POST /command` for slash commands without LLM roundtrip
- **Warm reload path** — external extensions reload without app restart; active sessions are cycled
- **Mind scaffolding/config updates** — scaffolded `extensions/`, `extensions.lock.json`, and mind-local `.gitignore`; config supports disabled extension IDs
- **Coverage + manual validation assets** — added extension runtime tests, `.aidocs/e2e-extension-test.md`, and sample extension repo `https://github.com/ipdelete/hello-world-extension`

**SDK surface:** `RegisterTool()` collects `AIFunction` instances (via `AIFunctionFactory.Create()`) and passes them to `SessionConfig.Tools` at session creation. `RegisterHook()` wraps the SDK's `SessionConfig.Hooks` (`OnPreToolUse`, `OnPostToolUse`, `OnSessionStart`, `OnSessionEnd`, `OnErrorOccurred`). `RegisterService()`, `RegisterCommand()`, and `RegisterHttpRoute()` have no SDK equivalent — they're pure .NET DI / ASP.NET concerns. Tools must be registered before session creation; the SDK wires them at `CreateSessionAsync()` time, not after.

### Architecture

Adapted from OpenClaw's plugin pattern for .NET:

```
extensions/<name>/
  ├── plugin.json       (id, kind, config schema, version)
  └── Extension.cs      (entry: implements IExtension, calls Register())
```

### Registration API

Extensions register capabilities through `IMsClawPluginApi` (the **core** plugin API):

| Method | What it registers |
|--------|------------------|
| `RegisterTool()` | Agent-callable tool/action |
| `RegisterHook()` | Lifecycle event handler |
| `RegisterService()` | Long-running background service |
| `RegisterCommand()` | Direct command (bypasses LLM) |
| `RegisterHttpRoute()` | Additional HTTP endpoint |

Note: `RegisterChannel()` is **not** on this interface — it lives on the gateway's own plugin API (see Phase 3). This avoids a coupling leak where core would need to import channel types for something only the gateway manages.

### Tasks

- [x] **Study OpenClaw's `register()` flow** — Adapted lifecycle to .NET DI/runtime model.
- [x] **Define `IExtension` interface** — Implemented with async lifecycle and registration method.
- [x] **Build `IMsClawPluginApi`** — Implemented all five capability registration methods.
- [x] **Extension loader** — Implemented discovery, validation, dynamic loading, and dependency ordering.
- [x] **Refactor MindReader as first extension** — Implemented as a core extension.
- [x] **Hook system** — Implemented lifecycle hook events and fire-and-await behavior.

### Success Criteria

1. ✅ MindReader works as an extension (not hardwired) — registers tools via the plugin API
2. ✅ A "hello world" extension can be dropped into `extensions/` and discovered on startup
3. ✅ Hook events fire at the key lifecycle points used by runtime/session flow
4. ✅ Extensions can be enabled/disabled via config ID list

### Known Follow-ups

- Route delegates are currently retained across warm reloads, which can expose pre-reload extension state on mapped endpoints.
- Distribution/management commands (`install`, `uninstall`, `list`, `update`, `restore`) still need full CLI implementation.

### What This Unlocks

- Gateway registers with core as a service (decoupled, but not a generic extension — it owns its own channel subsystem)
- Third-party or user-defined tools without modifying MsClaw source
- The hook system enables heartbeat, morning briefings, and proactive behaviors

---

## Phase 3: Gateway & Channels

**Goal:** Channel-agnostic message routing. The gateway is a **self-contained subsystem that owns channels** — managing their lifecycle, routing, and format conversion. It registers with core as a service but manages its own plugin surface internally. First channel: Telegram (the [[Miss Moneypenny's Cellphone]] initiative).

**What exists today:** A single HTTP endpoint (`POST /chat`) that takes a message and returns a response. No channel abstraction, no message routing, no format conversion.

**SDK surface:** Session routing is the core seam — `CreateSessionAsync()` for new users, `ResumeSessionAsync(sessionId)` for returning ones. Inbound messages go through `SendAndWaitAsync()` (or `SendAsync()` + event subscription for streaming). Outbound responses come from `AssistantMessageEvent.Data.Content` via `session.On()`. One singleton `CopilotClient` (spawns CLI process), many concurrent `CopilotSession` instances (one per user). Infinite sessions handle context/persistence automatically. Proactive messages (morning briefings) are just `SendAsync()` calls on a timer — the SDK doesn't care who initiates. Chunking, auth, and format conversion are post-SDK concerns.

### Architecture

The gateway owns channels. Channel extensions register with the gateway via `RegisterChannel()` — a gateway-internal API, not the core plugin API. Once registered, the `ChannelManager` handles all lifecycle management. Core never imports channel types.

```
Channel (Telegram, CLI, ...)
    ↓ inbound message
Gateway (registers with core as a service)
    ├── ChannelManager (start/stop/restart/health per channel)
    ├── Sanitize input
    ├── Resolve session (channel + user → session ID)
    ├── Route to agent core
    ├── Receive response
    └── Route back to channel
    ↓ formatted response
Channel
```

**Two-tier API design:** Most extensions (mind-reader, GitHub, heartbeat) only use the core plugin API (`RegisterTool`, `RegisterHook`, etc.). Channel extensions only use the gateway's API (`RegisterChannel`). A channel extension *may* also use core hooks, but it never needs to — the boundary is clean.

### Channel Contract

Each channel adapter implements `IChannelPlugin` (registered with the gateway, not core):

| Concern | What it handles |
|---------|----------------|
| **Config** | Account list, enable/disable, validation |
| **Inbound** | Receive messages, normalize to common format |
| **Outbound** | Target resolution, send (text/media), chunking for platform limits |
| **Lifecycle** | Start/stop, reconnection, health checks |
| **Auth** | DM policy, allowlist enforcement |
| **Formatting** | Markdown → channel-native format (Telegram HTML, Discord markdown, etc.) |

### ChannelManager

Gateway-internal lifecycle controller (modeled after OpenClaw's `server-channels.ts`):

- Start/stop channels per account
- Auto-restart with exponential backoff on failure
- Health monitoring and state tracking (enabled/disabled/configured/running)
- Manual stop tracking (don't auto-restart manually stopped channels)

### Tasks

- [ ] **Define `IChannelPlugin` interface** — The contract for channel adapters. Gateway-internal, not on the core plugin API. Config, inbound, outbound, lifecycle, auth, formatting concerns.
- [ ] **Build `ChannelManager`** — Lifecycle controller: start/stop/restart with backoff, health monitoring, account state tracking. Informed by OpenClaw's `createChannelManager()`.
- [ ] **Build gateway routing** — Inbound: sanitize → resolve session → route to core. Outbound: format → chunk → deliver. Gateway registers with core as a service via `RegisterService()`.
- [ ] **Gateway plugin API** — `RegisterChannel()` as the handoff seam. Channel extensions call this; `ChannelManager` takes ownership from there.
- [ ] **Session routing** — Map channel+user to a session. A Telegram user gets a persistent session; multiple channels for the same user could share or isolate sessions (configurable).
- [ ] **Message format normalization** — Common internal format that channels convert to/from. Rich content (images, code blocks, links) needs to survive the round trip.
- [ ] **Build Telegram adapter** — First channel. Leverage the [[Miss Moneypenny's Cellphone]] research (seedprod POC, Telegram Bot API). Registers via gateway's `RegisterChannel()`.
- [ ] **Chunking strategy** — Telegram has a 4096-char message limit. Long responses need intelligent splitting (not mid-sentence, preserve code blocks).
- [ ] **Allowlist / auth** — Who can talk to the agent? At minimum: a configurable allowlist of Telegram user IDs. No open access.
- [ ] **Permission handling** — `PermissionHandler.ApproveAll` is hardcoded today. With remote users sending messages via Telegram, the agent can execute any tool without consent. Discuss with Ian: keep as-is, whitelist safe tools, or route permission requests to the channel?
- [ ] **Decide hosting strategy** — Channels require always-on. Home server? Cloud VM? Container? This decision gates whether Telegram actually works day-to-day.

### Success Criteria

1. Telegram messages reach the agent and responses come back — full round trip
2. Conversations persist across messages (session continuity per Telegram user)
3. Long responses are chunked intelligently
4. Only allowlisted users can interact
5. Adding a second channel means writing a new `IChannelPlugin` and calling `RegisterChannel()` — no gateway or core modifications needed

### What This Unlocks

- Miss Moneypenny on the phone — reachable anywhere, not just from a terminal
- Additional channels (Discord, Signal, web UI, SMS) are "just another adapter"
- The hook system + gateway enables proactive messages (morning briefings pushed to Telegram)

---

## Connections

- [[MsClaw]] — northstar architecture in `miss-moneypenny/initiatives/msclaw/`
- [[MsClaw — MVP]] — completed foundation this roadmap builds on
- [[Miss Moneypenny's Cellphone]] — the Telegram channel, first gateway consumer
- [[Directive Plane]] — governance framework; maps to node host approval gates (future, post-Phase 3)
- [[Agent Craft]] — patterns and principles baked into the runtime
