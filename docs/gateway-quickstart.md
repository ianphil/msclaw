# Gateway Quickstart

Manual testing guide for the MsClaw Gateway. Scaffolds a disposable mind, starts the gateway, verifies the endpoints, chats with the agent through the browser UI and HTTP API, and cleans up.

## Prerequisites

| Requirement | Why |
|---|---|
| .NET 10 SDK | Build the gateway binary |
| GitHub Copilot CLI on PATH | The gateway spawns it as a child process |
| A terminal with `curl` (or Invoke-WebRequest) | Hit the HTTP endpoints |
| A web browser | Use the built-in chat UI |

Build the gateway from the repo root:

```bash
dotnet build src/MsClaw.slnx --nologo
```

The output binary lives at `src/MsClaw.Gateway/bin/Debug/net10.0/msclaw` (`.exe` on Windows).

> **Tip:** When using `dotnet run`, put a bare `--` before any msclaw flags so they aren't swallowed by dotnet.

---

## 0 — Set your working directory

**Important:** Change to a directory _outside_ the MsClaw repo before scaffolding or starting the gateway. The Copilot CLI walks the directory tree looking for `.github/copilot-instructions.md` — if the mind lives inside the repo, the CLI loads the repo's instruction files instead of the mind's personality, and the bootstrap flow won't activate.

```bash
# Use ~/src (or any directory outside the msclaw repo)
cd ~/src
```

All commands below assume `~/src` as the working directory and reference the gateway binary by its full path. Adjust if your repo checkout is somewhere other than `~/src/msclaw`:

```bash
# Alias for convenience (bash/zsh)
alias msclaw=~/src/msclaw/src/MsClaw.Gateway/bin/Debug/net10.0/msclaw

# PowerShell equivalent
Set-Alias msclaw ~/src/msclaw/src/MsClaw.Gateway/bin/Debug/net10.0/msclaw.exe
```

---

## 1 — Scaffold a mind

Create a disposable mind directory with the required structure (`SOUL.md`, `.working-memory/`, etc.):

```bash
msclaw mind scaffold ./test-mind
```

Expected output: a success message and the path to the new mind. Verify the structure:

```bash
ls ./test-mind
# SOUL.md  .working-memory/
```

---

## 2 — Validate the mind

Check that the scaffolded mind has all the required pieces:

```bash
msclaw mind validate ./test-mind
```

You should see a tree of results — green **Found** entries for `SOUL.md` and `.working-memory/`. If something is missing, the command prints red **Error** entries and exits with code `1`.

Try breaking it on purpose:

```bash
rm ./test-mind/SOUL.md
msclaw mind validate ./test-mind   # expect errors
```

Re-scaffold before continuing:

```bash
msclaw mind scaffold ./test-mind
```

---

## 3 — Start the gateway

Point the gateway at the mind you just created:

```bash
msclaw start --mind ./test-mind
```

The gateway:

1. Validates the mind directory.
2. Loads the system message from `SOUL.md` + any `.github/agents/*.agent.md` files.
3. Starts a `CopilotClient` (spawns the Copilot CLI).
4. Binds Kestrel to `http://127.0.0.1:18789`.
5. Serves the chat UI, SignalR hub, OpenResponses endpoint, and health probes.

You should see ASP.NET Core startup logs in the terminal.

> **Alternative:** `msclaw start --new-mind ./fresh-mind` scaffolds and starts in one shot. The two flags are mutually exclusive — supply exactly one.

---

## 4 — Health probes

Open a second terminal and verify the gateway is alive and ready.

### Liveness (process alive)

```bash
curl -s http://127.0.0.1:18789/health | jq .
```

Always returns **200** when the process is running:

```json
{
  "status": "Healthy"
}
```

### Readiness (runtime initialized)

```bash
curl -s http://127.0.0.1:18789/health/ready | jq .
```

**Healthy** (200) — mind validated, identity loaded, CopilotClient connected:

```json
{
  "status": "Healthy"
}
```

**Unhealthy** (503) — startup failed, with the failing component identified:

```json
{
  "status": "Unhealthy",
  "component": "hosted-service",
  "error": "SOUL.md is missing"
}
```

### PowerShell equivalent

```powershell
Invoke-RestMethod http://127.0.0.1:18789/health
Invoke-RestMethod http://127.0.0.1:18789/health/ready
```

---

## 5 — Chat UI (browser)

Open your browser to [http://127.0.0.1:18789](http://127.0.0.1:18789).

The built-in chat interface connects to the SignalR hub automatically. You can:

- **Send messages** — type a prompt and press Enter (or click Send)
- **Watch streaming responses** — assistant text appears incrementally as it's produced
- **See tool execution** — tool calls are shown in the Run Activity panel
- **Create new sessions** — click "New Session" to start a fresh conversation
- **Abort a run** — click "Abort" to cancel an active response

The connection status indicator shows whether the SignalR connection is active.

### GENESIS bootstrap (fresh minds only)

When the gateway starts with a freshly scaffolded mind, the mind contains a `bootstrap.md` file and a generic `SOUL.md`. The agent's `.github/copilot-instructions.md` tells it to detect `bootstrap.md` and run the GENESIS flow instead of normal conversation.

**What you should see:**

1. **Send any message** (even just "hi") — the agent detects the unbootstrapped mind and starts GENESIS.

2. **Question 1 — Character:** The agent asks you to pick a fictional character whose personality will become the agent's voice. It offers suggestions (Jarvis, Alfred, Wednesday, Samwise, etc.) or you can name anyone.

3. **Question 2 — Role:** The agent asks what role the agent should fill — Chief of Staff, PM, Engineering Partner, Research Assistant, or something custom.

4. **SOUL.md generation:** The agent rewrites `SOUL.md` in the character's voice, tailored to the role. It asks you to confirm or adjust.

5. **Agent file generation:** Creates `.github/agents/{agent-name}.agent.md` with operating instructions for the role.

6. **Working memory seeding:** Seeds `.working-memory/memory.md`, `rules.md`, and `log.md` with initial context.

7. **Cleanup:** Deletes `bootstrap.md`. The mind is now live.

After GENESIS completes, start a new session — the agent should respond in the character's voice with the role's operational style. Three skills are pre-installed: **capture**, **commit**, and **daily-report**.

> **Tip:** If the agent responds generically without starting GENESIS, the Copilot CLI may be loading instruction files from a parent directory (e.g., the repo's `.github/copilot-instructions.md`). Make sure the mind directory is **outside** any git repository — see Step 0.

---

## 6 — SignalR hub

The gateway maps a strongly-typed SignalR hub at `/gateway`. You can verify the negotiation endpoint is live:

```bash
curl -s -X POST http://127.0.0.1:18789/gateway/negotiate?negotiateVersion=1
```

A successful response returns a JSON payload with a `connectionToken` and available transports.

### Hub methods

| Method | Direction | Description |
|---|---|---|
| `SendMessage(prompt)` | Client → Server (streaming) | Sends a prompt and streams back `SessionEvent` objects |
| `CreateSession()` | Client → Server | Creates a new session and returns the session ID |
| `ListSessions()` | Client → Server | Returns metadata for all tracked sessions |
| `GetHistory()` | Client → Server | Returns conversation events for the caller's session |
| `AbortResponse()` | Client → Server | Cancels the active run for the caller |
| `ReceiveEvent(event)` | Server → Client | Pushes a session event during an active run |
| `ReceivePresence(snapshot)` | Server → Client | Pushes connection count updates |

### Concurrency

Each caller (identified by SignalR connection ID) is limited to one active run at a time. A second `SendMessage` while a run is in progress will be rejected.

---

## 7 — OpenResponses HTTP API

The gateway exposes an [OpenResponses](https://www.openresponses.org)-compliant endpoint at `POST /v1/responses` for stateless HTTP consumers.

### Non-streaming request

```bash
curl -s -X POST http://127.0.0.1:18789/v1/responses \
  -H "Content-Type: application/json" \
  -d '{"model": "gpt-5", "input": "What is 2+2?"}' | jq .
```

**Response** (200):

```json
{
  "object": "response",
  "id": "resp_abc123",
  "status": "completed",
  "output": [
    {
      "type": "message",
      "role": "assistant",
      "content": [
        { "type": "output_text", "text": "2+2 equals 4." }
      ]
    }
  ]
}
```

### Streaming request (SSE)

```bash
curl -s -N -X POST http://127.0.0.1:18789/v1/responses \
  -H "Content-Type: application/json" \
  -d '{"model": "gpt-5", "input": "Tell me a story", "stream": true}'
```

Returns a `text/event-stream` with events:

```
event: response.created
data: {"object":"response","id":"resp_abc123","status":"in_progress","output":[]}

event: response.output_text.delta
data: {"type":"output_text","delta":"Once upon"}

event: response.output_text.delta
data: {"type":"output_text","delta":" a time..."}

event: response.output_text.done
data: {"type":"output_text","text":"Once upon a time..."}

event: response.completed
data: {"object":"response","id":"resp_abc123","status":"completed","output":[...]}

data: [DONE]
```

### Request fields

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `model` | string | Yes | — | Model identifier (e.g. `gpt-5`) |
| `input` | string or array | Yes | — | User prompt or message array |
| `stream` | bool | No | `false` | Return SSE stream instead of JSON |
| `user` | string | No | — | Stable caller key for session routing |

### Error responses

| Status | Code | When |
|---|---|---|
| 400 | `invalid_request` | Missing model, empty input, or malformed JSON |
| 409 | `conflict` | Caller already has an active run |
| 500 | `runtime_error` | Agent processing failed |

### PowerShell equivalent

```powershell
$body = @{ model = "gpt-5"; input = "What is 2+2?" } | ConvertTo-Json
Invoke-RestMethod -Uri http://127.0.0.1:18789/v1/responses -Method Post -Body $body -ContentType "application/json"
```

---

## 8 — Shut down and clean up

Press `Ctrl+C` in the terminal running the gateway. The hosted service disposes the `CopilotClient` and Kestrel shuts down cleanly.

Remove the disposable mind:

```bash
rm -rf ./test-mind
```

---

## Quick reference

| Action | Command |
|---|---|
| Build | `dotnet build src/MsClaw.slnx --nologo` |
| Scaffold a mind | `msclaw mind scaffold <path>` |
| Validate a mind | `msclaw mind validate <path>` |
| Start (existing mind) | `msclaw start --mind <path>` |
| Start (new mind) | `msclaw start --new-mind <path>` |
| Chat UI | Open `http://127.0.0.1:18789` in browser |
| Liveness probe | `curl http://127.0.0.1:18789/health` |
| Readiness probe | `curl http://127.0.0.1:18789/health/ready` |
| Send message (HTTP) | `curl -X POST http://127.0.0.1:18789/v1/responses -H "Content-Type: application/json" -d '{"model":"gpt-5","input":"hello"}'` |
| SignalR negotiate | `curl -X POST http://127.0.0.1:18789/gateway/negotiate?negotiateVersion=1` |

## Endpoints

| Endpoint | Method | Description |
|---|---|---|
| `/` | GET | Chat UI (static HTML/JS) |
| `/health` | GET | Liveness probe — always 200 |
| `/health/ready` | GET | Readiness probe — 200 or 503 |
| `/v1/responses` | POST | OpenResponses API (JSON or SSE) |
| `/gateway` | POST | SignalR hub |

## Defaults

| Setting | Value |
|---|---|
| Host | `127.0.0.1` |
| Port | `18789` |
| Concurrency | One active run per caller (reject mode) |
