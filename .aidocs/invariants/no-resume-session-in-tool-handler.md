# Invariant: No ResumeSessionAsync from within a tool handler

## Rule

Never call `CopilotClient.ResumeSessionAsync` (or any SDK JSON-RPC method that
blocks on the CLI process) from inside a tool handler function. This causes a
deadlock.

## Why

The Copilot CLI uses JSON-RPC over stdio. When the CLI dispatches a tool call to
the SDK client, it **blocks waiting for the tool result** before processing any
other RPC requests. If the tool handler sends a `session.resume` RPC request back
to the CLI, both sides are waiting on each other — classic deadlock.

```
CLI                          SDK Client
 │                              │
 ├── tool.call("expand_tools") ──►│
 │   (blocked, waiting result)  │
 │                              ├── session.resume ──►│
 │                              │   (blocked, waiting │
 │                              │    CLI to respond)   │
 │◄─────────── DEADLOCK ────────►│
```

## What to do instead

Use **deferred tool sync**: the tool handler adds tools to an in-memory list and
returns immediately. Before the *next* `session.SendAsync`, the gateway calls
`ResumeSessionAsync` with the updated tool list. This means tools loaded via
`expand_tools` are callable on the **next message**, not the current one.

The expand_tools result includes a `Note` field telling the agent to inform the
user that tools will be available on the next turn.

## Affected code

- `ToolExpander.ExpandToolsAsync` — must NOT call ResumeSessionAsync
- `AgentMessageService.SyncToolsIfNeededAsync` — calls ResumeSession before SendAsync
- `AgentMessageService.ToolSyncState` — tracks tool count vs synced count

## SDK evidence

From `C:\src\copilot-sdk\dotnet\src`:

- `Session.RegisterTools()` copies tools into a private `Dictionary<string, AIFunction>`
  (snapshot, not a reference to the original list)
- `ResumeSessionAsync` sends `session.resume` RPC and creates a **new** session object
- The SDK has no public API for adding tools to a running session without ResumeSession
- Tool dispatch (`tool.call` handler) looks up tools in the session's internal dictionary,
  not the original config list

## How to verify

1. Start gateway: `dotnet run --project src\MsClaw.Gateway -- start --mind C:\src\ernist`
2. Send: "Use expand_tools to load echo_text"
3. Agent responds with note about next-message availability
4. Send: "Now use echo_text with hello world"
5. Agent successfully calls echo_text and returns "Echo: hello world"

If step 4 hangs forever, the deferred sync is broken.
If step 2 hangs forever, someone re-introduced ResumeSessionAsync in the tool handler.
