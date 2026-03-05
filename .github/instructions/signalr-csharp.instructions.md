---
applyTo: '**.cs, **.csproj, **.razor, **.ts, **.js'
description: 'This file provides guidance on building ASP.NET Core SignalR applications.'
name: 'ASP.NET Core SignalR Instructions'
---

## Core Principles

- Use ASP.NET Core SignalR for real-time server-to-client and client-to-server communication.
- Prefer strongly typed contracts (`Hub<TClient>`) to reduce string-based runtime errors.
- Treat each hub method invocation as transient and stateless.
- Use async/await end-to-end for hub methods, client calls, and background broadcasting.
- Keep authentication and authorization explicit for every hub endpoint.

## Installation

### Server Package

SignalR is included in the ASP.NET Core shared framework for web apps. Only add the package explicitly for class libraries:

```bash
dotnet add package Microsoft.AspNetCore.SignalR
```

### .NET Client Package

Install the .NET client package for desktop, worker, or console clients:

```bash
dotnet add package Microsoft.AspNetCore.SignalR.Client
```

### JavaScript Client Package

Install the JavaScript client package:

```bash
npm install @microsoft/signalr
```

## Server Initialization

### Basic Setup

Register SignalR services and map hubs in `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

var app = builder.Build();

app.MapHub<ChatHub>("/hubs/chat");

app.Run();
```

### Global Hub Options

Configure global hub behavior in `AddSignalR`:

```csharp
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = false;
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    options.MaximumReceiveMessageSize = 32 * 1024;
});
```

### Per-Hub Options

Override global defaults for a specific hub:

```csharp
builder.Services.AddSignalR()
    .AddHubOptions<ChatHub>(options =>
    {
        options.MaximumParallelInvocationsPerClient = 1;
    });
```

## Hub Authoring

### Basic Hub

Create hubs by inheriting from `Hub` and exposing `public` methods:

```csharp
using Microsoft.AspNetCore.SignalR;

public sealed class ChatHub : Hub
{
    public async Task SendMessageAsync(string user, string message)
    {
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }
}
```

### Hub Lifetime Rules

- Never store mutable state in hub instance fields or properties.
- Never resolve hubs directly from dependency injection; use `IHubContext<THub>`.
- Always `await` `SendAsync` and other async hub operations before method return.

### Strongly Typed Hubs

Use `Hub<TClient>` for compile-time checked client method calls:

```csharp
public interface IChatClient
{
    Task ReceiveMessage(string user, string message);
}

public sealed class ChatHub : Hub<IChatClient>
{
    public async Task SendMessageAsync(string user, string message)
    {
        await Clients.All.ReceiveMessage(user, message);
    }
}
```

## Hub Lifecycle Events

### OnConnectedAsync and OnDisconnectedAsync

Override lifecycle methods to run logic when connections open or close:

```csharp
public sealed class ChatHub : Hub<IChatClient>
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "lobby");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "lobby");
        await base.OnDisconnectedAsync(exception);
    }
}
```

### Lifecycle Caveats

- Always call the `base` implementation to preserve framework behavior.
- `OnDisconnectedAsync` receives a non-null exception only when the connection closed due to an error.
- Hubs are transient — each method call, including lifecycle methods, runs on a new instance. Do not store state in instance fields.
- Do not call `Clients.Client(connectionId).InvokeAsync<T>()` (client results) inside `OnConnectedAsync` or `OnDisconnectedAsync`; this causes deadlocks.
- With Long Polling, `OnDisconnectedAsync` may be delayed or not fire when the client abruptly loses connectivity.

## Hub Return Values

### Returning Results to Clients

Hub methods can return `Task<T>` to send a result back to the calling client:

```csharp
public sealed class MathHub : Hub
{
    /// <summary>Adds two integers and returns the result.</summary>
    public Task<int> AddAsync(int a, int b) => Task.FromResult(a + b);
}
```

The client receives the result through `InvokeAsync<T>`:

```csharp
int result = await connection.InvokeAsync<int>("AddAsync", 1, 2);
```

JavaScript equivalent:

```javascript
const result = await connection.invoke("AddAsync", 1, 2);
```

### Invoke vs Send

- Use `InvokeAsync` when the client needs the hub method return value.
- Use `SendAsync` for fire-and-forget calls where no return value is needed.

## Connection Context and Targeting

### Context Usage

Use `Context` for connection and user metadata:

- `Context.ConnectionId`
- `Context.UserIdentifier`
- `Context.User`
- `Context.ConnectionAborted`
- `Context.GetHttpContext()`

### Client Targeting

Use `Clients` for fan-out patterns:

- `Clients.All`
- `Clients.Caller`
- `Clients.Others`
- `Clients.Client(connectionId)`
- `Clients.Group(groupName)`
- `Clients.User(userId)`

## Groups and Users

### Group Membership

Add and remove connections using `Groups`:

```csharp
public async Task JoinRoomAsync(string room)
{
    await Groups.AddToGroupAsync(Context.ConnectionId, room);
}

public async Task LeaveRoomAsync(string room)
{
    await Groups.RemoveFromGroupAsync(Context.ConnectionId, room);
}
```

### Group Caveats

- Group names are case-sensitive.
- Group membership is not preserved across reconnect.
- Group membership is in-memory and is not persisted across server restarts.
- Do not treat groups as a security boundary; enforce authorization separately.

## Streaming

### Server-to-Client Streaming

A hub method that returns `IAsyncEnumerable<T>` or `ChannelReader<T>` is automatically treated as a streaming method. Prefer `IAsyncEnumerable<T>` for cleaner code and built-in cancellation:

```csharp
public sealed class StreamHub : Hub
{
    /// <summary>Streams a countdown of integers to the client.</summary>
    public async IAsyncEnumerable<int> CounterAsync(
        int count,
        int delayMs,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return i;
            await Task.Delay(delayMs, cancellationToken);
        }
    }
}
```

.NET client consumption:

```csharp
await foreach (var item in connection.StreamAsync<int>("CounterAsync", 10, 500))
{
    Console.WriteLine(item);
}
```

JavaScript client consumption:

```javascript
connection.stream("CounterAsync", 10, 500).subscribe({
  next: (item) => console.log(item),
  complete: () => console.log("Stream finished"),
  error: (err) => console.error(err),
});
```

### Client-to-Server Streaming

Hub methods that accept `IAsyncEnumerable<T>` or `ChannelReader<T>` as a parameter receive streamed data from the client:

```csharp
public sealed class UploadHub : Hub
{
    /// <summary>Receives a stream of payloads from the client.</summary>
    public async Task UploadStreamAsync(IAsyncEnumerable<string> stream)
    {
        await foreach (var item in stream)
        {
            // Process each streamed item
        }
    }
}
```

.NET client sending a stream:

```csharp
async IAsyncEnumerable<string> ProduceDataAsync()
{
    for (var i = 0; i < 10; i++)
    {
        yield return $"item-{i}";
        await Task.Delay(100);
    }
}

await connection.SendAsync("UploadStreamAsync", ProduceDataAsync());
```

### Streaming Guidance

- Prefer `IAsyncEnumerable<T>` over `ChannelReader<T>` unless you need manual producer control.
- Always accept a `[EnumeratorCancellation] CancellationToken` in server-to-client streaming methods.
- Streaming methods can also return `Task<IAsyncEnumerable<T>>` or `Task<ChannelReader<T>>` when async setup is needed before yielding.

## Serialization and Protocols

### JSON Protocol

Configure JSON payload serialization:

```csharp
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = null;
    });
```

### MessagePack Protocol

Enable MessagePack when payload size and binary protocol efficiency are needed:

```csharp
builder.Services.AddSignalR()
    .AddMessagePackProtocol();
```

### Protocol Governance

- Default to JSON unless MessagePack is required and validated end-to-end.
- Keep DTOs version-tolerant for mixed client versions.
- Set realistic message size limits to reduce DoS risk.

## .NET Client Patterns

### Connection Setup

```csharp
using Microsoft.AspNetCore.SignalR.Client;

var connection = new HubConnectionBuilder()
    .WithUrl("https://localhost:5001/hubs/chat")
    .WithAutomaticReconnect()
    .Build();
```

### Register Handlers Before Start

```csharp
connection.On<string, string>("ReceiveMessage", (user, message) =>
{
    Console.WriteLine($"{user}: {message}");
});

await connection.StartAsync();
```

### Invoke Hub Methods

```csharp
await connection.InvokeAsync("SendMessageAsync", "operator", "hello");
```

### Reconnect Lifecycle

Use reconnect events to coordinate UI and queued operations:

- `Reconnecting`
- `Reconnected`
- `Closed`

For initial connect failures, implement explicit retry around `StartAsync`.

## JavaScript Client Patterns

### Connection Setup

```javascript
const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/chat")
  .withAutomaticReconnect()
  .configureLogging(signalR.LogLevel.Information)
  .build();
```

### Handler Registration and Start

```javascript
connection.on("ReceiveMessage", (user, message) => {
  console.log(`${user}: ${message}`);
});

await connection.start();
```

### Send and Invoke

- Use `invoke` when a server response is required.
- Use `send` for fire-and-forget semantics.

## Authentication and Authorization

### Server Pipeline

Always enable authentication and authorization middleware before endpoint mapping:

```csharp
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapHub<ChatHub>("/hubs/chat");
```

### Bearer Tokens

Configure token flow from clients:

- JavaScript client: `accessTokenFactory`
- .NET client: `AccessTokenProvider`

When using WebSockets or Server-Sent Events in browsers, access tokens may be sent via query string due to browser API limitations.

### Hub Authorization

Use `[Authorize]` attributes on hub classes or methods and policy-based authorization for privileged operations.

### Custom User ID Provider

Override the default `IUserIdProvider` to control how SignalR maps connections to user identifiers for `Clients.User(userId)`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

public sealed class CustomUserIdProvider : IUserIdProvider
{
    /// <summary>Maps a connection to a user ID from the authentication claims.</summary>
    public string? GetUserId(HubConnectionContext connection)
    {
        return connection.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }
}
```

Register it as a singleton in `Program.cs`:

```csharp
builder.Services.AddSingleton<IUserIdProvider, CustomUserIdProvider>();
```

The default provider uses `ClaimTypes.NameIdentifier`. Only register a custom provider when your claims structure differs.

## Security Requirements

- Use HTTPS for all SignalR traffic.
- Configure CORS with explicit trusted origins, `GET` and `POST`, and credentials when needed.
- Do not expose `ConnectionId` as a security token.
- Avoid logging access tokens from query strings.
- Keep `EnableDetailedErrors` disabled in production.
- Keep message buffer and max message sizes constrained by expected payloads.

## Transport and HTTP Configuration

### Transport Restrictions

Restrict transports only when required by environment constraints:

```csharp
using Microsoft.AspNetCore.Http.Connections;

app.MapHub<ChatHub>("/hubs/chat", options =>
{
    options.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
});
```

### Dispatcher Options

Use `HttpConnectionDispatcherOptions` to control:

- `ApplicationMaxBufferSize`
- `TransportMaxBufferSize`
- `CloseOnAuthenticationExpiration`
- `LongPolling.PollTimeout`
- `WebSockets.CloseTimeout`

## Scale and Hosting

### Sticky Sessions

In multi-server deployments, enforce sticky sessions unless one of these conditions is true:

1. Single server/single process hosting.
2. Azure SignalR Service is used.
3. All clients use WebSockets and skip negotiation.

### Scale-Out Options

- Prefer Azure SignalR Service for Azure-hosted workloads.
- Use Redis backplane for self-hosted scale-out where appropriate.
- Budget for persistent TCP connection and memory usage per connected client.

## Diagnostics and Observability

### Server Logging

Use structured logging and set category filters for SignalR internals during troubleshooting.

### Client Logging

- .NET: `ConfigureLogging(...)`
- JavaScript: `.configureLogging(signalR.LogLevel.Information)`

### Health and Failure Modes

- Track connection churn, reconnect rates, and invocation failures.
- Monitor transport fallback frequency and authentication failures.
- Monitor hub method latency and queue depth under load.

## Testing Guidance

- Unit test hub method behavior and authorization policies.
- Integration test reconnect, token expiry, and transport fallback paths.
- Load test high-connection scenarios and message fan-out patterns.
- Validate scale-out behavior with Azure SignalR Service or Redis backplane before production.

## Best Practices

1. Keep hub methods small and delegate business logic to services.
2. Use strongly typed hubs for compile-time safety.
3. Register client handlers before starting connections.
4. Use automatic reconnect and explicit initial-connect retry.
5. Keep security defaults strict (HTTPS, CORS allowlist, no token logging).
6. Use cancellation tokens (`Context.ConnectionAborted`) in long-running operations.
7. Prefer policy-based authorization over ad-hoc conditional checks in hub methods.
8. Validate payload sizes and tune buffer limits deliberately.
9. Treat group membership as routing, not authorization.
10. Plan scale strategy (single node, Azure SignalR Service, or Redis) before launch.
11. Prefer `IAsyncEnumerable<T>` over `ChannelReader<T>` for streaming unless manual producer control is needed.
12. Inject `IHubContext<THub, TClient>` (not `IHubContext<THub>`) to keep strongly typed calls outside the hub.
13. Always call `base.OnConnectedAsync()` / `base.OnDisconnectedAsync()` in lifecycle overrides.

## Common Patterns

### Broadcast to All

```csharp
public async Task BroadcastAsync(string message)
{
    await Clients.All.SendAsync("BroadcastReceived", message);
}
```

### Direct Message to User

```csharp
public async Task SendToUserAsync(string userId, string message)
{
    await Clients.User(userId).SendAsync("PrivateMessageReceived", message);
}
```

### Server Push Outside Hub (Strongly Typed)

```csharp
using Microsoft.AspNetCore.SignalR;

public sealed class NotificationPublisher(IHubContext<ChatHub, IChatClient> hubContext)
{
    /// <summary>Sends a notification to all connected clients.</summary>
    public async Task NotifyAsync(string message, CancellationToken cancellationToken)
    {
        await hubContext.Clients.All.ReceiveMessage("system", message);
    }
}
```

When using strongly typed hubs, always inject `IHubContext<THub, TClient>` instead of `IHubContext<THub>` to preserve compile-time safety outside the hub.

### Client Results (Server Invokes Client)

The server can call a client method and await a result. The client must register a handler that returns a value:

```csharp
// Server — request a value from a specific client
public sealed class InteractiveHub : Hub<IInteractiveClient>
{
    public async Task<string> GetClientValueAsync(string connectionId)
    {
        return await Clients.Client(connectionId).GetPreferredTheme();
    }
}

public interface IInteractiveClient
{
    Task<string> GetPreferredTheme();
}
```

.NET client handler returning a result:

```csharp
connection.On("GetPreferredTheme", () => Task.FromResult("dark"));
```

Increase `MaximumParallelInvocationsPerClient` if the server invokes client results while the client is already inside a hub method call.
