# Dev Tunnel Support for Remote Gateway Access

## Problem

The MsClaw gateway runs locally alongside the Copilot CLI subprocess. Currently you can only interact with your mind from the machine running the gateway. There's no way to chat with your mind from a phone, tablet, or another machine.

## Proposal

Add optional Microsoft Dev Tunnel integration so the gateway can be reached from anywhere over HTTPS with Entra ID authentication handled by the tunnel.

### Architecture

```
Browser (anywhere) ──HTTPS──▶ Dev Tunnel (cloud relay) ──▶ Gateway (local:18789) ──▶ Copilot CLI ──▶ Mind
```

- Gateway starts locally as usual on port 18789
- When enabled, a `devtunnel host` subprocess exposes that port via a public HTTPS URL
- Dev Tunnels provides Microsoft account authentication at the tunnel level
- SignalR and OpenResponses API both work through the tunnel with no code changes

### Why Dev Tunnels (not Azure SignalR Service)

- **1:1 use case** — single user, single gateway, no need for connection scale-out
- **No negotiate endpoint problem** — tunnel exposes the full gateway, including `/gateway/negotiate`
- **Entra auth built-in** — tunnel requires Microsoft account login before traffic reaches the gateway
- **Already proven** — the [promptbin](https://github.com/ianphil/promptbin) repo has a working `TunnelManager` pattern to port from

### Implementation Notes

Reference `promptbin/src/promptbin/managers/tunnel_manager.py` for the pattern:

1. **Tunnel lifecycle management** — spawn `devtunnel host --port 18789`, parse the public URL from stdout, graceful shutdown via `IHostApplicationLifetime`
2. **Configuration** — environment variable or CLI flag (`--tunnel`) to opt in; off by default
3. **URL surfacing** — log the public tunnel URL at startup so the user can copy it
4. **Health check** — tunnel status available at `/health` or a new `/api/tunnel/status` endpoint

### Persistent vs Temporary Tunnels

Dev Tunnels supports two modes:

| Mode | Command | URL Behavior |
|------|---------|-------------|
| **Temporary** | `devtunnel host -p 18789` | New URL each session, auto-deleted on stop |
| **Persistent** | `devtunnel create` + `devtunnel host --tunnel-id <id> -p 18789` | Stable bookmarkable URL, survives restarts |

**Recommendation:** Use persistent tunnels. A stable URL means you can bookmark it on your phone or save it as a PWA shortcut. The tunnel ID is stored as a property in `~/.msclaw/config.json` (user-level config directory — new, nothing uses `~/.msclaw/` yet). This establishes `~/.msclaw/config.json` as the canonical user-level config file for MsClaw. Can also be overridden via `MSCLAW_TUNNEL_ID` env var or `--tunnel-id` CLI flag. Persistent tunnels expire after **30 days of inactivity** (sliding window, reset on each connection). Expiration is configurable from 1 hour to 30 days via `--expiration`.

Example `~/.msclaw/config.json`:
```json
{
  "tunnelId": "my-msclaw-tunnel"
}
```

### ⚠️ Known Issue: WebSocket Upgrade Through Dev Tunnels

There is a [known issue](https://github.com/microsoft/dev-tunnels/issues/501) where Dev Tunnels may not correctly upgrade HTTPS to WSS for WebSocket connections.

**Impact on MsClaw:**
- SignalR negotiates transport automatically — if WebSockets fail, it falls back to **Server-Sent Events**, then **Long Polling**
- The gateway will still work, but with higher latency on the fallback transports
- The OpenResponses API (`/v1/responses`) is pure HTTP POST and is **not affected**

**Mitigations:**
- SignalR's built-in transport fallback handles this transparently
- Monitor which transport is negotiated (log in `OnConnectedAsync`)
- If WebSockets work through the tunnel, great; if not, SSE/Long Polling is acceptable for a single-user chat UX
- Revisit if/when the upstream issue is resolved

### Access Control (Two Layers)

1. **Tunnel layer** — Dev Tunnels requires Microsoft account or GitHub login by default (no `--allow-anonymous`). Only the tunnel creator can access unless explicitly shared.
2. **Gateway layer** — Entra JWT bearer auth on SignalR hub and OpenResponses endpoint (already implemented)

This gives defense in depth: even if someone discovers the tunnel URL, they still need a valid Entra token for the MsClaw app registration.

### Rate Limits and Quotas

- **Bandwidth:** 20 MB/s upload, 20 MB/s download (sufficient for chat/AI responses)
- **Dev use only** — Microsoft states tunnels are for development/testing, not production hosting
- **Concurrent tunnels/connections:** No published hard limits, but designed for light dev traffic

### Anti-Phishing Interstitial Page

Dev Tunnels shows an anti-phishing confirmation page on first browser visit (GET with `Accept: text/html`). This is a one-time-per-tunnel cookie-based prompt — click Continue once and it won't appear again for that tunnel.

**The `--tenant` flag does NOT skip the interstitial** — it controls who can access the tunnel (Entra tenant members), not whether the warning page is shown. There is no CLI flag to disable the interstitial; this is by design.

**Impact on MsClaw:**
- **Browser UI:** One-time Continue click per device per tunnel. Since we use persistent tunnels, this is effectively once ever.
- **OpenResponses API:** Not affected — POST requests bypass the interstitial automatically.
- **SignalR:** Not affected — negotiate and WebSocket requests don't send `Accept: text/html`.
- **Programmatic clients:** Can add `X-Tunnel-Skip-AntiPhishing-Page: True` header to bypass entirely.

**Tunnel setup flow for `~/.msclaw/config.json`:**
1. First time: `msclaw start --tunnel` detects no `tunnelId` in config → runs `devtunnel create`, `devtunnel access create <id> --tenant`, saves `tunnelId` to config
2. Subsequent runs: reads `tunnelId` from config → runs `devtunnel host <id> -p 18789`

```bash
devtunnel create                              # create persistent tunnel
devtunnel access create <id> --tenant         # restrict to Entra tenant members
devtunnel port create <id> -p 18789           # register the port
devtunnel host <id>                           # host (port already configured)
```

### Project Placement

The tunnel manager should live in its own class library: **MsClaw.Tunnel**. This keeps tunnel concerns (CLI detection, process lifecycle, config) isolated from Gateway and Core. The Gateway project references it and wires up the `--tunnel` flag.

- **MsClaw.Core** — `UserConfigLoader` (reads `~/.msclaw/config.json`) — shared by Tunnel and future consumers
- **MsClaw.Tunnel** — `TunnelManager`, `DevTunnelLocator`, tunnel config types
- **MsClaw.Gateway** — references MsClaw.Tunnel, adds `--tunnel` CLI flag, registers services

### Considerations

- Requires `devtunnel` CLI on PATH (already standard on Microsoft devboxes)
- Should validate `devtunnel user login` status before attempting to host
- Process cleanup on gateway shutdown — kill the devtunnel subprocess
- Persistent tunnel ID should be configurable (env var or CLI arg)
- Consider a `--tunnel-anonymous` flag for demos (maps to `--allow-anonymous`)

### Scope

- [ ] Create `MsClaw.Tunnel` class library project
- [ ] Implement `UserConfigLoader` in MsClaw.Core for `~/.msclaw/config.json`
- [ ] Add `--tunnel` flag to `start` command
- [ ] Implement `TunnelManager` service (start/stop/status)
- [ ] Support persistent tunnels — auto-create on first `--tunnel` run, save ID to `~/.msclaw/config.json`
- [ ] Run `devtunnel access create <id> --tenant` during setup to skip anti-phishing interstitial
- [ ] Register with `IHostApplicationLifetime.ApplicationStopping` for cleanup
- [ ] Log tunnel URL on successful connection
- [ ] Add `devtunnel` CLI detection (similar to `CliLocator` for copilot)
- [ ] Log SignalR transport type in `OnConnectedAsync` to monitor WebSocket vs fallback
- [ ] Documentation update
