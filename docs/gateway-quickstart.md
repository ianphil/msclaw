# Gateway Quickstart

Manual testing guide for the MsClaw Gateway MVP. Scaffolds a disposable mind, validates it, starts the gateway, verifies the endpoints, and cleans up.

## Prerequisites

| Requirement | Why |
|---|---|
| .NET 10 SDK | Build the gateway binary |
| GitHub Copilot CLI on PATH | The gateway spawns it as a child process |
| A terminal with `curl` (or Invoke-WebRequest) | Hit the health endpoint |

Build the gateway from the repo root:

```bash
dotnet build src/MsClaw.slnx --nologo
```

The output binary lives at `src/MsClaw.Gateway/bin/Debug/net10.0/msclaw` (`.exe` on Windows).

For convenience, alias it or run via `dotnet run`:

```bash
# Option A — run directly
./src/MsClaw.Gateway/bin/Debug/net10.0/msclaw

# Option B — dotnet run (from gateway project dir)
cd src/MsClaw.Gateway
dotnet run --
```

> **Tip:** When using `dotnet run`, put a bare `--` before any msclaw flags so they aren't swallowed by dotnet.

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

You should see ASP.NET Core startup logs in the terminal.

> **Alternative:** `msclaw start --new-mind ./fresh-mind` scaffolds and starts in one shot. The two flags are mutually exclusive — supply exactly one.

---

## 4 — Health check

Open a second terminal and hit the health endpoint:

```bash
curl -s http://127.0.0.1:18789/healthz | jq .
```

**Healthy response** (HTTP 200):

```json
{
  "status": "Healthy"
}
```

**Unhealthy response** (HTTP 503) — appears when mind validation fails or the CopilotClient didn't start:

```json
{
  "status": "Unhealthy",
  "error": "SOUL.md is missing"
}
```

### PowerShell equivalent

```powershell
Invoke-RestMethod http://127.0.0.1:18789/healthz
```

---

## 5 — SignalR hub

The gateway maps a SignalR hub at `/gateway`. The hub is a stub in this MVP — no methods are defined yet — but you can verify the negotiation endpoint is live:

```bash
curl -s -X POST http://127.0.0.1:18789/gateway/negotiate?negotiateVersion=1
```

A successful response returns a JSON payload with a `connectionToken` and available transports, confirming the hub is wired up.

---

## 6 — Shut down and clean up

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
| Health check | `curl http://127.0.0.1:18789/healthz` |
| SignalR negotiate | `curl -X POST http://127.0.0.1:18789/gateway/negotiate?negotiateVersion=1` |

## Defaults

| Setting | Value |
|---|---|
| Host | `127.0.0.1` |
| Port | `18789` |
| Health endpoint | `/healthz` |
| SignalR hub | `/gateway` |
