# MsClaw — MVP Specification

## Overview

MsClaw is a personal agent runtime in the claw ecosystem, powered by the GitHub Copilot SDK. It gives an AI agent — defined by a markdown "mind" — an HTTP-accessible runtime with persistent sessions.

MVP proves one thing: **the agent can live outside the CLI**. POST a message, get the agent back, pick up where you left off.

## Architecture

```
┌─────────────────────────────────────────────┐
│  HTTP Client (curl, Telegram adapter, etc.) │
└──────────────────┬──────────────────────────┘
                   │ POST /chat
┌──────────────────▼──────────────────────────┐
│  ASP.NET Minimal API                        │
│  - Single POST route                        │
│  - Request/response JSON                    │
│  - Session resolution                       │
└──────────────────┬──────────────────────────┘
                   │
┌──────────────────▼──────────────────────────┐
│  MsClaw Core                                │
│  ┌────────────────────────────────────────┐ │
│  │ Copilot SDK Client                     │ │
│  │ - Manages CLI server mode lifecycle    │ │
│  │ - JSON-RPC communication               │ │
│  │ - Tool invocation                      │ │
│  └────────────────────────────────────────┘ │
│  ┌────────────────────────────────────────┐ │
│  │ Identity Loader                        │ │
│  │ - Reads SOUL.md from mind root         │ │
│  │ - Reads agent operating instructions   │ │
│  │ - Composes system message              │ │
│  └────────────────────────────────────────┘ │
│  ┌────────────────────────────────────────┐ │
│  │ Session Manager                        │ │
│  │ - Single-user, file-based JSON         │ │
│  │ - Resume across restarts               │ │
│  │ - One active session at a time (MVP)   │ │
│  └────────────────────────────────────────┘ │
│  ┌────────────────────────────────────────┐ │
│  │ Mind Reader                            │ │
│  │ - Local filesystem access (read-only)  │ │
│  │ - Resolves mind root via convention    │ │
│  │ - Exposes files to agent as context    │ │
│  └────────────────────────────────────────┘ │
└─────────────────────────────────────────────┘
```

## Components

### 1. Copilot SDK Client

Wraps the GitHub Copilot SDK for .NET. Manages the Copilot CLI in server mode via JSON-RPC.

**Responsibilities:**
- Start/stop the Copilot CLI process in server mode
- Send messages via JSON-RPC
- Receive responses (blocking — no streaming for MVP)
- Pass system message (identity + operating instructions)
- Handle tool calls if the SDK supports them

**Key decisions:**
- Language: .NET
- NuGet package: `GitHub.CopilotSdk` (or whatever the preview package name is)
- The SDK is a technical preview (Feb 2026) — expect rough edges

### 2. Identity Loader

Reads the agent's personality and operating instructions from the mind and composes them into a system message.

**Responsibilities:**
- Discover mind root (configured path or convention-based: look for `SOUL.md` at root)
- Read `SOUL.md` → personality and voice
- Read agent operating instructions (the agent instructions file)
- Compose a single system message from both sources
- Fail clearly if SOUL.md is missing ("no identity found at {path}")

**Convention-based discovery:**
```
{mind_root}/
  SOUL.md                    → identity / personality
  .working-memory/           → agent memory (memory.md, rules.md, log.md)
  domains/                   → knowledge (IDEA method)
  initiatives/               → projects
  expertise/                 → learning
  Archive/                   → completed work
```

### 3. Session Manager

Maintains conversation state across requests and restarts.

**Responsibilities:**
- Create new session on first request (or when explicitly requested)
- Persist conversation history to disk as JSON
- Resume session on subsequent requests
- Single-user, single-session for MVP (no session routing)

**Storage format:**
```json
{
  "sessionId": "uuid",
  "createdAt": "ISO-8601",
  "updatedAt": "ISO-8601",
  "messages": [
    { "role": "user", "content": "...", "timestamp": "ISO-8601" },
    { "role": "assistant", "content": "...", "timestamp": "ISO-8601" }
  ]
}
```

**Storage location:** `{app_data}/sessions/{sessionId}.json` — configurable, defaults to `./data/sessions/`.

### 4. Mind Reader

Provides read-only access to the mind's files so the agent can reference notes, initiatives, and domain knowledge.

**Responsibilities:**
- Read files from the mind root by path
- List directory contents
- Optionally run `git pull` on the mind repo before reading (configurable, off by default)
- Expose as tool(s) the agent can call: `read_file(path)`, `list_directory(path)`
- Path traversal protection — never read outside the mind root

**Not in scope:** Writing to the mind. That comes post-MVP when trust in the loop is established.

### 5. HTTP Endpoint

A single POST route that accepts a message and returns the agent's response.

**Route:** `POST /chat`

**Request:**
```json
{
  "message": "what's on my plate today?"
}
```

**Response:**
```json
{
  "response": "Good morning. Three things need your attention...",
  "sessionId": "uuid"
}
```

**Behavior:**
- If no active session exists, create one
- Load identity and system message
- Append user message to session history
- Send full conversation (system message + history) to Copilot SDK
- Append assistant response to session history
- Persist session to disk
- Return response

**Additional endpoints (minimal):**
- `GET /health` — returns 200 if service is running
- `POST /session/new` — explicitly start a fresh session (optional convenience)

## Configuration

Minimal config via `appsettings.json` or environment variables:

| Setting | Default | Description |
|---------|---------|-------------|
| `MindRoot` | `../miss-moneypenny` | Path to the mind directory |
| `SessionStore` | `./data/sessions` | Path for session JSON files |
| `Port` | `5000` | HTTP listen port |
| `AutoGitPull` | `false` | Pull mind repo before reads |

## Success Criteria

MVP is complete when all five pass:

1. **`dotnet run` starts a local HTTP server** on the configured port
2. **`curl -X POST localhost:5000/chat -d '{"message":"what's on my plate?"}'`** returns a response in the agent's voice
3. **The response demonstrates mind awareness** — references real notes, initiatives, or people from the mind
4. **A follow-up message in the same session shows continuity** — the agent remembers what was just discussed
5. **After restarting the service, the session resumes** — previous conversation context is preserved

## What's Explicitly Out

- Plugin/extension API
- Gateway/channel routing
- Telegram or any channel adapter
- Mind write operations
- Multi-user or authentication
- Streaming responses
- Node host / approval workflows
- Hosting or deployment (runs locally)
- Bootstrap/scaffold mode (MVP only does "point at existing mind")

## Project Structure (Suggested)

```
msclaw/
  .aidocs/
    mvp-spec.md              ← this file
  src/
    MsClaw/
      MsClaw.csproj
      Program.cs              ← HTTP setup, DI, startup
      Core/
        CopilotClient.cs      ← SDK wrapper, JSON-RPC lifecycle
        IdentityLoader.cs     ← SOUL.md + instructions → system message
        SessionManager.cs     ← File-based session persistence
        MindReader.cs         ← Read-only mind file access
      Models/
        ChatRequest.cs
        ChatResponse.cs
        Session.cs
  MsClaw.sln
  README.md
```

## Dependencies

- .NET 8+ (LTS)
- GitHub Copilot SDK for .NET (NuGet — technical preview)
- ASP.NET Minimal API (built-in)
- System.Text.Json (built-in)

## Connections

- **Source mind:** `ianphil/miss-moneypenny` — the private instance this MVP is built for
- **Northstar architecture:** See `miss-moneypenny/initiatives/msclaw/msclaw.md` for full vision (gateway, extensions, node host)
- **First downstream consumer:** Miss Moneypenny's Cellphone (Telegram adapter) — plugs into this HTTP endpoint post-MVP
- **Governance framework:** Directive Plane — defines what the agent can do without asking; maps to future node host approval gates
