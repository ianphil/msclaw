# Data Model: Gateway SignalR + OpenResponses

## Design Principle

> **Never Rewrite What You've Already Imported.**
> The Copilot SDK provides `SessionEvent`, `AssistantMessageEvent`, `AssistantMessageDeltaEvent`,
> `ToolExecutionStartEvent`, `ToolExecutionCompleteEvent`, `SessionIdleEvent`, `SessionErrorEvent`.
> These are the data types that flow through the system. We document only the NEW entities we create.

## SDK Types (pass-through, NOT wrapped)

The following SDK types (`GitHub.Copilot.SDK`) flow through the hub and middleware without transformation:

| SDK Type | Purpose |
|----------|---------|
| `SessionEvent` | Base event type received by `session.On(evt => ...)` |
| `AssistantMessageEvent` | Complete assistant response (`.Data.Content`) |
| `AssistantMessageDeltaEvent` | Incremental text chunk (`.Data.DeltaContent`) |
| `AssistantReasoningEvent` | Complete reasoning content |
| `AssistantReasoningDeltaEvent` | Incremental reasoning chunk |
| `ToolExecutionStartEvent` | Tool invocation began |
| `ToolExecutionCompleteEvent` | Tool invocation finished |
| `SessionStartEvent` | Session started |
| `SessionIdleEvent` | Processing complete (terminal event) |
| `SessionErrorEvent` | Error occurred (`.Data.Message`) |
| `UserMessageEvent` | User message sent |
| `CopilotSession` | Active session instance |

These types are documented here for reference but are NOT redefined in our codebase.

## New Entities (genuinely new logic)

### CallerRegistry State (implements IConcurrencyGate + ISessionMap)

A single `CallerRegistry` class implements both `IConcurrencyGate` and `ISessionMap` with two ConcurrentDictionaries. Consumers depend only on the interface they use (ISP). No custom entity classes — just dictionary entries.

**Session Map**: `ConcurrentDictionary<string, string>`
| Key | Value | Description |
|-----|-------|-------------|
| CallerKey (string) | SessionId (string) | Maps a caller (ConnectionId or HTTP-derived key) to a Copilot SDK session ID |

**Concurrency Map**: `ConcurrentDictionary<string, SemaphoreSlim>`
| Key | Value | Description |
|-----|-------|-------------|
| CallerKey (string) | SemaphoreSlim(1) | Per-caller concurrency gate; acquired before a run, released after |

**Invariants:**
- Both maps MUST have the same keys (a caller always has both a session and a semaphore)
- TryAcquire MUST be non-blocking (`semaphore.Wait(0)`)
- Release MUST always be called after a run completes (success, abort, or error)

### ResponseRequest (OpenResponses)

Incoming HTTP request to POST /v1/responses.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| Model | string | Yes | - | Model identifier (passed to runtime) |
| Input | string or array | Yes | - | User prompt (string) or message array |
| Stream | bool | No | false | Whether to stream response as SSE |
| User | string? | No | null | Stable caller key for session routing |

**Invariants:**
- Model MUST be a non-empty string
- Input MUST be non-empty (string or at least one message)
- When Input is an array, each item MUST have type, role, and content fields

### ResponseObject (OpenResponses)

Outgoing HTTP response from POST /v1/responses.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| Object | string | Yes | "response" | Always "response" |
| Id | string | Yes | - | Unique response identifier |
| Status | string | Yes | - | "completed", "failed", or "in_progress" |
| Output | OutputItem[] | Yes | - | Array of output items (messages) |
| Error | ErrorDetail? | No | null | Error detail (for failed status) |

**Invariants:**
- Object MUST always be "response"
- Status MUST be one of: completed, failed, in_progress
- Output MUST contain at least one item when Status is completed

### OutputItem (OpenResponses)

A single output entry in a response.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| Type | string | Yes | "message" | Item type (always "message" for v1) |
| Role | string | Yes | "assistant" | Message role |
| Content | ContentPart[] | Yes | - | Array of content parts |

### ContentPart (OpenResponses)

A content fragment within an output item.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| Type | string | Yes | "output_text" | Content type |
| Text | string | Yes | - | Text content |

## State Transitions

### Agent Run Lifecycle

```
                  SendAsync(callerKey, prompt)
                          │
                          ▼
                 ┌─────────────────┐
                 │   Acquiring     │
                 │  (SemaphoreSlim)│
                 └────────┬────────┘
                          │
              ┌───────────┼───────────┐
              │           │           │
              ▼           ▼           ▼
        ┌──────────┐ ┌────────┐ ┌────────────┐
        │ Rejected │ │Running │ │  Queued    │
        │  (409)   │ │        │ │  (future)  │
        └──────────┘ └───┬────┘ └────────────┘
                         │
              ┌──────────┼──────────┐
              │          │          │
              ▼          ▼          ▼
        ┌──────────┐ ┌────────┐ ┌────────┐
        │ Completed│ │Aborted │ │ Failed │
        └──────────┘ └────────┘ └────────┘
```

| State | Description |
|-------|-------------|
| Acquiring | Attempting to acquire per-caller semaphore |
| Rejected | Semaphore not available (another run active), returns error |
| Running | Semaphore acquired, SDK session active, events streaming |
| Completed | SDK session idle, final message emitted, semaphore released |
| Aborted | Operator requested abort, lifecycle-abort emitted, semaphore released |
| Failed | Runtime error, lifecycle-error emitted, semaphore released |

### Gateway Hosted Service State (existing)

```
Starting ──► Validating ──► Ready ──► Stopping ──► Stopped
                   │
                   └──► Failed
```

No changes to the existing state machine. The hosted service transitions to Ready after CopilotClient starts and IAgentRuntime is initialized.

## Data Flow

### Startup Sequence

```
GatewayHostedService.StartAsync()
  │
  ├─ 1. MindValidator.Validate(mindPath)
  │     └─ Errors? → State = Failed, return
  │
  ├─ 2. IdentityLoader.LoadSystemMessageAsync(mindPath)
  │     └─ Returns systemMessage string
  │
  ├─ 3. CopilotClient = factory(mindPath)
  │     └─ client.StartAsync()
  │
  ├─ 4. AgentRuntime = new AgentRuntime(client, systemMessage)
  │     └─ Registered in DI as singleton
  │
  └─ 5. State = Ready
```

### Health Check Flow

```
GET /health
  └─ Always returns { status: "Healthy" } 200 if process is alive

GET /health/ready
  │
  ├─ Check: IGatewayHostedService.IsReady?
  │   └─ No → { status: "Unhealthy", component: "hosted-service", error: "..." } 503
  │
  ├─ Check: IAgentRuntime.IsReady?
  │   └─ No → { status: "Unhealthy", component: "agent-runtime", error: "..." } 503
  │
  └─ Yes → { status: "Healthy" } 200
```

## Validation Summary

| Entity | Rule | Error |
|--------|------|-------|
| StreamEvent | SequenceNumber must increase | InvalidOperationException |
| StreamEvent | Terminal event must end each run | Channel completed without terminal |
| CallerSession | CallerKey must be unique | ArgumentException (duplicate key) |
| CallerSession | Semaphore must be acquired before run | ConcurrencyRejectedException |
| ResponseRequest | Model must be non-empty | 400 Bad Request |
| ResponseRequest | Input must be non-empty | 400 Bad Request |
| ResponseObject | Object must be "response" | Serialization invariant |
