# GitHub Copilot .NET SDK — Walkthrough

*2026-03-02T01:06:08Z by Showboat 0.6.1*
<!-- showboat-id: 3f0e4719-6fe4-45ec-b7e8-c0f5b6f0a97e -->

## Overview

The Copilot SDK for .NET is a client library that lets your C# applications programmatically control the GitHub Copilot CLI via JSON-RPC. Your app creates a `CopilotClient`, which spawns or connects to a CLI server, then creates one or more `CopilotSession` instances to hold conversations with the model.

The key insight: **the SDK doesn't run the models itself**. Instead, it acts as a remote control for the CLI server, sending messages and receiving events over a protocol. This keeps your code clean and lets the CLI handle all the heavy lifting—model inference, context management, tool invocation, session persistence.

Think of it as three layers:
1. **Your app** — creates CopilotClient, sends prompts, handles events
2. **The SDK** — JSON-RPC client, event serialization, type safety
3. **The CLI** — spawned process or remote server that does the actual work

## Entry Point: CopilotClient

Every Copilot app starts with a `CopilotClient`. This class handles:
- **Server lifecycle** — spawning the CLI or connecting to an existing server
- **Session management** — creating, resuming, listing, and deleting sessions
- **Connection state** — tracking whether the server is running
- **Lifecycle events** — notifying you when sessions are created, deleted, or moved to foreground

Let's look at how you create one:

```bash
sed -n '1,100p' /home/cip/src/copilot-sdk/dotnet/src/Client.cs
```

```output
/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *--------------------------------------------------------------------------------------------*/

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StreamJsonRpc;
using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GitHub.Copilot.SDK.Rpc;

namespace GitHub.Copilot.SDK;

/// <summary>
/// Provides a client for interacting with the Copilot CLI server.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="CopilotClient"/> manages the connection to the Copilot CLI server and provides
/// methods to create and manage conversation sessions. It can either spawn a CLI server process
/// or connect to an existing server.
/// </para>
/// <para>
/// The client supports both stdio (default) and TCP transport modes for communication with the CLI server.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Create a client with default options (spawns CLI server)
/// await using var client = new CopilotClient();
///
/// // Create a session
/// await using var session = await client.CreateSessionAsync(new SessionConfig { Model = "gpt-4" });
///
/// // Handle events
/// using var subscription = session.On(evt =>
/// {
///     if (evt is AssistantMessageEvent assistantMessage)
///         Console.WriteLine(assistantMessage.Data?.Content);
/// });
///
/// // Send a message
/// await session.SendAsync(new MessageOptions { Prompt = "Hello!" });
/// </code>
/// </example>
public partial class CopilotClient : IDisposable, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, CopilotSession> _sessions = new();
    private readonly CopilotClientOptions _options;
    private readonly ILogger _logger;
    private Task<Connection>? _connectionTask;
    private bool _disposed;
    private readonly int? _optionsPort;
    private readonly string? _optionsHost;
    private List<ModelInfo>? _modelsCache;
    private readonly SemaphoreSlim _modelsCacheLock = new(1, 1);
    private readonly List<Action<SessionLifecycleEvent>> _lifecycleHandlers = new();
    private readonly Dictionary<string, List<Action<SessionLifecycleEvent>>> _typedLifecycleHandlers = new();
    private readonly object _lifecycleHandlersLock = new();
    private ServerRpc? _rpc;

    /// <summary>
    /// Gets the typed RPC client for server-scoped methods (no session required).
    /// </summary>
    /// <remarks>
    /// The client must be started before accessing this property. Use <see cref="StartAsync"/> or set <see cref="CopilotClientOptions.AutoStart"/> to true.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown if the client has been disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the client is not started.</exception>
    public ServerRpc Rpc => _disposed
        ? throw new ObjectDisposedException(nameof(CopilotClient))
        : _rpc ?? throw new InvalidOperationException("Client is not started. Call StartAsync first.");

    /// <summary>
    /// Creates a new instance of <see cref="CopilotClient"/>.
    /// </summary>
    /// <param name="options">Options for creating the client. If null, default options are used.</param>
    /// <exception cref="ArgumentException">Thrown when mutually exclusive options are provided (e.g., CliUrl with UseStdio or CliPath).</exception>
    /// <example>
    /// <code>
    /// // Default options - spawns CLI server using stdio
    /// var client = new CopilotClient();
    ///
    /// // Connect to an existing server
    /// var client = new CopilotClient(new CopilotClientOptions { CliUrl = "localhost:3000", UseStdio = false });
    ///
    /// // Custom CLI path with specific log level
    /// var client = new CopilotClient(new CopilotClientOptions
    /// {
    ///     CliPath = "/usr/local/bin/copilot",
    ///     LogLevel = "debug"
    /// });
    /// </code>
```

The constructor accepts `CopilotClientOptions` (or null for defaults). Notice the internal fields:
- `_sessions` — keeps track of all active sessions by their ID
- `_connectionTask` — lazy-loads the connection to the CLI server
- `_lifecycleHandlers` — event subscriptions for session lifecycle changes
- `_rpc` — the JSON-RPC client that talks to the server

The key pattern here is **lazy connection**: the client doesn't connect until you actually call `StartAsync()` or create a session.

### Starting the Client

To use the client, you must call `StartAsync()`. This is where the connection actually happens:

```bash
grep -A 30 'public async Task StartAsync' /home/cip/src/copilot-sdk/dotnet/src/Client.cs | head -40
```

```output
```

```bash
sed -n '193,230p' /home/cip/src/copilot-sdk/dotnet/src/Client.cs
```

```output
            }

            var connection = await result;

            // Verify protocol version compatibility
            await VerifyProtocolVersionAsync(connection, ct);

            _logger.LogInformation("Copilot client connected");
            return connection;
        }
    }

    /// <summary>
    /// Disconnects from the Copilot server and stops all active sessions.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// This method performs graceful cleanup:
    /// <list type="number">
    ///     <item>Destroys all active sessions</item>
    ///     <item>Closes the JSON-RPC connection</item>
    ///     <item>Terminates the CLI server process (if spawned by this client)</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <exception cref="AggregateException">Thrown when multiple errors occur during cleanup.</exception>
    /// <example>
    /// <code>
    /// await client.StopAsync();
    /// </code>
    /// </example>
    public async Task StopAsync()
    {
        var errors = new List<Exception>();

        foreach (var session in _sessions.Values.ToArray())
        {
```

```bash
sed -n '150,190p' /home/cip/src/copilot-sdk/dotnet/src/Client.cs
```

```output
    }

    /// <summary>
    /// Starts the Copilot client and connects to the server.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> that can be used to cancel the operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// If the server is not already running and the client is configured to spawn one (default), it will be started.
    /// If connecting to an external server (via CliUrl), only establishes the connection.
    /// </para>
    /// <para>
    /// This method is called automatically when creating a session if <see cref="CopilotClientOptions.AutoStart"/> is true (default).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var client = new CopilotClient(new CopilotClientOptions { AutoStart = false });
    /// await client.StartAsync();
    /// // Now ready to create sessions
    /// </code>
    /// </example>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return _connectionTask ??= StartCoreAsync(cancellationToken);

        async Task<Connection> StartCoreAsync(CancellationToken ct)
        {
            _logger.LogDebug("Starting Copilot client");

            Task<Connection> result;

            if (_optionsHost is not null && _optionsPort is not null)
            {
                // External server (TCP)
                result = ConnectToServerAsync(null, _optionsHost, _optionsPort, ct);
            }
            else
            {
                // Child process (stdio or TCP)
```

`StartAsync()` does three things:
1. If you're connecting to an external server (via `CliUrl`), it connects to it
2. If you're spawning a CLI process, it starts that process and waits for it to be ready
3. It verifies that the protocol version matches

Notice the pattern: `_connectionTask ??= StartCoreAsync(...)`. This is a **lazy singleton**—it only connects once, even if you call `StartAsync()` multiple times. Handy for avoiding duplicate connections.

### Creating a Session

Once the client is started, you create sessions. Sessions are where conversations happen:

```bash
sed -n '337,420p' /home/cip/src/copilot-sdk/dotnet/src/Client.cs
```

```output
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> that can be used to cancel the operation.</param>
    /// <returns>A task that resolves to provide the <see cref="CopilotSession"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the client is not connected and AutoStart is disabled, or when a session with the same ID already exists.</exception>
    /// <remarks>
    /// Sessions maintain conversation state, handle events, and manage tool execution.
    /// If the client is not connected and <see cref="CopilotClientOptions.AutoStart"/> is enabled (default),
    /// this will automatically start the connection.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Basic session
    /// var session = await client.CreateSessionAsync();
    ///
    /// // Session with model and tools
    /// var session = await client.CreateSessionAsync(new SessionConfig
    /// {
    ///     Model = "gpt-4",
    ///     Tools = [AIFunctionFactory.Create(MyToolMethod)]
    /// });
    /// </code>
    /// </example>
    public async Task<CopilotSession> CreateSessionAsync(SessionConfig? config = null, CancellationToken cancellationToken = default)
    {
        var connection = await EnsureConnectedAsync(cancellationToken);

        var hasHooks = config?.Hooks != null && (
            config.Hooks.OnPreToolUse != null ||
            config.Hooks.OnPostToolUse != null ||
            config.Hooks.OnUserPromptSubmitted != null ||
            config.Hooks.OnSessionStart != null ||
            config.Hooks.OnSessionEnd != null ||
            config.Hooks.OnErrorOccurred != null);

        var request = new CreateSessionRequest(
            config?.Model,
            config?.SessionId,
            config?.ReasoningEffort,
            config?.Tools?.Select(ToolDefinition.FromAIFunction).ToList(),
            config?.SystemMessage,
            config?.AvailableTools,
            config?.ExcludedTools,
            config?.Provider,
            config?.OnPermissionRequest != null ? true : null,
            config?.OnUserInputRequest != null ? true : null,
            hasHooks ? true : null,
            config?.WorkingDirectory,
            config?.Streaming == true ? true : null,
            config?.McpServers,
            config?.CustomAgents,
            config?.ConfigDir,
            config?.SkillDirectories,
            config?.DisabledSkills,
            config?.InfiniteSessions);

        var response = await InvokeRpcAsync<CreateSessionResponse>(
            connection.Rpc, "session.create", [request], cancellationToken);

        var session = new CopilotSession(response.SessionId, connection.Rpc, response.WorkspacePath);
        session.RegisterTools(config?.Tools ?? []);
        if (config?.OnPermissionRequest != null)
        {
            session.RegisterPermissionHandler(config.OnPermissionRequest);
        }
        if (config?.OnUserInputRequest != null)
        {
            session.RegisterUserInputHandler(config.OnUserInputRequest);
        }
        if (config?.Hooks != null)
        {
            session.RegisterHooks(config.Hooks);
        }

        if (!_sessions.TryAdd(response.SessionId, session))
        {
            throw new InvalidOperationException($"Session {response.SessionId} already exists");
        }

        return session;
    }

    /// <summary>
    /// Resumes an existing Copilot session with the specified configuration.
    /// </summary>
    /// <param name="sessionId">The ID of the session to resume.</param>
```

`CreateSessionAsync()` does quite a bit:
1. **Ensures connection** — calls `EnsureConnectedAsync()` to start the client if needed
2. **Prepares the config** — checks for hooks, tools, and user input handlers
3. **Sends RPC request** — marshals all config into a `CreateSessionRequest` and sends it to the CLI
4. **Registers handlers** — stores tool definitions, permission handlers, hooks locally so the SDK can respond when the CLI invokes them
5. **Tracks the session** — adds it to `_sessions` dictionary for lifecycle management

The key insight: **tools and hooks are registered client-side**, not sent to the CLI. When the CLI needs to invoke a tool or trigger a hook, it calls back into the SDK, and the SDK dispatches to your registered handler.

## Inside a Session: CopilotSession

Now that you have a session, you can send messages and receive events. Let's look at how that works:

```bash
sed -n '1,80p' /home/cip/src/copilot-sdk/dotnet/src/Session.cs
```

```output
/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *--------------------------------------------------------------------------------------------*/

using Microsoft.Extensions.AI;
using StreamJsonRpc;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GitHub.Copilot.SDK.Rpc;

namespace GitHub.Copilot.SDK;

/// <summary>
/// Represents a single conversation session with the Copilot CLI.
/// </summary>
/// <remarks>
/// <para>
/// A session maintains conversation state, handles events, and manages tool execution.
/// Sessions are created via <see cref="CopilotClient.CreateSessionAsync"/> or resumed via
/// <see cref="CopilotClient.ResumeSessionAsync"/>.
/// </para>
/// <para>
/// The session provides methods to send messages, subscribe to events, retrieve
/// conversation history, and manage the session lifecycle.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// await using var session = await client.CreateSessionAsync(new SessionConfig { Model = "gpt-4" });
///
/// // Subscribe to events
/// using var subscription = session.On(evt =>
/// {
///     if (evt is AssistantMessageEvent assistantMessage)
///     {
///         Console.WriteLine($"Assistant: {assistantMessage.Data?.Content}");
///     }
/// });
///
/// // Send a message and wait for completion
/// await session.SendAndWaitAsync(new MessageOptions { Prompt = "Hello, world!" });
/// </code>
/// </example>
public partial class CopilotSession : IAsyncDisposable
{
    private readonly HashSet<SessionEventHandler> _eventHandlers = new();
    private readonly Dictionary<string, AIFunction> _toolHandlers = new();
    private readonly JsonRpc _rpc;
    private PermissionHandler? _permissionHandler;
    private readonly SemaphoreSlim _permissionHandlerLock = new(1, 1);
    private UserInputHandler? _userInputHandler;
    private readonly SemaphoreSlim _userInputHandlerLock = new(1, 1);
    private SessionHooks? _hooks;
    private readonly SemaphoreSlim _hooksLock = new(1, 1);
    private SessionRpc? _sessionRpc;

    /// <summary>
    /// Gets the unique identifier for this session.
    /// </summary>
    /// <value>A string that uniquely identifies this session.</value>
    public string SessionId { get; }

    /// <summary>
    /// Gets the typed RPC client for session-scoped methods.
    /// </summary>
    public SessionRpc Rpc => _sessionRpc ??= new SessionRpc(_rpc, SessionId);

    /// <summary>
    /// Gets the path to the session workspace directory when infinite sessions are enabled.
    /// </summary>
    /// <value>
    /// The path to the workspace containing checkpoints/, plan.md, and files/ subdirectories,
    /// or null if infinite sessions are disabled.
    /// </value>
    public string? WorkspacePath { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CopilotSession"/> class.
    /// </summary>
```

`CopilotSession` is the interface you use for most work. Key fields:
- `_eventHandlers` — subscribed event listeners
- `_toolHandlers` — your local tool implementations (used when CLI invokes a tool)
- `_rpc` — the JSON-RPC client (shared with the client)
- `_permissionHandler`, `_userInputHandler`, `_hooks` — lifecycle callbacks
- `SessionId` — unique session ID
- `WorkspacePath` — path to the session workspace (when infinite sessions are enabled)

### Sending a Message

To send a prompt, you use `SendAsync()`:

```bash
sed -n '155,210p' /home/cip/src/copilot-sdk/dotnet/src/Session.cs
```

```output
    /// </para>
    /// <para>
    /// Events are still delivered to handlers registered via <see cref="On"/> while waiting.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Send and wait for completion with default 60s timeout
    /// var response = await session.SendAndWaitAsync(new MessageOptions { Prompt = "What is 2+2?" });
    /// Console.WriteLine(response?.Data?.Content); // "4"
    /// </code>
    /// </example>
    public async Task<AssistantMessageEvent?> SendAndWaitAsync(
        MessageOptions options,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        var tcs = new TaskCompletionSource<AssistantMessageEvent?>();
        AssistantMessageEvent? lastAssistantMessage = null;

        void Handler(SessionEvent evt)
        {
            switch (evt)
            {
                case AssistantMessageEvent assistantMessage:
                    lastAssistantMessage = assistantMessage;
                    break;

                case SessionIdleEvent:
                    tcs.TrySetResult(lastAssistantMessage);
                    break;

                case SessionErrorEvent errorEvent:
                    var message = errorEvent.Data?.Message ?? "session error";
                    tcs.TrySetException(new InvalidOperationException($"Session error: {message}"));
                    break;
            }
        }

        using var subscription = On(Handler);

        await SendAsync(options, cancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(effectiveTimeout);

        using var registration = cts.Token.Register(() =>
            tcs.TrySetException(new TimeoutException($"SendAndWaitAsync timed out after {effectiveTimeout}")));
        return await tcs.Task;
    }

    /// <summary>
    /// Registers a callback for session events.
    /// </summary>
    /// <param name="handler">A callback to be invoked when a session event occurs.</param>
```

```bash
sed -n '110,155p' /home/cip/src/copilot-sdk/dotnet/src/Session.cs
```

```output
    /// Subscribe to events via <see cref="On"/> to receive streaming responses and other session events.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var messageId = await session.SendAsync(new MessageOptions
    /// {
    ///     Prompt = "Explain this code",
    ///     Attachments = new List&lt;Attachment&gt;
    ///     {
    ///         new() { Type = "file", Path = "./Program.cs" }
    ///     }
    /// });
    /// </code>
    /// </example>
    public async Task<string> SendAsync(MessageOptions options, CancellationToken cancellationToken = default)
    {
        var request = new SendMessageRequest
        {
            SessionId = SessionId,
            Prompt = options.Prompt,
            Attachments = options.Attachments,
            Mode = options.Mode
        };

        var response = await InvokeRpcAsync<SendMessageResponse>(
            "session.send", [request], cancellationToken);

        return response.MessageId;
    }

    /// <summary>
    /// Sends a message to the Copilot session and waits until the session becomes idle.
    /// </summary>
    /// <param name="options">Options for the message to be sent, including the prompt and optional attachments.</param>
    /// <param name="timeout">Timeout duration (default: 60 seconds). Controls how long to wait; does not abort in-flight agent work.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> that can be used to cancel the operation.</param>
    /// <returns>A task that resolves with the final assistant message event, or null if none was received.</returns>
    /// <exception cref="TimeoutException">Thrown if the timeout is reached before the session becomes idle.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the session has been disposed.</exception>
    /// <remarks>
    /// <para>
    /// This is a convenience method that combines <see cref="SendAsync"/> with waiting for
    /// the <c>session.idle</c> event. Use this when you want to block until the assistant
    /// has finished processing the message.
    /// </para>
```

`SendAsync()` is simple—it wraps your prompt in a `SendMessageRequest` and sends it via RPC to the CLI. The method returns immediately with a message ID; the actual response comes back as events you receive via your event subscriptions.

If you want to wait for the response, use `SendAndWaitAsync()` instead. It:
1. Subscribes to events internally
2. Calls `SendAsync()`
3. Waits for a `SessionIdleEvent` (or error) before returning
4. Has a timeout (default 60s) to prevent hanging forever

### Subscribing to Events

The magic is in event subscriptions. When you call `session.On(handler)`, you register a callback that fires whenever the CLI sends an event:

```bash
sed -n '210,270p' /home/cip/src/copilot-sdk/dotnet/src/Session.cs
```

```output
    /// <param name="handler">A callback to be invoked when a session event occurs.</param>
    /// <returns>An <see cref="IDisposable"/> that, when disposed, unsubscribes the handler.</returns>
    /// <remarks>
    /// <para>
    /// Events include assistant messages, tool executions, errors, and session state changes.
    /// Multiple handlers can be registered and will all receive events.
    /// </para>
    /// <para>
    /// Handler exceptions are allowed to propagate so they are not lost.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using var subscription = session.On(evt =>
    /// {
    ///     switch (evt)
    ///     {
    ///         case AssistantMessageEvent:
    ///             Console.WriteLine($"Assistant: {evt.Data?.Content}");
    ///             break;
    ///         case SessionErrorEvent:
    ///             Console.WriteLine($"Error: {evt.Data?.Message}");
    ///             break;
    ///     }
    /// });
    ///
    /// // The handler is automatically unsubscribed when the subscription is disposed.
    /// </code>
    /// </example>
    public IDisposable On(SessionEventHandler handler)
    {
        _eventHandlers.Add(handler);
        return new OnDisposeCall(() => _eventHandlers.Remove(handler));
    }

    /// <summary>
    /// Dispatches an event to all registered handlers.
    /// </summary>
    /// <param name="sessionEvent">The session event to dispatch.</param>
    /// <remarks>
    /// This method is internal. Handler exceptions are allowed to propagate so they are not lost.
    /// </remarks>
    internal void DispatchEvent(SessionEvent sessionEvent)
    {
        foreach (var handler in _eventHandlers.ToArray())
        {
            // We allow handler exceptions to propagate so they are not lost
            handler(sessionEvent);
        }
    }

    /// <summary>
    /// Registers custom tool handlers for this session.
    /// </summary>
    /// <param name="tools">A collection of AI functions that can be invoked by the assistant.</param>
    /// <remarks>
    /// Tools allow the assistant to execute custom functions. When the assistant invokes a tool,
    /// the corresponding handler is called with the tool arguments.
    /// </remarks>
    internal void RegisterTools(ICollection<AIFunction> tools)
    {
```

The `On(handler)` method:
1. Adds your handler to `_eventHandlers` (a HashSet)
2. Returns a disposable that removes the handler when disposed
3. Allows multiple handlers to be registered—they all receive events

When the CLI sends an event (via RPC), the SDK deserializes it into a `SessionEvent` subclass and calls `DispatchEvent()`, which iterates through all registered handlers. This is a classic pub-sub pattern.

## Event Types and Streaming

The SDK defines many event types. Let's look at what events you can expect:

```bash
grep -E '^\s*(public )?class .* : SessionEvent' /home/cip/src/copilot-sdk/dotnet/src/Generated/SessionEvents.cs | head -25
```

```output
```

The SDK generates **many** event types from the protocol schema. Here are the main categories:

**Lifecycle events:**
- `SessionStartEvent` — session started
- `SessionIdleEvent` — session finished processing
- `SessionErrorEvent` — an error occurred
- `SessionShutdownEvent` — session is shutting down

**Message events:**
- `UserMessageEvent` — user message was added
- `AssistantMessageEvent` — complete assistant response
- `AssistantMessageDeltaEvent` — streaming chunk of response (when `Streaming = true`)

**Context/state events:**
- `SessionCompactionStartEvent` — background compaction started
- `SessionCompactionCompleteEvent` — compaction finished
- `SessionUsageInfoEvent` — token usage stats

**Tool events:**
- `ToolExecutionStartEvent` — CLI is about to invoke your tool
- `ToolExecutionCompleteEvent` — tool execution finished

You use pattern matching to handle them:

```bash
sed -n '280,330p' /home/cip/src/copilot-sdk/dotnet/README.md
```

````output
    Streaming = true
});

// Use TaskCompletionSource to wait for completion
var done = new TaskCompletionSource();

session.On(evt =>
{
    switch (evt)
    {
        case AssistantMessageDeltaEvent delta:
            // Streaming message chunk - print incrementally
            Console.Write(delta.Data.DeltaContent);
            break;
        case AssistantReasoningDeltaEvent reasoningDelta:
            // Streaming reasoning chunk (if model supports reasoning)
            Console.Write(reasoningDelta.Data.DeltaContent);
            break;
        case AssistantMessageEvent msg:
            // Final message - complete content
            Console.WriteLine("\n--- Final message ---");
            Console.WriteLine(msg.Data.Content);
            break;
        case AssistantReasoningEvent reasoningEvt:
            // Final reasoning content (if model supports reasoning)
            Console.WriteLine("--- Reasoning ---");
            Console.WriteLine(reasoningEvt.Data.Content);
            break;
        case SessionIdleEvent:
            // Session finished processing
            done.SetResult();
            break;
    }
});

await session.SendAsync(new MessageOptions { Prompt = "Tell me a short story" });
await done.Task; // Wait for streaming to complete
```

When `Streaming = true`:

- `AssistantMessageDeltaEvent` events are sent with `DeltaContent` containing incremental text
- `AssistantReasoningDeltaEvent` events are sent with `DeltaContent` for reasoning/chain-of-thought (model-dependent)
- Accumulate `DeltaContent` values to build the full response progressively
- The final `AssistantMessageEvent` and `AssistantReasoningEvent` events contain the complete content

Note: `AssistantMessageEvent` and `AssistantReasoningEvent` (final events) are always sent regardless of streaming setting.

## Infinite Sessions

By default, sessions use **infinite sessions** which automatically manage context window limits through background compaction and persist state to a workspace directory.
````

When you enable streaming (`Streaming = true`), the CLI sends response chunks incrementally as `AssistantMessageDeltaEvent` events. This is useful for long-running requests—you can display output to the user as it arrives, rather than waiting for the complete response. The final `AssistantMessageEvent` always contains the complete, final content.

## Tools: Extending the CLI

One of the most powerful features is **tools**—functions in your app that the CLI can call back into when the assistant needs them. Let's see how that works:

```bash
sed -n '260,320p' /home/cip/src/copilot-sdk/dotnet/src/Session.cs
```

```output

    /// <summary>
    /// Registers custom tool handlers for this session.
    /// </summary>
    /// <param name="tools">A collection of AI functions that can be invoked by the assistant.</param>
    /// <remarks>
    /// Tools allow the assistant to execute custom functions. When the assistant invokes a tool,
    /// the corresponding handler is called with the tool arguments.
    /// </remarks>
    internal void RegisterTools(ICollection<AIFunction> tools)
    {
        _toolHandlers.Clear();
        foreach (var tool in tools)
        {
            _toolHandlers.Add(tool.Name, tool);
        }
    }

    /// <summary>
    /// Retrieves a registered tool by name.
    /// </summary>
    /// <param name="name">The name of the tool to retrieve.</param>
    /// <returns>The tool if found; otherwise, <c>null</c>.</returns>
    internal AIFunction? GetTool(string name) =>
        _toolHandlers.TryGetValue(name, out var tool) ? tool : null;

    /// <summary>
    /// Registers a handler for permission requests.
    /// </summary>
    /// <param name="handler">The permission handler function.</param>
    /// <remarks>
    /// When the assistant needs permission to perform certain actions (e.g., file operations),
    /// this handler is called to approve or deny the request.
    /// </remarks>
    internal void RegisterPermissionHandler(PermissionHandler handler)
    {
        _permissionHandlerLock.Wait();
        try
        {
            _permissionHandler = handler;
        }
        finally
        {
            _permissionHandlerLock.Release();
        }
    }

    /// <summary>
    /// Handles a permission request from the Copilot CLI.
    /// </summary>
    /// <param name="permissionRequestData">The permission request data from the CLI.</param>
    /// <returns>A task that resolves with the permission decision.</returns>
    internal async Task<PermissionRequestResult> HandlePermissionRequestAsync(JsonElement permissionRequestData)
    {
        await _permissionHandlerLock.WaitAsync();
        PermissionHandler? handler;
        try
        {
            handler = _permissionHandler;
        }
        finally
```

When you create a session with tools, the SDK stores them in `_toolHandlers` (a dictionary mapping tool name to function). When the CLI invokes a tool, it sends an RPC call back to the SDK, the SDK looks up the tool, executes it, and returns the result to the CLI.

This is a **callback pattern**—your tools are stored locally and invoked synchronously when needed.

### How Tool Invocation Works

Let's look at how the RPC layer handles incoming tool calls:

```bash
sed -n '1,150p' /home/cip/src/copilot-sdk/dotnet/src/Generated/Rpc.cs
```

```output
/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *--------------------------------------------------------------------------------------------*/

// AUTO-GENERATED FILE - DO NOT EDIT
// Generated from: api.schema.json

using System.Text.Json;
using System.Text.Json.Serialization;
using StreamJsonRpc;

namespace GitHub.Copilot.SDK.Rpc;

public class PingResult
{
    /// <summary>Echoed message (or default greeting)</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>Server timestamp in milliseconds</summary>
    [JsonPropertyName("timestamp")]
    public double Timestamp { get; set; }

    /// <summary>Server protocol version number</summary>
    [JsonPropertyName("protocolVersion")]
    public double ProtocolVersion { get; set; }
}

internal class PingRequest
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public class ModelCapabilitiesSupports
{
    [JsonPropertyName("vision")]
    public bool Vision { get; set; }

    /// <summary>Whether this model supports reasoning effort configuration</summary>
    [JsonPropertyName("reasoningEffort")]
    public bool ReasoningEffort { get; set; }
}

public class ModelCapabilitiesLimits
{
    [JsonPropertyName("max_prompt_tokens")]
    public double? MaxPromptTokens { get; set; }

    [JsonPropertyName("max_output_tokens")]
    public double? MaxOutputTokens { get; set; }

    [JsonPropertyName("max_context_window_tokens")]
    public double MaxContextWindowTokens { get; set; }
}

/// <summary>Model capabilities and limits</summary>
public class ModelCapabilities
{
    [JsonPropertyName("supports")]
    public ModelCapabilitiesSupports Supports { get; set; } = new();

    [JsonPropertyName("limits")]
    public ModelCapabilitiesLimits Limits { get; set; } = new();
}

/// <summary>Policy state (if applicable)</summary>
public class ModelPolicy
{
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("terms")]
    public string Terms { get; set; } = string.Empty;
}

/// <summary>Billing information</summary>
public class ModelBilling
{
    [JsonPropertyName("multiplier")]
    public double Multiplier { get; set; }
}

public class Model
{
    /// <summary>Model identifier (e.g., "claude-sonnet-4.5")</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Model capabilities and limits</summary>
    [JsonPropertyName("capabilities")]
    public ModelCapabilities Capabilities { get; set; } = new();

    /// <summary>Policy state (if applicable)</summary>
    [JsonPropertyName("policy")]
    public ModelPolicy? Policy { get; set; }

    /// <summary>Billing information</summary>
    [JsonPropertyName("billing")]
    public ModelBilling? Billing { get; set; }

    /// <summary>Supported reasoning effort levels (only present if model supports reasoning effort)</summary>
    [JsonPropertyName("supportedReasoningEfforts")]
    public List<string>? SupportedReasoningEfforts { get; set; }

    /// <summary>Default reasoning effort level (only present if model supports reasoning effort)</summary>
    [JsonPropertyName("defaultReasoningEffort")]
    public string? DefaultReasoningEffort { get; set; }
}

public class ModelsListResult
{
    /// <summary>List of available models with full metadata</summary>
    [JsonPropertyName("models")]
    public List<Model> Models { get; set; } = new();
}

public class Tool
{
    /// <summary>Tool identifier (e.g., "bash", "grep", "str_replace_editor")</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional namespaced name for declarative filtering (e.g., "playwright/navigate" for MCP tools)</summary>
    [JsonPropertyName("namespacedName")]
    public string? NamespacedName { get; set; }

    /// <summary>Description of what the tool does</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>JSON Schema for the tool's input parameters</summary>
    [JsonPropertyName("parameters")]
    public Dictionary<string, object>? Parameters { get; set; }

    /// <summary>Optional instructions for how to use this tool effectively</summary>
    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }
}

public class ToolsListResult
{
    /// <summary>List of available built-in tools with metadata</summary>
    [JsonPropertyName("tools")]
    public List<Tool> Tools { get; set; } = new();
}
```

```bash
grep -A 50 'OnToolCall' /home/cip/src/copilot-sdk/dotnet/src/Client.cs | head -60
```

```output
        rpc.AddLocalRpcMethod("tool.call", handler.OnToolCall);
        rpc.AddLocalRpcMethod("permission.request", handler.OnPermissionRequest);
        rpc.AddLocalRpcMethod("userInput.request", handler.OnUserInputRequest);
        rpc.AddLocalRpcMethod("hooks.invoke", handler.OnHooksInvoke);
        rpc.StartListening();

        _rpc = new ServerRpc(rpc);

        return new Connection(rpc, cliProcess, tcpClient, networkStream);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Using happy path from https://microsoft.github.io/vs-streamjsonrpc/docs/nativeAOT.html")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Using happy path from https://microsoft.github.io/vs-streamjsonrpc/docs/nativeAOT.html")]
    private static SystemTextJsonFormatter CreateSystemTextJsonFormatter() =>
        new SystemTextJsonFormatter() { JsonSerializerOptions = SerializerOptionsForMessageFormatter };

    private static JsonSerializerOptions SerializerOptionsForMessageFormatter { get; } = CreateSerializerOptions();

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            AllowOutOfOrderMetadataProperties = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        options.TypeInfoResolverChain.Add(ClientJsonContext.Default);
        options.TypeInfoResolverChain.Add(TypesJsonContext.Default);
        options.TypeInfoResolverChain.Add(CopilotSession.SessionJsonContext.Default);
        options.TypeInfoResolverChain.Add(SessionEventsJsonContext.Default);
        options.TypeInfoResolverChain.Add(SDK.Rpc.RpcJsonContext.Default);

        options.MakeReadOnly();

        return options;
    }

    internal CopilotSession? GetSession(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var session) ? session : null;

    /// <summary>
    /// Disposes the <see cref="CopilotClient"/> synchronously.
    /// </summary>
    /// <remarks>
    /// Prefer using <see cref="DisposeAsync"/> for better performance in async contexts.
    /// </remarks>
    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }

--
        public async Task<ToolCallResponse> OnToolCall(string sessionId,
            string toolCallId,
            string toolName,
            object? arguments)
        {
            var session = client.GetSession(sessionId);
            if (session == null)
            {
```

```bash
sed -n '560,630p' /home/cip/src/copilot-sdk/dotnet/src/Client.cs
```

```output
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> that can be used to cancel the operation.</param>
    /// <returns>A task that resolves with the authentication status.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the client is not connected.</exception>
    public async Task<GetAuthStatusResponse> GetAuthStatusAsync(CancellationToken cancellationToken = default)
    {
        var connection = await EnsureConnectedAsync(cancellationToken);

        return await InvokeRpcAsync<GetAuthStatusResponse>(
            connection.Rpc, "auth.getStatus", [], cancellationToken);
    }

    /// <summary>
    /// Lists available models with their metadata.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> that can be used to cancel the operation.</param>
    /// <returns>A task that resolves with a list of available models.</returns>
    /// <remarks>
    /// Results are cached after the first successful call to avoid rate limiting.
    /// The cache is cleared when the client disconnects.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when the client is not connected or not authenticated.</exception>
    public async Task<List<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var connection = await EnsureConnectedAsync(cancellationToken);

        // Use semaphore for async locking to prevent race condition with concurrent calls
        await _modelsCacheLock.WaitAsync(cancellationToken);
        try
        {
            // Check cache (already inside lock)
            if (_modelsCache is not null)
            {
                return new List<ModelInfo>(_modelsCache); // Return a copy to prevent cache mutation
            }

            // Cache miss - fetch from backend while holding lock
            var response = await InvokeRpcAsync<GetModelsResponse>(
                connection.Rpc, "models.list", [], cancellationToken);

            // Update cache before releasing lock
            _modelsCache = response.Models;

            return new List<ModelInfo>(response.Models); // Return a copy to prevent cache mutation
        }
        finally
        {
            _modelsCacheLock.Release();
        }
    }

    /// <summary>
    /// Gets the ID of the most recently used session.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> that can be used to cancel the operation.</param>
    /// <returns>A task that resolves with the session ID, or null if no sessions exist.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the client is not connected.</exception>
    /// <example>
    /// <code>
    /// var lastId = await client.GetLastSessionIdAsync();
    /// if (lastId != null)
    /// {
    ///     var session = await client.ResumeSessionAsync(lastId);
    /// }
    /// </code>
    /// </example>
    public async Task<string?> GetLastSessionIdAsync(CancellationToken cancellationToken = default)
    {
        var connection = await EnsureConnectedAsync(cancellationToken);

        var response = await InvokeRpcAsync<GetLastSessionIdResponse>(
```

```bash
sed -n '1152,1200p' /home/cip/src/copilot-sdk/dotnet/src/Client.cs
```

```output
        public async Task<ToolCallResponse> OnToolCall(string sessionId,
            string toolCallId,
            string toolName,
            object? arguments)
        {
            var session = client.GetSession(sessionId);
            if (session == null)
            {
                throw new ArgumentException($"Unknown session {sessionId}");
            }

            if (session.GetTool(toolName) is not { } tool)
            {
                return new ToolCallResponse(new ToolResultObject
                {
                    TextResultForLlm = $"Tool '{toolName}' is not supported.",
                    ResultType = "failure",
                    Error = $"tool '{toolName}' not supported"
                });
            }

            try
            {
                var invocation = new ToolInvocation
                {
                    SessionId = sessionId,
                    ToolCallId = toolCallId,
                    ToolName = toolName,
                    Arguments = arguments
                };

                // Map args from JSON into AIFunction format
                var aiFunctionArgs = new AIFunctionArguments
                {
                    Context = new Dictionary<object, object?>
                    {
                        // Allow recipient to access the raw ToolInvocation if they want, e.g., to get SessionId
                        // This is an alternative to using MEAI's ConfigureParameterBinding, which we can't use
                        // because we're not the ones producing the AIFunction.
                        [typeof(ToolInvocation)] = invocation
                    }
                };

                if (arguments is not null)
                {
                    if (arguments is not JsonElement incomingJsonArgs)
                    {
                        throw new InvalidOperationException($"Incoming arguments must be a {nameof(JsonElement)}; received {arguments.GetType().Name}");
                    }
```

When the CLI needs to invoke a tool, it sends an RPC call `tool.call` with the session ID, tool name, and arguments. Here's what happens:

1. The RPC handler `OnToolCall()` is invoked by the SDK's RPC listener
2. It looks up the session by ID
3. It retrieves the tool function from the session's `_toolHandlers` dictionary
4. It constructs `AIFunctionArguments` from the incoming JSON arguments
5. It invokes the tool function
6. It wraps the result in a `ToolCallResponse` and returns it to the CLI

All of this happens synchronously—the CLI blocks waiting for the tool to complete.

## Infinite Sessions and Workspace State

By default, .NET SDK sessions use **infinite sessions**—automatic background compaction that keeps context window usage manageable and persists state to disk:

```bash
sed -n '328,362p' /home/cip/src/copilot-sdk/dotnet/README.md
```

````output
## Infinite Sessions

By default, sessions use **infinite sessions** which automatically manage context window limits through background compaction and persist state to a workspace directory.

```csharp
// Default: infinite sessions enabled with default thresholds
var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = "gpt-5"
});

// Access the workspace path for checkpoints and files
Console.WriteLine(session.WorkspacePath);
// => ~/.copilot/session-state/{sessionId}/

// Custom thresholds
var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = "gpt-5",
    InfiniteSessions = new InfiniteSessionConfig
    {
        Enabled = true,
        BackgroundCompactionThreshold = 0.80, // Start compacting at 80% context usage
        BufferExhaustionThreshold = 0.95      // Block at 95% until compaction completes
    }
});

// Disable infinite sessions
var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = "gpt-5",
    InfiniteSessions = new InfiniteSessionConfig { Enabled = false }
});
```

````

When infinite sessions are enabled (the default), the CLI automatically emits `SessionCompactionStartEvent` and `SessionCompactionCompleteEvent` when it's managing context. You can watch for these to know when cleanup is happening:

- **BackgroundCompactionThreshold** (default: 0.8) — start compacting at 80% context window usage
- **BufferExhaustionThreshold** (default: 0.95) — block user messages at 95% and wait for compaction

The workspace path (`~/.copilot/session-state/{sessionId}/`) contains:
- `checkpoints/` — compressed snapshots of conversation history
- `plan.md` — user's notes and task breakdowns
- `files/` — session artifacts

This gives your app persistent access to session state across sessions and machine restarts.

## JSON-RPC Under the Hood

The entire SDK is built on **JSON-RPC 2.0**—a lightweight protocol for remote procedure calls. The SDK uses StreamJsonRpc, Microsoft's RPC library, which handles serialization, message framing, and concurrency.

Here's the flow:

```bash
cat > /tmp/rpc_explanation.txt << 'EOF'
1. **You call**: client.CreateSessionAsync()
2. **SDK constructs**: a JSON-RPC request { jsonrpc: "2.0", id: 1, method: "session.create", params: [...] }
3. **Sends over**: stdio or TCP to the CLI process
4. **CLI receives**: deserializes, creates session, returns { jsonrpc: "2.0", id: 1, result: { sessionId: "abc" } }
5. **SDK deserializes**: the result and returns the parsed response

Throughout the session:
- **CLI → SDK**: session.event, session.lifecycle, tool.call, permission.request, userInput.request, hooks.invoke
- **SDK → CLI**: session.send, session.abort, session.destroy, etc.

Both sides are listening and can initiate calls. This is **bidirectional RPC**—not just request/response but real-time event streaming.
EOF
cat /tmp/rpc_explanation.txt
```

```output
1. **You call**: client.CreateSessionAsync()
2. **SDK constructs**: a JSON-RPC request { jsonrpc: "2.0", id: 1, method: "session.create", params: [...] }
3. **Sends over**: stdio or TCP to the CLI process
4. **CLI receives**: deserializes, creates session, returns { jsonrpc: "2.0", id: 1, result: { sessionId: "abc" } }
5. **SDK deserializes**: the result and returns the parsed response

Throughout the session:
- **CLI → SDK**: session.event, session.lifecycle, tool.call, permission.request, userInput.request, hooks.invoke
- **SDK → CLI**: session.send, session.abort, session.destroy, etc.

Both sides are listening and can initiate calls. This is **bidirectional RPC**—not just request/response but real-time event streaming.
```

## Architecture at a Glance

Here's the big picture:



Your app is the **client**. You:
- Start the client
- Create sessions
- Send prompts
- Subscribe to events
- Define tools

The CLI is the **server**. It:
- Handles model inference
- Manages conversation state
- Calls back into your tools
- Emits session events
- Manages infinite session compaction

The protocol between them is JSON-RPC. Both sides can initiate calls. This async, bidirectional design lets your app feel responsive while the CLI does heavy lifting.

## Architecture at a Glance

Here's the big picture:

Your app (CopilotClient + CopilotSession) ↔ JSON-RPC (stdio/TCP) ↔ Copilot CLI (server)

Your app is the **client**. You:
- Start the client
- Create sessions
- Send prompts
- Subscribe to events
- Define tools

The CLI is the **server**. It:
- Handles model inference
- Manages conversation state
- Calls back into your tools
- Emits session events
- Manages infinite session compaction

The protocol between them is JSON-RPC. Both sides can initiate calls. This async, bidirectional design lets your app feel responsive while the CLI does heavy lifting.

## Common Patterns and Best Practices

### Pattern 1: Fire and Forget with Event Subscription

Don't wait for responses. Subscribe to events and let them come in naturally:

```csharp
using var subscription = session.On(evt =>
{
    if (evt is AssistantMessageEvent msg)
        Console.WriteLine(msg.Data?.Content);
    else if (evt is SessionErrorEvent err)
        Console.WriteLine($"Error: {err.Data?.Message}");
});

// Fire the message
await session.SendAsync(new MessageOptions { Prompt = "Hello" });

// Your code continues... events arrive asynchronously
```

### Pattern 2: Blocking Wait with SendAndWaitAsync

When you need a response before continuing (like a REPL or synchronous API):

```csharp
var response = await session.SendAndWaitAsync(
    new MessageOptions { Prompt = "What is 2+2?" },
    timeout: TimeSpan.FromSeconds(30)
);

Console.WriteLine(response?.Data?.Content);
```

### Pattern 3: Multiple Sessions

Use different sessions for different tasks. Each has its own conversation history and context:

```csharp
var session1 = await client.CreateSessionAsync(new SessionConfig { Model = "gpt-5" });
var session2 = await client.CreateSessionAsync(new SessionConfig { Model = "claude-sonnet-4.5" });

// Both run in parallel
await session1.SendAsync(new MessageOptions { Prompt = "Analyze this code" });
await session2.SendAsync(new MessageOptions { Prompt = "Write a test" });
```

### Pattern 4: Tools That Call Back

Tools are callbacks that run synchronously in response to model requests:

```csharp
using Microsoft.Extensions.AI;

var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = "gpt-5",
    Tools = new[]
    {
        AIFunctionFactory.Create(
            async (string query) => await SearchDatabase(query),
            "lookup_data",
            "Search our database"
        )
    }
});

// When the model needs data, it invokes lookup_data
// Your function runs, returns results, model continues
```

### Pattern 5: Graceful Shutdown

Always clean up properly:

```csharp
try
{
    await using var client = new CopilotClient();
    await client.StartAsync();
    
    await using var session = await client.CreateSessionAsync();
    // ... use session ...
}
finally
{
    // Automatically called by 'using'
    // Destroys sessions, closes RPC, stops CLI
}
```

## Summary

The Copilot .NET SDK is a **thin, type-safe wrapper** around JSON-RPC communication with the Copilot CLI. Key takeaways:

1. **CopilotClient** manages server lifecycle and session creation
2. **CopilotSession** is your interface to conversations—send prompts, subscribe to events, define tools
3. **Events** are how you receive responses—subscription-based, not blocking
4. **Tools** are callbacks that let the model invoke your code synchronously
5. **Infinite sessions** automatically manage context and persist state to disk
6. **JSON-RPC** is bidirectional—both client and server can initiate calls
7. **Streaming** lets you receive response chunks as they arrive for better UX

The design prioritizes **simplicity** (three main classes: Client, Session, SessionEvent), **flexibility** (support for tools, hooks, custom prompts), and **robustness** (proper cleanup, infinite sessions, error handling).

For more details, read the SDK README and explore the examples in the test directory. Happy coding!
