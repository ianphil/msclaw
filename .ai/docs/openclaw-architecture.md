# OpenClaw Architecture — Gateway, Node, Client, Agent

Reference: [openclaw/openclaw](https://github.com/openclaw/openclaw) · `docs/concepts/architecture.md`

```
                        ┌─────────────────────────────────────────────────────────────┐
                        │                      GATEWAY (daemon)                       │
                        │                    :18789  (WS + HTTP)                       │
                        │                                                             │
                        │  ┌───────────────────────┐   ┌────────────────────────────┐ │
                        │  │     AGENT RUNTIME      │   │    MESSAGING CHANNELS      │ │
                        │  │    (embedded pi-mono)   │   │                            │ │
                        │  │                         │   │  WhatsApp (Baileys)        │ │
                        │  │  prompt assembly        │   │  Telegram  (grammY)        │ │
                        │  │  model inference        │   │  Slack / Discord           │ │
                        │  │  tool execution ────────┼───│  Signal / iMessage         │ │
                        │  │  session management     │   │  WebChat                   │ │
                        │  │  streaming replies      │   │                            │ │
                        │  └──────────┬──────────────┘   └────────────────────────────┘ │
                        │             │ tool calls                                     │
                        │  ┌──────────▼──────────────┐   ┌────────────────────────────┐ │
                        │  │        SKILLS           │   │   CANVAS HOST              │ │
                        │  │  bundled / managed /     │   │  /__openclaw__/canvas/     │ │
                        │  │  workspace skills        │   │  /__openclaw__/a2ui/       │ │
                        │  └─────────────────────────┘   └────────────────────────────┘ │
                        └────────┬──────────────┬──────────────────┬───────────────────┘
                                 │              │                  │
                        WebSocket│     WebSocket│         WebSocket│
                        (JSON)   │     (JSON)   │         (JSON)   │
                                 │  role:"node" │                  │
                 ┌───────────────┘              │                  └────────────────┐
                 │                              │                                   │
                 ▼                              ▼                                   ▼
  ┌──────────────────────────┐  ┌──────────────────────────┐  ┌──────────────────────────┐
  │      CLIENT (operator)    │  │      NODE (device)        │  │      CLIENT (operator)    │
  │                           │  │                           │  │                           │
  │  macOS app / CLI /        │  │  macOS / iOS / Android /  │  │  Web UI / Automations     │
  │  Web Admin                │  │  Headless                 │  │                           │
  │                           │  │                           │  │                           │
  │  ► health, status         │  │  ► camera.*               │  │  ► send, agent            │
  │  ► send, agent            │  │  ► canvas.*               │  │  ► subscribe events       │
  │  ► subscribe events       │  │  ► screen.record          │  │                           │
  │                           │  │  ► location.get           │  │                           │
  │  device identity          │  │                           │  │  device identity           │
  │  + pairing                │  │  device identity          │  │  + pairing                │
  └──────────────────────────┘  │  + pairing + caps          │  └──────────────────────────┘
                                 └──────────────────────────┘
```

## Flow Summary

- **Clients** ask the Gateway to do things (send messages, run the agent)
- **Gateway** runs the **Agent** internally — prompt → model → tools → reply
- **Nodes** provide hardware capabilities the Agent's tools can reach through the Gateway
- All connections are **WebSocket JSON frames** with mandatory handshake + device pairing

## Key Concepts

| Component | Role | Lives Where |
|-----------|------|-------------|
| **Gateway** | Central daemon — messaging hub, agent host, WS API server | Single process per host |
| **Agent** | Embedded runtime (pi-mono) — prompt assembly, inference, tool execution | Inside the Gateway |
| **Node** | Device endpoint providing hardware capabilities | macOS / iOS / Android / headless |
| **Client** | Operator interface for control-plane actions | macOS app / CLI / Web UI |

## Wire Protocol

- Transport: WebSocket, text frames with JSON payloads
- First frame **must** be `connect`
- Requests: `{type:"req", id, method, params}` → `{type:"res", id, ok, payload|error}`
- Events: `{type:"event", event, payload, seq?, stateVersion?}`
- Auth token via `OPENCLAW_GATEWAY_TOKEN` or `--token`
- Nodes include `role: "node"` plus caps/commands/permissions in `connect`
