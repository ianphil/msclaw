# MsClaw — Post-MVP Roadmap

Three phases, in order. Each builds on the last.

---

## Phase 1: Bootstrap / Mind Discovery

**Goal:** Replace the `MIND_ROOT` env var with a proper first-run experience. MsClaw becomes a framework — not just a hardwired instance.

**What exists today:** `IdentityLoader` reads SOUL.md from a path set via `appsettings.json` / env var. No first-run detection, no scaffolding, no validation beyond "file not found."

### Boot Modes

1. **Point at existing mind** — User provides a path (or MsClaw discovers one via convention). Validate: does it have `SOUL.md`? `.working-memory/`? IDEA folders? Report what's found, what's missing, and whether the mind is usable.

2. **Scaffold fresh** — No mind found (or user requests it). Create a new mind directory with starter structure:
   ```
   {mind_root}/
     SOUL.md                    ← blank template ("Who are you?")
     .working-memory/
       memory.md                ← empty
       rules.md                 ← empty
       log.md                   ← empty
     domains/
     initiatives/
     expertise/
     inbox/
     Archive/
   ```

### Tasks

- [ ] **Design bootstrap flow** — First-run detection: does the configured mind root exist and pass validation? If not, offer scaffold or reconfigure. Consider: CLI flags (`--mind-root <path>`, `--scaffold <path>`) vs interactive prompt vs config file.
- [ ] **Mind validation** — Check for SOUL.md, .working-memory/, IDEA folders. Return a structured result (found/missing/warnings) rather than just pass/fail. IdentityLoader already does basic "file not found" — extend into a proper `MindValidator`.
- [ ] **Convention-based discovery** — Look for a mind at well-known locations (current dir, `~/.msclaw/mind`, configured path) before falling back to "not found." This replaces the env var as the primary mechanism.
- [ ] **Scaffold mode** — Generate starter IDEA structure + blank SOUL.md. The templates already exist conceptually in `miss-moneypenny` — formalize them as the default scaffold.
- [ ] **Configuration persistence** — Once a mind root is resolved (pointed or scaffolded), persist it so subsequent runs don't re-prompt. `appsettings.json`, a dotfile, or `~/.msclaw/config.json`.

### Success Criteria

1. `dotnet run` with no config → detects no mind → offers to scaffold or configure
2. `dotnet run --mind-root ~/src/miss-moneypenny` → validates the mind → starts serving
3. `dotnet run --scaffold ~/src/my-new-agent` → creates IDEA structure → starts serving
4. After first successful run, subsequent `dotnet run` remembers the mind root

### What This Unlocks

- Anyone can `dotnet run` MsClaw and get a working agent — not just Ian
- The "framework vs instance" distinction becomes real at the code level
- Extensions and gateway both inherit proper mind resolution

---

## Phase 2: Extension System

**Goal:** Modular capabilities via a plugin API. The gateway, channels, tools, and hooks all register as extensions — this is the seam that makes MsClaw composable.

**What exists today:** Core capabilities (MindReader, SessionManager, IdentityLoader) are hardwired in DI. No plugin discovery, no registration API, no lifecycle management.

### Architecture

Adapted from OpenClaw's plugin pattern for .NET:

```
extensions/<name>/
  ├── plugin.json       (id, kind, config schema, version)
  └── Extension.cs      (entry: implements IExtension, calls Register())
```

### Registration API

Extensions register capabilities through `IMsClawPluginApi`:

| Method | What it registers |
|--------|------------------|
| `RegisterTool()` | Agent-callable tool/action |
| `RegisterChannel()` | Chat channel adapter (gateway consumer) |
| `RegisterHook()` | Lifecycle event handler |
| `RegisterService()` | Long-running background service |
| `RegisterCommand()` | Direct command (bypasses LLM) |
| `RegisterHttpRoute()` | Additional HTTP endpoint |

### Tasks

- [ ] **Study OpenClaw's `register()` flow** — How do callbacks wire into the runtime loop? What's the lifecycle (load → validate → register → start → stop)? Map this onto .NET DI patterns.
- [ ] **Define `IExtension` interface** — The contract every extension implements. At minimum: `Id`, `Name`, `Version`, `Register(IMsClawPluginApi api)`, `Start()`, `Stop()`.
- [ ] **Build `IMsClawPluginApi`** — The API object passed to extensions during registration. Implements RegisterTool, RegisterHook, RegisterChannel, etc.
- [ ] **Extension loader** — Discover extensions from a configured directory, validate plugin.json, instantiate, call Register(). Consider: assembly loading, config injection, dependency ordering.
- [ ] **Refactor MindReader as first extension** — Currently hardwired. Move it behind the extension API as a proof-of-concept: registers `read_file` and `list_directory` as tools.
- [ ] **Hook system** — Lifecycle events: `session:create`, `session:resume`, `session:end`, `message:received`, `message:sent`, `agent:bootstrap`. Extensions subscribe via `RegisterHook()`.

### Success Criteria

1. MindReader works as an extension (not hardwired) — registers tools via the plugin API
2. A "hello world" extension can be dropped into `extensions/` and discovered on startup
3. Hook events fire at the right lifecycle points
4. Extensions can be enabled/disabled via config

### What This Unlocks

- Gateway registers as an extension (not baked into core)
- Third-party or user-defined tools without modifying MsClaw source
- The hook system enables heartbeat, morning briefings, and proactive behaviors

---

## Phase 3: Gateway & Channels

**Goal:** Channel-agnostic message routing. The gateway sits between inbound channels (Telegram, CLI, future: Discord, web, etc.) and the agent core. First channel: Telegram (the [[Miss Moneypenny's Cellphone]] initiative).

**What exists today:** A single HTTP endpoint (`POST /chat`) that takes a message and returns a response. No channel abstraction, no message routing, no format conversion.

### Architecture

```
Channel (Telegram, CLI, ...)
    ↓ inbound message
Gateway
    ├── Sanitize input
    ├── Resolve session (channel + user → session ID)
    ├── Route to agent core
    ├── Receive response
    └── Route back to channel
    ↓ formatted response
Channel
```

### Channel Contract

Each channel adapter implements `IChannelPlugin` (registered via the extension system):

| Concern | What it handles |
|---------|----------------|
| **Config** | Account list, enable/disable, validation |
| **Inbound** | Receive messages, normalize to common format |
| **Outbound** | Target resolution, send (text/media), chunking for platform limits |
| **Lifecycle** | Start/stop, reconnection, health checks |
| **Auth** | DM policy, allowlist enforcement |
| **Formatting** | Markdown → channel-native format (Telegram HTML, Discord markdown, etc.) |

### Tasks

- [ ] **Design `IChannelPlugin` interface** — The contract for channel adapters. Builds on OpenClaw's pattern, registered via the extension system's `RegisterChannel()`.
- [ ] **Build gateway routing** — Inbound dispatch: sanitize → resolve session → route to core. Outbound dispatch: format → chunk → deliver. The gateway itself is an extension.
- [ ] **Session routing** — Map channel+user to a session. A Telegram user gets a persistent session; multiple channels for the same user could share or isolate sessions (configurable).
- [ ] **Message format normalization** — Common internal format that channels convert to/from. Rich content (images, code blocks, links) needs to survive the round trip.
- [ ] **Build Telegram adapter** — First channel. Leverage the [[Miss Moneypenny's Cellphone]] research (seedprod POC, Telegram Bot API). Register as a channel extension.
- [ ] **Chunking strategy** — Telegram has a 4096-char message limit. Long responses need intelligent splitting (not mid-sentence, preserve code blocks).
- [ ] **Allowlist / auth** — Who can talk to the agent? At minimum: a configurable allowlist of Telegram user IDs. No open access.
- [ ] **Decide hosting strategy** — Channels require always-on. Home server? Cloud VM? Container? This decision gates whether Telegram actually works day-to-day.

### Success Criteria

1. Telegram messages reach the agent and responses come back — full round trip
2. Conversations persist across messages (session continuity per Telegram user)
3. Long responses are chunked intelligently
4. Only allowlisted users can interact
5. The gateway is an extension, not hardwired — a second channel could be added without modifying gateway code

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
