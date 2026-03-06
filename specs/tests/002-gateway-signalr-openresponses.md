---
target:
  - src/MsClaw.Gateway/Services/ICallerRegistry.cs
  - src/MsClaw.Gateway/Services/CallerRegistry.cs
  - src/MsClaw.Gateway/Hubs/GatewayHub.cs
  - src/MsClaw.Gateway/Hubs/IGatewayHubClient.cs
  - src/MsClaw.Gateway/Commands/StartCommand.cs
  - src/MsClaw.Gateway/Hosting/GatewayHostedService.cs
  - src/MsClaw.OpenResponses/OpenResponsesMiddleware.cs
  - src/MsClaw.OpenResponses/Models/ResponseRequest.cs
  - src/MsClaw.OpenResponses/Models/ResponseObject.cs
  - src/MsClaw.OpenResponses/Extensions/EndpointRouteBuilderExtensions.cs
  - src/MsClaw.Gateway/MsClaw.Gateway.csproj
  - src/MsClaw.OpenResponses/MsClaw.OpenResponses.csproj
---

# Gateway SignalR + OpenResponses Spec Tests

Validates that the caller registry, SignalR hub, OpenResponses middleware, and health endpoints are correctly implemented. SDK types (SessionEvent, CopilotClient) flow through directly — no parallel event hierarchy.

## Caller Registry (Thin Coordination Layer)

### Registry provides caller-key mapping and concurrency gating

Both SignalR and HTTP surfaces need to map callers to sessions and enforce one-run-per-caller. Without a shared registry, each surface would implement its own mapping and concurrency — violating DRY.

```
Given the src/MsClaw.Gateway/Services/ICallerRegistry.cs file
When examining the ICallerRegistry interface
Then it declares TryAcquire and Release methods for per-caller concurrency
And it declares GetSessionId and SetSessionId methods for caller-to-session mapping
And it declares a ListCallers method
```

### Registry uses ConcurrentDictionary for thread safety

Multiple hub connections and HTTP requests arrive concurrently. Without thread-safe data structures, session mapping and concurrency state would race.

```
Given the src/MsClaw.Gateway/Services/CallerRegistry.cs file
When examining the CallerRegistry implementation
Then it uses ConcurrentDictionary for session mapping
And it uses ConcurrentDictionary with SemaphoreSlim values for concurrency gating
And TryAcquire performs a non-blocking check (does not wait)
```

## SignalR Hub

### Hub uses strongly-typed client contract

String-based method calls break silently at runtime. A strongly-typed hub catches contract mismatches at compile time, preventing an entire class of bugs.

```
Given the src/MsClaw.Gateway/Hubs/GatewayHub.cs file
When examining the GatewayHub class declaration
Then it inherits from Hub<IGatewayHubClient>
And it is a sealed class
```

### Hub client interface defines push methods

Clients need to receive events and presence updates from the server. Without a typed client interface, the server would use magic strings for method names.

```
Given the src/MsClaw.Gateway/Hubs/IGatewayHubClient.cs file
When examining the IGatewayHubClient interface
Then it declares a ReceiveEvent method
And it declares a ReceivePresence method
```

### Hub injects SDK CopilotClient directly — no wrapper

The principle "Never Rewrite What You've Already Imported" means the hub works with the SDK's CopilotClient directly. No monolithic IAgentRuntime wrapper.

```
Given the src/MsClaw.Gateway/Hubs/GatewayHub.cs file
When examining the constructor or primary constructor parameters
Then it accepts a CopilotClient parameter (from GitHub.Copilot.SDK)
And it accepts an ICallerRegistry parameter
And it does NOT depend on an IAgentRuntime or AgentRuntime type
```

### Hub streams SDK event types directly

SDK events pass through without transformation. The hub yields the SDK's own SessionEvent types via IAsyncEnumerable — no parallel event hierarchy.

```
Given the src/MsClaw.Gateway/Hubs/GatewayHub.cs file
When examining the SendMessage method
Then it returns IAsyncEnumerable of a type from the SDK event hierarchy
And it uses ICallerRegistry.TryAcquire for concurrency checking
And it uses ICallerRegistry.Release in a finally block or equivalent cleanup
```

### Hub exposes session management methods

Operators need to create sessions, list sessions, get history, and abort responses. These methods delegate to the SDK's CopilotClient and CopilotSession directly.

```
Given the src/MsClaw.Gateway/Hubs/GatewayHub.cs file
When examining the public methods
Then there are CreateSession, ListSessions, GetHistory, and AbortResponse methods
And these methods call CopilotClient or CopilotSession methods (not an intermediate runtime)
```

## Hosted Service

### Hosted service exposes CopilotClient for DI

The hub and middleware need the CopilotClient that the hosted service creates. Without exposing it, other services cannot access the SDK client.

```
Given the src/MsClaw.Gateway/Hosting/GatewayHostedService.cs file
When examining the public properties or methods
Then the started CopilotClient is accessible to other services
And the loaded system message string is accessible to other services
```

## Health Probes

### Liveness and readiness are separate endpoints

Infrastructure operators need to distinguish between "process alive" and "fully initialized." A combined endpoint forces orchestrators to treat initialization failures as process failures.

```
Given the src/MsClaw.Gateway/Commands/StartCommand.cs file
When examining the MapEndpoints method
Then there is a GET /health endpoint mapped
And there is a GET /health/ready endpoint mapped
```

### Readiness reflects actual hosted service state

A readiness probe that always returns healthy is useless. The probe must check that the hosted service completed initialization.

```
Given the src/MsClaw.Gateway/Commands/StartCommand.cs file
When examining the readiness endpoint handler
Then it checks whether the hosted service is ready
And it returns an unhealthy status with component identification when not ready
```

## OpenResponses Library

### OpenResponses lives in a separate project

Keeping the OpenResponses endpoint in a separate library enables reuse without depending on the full gateway.

```
Given the src/MsClaw.OpenResponses/MsClaw.OpenResponses.csproj file
When examining the project configuration
Then the SDK is Microsoft.NET.Sdk (class library, not web)
And the target framework is net10.0
```

### Request DTO matches OpenResponses schema

Automation clients using OpenResponses-compliant libraries expect a specific request format.

```
Given the src/MsClaw.OpenResponses/Models/ResponseRequest.cs file
When examining the ResponseRequest type
Then it has a Model property of type string
And it has an Input property that accepts a string or message array
And it has a Stream property of type bool with a default of false
```

### Response DTO matches OpenResponses schema

The response must conform to the OpenResponses specification so that any compliant client can parse it.

```
Given the src/MsClaw.OpenResponses/Models/ResponseObject.cs file
When examining the ResponseObject type
Then it has an Object property that defaults to "response"
And it has Id, Status, and Output properties
And the Output property is a collection of output items with type, role, and content
```

### Middleware maps SDK events to OpenResponses format

This is genuinely new logic — the SDK knows nothing about OpenResponses. The middleware transforms SDK events into OpenResponses JSON and SSE format.

```
Given the src/MsClaw.OpenResponses/OpenResponsesMiddleware.cs file
When examining the implementation
Then it maps SDK AssistantMessageDeltaEvent to OpenResponses output_text.delta SSE events
And it maps SDK AssistantMessageEvent to OpenResponses response.completed SSE events
And it maps SDK SessionIdleEvent to a terminal [DONE] marker
```

### Middleware registers via endpoint route builder

The OpenResponses endpoint must be composable via standard ASP.NET Core endpoint routing.

```
Given the src/MsClaw.OpenResponses/Extensions/EndpointRouteBuilderExtensions.cs file
When examining the extension methods
Then there is a MapOpenResponses method that extends IEndpointRouteBuilder
And it registers a POST /v1/responses endpoint
```

### Gateway project references OpenResponses library

The Gateway must use the separate library to serve the OpenResponses endpoint.

```
Given the src/MsClaw.Gateway/MsClaw.Gateway.csproj file
When examining the project references
Then it includes a reference to MsClaw.OpenResponses
```

## Static Chat UI

### Gateway serves static files

Without static file serving, the chat UI HTML/JS/CSS files would not be accessible via HTTP.

```
Given the src/MsClaw.Gateway/Commands/StartCommand.cs file
When examining the RunGatewayAsync method
Then the application pipeline includes UseDefaultFiles and UseStaticFiles middleware calls
```
