# Interface Contracts

## Design Principle: SDK Types Pass Through

The Copilot SDK (`GitHub.Copilot.SDK`) provides `CopilotClient`, `CopilotSession`, `SessionEvent`, and all event subtypes. These are the types that flow through the system. We don't wrap them — we add only what the SDK doesn't have: caller-key mapping and per-caller concurrency.

## IConcurrencyGate

Enforces one active run per caller. Separated from session mapping per Interface Segregation Principle — concurrency strategy (reject → queue → replace) changes independently from caller-to-session mapping.

```csharp
/// <summary>
/// Enforces per-caller concurrency — one active run at a time.
/// Separated from session mapping per ISP: concurrency strategy changes
/// independently from caller-to-session mapping.
/// </summary>
public interface IConcurrencyGate
{
    /// <summary>
    /// Attempts to acquire the per-caller concurrency slot.
    /// Returns false if the caller already has an active run (reject mode).
    /// </summary>
    bool TryAcquire(string callerKey);

    /// <summary>Releases the per-caller concurrency slot.</summary>
    void Release(string callerKey);
}
```

## ISessionMap

Maps caller keys to SDK session IDs. Separated from concurrency gating per ISP — a mapping change (e.g., multi-session per caller) doesn't affect the gate, and a gate change (reject → queue) doesn't affect the mapping.

```csharp
/// <summary>
/// Maps caller keys to SDK session IDs.
/// Separated from concurrency gating per ISP: mapping structure changes
/// independently from concurrency strategy.
/// </summary>
public interface ISessionMap
{
    /// <summary>Gets the SDK session ID for a caller, or null if no session exists.</summary>
    string? GetSessionId(string callerKey);

    /// <summary>Associates a caller key with an SDK session ID.</summary>
    void SetSessionId(string callerKey, string sessionId);

    /// <summary>Lists all registered caller keys and their session IDs.</summary>
    IReadOnlyList<(string CallerKey, string SessionId)> ListCallers();
}
```

A single `CallerRegistry` class implements both interfaces with two `ConcurrentDictionary` fields:
- `ConcurrentDictionary<string, SemaphoreSlim>` — callerKey → SemaphoreSlim(1) (for `IConcurrencyGate`)
- `ConcurrentDictionary<string, string>` — callerKey → sessionId (for `ISessionMap`)

Consumers depend only on the interface they use. `TryAcquire` calls `semaphore.Wait(0)` (non-blocking). `Release` calls `semaphore.Release()`.

## IGatewayClient (Extended) + IGatewaySession

The existing `IGatewayClient` provides a testable boundary around the SDK's `CopilotClient`. We extend it with session operations rather than exposing `CopilotClient` directly via DI. This preserves the Dependency Rule (hub depends inward on an interface, not outward on an SDK concrete class) and keeps tests fast and deterministic.

SDK **data types** (`SessionEvent`, `SessionConfig`, `MessageOptions`) flow through — we don't wrap DTOs. SDK **service types** (`CopilotClient`, `CopilotSession`) stay behind interfaces — that's where the testability seam lives.

```csharp
/// <summary>
/// Testable boundary around the Copilot SDK client.
/// Extends the existing IGatewayClient with session lifecycle operations.
/// SDK data types flow through; only the service is abstracted.
/// </summary>
public interface IGatewayClient : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task<IGatewaySession> CreateSessionAsync(SessionConfig? config = null);
    Task<IGatewaySession> ResumeSessionAsync(string sessionId, ResumeSessionConfig? config = null);
    Task<IReadOnlyList<SessionMetadata>> ListSessionsAsync();
    Task DeleteSessionAsync(string sessionId);
}

/// <summary>
/// Testable boundary around the Copilot SDK session.
/// SDK data types (SessionEvent, MessageOptions) pass through — only the service is abstracted.
/// </summary>
public interface IGatewaySession : IAsyncDisposable
{
    string SessionId { get; }
    IDisposable On(Action<SessionEvent> handler);
    Task SendAsync(MessageOptions options);
    Task AbortAsync();
    Task<IReadOnlyList<SessionEvent>> GetMessagesAsync();
}
```

The existing `CopilotGatewayClient` wrapper is extended to implement the new methods by delegating to the SDK. A new `CopilotGatewaySession` wrapper does the same for `CopilotSession`. Both are thin delegation — not rewrites.

## IGatewayHubClient

```csharp
/// <summary>
/// Strongly-typed contract for server-to-client push methods on the Gateway hub.
/// </summary>
public interface IGatewayHubClient
{
    /// <summary>Receives an SDK session event during an agent run.</summary>
    Task ReceiveEvent(SessionEvent sessionEvent);

    /// <summary>Receives a presence update (connected/disconnected operators).</summary>
    Task ReceivePresence(PresenceSnapshot presence);
}
```

Note: `SessionEvent` is the SDK's own base event type from `GitHub.Copilot.SDK`. No custom wrapper.

## SessionEventBridge

The Copilot SDK emits events via push (`session.On(evt => ...)`), but SignalR streaming and OpenResponses SSE both need pull (`IAsyncEnumerable<T>`). This bridge converts push to pull using `Channel<SessionEvent>`.

Extracted as a shared utility because **both** the SignalR hub and the OpenResponses middleware need the same bridge. Inline duplication would violate DRY.

```csharp
/// <summary>
/// Bridges SDK push-based events to IAsyncEnumerable pull-based consumption.
/// Used by both GatewayHub (via AgentMessageService) and OpenResponsesMiddleware.
/// </summary>
internal static class SessionEventBridge
{
    /// <summary>
    /// Subscribes to session events and returns an async enumerable that yields them.
    /// Completes when SessionIdleEvent or SessionErrorEvent fires, or when cancelled.
    /// </summary>
    public static (IDisposable Subscription, IAsyncEnumerable<SessionEvent> Events)
        Bridge(IGatewaySession session, CancellationToken cancellationToken);
}
```

Implementation: creates an unbounded `Channel<SessionEvent>`. The SDK callback writes each event; terminal events (`SessionIdleEvent`, `SessionErrorEvent`) write then complete the channel. Cancellation completes the channel. Reader side is `channel.Reader.ReadAllAsync()`.

## AgentMessageService

Orchestrates the full send-message flow: acquire concurrency gate → resolve or create session → bridge events → yield to caller → release gate. Extracted from the hub so that:

1. The hub is a **thin routing layer** (one-liner delegations)
2. The orchestration is **testable without SignalR infrastructure**
3. The OpenResponses middleware can reuse the **same orchestration logic**

```csharp
/// <summary>
/// Orchestrates message sending: concurrency → session → bridge → stream → release.
/// Shared by both GatewayHub and OpenResponsesMiddleware.
/// </summary>
internal sealed class AgentMessageService(
    IConcurrencyGate gate,
    ISessionMap sessions,
    IGatewayClient client,
    string systemMessage)
{
    /// <summary>
    /// Sends a prompt to the agent and streams back SDK events.
    /// Acquires the concurrency gate for the caller, creates or resumes a session,
    /// bridges SDK events to IAsyncEnumerable, and releases the gate on completion.
    /// </summary>
    public async IAsyncEnumerable<SessionEvent> SendAsync(
        string callerKey,
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken);
}
```

## GatewayHub Methods

The hub is a **thin routing layer**. Every method is a one-liner delegation to `AgentMessageService` or `IGatewayClient`. No business logic, no orchestration, no concurrency management in the hub itself.

```csharp
/// <summary>
/// SignalR hub for real-time agent interaction.
/// Thin routing layer — delegates to AgentMessageService and IGatewayClient.
/// </summary>
public sealed class GatewayHub(
    AgentMessageService messageService,
    IGatewayClient client,
    ISessionMap sessions) : Hub<IGatewayHubClient>
{
    /// <summary>
    /// Sends a message to the agent and streams back SDK events.
    /// Delegates entirely to AgentMessageService.
    /// </summary>
    public IAsyncEnumerable<SessionEvent> SendMessage(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        => messageService.SendAsync(Context.ConnectionId, prompt, cancellationToken);

    /// <summary>Creates a new session for the calling operator.</summary>
    public async Task<string> CreateSession();

    /// <summary>Lists all tracked sessions.</summary>
    public Task<IReadOnlyList<SessionMetadata>> ListSessions()
        => client.ListSessionsAsync();

    /// <summary>Retrieves conversation history for the caller's session.</summary>
    public async Task<IReadOnlyList<SessionEvent>> GetHistory();

    /// <summary>Aborts the active run for the caller's session.</summary>
    public async Task AbortResponse();
}
```

## Health Endpoint Contracts

```
GET /health

Response (200 OK — always, if process is alive):
{ "status": "Healthy" }

GET /health/ready

Response (200 OK — runtime ready):
{ "status": "Healthy" }

Response (503 Service Unavailable — not ready):
{ "status": "Unhealthy", "component": "hosted-service", "error": "CopilotClient not started" }
```

## OpenResponses Endpoint Contract

The OpenResponses middleware maps SDK events to OpenResponses JSON. This mapping IS genuinely new logic — the SDK knows nothing about the OpenResponses spec.

```
POST /v1/responses
Content-Type: application/json

Request:
{
  "model": "gpt-5",
  "input": "What is 2+2?",
  "stream": false
}

Response (200 OK — non-streaming):
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

SSE Response (200 OK — streaming, stream: true):
Content-Type: text/event-stream

event: response.created
data: {"object":"response","id":"resp_abc123","status":"in_progress","output":[]}

event: response.output_text.delta
data: {"type":"output_text","delta":"2+2"}

event: response.output_text.delta
data: {"type":"output_text","delta":" equals 4."}

event: response.output_text.done
data: {"type":"output_text","text":"2+2 equals 4."}

event: response.completed
data: {"object":"response","id":"resp_abc123","status":"completed","output":[...]}

data: [DONE]

Error Response (409 Conflict):
{
  "error": {
    "code": "conflict",
    "message": "Caller already has an active run.",
    "request_id": "req_xyz789"
  }
}
```

## OpenResponses DTOs (genuinely new — no SDK equivalent)

```csharp
/// <summary>OpenResponses request body for POST /v1/responses.</summary>
public sealed record ResponseRequest
{
    public required string Model { get; init; }
    public required JsonElement Input { get; init; }  // string or message array
    public bool Stream { get; init; }
    public string? User { get; init; }
}

/// <summary>OpenResponses response object.</summary>
public sealed record ResponseObject
{
    public string Object { get; init; } = "response";
    public required string Id { get; init; }
    public required string Status { get; init; }
    public required IReadOnlyList<OutputItem> Output { get; init; }
}

/// <summary>An output item in the response.</summary>
public sealed record OutputItem
{
    public string Type { get; init; } = "message";
    public string Role { get; init; } = "assistant";
    public required IReadOnlyList<ContentPart> Content { get; init; }
}

/// <summary>A content fragment within an output item.</summary>
public sealed record ContentPart
{
    public string Type { get; init; } = "output_text";
    public required string Text { get; init; }
}
```

## SDK Event → OpenResponses Mapping

This mapping is the core of the OpenResponses middleware. It transforms SDK push events into OpenResponses SSE events:

| SDK Event Type | OpenResponses SSE Event |
|---------------|------------------------|
| (run starts) | `response.created` |
| `AssistantMessageDeltaEvent` | `response.output_text.delta` |
| `AssistantMessageEvent` | `response.output_text.done` + `response.completed` |
| `SessionIdleEvent` | `data: [DONE]` |
| `SessionErrorEvent` | `response.failed` + `data: [DONE]` |

This is genuinely new logic — the SDK has no concept of OpenResponses. The middleware earns its existence.

## DI Registration

```csharp
// In StartCommand.ConfigureServices:
services.AddSingleton<CallerRegistry>();
services.AddSingleton<IConcurrencyGate>(sp => sp.GetRequiredService<CallerRegistry>());
services.AddSingleton<ISessionMap>(sp => sp.GetRequiredService<CallerRegistry>());
// IGatewayClient already registered via hosted service (extended with session ops)
// AgentMessageService registered as singleton, receives system message from hosted service
services.AddSingleton<AgentMessageService>();
```
