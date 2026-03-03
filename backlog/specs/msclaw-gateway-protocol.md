# MsClaw Gateway Protocol

> SignalR-based real-time protocol for MsClaw agent communication.

## Overview

The MsClaw Gateway Protocol defines how **clients** (CLI, web UI, desktop apps),
**nodes** (device endpoints), and the **agent runtime** (MsClaw.Core + Copilot SDK)
communicate through a central **Gateway** process over ASP.NET Core SignalR.

Inspired by [OpenClaw's WebSocket protocol](https://github.com/openclaw/openclaw),
this spec replaces hand-rolled JSON frame dispatch with SignalR's native RPC,
streaming, grouping, and reconnection primitives.

## Goals

- Single Gateway process per host — owns messaging, agent runtime, and node coordination.
- Typed hub contracts — compile-time checked client ↔ server interfaces.
- Native streaming — `IAsyncEnumerable<AgentEvent>` for agent output instead of manually sequenced event frames.
- Role-based groups — operators and nodes receive only the events they need.
- Mind-backed identity — Gateway loads the agent's personality from a mind directory via MsClaw.Core.

## Architecture

```
                         ┌──────────────────────────────────────────────┐
                         │              MSCLAW GATEWAY                  │
                         │         (ASP.NET Core + SignalR)             │
                         │                                              │
                         │  ┌─────────────────────┐  ┌───────────────┐  │
                         │  │   AGENT RUNTIME     │  │  MIND (disk)  │  │
                         │  │                     │  │               │  │
                         │  │  CopilotClient      │  │  SOUL.md      │  │
                         │  │  (Copilot SDK)      │  │  .working-    │  │
                         │  │                     │  │   memory/     │  │
                         │  │  IdentityLoader ────┼──│  .github/     │  │
                         │  │  SessionManager     │  │   agents/     │  │
                         │  └────────┬────────────┘  └───────────────┘  │
                         │           │                                  │
                         │  ┌────────▼────────────────────────────────┐ │
                         │  │           GatewayHub                    │ │
                         │  │                                         │ │
                         │  │  Groups:                                │ │
                         │  │    "operators"  — CLI, Web, Desktop     │ │
                         │  │    "nodes"      — iOS, macOS, headless  │ │
                         │  │    "node:{id}"  — per-device targeting  │ │
                         │  └─────┬──────────────┬──────────────┬─────┘ │
                         └────────┼──────────────┼──────────────┼───────┘
                                  │              │              │
                          SignalR │      SignalR │      SignalR │
                                  │              │              │
                    ┌─────────────┘              │              └────────────┐
                    │                            │                           │
                    ▼                            ▼                           ▼
         ┌──────────────────┐       ┌──────────────────┐       ┌──────────────────┐
         │ CLIENT (operator)│       │   NODE (device)  │       │ CLIENT (operator)│
         │  CLI / Web / App │       │ iOS / macOS / etc│       │    Automations   │
         └──────────────────┘       └──────────────────┘       └──────────────────┘
```

## Hub Contracts

### Server → Client (IGatewayClient)

The interface the server uses to push events to connected clients.

```csharp
/// <summary>
/// Events the Gateway pushes to connected clients.
/// Implementations are auto-generated on the client side by SignalR.
/// </summary>
public interface IGatewayClient
{
    // Presence
    Task OnPresence(PresenceEvent e);

    // Chat
    Task OnChatMessage(ChatMessageEvent e);

    // Exec approvals
    Task OnApprovalRequested(ExecApprovalEvent e);
    Task OnApprovalResolved(ExecApprovalResolvedEvent e);

    // Node invoke (pushed to nodes only)
    Task OnNodeInvokeRequest(NodeInvokeRequestEvent e);

    // Device pairing
    Task OnPairRequested(DevicePairRequestedEvent e);
    Task OnPairResolved(DevicePairResolvedEvent e);

    // System
    Task OnShutdown(ShutdownEvent e);
}
```

### Client → Server (GatewayHub)

Hub methods clients can invoke. Authorization is per-method via policies.

```csharp
public class GatewayHub : Hub<IGatewayClient>
{
    // ── Agent ────────────────────────────────────────────────────
    // Streams agent events (lifecycle, assistant deltas, tool events)
    IAsyncEnumerable<AgentEvent> Agent(AgentRequest request, CancellationToken ct);
    Task<AgentIdentity> AgentIdentity(AgentIdentityRequest request);

    // ── Messaging ────────────────────────────────────────────────
    Task<SendResult> Send(SendRequest request);
    Task<PollResult> Poll(PollRequest request);

    // ── Chat ─────────────────────────────────────────────────────
    Task<ChatHistory> ChatHistory(ChatHistoryRequest request);
    Task ChatAbort(ChatAbortRequest request);

    // ── Sessions ─────────────────────────────────────────────────
    Task<SessionListResult> SessionsList();
    Task<SessionPreview> SessionsPreview(SessionsPreviewRequest request);
    Task SessionsReset(SessionsResetRequest request);
    Task SessionsDelete(SessionsDeleteRequest request);

    // ── Mind ─────────────────────────────────────────────────────
    Task<MindValidationResult> MindValidate();
    Task<string> MindReadFile(MindReadFileRequest request);

    // ── Models ───────────────────────────────────────────────────
    Task<ModelsListResult> ModelsList();

    // ── Config ───────────────────────────────────────────────────
    Task<ConfigSnapshot> ConfigGet();
    Task ConfigSet(ConfigSetRequest request);

    // ── Nodes ────────────────────────────────────────────────────
    Task<NodeListResult> NodesList();
    Task<NodeInvokeResult> NodeInvoke(NodeInvokeRequest request);
    Task NodeInvokeResult(NodeInvokeResultEvent result);  // node responds
    Task RegisterNode(NodeRegistration registration);

    // ── Devices / Pairing ────────────────────────────────────────
    Task<DevicePairListResult> DevicePairList();
    Task DevicePairApprove(DevicePairApproveRequest request);
    Task DevicePairReject(DevicePairRejectRequest request);

    // ── Exec Approvals ───────────────────────────────────────────
    Task ExecApprovalResolve(ExecApprovalResolveRequest request);

    // ── System ───────────────────────────────────────────────────
    Task<HealthResult> Health();
    Task<PresenceResult> Presence();
}
```

## Connection Lifecycle

### 1. Connect + Authenticate

SignalR handles transport negotiation. Authentication happens via middleware
before the hub connection is established.

```
CLIENT                                   GATEWAY
  │                                         │
  │── SignalR negotiate ───────────────────►│  Transport negotiation
  │◄── WebSocket upgrade ─────────────────│  (auto-fallback to SSE/LP)
  │                                         │
  │   [Auth middleware validates token]      │
  │   [OnConnectedAsync assigns groups]     │
  │                                         │
  │◄── OnPresence(snapshot) ──────────────│  Current system state
  │                                         │
```

### 2. Authentication Options

| Method | When |
|--------|------|
| Bearer token in query string | `?access_token=<gateway_token>` |
| Bearer token in header | `Authorization: Bearer <token>` |
| Device token (post-pairing) | Custom header or query param |

### 3. Group Assignment (OnConnectedAsync)

```csharp
public override async Task OnConnectedAsync()
{
    var role = Context.User?.FindFirst("role")?.Value ?? "operator";
    var deviceId = Context.User?.FindFirst("device_id")?.Value;

    // Role group
    await Groups.AddToGroupAsync(Context.ConnectionId,
        role == "node" ? "nodes" : "operators");

    // Per-device group (for targeted node invocations)
    if (deviceId is not null)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"node:{deviceId}");
    }

    // Push initial presence snapshot
    await Clients.Caller.OnPresence(BuildPresenceSnapshot());
}
```

## Agent Streaming

The core interaction — asking the agent to process a message and streaming
the response back.

### Flow

```
CLIENT                                   GATEWAY
  │                                         │
  │── Agent(request) ─────────────────────►│  Hub method invocation
  │                                         │  Gateway resolves session,
  │                                         │  loads mind identity,
  │                                         │  calls CopilotClient
  │                                         │
  │◄── yield AgentEvent(lifecycle:start) ──│
  │◄── yield AgentEvent(assistant:delta) ──│  IAsyncEnumerable<AgentEvent>
  │◄── yield AgentEvent(assistant:delta) ──│  streaming chunks
  │◄── yield AgentEvent(tool:start) ───────│
  │◄── yield AgentEvent(tool:end) ─────────│
  │◄── yield AgentEvent(assistant:delta) ──│
  │◄── yield AgentEvent(lifecycle:end) ────│  Stream completes
  │                                         │
```

### AgentEvent Schema

```csharp
public record AgentEvent
{
    public required string RunId { get; init; }
    public required int Seq { get; init; }
    public required AgentEventStream Stream { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required object Data { get; init; }
}

public enum AgentEventStream
{
    Lifecycle,   // start, end, error
    Assistant,   // streaming text deltas from the model
    Tool         // tool execution start, update, end
}
```

### Server Implementation Sketch

```csharp
public async IAsyncEnumerable<AgentEvent> Agent(
    AgentRequest request,
    [EnumeratorCancellation] CancellationToken ct)
{
    var mind = new MindReader(mindRoot);
    var identity = new IdentityLoader();
    var systemMessage = await identity.LoadSystemMessageAsync(mindRoot, ct);

    await using var session = await copilotClient.CreateSessionAsync(new SessionConfig
    {
        Model = request.Model ?? "gpt-5",
        Streaming = true,
        SystemMessage = new SystemMessageConfig
        {
            Mode = SystemMessageMode.Append,
            Content = systemMessage
        }
    });

    var runId = Guid.NewGuid().ToString("N");
    var seq = 0;

    yield return new AgentEvent
    {
        RunId = runId,
        Seq = seq++,
        Stream = AgentEventStream.Lifecycle,
        Timestamp = DateTimeOffset.UtcNow,
        Data = new { Phase = "start" }
    };

    var done = new TaskCompletionSource<List<AgentEvent>>();
    var buffer = new Channel<AgentEvent>(Channel.CreateUnbounded<AgentEvent>());

    session.On(evt =>
    {
        switch (evt)
        {
            case AssistantMessageDeltaEvent delta:
                buffer.Writer.TryWrite(new AgentEvent
                {
                    RunId = runId, Seq = seq++,
                    Stream = AgentEventStream.Assistant,
                    Timestamp = DateTimeOffset.UtcNow,
                    Data = new { Content = delta.Data.DeltaContent }
                });
                break;

            case ToolExecutionStartEvent tool:
                buffer.Writer.TryWrite(new AgentEvent
                {
                    RunId = runId, Seq = seq++,
                    Stream = AgentEventStream.Tool,
                    Timestamp = DateTimeOffset.UtcNow,
                    Data = new { Phase = "start", Tool = tool.Data }
                });
                break;

            case SessionIdleEvent:
                buffer.Writer.Complete();
                break;
        }
    });

    await session.SendAsync(new MessageOptions { Prompt = request.Message });

    await foreach (var agentEvent in buffer.Reader.ReadAllAsync(ct))
    {
        yield return agentEvent;
    }

    yield return new AgentEvent
    {
        RunId = runId,
        Seq = seq++,
        Stream = AgentEventStream.Lifecycle,
        Timestamp = DateTimeOffset.UtcNow,
        Data = new { Phase = "end" }
    };
}
```

## Roles, Groups, and Authorization

### Groups

| Group | Members | Receives |
|-------|---------|----------|
| `operators` | CLI, Web UI, Desktop | Presence, chat, approvals, agent events |
| `nodes` | iOS, macOS, Android, headless | Node invoke requests |
| `node:{deviceId}` | Single device | Targeted invoke for that device |

### Authorization Policies

| Policy | Required Scope | Protects |
|--------|----------------|----------|
| `OperatorRead` | `operator.read` | Health, Presence, SessionsList, ModelsList |
| `OperatorWrite` | `operator.write` | Agent, Send, Poll, ChatAbort |
| `OperatorAdmin` | `operator.admin` | ConfigSet, MindValidate |
| `OperatorApprovals` | `operator.approvals` | ExecApprovalResolve |
| `NodeRole` | `role=node` | RegisterNode, NodeInvokeResult |

## Mind Integration

The Gateway uses MsClaw.Core to manage the agent's identity and workspace:

| MsClaw.Core Type | Gateway Usage |
|------------------|---------------|
| `MsClawClientFactory` | Creates the singleton `CopilotClient` pointed at the mind root |
| `IdentityLoader` | Assembles `SOUL.md` + agent files into the system message |
| `MindValidator` | Validates mind structure on startup and via `MindValidate()` |
| `MindReader` | Reads mind files (with path-traversal protection) for runtime context |
| `MindScaffold` | Creates new mind directories (setup wizard) |

### Startup Flow

```
1. Validate mind directory (MindValidator)
2. Load system message (IdentityLoader)
3. Create CopilotClient singleton (MsClawClientFactory)
4. Start SignalR Gateway on configured host:port
5. Accept connections → assign groups → stream agent events
```

## Configuration

```json
{
  "MsClaw": {
    "Gateway": {
      "BindHost": "127.0.0.1",
      "Port": 18789,
      "Token": null,
      "MindRoot": "~/src/ernist",
      "AutoGitPull": false
    },
    "Agent": {
      "DefaultModel": "gpt-5",
      "TimeoutSeconds": 600
    }
  }
}
```

## Wire Comparison: OpenClaw WS vs MsClaw SignalR

| Concern | OpenClaw (raw WS) | MsClaw (SignalR) |
|---------|-------------------|------------------|
| Transport | WebSocket only | WS + SSE + Long-polling fallback |
| Framing | Hand-rolled `{type, id, method, params}` | Built-in RPC dispatch |
| Streaming | Manual `event` frames with `seq` counter | `IAsyncEnumerable<T>` |
| Heartbeat | Custom `tick` events | Built-in keepalive |
| Reconnection | Client-managed | Automatic with state recovery |
| Role routing | Manual connection iteration | `Groups` |
| Auth | Custom `connect` handshake | ASP.NET auth middleware |
| Schema | TypeBox → JSON Schema → codegen | C# interfaces → typed hub proxy |
| Type safety | Runtime JSON validation | Compile-time contracts |

## Open Questions

- [ ] Should the Gateway support multiple minds (multi-agent)?
- [ ] How should node pairing/approval work — reuse OpenClaw's challenge-sign model or simplify?
- [ ] Should we support inter-node communication (node A invoking node B through Gateway)?
- [ ] Persistence layer for sessions — file-based JSONL (like OpenClaw) or SQLite?
- [ ] Should the Gateway expose an HTTP REST API alongside SignalR for simple integrations?

## Future Considerations

- **Skills system**: Loading and invoking skills from the mind workspace.
- **Cron/scheduled tasks**: Agent runs on a schedule.
- **Channel adapters**: WhatsApp, Telegram, Discord as pluggable channels.
- **Multi-tenant**: Multiple minds per gateway for multi-agent setups.
