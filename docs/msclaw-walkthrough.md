# MsClaw — Code Walkthrough

*2026-03-02T04:32:39Z by Showboat 0.6.1*
<!-- showboat-id: 826cc533-ce4a-4d76-9415-d9964866e30a -->

# What is MsClaw?

MsClaw is a GitHub Copilot Extension that gives AI agents a **persistent identity** — a 'mind'. Instead of starting fresh with each conversation, MsClaw loads a mind directory containing an identity file (SOUL.md), agent definitions, and working memory, then serves it as a Copilot agent through the GitHub Copilot Runtime API.

Think of it like this:
- **Without MsClaw**: Every conversation with Copilot is stateless. The agent has no memory of previous interactions, no persistent identity, no ongoing context.
- **With MsClaw**: An agent has a mind with a defined SOUL, can load its own agents, maintains working memory, and persists across sessions.

The core responsibility is simple: **load a mind directory, validate it, expose it through the Copilot Runtime API on localhost:5050, and provide an extension system for dynamic behavior injection**.

## Architecture Overview

MsClaw is a .NET 9 C# application with these main pieces:

1. **Program.cs** — CLI entry point, argument parsing, service registration
2. **Core/** — Business logic: mind discovery, validation, reading, orchestration, and extension management
3. **Models/** — Data structures for configuration and chat request/response payloads
4. **Templates/** — Embedded templates for scaffolding new minds

The app boots in this sequence:
1. Parse CLI arguments (--mind, --new-mind, or auto-discover)
2. Scaffold a mind if --new-mind was specified
3. Validate the mind directory
4. Load the mind's identity, agents, and working memory
5. Initialize the extension system (load core and external extensions)
6. Register a Copilot agent with the Runtime
7. Start an HTTP server listening on :5050
8. Handle incoming chat requests and extension hooks

## Entry Point: Program.cs

Program.cs is the first code that runs. It parses CLI arguments, sets up dependency injection, and starts the HTTP server.

```bash
sed -n '1,50p' src/MsClaw/Program.cs
```

```output
using MsClaw.Core;
using MsClaw.Models;
using GitHub.Copilot.SDK;

var builder = WebApplication.CreateBuilder(args);

// Bootstrap: resolve mind root before starting the server
var validator = new MindValidator();
var configPersistence = new ConfigPersistence();
var discovery = new MindDiscovery(configPersistence, validator);
var scaffold = new MindScaffold();
var orchestrator = new BootstrapOrchestrator(validator, discovery, scaffold, configPersistence);

string resolvedMindRoot;
try
{
    var bootstrapResult = orchestrator.Run(args);
    if (bootstrapResult is null)
    {
        // --reset-config was used, exit cleanly
        return;
    }

    resolvedMindRoot = bootstrapResult.MindRoot;
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return;
}

builder.Services.Configure<MsClawOptions>(builder.Configuration);
builder.Services.Configure<MsClawOptions>(opts =>
{
    opts.MindRoot = resolvedMindRoot;
});

// Register the same instances used during bootstrap — avoids duplicate instantiation
builder.Services.AddSingleton<IMindValidator>(validator);
builder.Services.AddSingleton<IConfigPersistence>(configPersistence);
builder.Services.AddSingleton<IMindDiscovery>(discovery);
builder.Services.AddSingleton<IMindScaffold>(scaffold);
builder.Services.AddSingleton<IIdentityLoader, IdentityLoader>();
builder.Services.AddSingleton<IMindReader, MindReader>();
builder.Services.AddSingleton<ExtensionManager>();
builder.Services.AddSingleton<IExtensionManager>(sp => sp.GetRequiredService<ExtensionManager>());

// Register CopilotClient as singleton
builder.Services.AddSingleton<CopilotClient>(sp =>
{
```

The bootstrap phase happens **before** the HTTP server starts. MsClaw uses a BootstrapOrchestrator to:
1. Validate the mind directory
2. Auto-discover or scaffold it based on CLI arguments
3. Persist the configuration for next runs

This is important because all downstream services (IMindReader, IMindValidator, etc.) depend on knowing the mind root path upfront. Notice that ExtensionManager is registered as a singleton—this is the new feature that enables dynamic behavior injection.

```bash
sed -n '49,90p' src/MsClaw/Program.cs
```

```output
builder.Services.AddSingleton<CopilotClient>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MsClawOptions>>().Value;
    return new CopilotClient(new CopilotClientOptions
    {
        Cwd = Path.GetFullPath(options.MindRoot),
        AutoStart = true,
        UseStdio = true,
        CliPath = CliLocator.ResolveCopilotCliPath()
    });
});

builder.Services.AddSingleton<CopilotRuntimeClient>();
builder.Services.AddSingleton<ICopilotRuntimeClient>(sp => sp.GetRequiredService<CopilotRuntimeClient>());
builder.Services.AddSingleton<ISessionControl>(sp => sp.GetRequiredService<CopilotRuntimeClient>());

var app = builder.Build();
var extensionManager = app.Services.GetRequiredService<IExtensionManager>();
await extensionManager.InitializeAsync();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/session/new", async (ICopilotRuntimeClient copilotClient, CancellationToken cancellationToken) =>
{
    var sessionId = await copilotClient.CreateSessionAsync(cancellationToken);
    return Results.Ok(new { sessionId });
});

app.MapPost("/command", async (
    ChatRequest request,
    IExtensionManager extensionManager,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "message is required" });
    }

    var result = await extensionManager.TryExecuteCommandAsync(request.Message, request.SessionId, cancellationToken);
    if (result is null)
    {
        return Results.BadRequest(new { error = "input is not a command" });
    }
```

After building the app, Program.cs initializes the extension system before starting the HTTP server. The extension manager hooks into the application lifecycle: it initializes on startup and shuts down gracefully when the app stops.

The app exposes three main endpoints:
- **/health** — Simple health check
- **/session/new** — Create a new Copilot session
- **/command** — Execute extension commands (anything starting with /)

The /command endpoint is new—it delegates message processing to the extension manager, allowing extensions to intercept and handle slash commands.

## The Extension System: A New Architecture

The extension system is the major new feature in MsClaw. It allows external code to hook into the application lifecycle, register tools, handle commands, and extend HTTP endpoints—all without modifying the core codebase.

### Extension Abstractions

Extensions implement a simple interface. Let's look at the abstraction layer.

```bash
sed -n '1,50p' src/MsClaw/Core/ExtensionAbstractions.cs
```

```output
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;

namespace MsClaw.Core;

public interface IExtension : IAsyncDisposable
{
    void Register(IMsClawPluginApi api);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public abstract class ExtensionBase : IExtension
{
    public abstract void Register(IMsClawPluginApi api);

    public virtual Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public virtual Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public interface IMsClawPluginApi
{
    string ExtensionId { get; }
    JsonElement? Config { get; }

    void RegisterTool(AIFunction tool);
    void RegisterHook(string eventName, ExtensionHookHandler handler);
    void RegisterService(IHostedService service);
    void RegisterCommand(string command, ExtensionCommandHandler handler);
    void RegisterHttpRoute(Action<IEndpointRouteBuilder> mapRoute);
}

public delegate Task ExtensionHookHandler(ExtensionHookContext context, CancellationToken cancellationToken);

public delegate Task<string> ExtensionCommandHandler(ExtensionCommandContext context, CancellationToken cancellationToken);

public sealed class ExtensionHookContext
{
    public required string EventName { get; init; }
    public string? SessionId { get; init; }
    public string? Message { get; init; }
    public string? Response { get; init; }
}

```

An extension starts by implementing **IExtension**, which has three lifecycle methods:
- **Register(IMsClawPluginApi api)** — Called once during initialization. This is where the extension tells MsClaw what it provides: tools, hooks, commands, services, HTTP routes.
- **StartAsync()** — Called after all extensions are loaded. Good for starting background services or logging.
- **StopAsync()** — Called during shutdown. Extensions can clean up resources.

The **IMsClawPluginApi** interface is the contract between MsClaw and extensions. An extension calls methods on this API to register:
- **RegisterTool(AIFunction)** — Add an AI function the Copilot agent can call
- **RegisterHook(eventName, handler)** — Listen for MsClaw lifecycle events
- **RegisterCommand(command, handler)** — Handle slash commands (e.g., /memory, /reset)
- **RegisterService(IHostedService)** — Register a background service
- **RegisterHttpRoute(mapRoute)** — Add custom HTTP endpoints

This design is **loosely coupled**. Core MsClaw doesn't know about specific extensions—it just fires hooks and asks extensions to register what they need.

```bash
sed -n '59,139p' src/MsClaw/Core/ExtensionAbstractions.cs
```

```output
public static class ExtensionEvents
{
    public const string SessionCreate = "session:create";
    public const string SessionResume = "session:resume";
    public const string SessionEnd = "session:end";
    public const string MessageReceived = "message:received";
    public const string MessageSent = "message:sent";
    public const string AgentBootstrap = "agent:bootstrap";
    public const string ExtensionLoaded = "extension:loaded";
}

public enum ExtensionTier
{
    Core,
    External
}

public sealed class LoadedExtensionInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required ExtensionTier Tier { get; init; }
    public bool Started { get; init; }
    public bool Failed { get; init; }
}

public sealed class PluginManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("entryAssembly")]
    public string EntryAssembly { get; set; } = "";

    [JsonPropertyName("entryType")]
    public string EntryType { get; set; } = "";

    [JsonPropertyName("dependencies")]
    public List<PluginDependency> Dependencies { get; set; } = [];

    [JsonPropertyName("config")]
    public JsonElement? Config { get; set; }
}

public sealed class PluginDependency
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("versionRange")]
    public string? VersionRange { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    public string Range => !string.IsNullOrWhiteSpace(VersionRange)
        ? VersionRange!
        : string.IsNullOrWhiteSpace(Version) ? "*" : Version!;
}

public interface IExtensionManager
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
    Task ReloadExternalAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<AIFunction> GetTools();
    IReadOnlyList<LoadedExtensionInfo> GetLoadedExtensions();
    void MapRoutes(IEndpointRouteBuilder endpointRouteBuilder);

    Task FireHookAsync(string eventName, ExtensionHookContext context, CancellationToken cancellationToken = default);
    Task<string?> TryExecuteCommandAsync(string input, string? sessionId, CancellationToken cancellationToken = default);
}
```

### Extension Events and Lifecycle

MsClaw fires specific events throughout the application lifecycle:

- **session:create** — A new Copilot session was created
- **session:resume** — An existing session was resumed
- **session:end** — A session ended
- **message:received** — A user sent a message
- **message:sent** — An agent response was sent
- **agent:bootstrap** — The agent is booting (after all extensions load)
- **extension:loaded** — All extensions have been loaded

Extensions listen for events by calling **RegisterHook(eventName, handler)**. For example, a logging extension might listen to 'message:received' to track conversations; a memory extension might listen to 'message:sent' to update its working memory.

### Plugin Manifests and Dependencies

Extensions are defined in a **plugin.json** manifest that specifies the extension ID, name, version, the entry assembly and type, optional configuration, and dependencies on other extensions.

**ExtensionTier** distinguishes between:
- **Core** — Built into MsClaw, always loaded
- **External** — Loaded from the mind directory, can be reloaded without restarting

This allows a mind to customize its behavior by adding external extensions.

## ExtensionManager: The Orchestrator

The **ExtensionManager** is the component responsible for loading, initializing, and coordinating extensions. It manages the lifecycle and provides the core dispatch mechanisms.

```bash
sed -n '48,79p' src/MsClaw/Core/ExtensionManager.cs
```

```output
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _reloadLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            await RegisterCoreExtensionsAsync(cancellationToken);
            await RegisterExternalExtensionsAsync(cancellationToken);

            await FireHookAsync(
                ExtensionEvents.ExtensionLoaded,
                new ExtensionHookContext { EventName = ExtensionEvents.ExtensionLoaded },
                cancellationToken);

            await StartExtensionsAsync(_loaded, cancellationToken);

            await FireHookAsync(
                ExtensionEvents.AgentBootstrap,
                new ExtensionHookContext { EventName = ExtensionEvents.AgentBootstrap },
                cancellationToken);

            _initialized = true;
        }
        finally
        {
            _reloadLock.Release();
        }
    }
```

### Initialization Sequence

The **InitializeAsync** method orchestrates the startup:

1. **RegisterCoreExtensionsAsync** — Load built-in extensions (part of MsClaw itself)
2. **RegisterExternalExtensionsAsync** — Load extensions from the mind directory
3. **FireHookAsync('extension:loaded')** — Notify extensions they're ready
4. **StartExtensionsAsync** — Call StartAsync() on all loaded extensions
5. **FireHookAsync('agent:bootstrap')** — Signal the agent to initialize

Notice the **_reloadLock** semaphore—it ensures initialization and reload are atomic. This prevents race conditions when external extensions are reloaded while the app is running.

The separation of 'extension:loaded' and 'agent:bootstrap' events is intentional. Core extensions might need to set up tools before the agent initializes. The 'agent:bootstrap' event signals that the extension system is ready and the agent can safely use all registered tools.

```bash
sed -n '183,211p' src/MsClaw/Core/ExtensionManager.cs
```

```output
    public async Task FireHookAsync(string eventName, ExtensionHookContext context, CancellationToken cancellationToken = default)
    {
        var normalizedEventName = NormalizeHookEventName(eventName);
        HookRegistration[] handlers;
        lock (_stateLock)
        {
            handlers = _hooks.TryGetValue(normalizedEventName, out var list)
                ? list.ToArray()
                : [];
        }

        foreach (var hook in handlers)
        {
            try
            {
                await hook.Handler(new ExtensionHookContext
                {
                    EventName = normalizedEventName,
                    SessionId = context.SessionId,
                    Message = context.Message,
                    Response = context.Response
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hook '{HookEvent}' failed in extension '{ExtensionId}'.", normalizedEventName, hook.ExtensionId);
            }
        }
    }
```

### Hook System

When an event fires, **FireHookAsync** executes all registered handlers for that event. The implementation:

1. **Normalizes the event name** (case-insensitive and trimmed)
2. **Locks _stateLock** and retrieves the handlers for that event
3. **Iterates through handlers** and calls each one
4. **Catches exceptions** per handler—if one extension's hook fails, others still run

This is **fail-safe by design**. One misbehaving extension can't crash the entire system or block other extensions from running. Errors are logged but don't propagate.

The _stateLock uses lock() statements to ensure thread safety—multiple threads might be firing hooks simultaneously while other threads are registering new tools or commands.

```bash
sed -n '213,255p' src/MsClaw/Core/ExtensionManager.cs
```

```output
    public async Task<string?> TryExecuteCommandAsync(string input, string? sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var trimmed = input.Trim();
        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            return null;
        }

        ParseCommand(trimmed, out var command, out var arguments);

        CommandRegistration? registration;
        lock (_stateLock)
        {
            _commands.TryGetValue(command, out registration);
        }

        if (registration is null)
        {
            return $"Unknown command: {command}";
        }

        var context = new ExtensionCommandContext
        {
            Command = command,
            Arguments = arguments,
            RawInput = trimmed,
            SessionId = sessionId
        };

        try
        {
            return await registration.Handler(context, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command '{Command}' failed in extension '{ExtensionId}'.", command, registration.ExtensionId);
            return $"Command failed: {ex.Message}";
        }
```

### Command Dispatch

**TryExecuteCommandAsync** handles user input that starts with a slash (e.g., /memory, /reset, /status). The flow:

1. **Validates** the input is non-empty and starts with /
2. **Parses** the command name and arguments (e.g., '/memory search foo' → command: 'memory', arguments: 'search foo')
3. **Looks up** the command in the _commands registry
4. **Executes** the handler if found, or returns an error if unknown

Like hooks, command execution is **fault-isolated**. If a command handler throws, the error is caught, logged, and returned as a string message back to the user. This prevents extensions from crashing the server.

Commands are how extensions provide interactive features to users. For example, a mind might register a /memory command to search or update its working memory, or a /status command to report internal state.

## Mind Reading and Identity Loading

Before the extension system can function, MsClaw must load the mind's identity and understand what agents it contains. Let's look at how minds are structured and loaded.

```bash
sed -n '1,40p' src/MsClaw/Core/IMindReader.cs
```

```output
namespace MsClaw.Core;

public interface IMindReader
{
    Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListDirectoryAsync(string path, CancellationToken cancellationToken = default);
    Task EnsureSyncedAsync(CancellationToken cancellationToken = default);
}
```

### IMindReader: Abstracting Mind Access

The **IMindReader** interface abstracts how MsClaw reads the mind directory. This abstraction exists to support future implementations:

- **ReadFileAsync(path)** — Read the contents of a file in the mind
- **ListDirectoryAsync(path)** — List contents of a directory
- **EnsureSyncedAsync()** — (Future use) Sync mind state with a remote source

The reader is implemented in **MindReader**, which currently just reads from the local filesystem. But the abstraction allows for future features like:
- Fetching minds from a remote registry
- Caching mind data
- Hot-reloading minds when they change on disk

```bash
sed -n '1,50p' src/MsClaw/Core/IdentityLoader.cs
```

```output
namespace MsClaw.Core;

public sealed class IdentityLoader : IIdentityLoader
{
    public async Task<string> LoadSystemMessageAsync(string mindRoot, CancellationToken cancellationToken = default)
    {
        var soulPath = Path.Combine(mindRoot, "SOUL.md");
        var soulContent = await File.ReadAllTextAsync(soulPath, cancellationToken);

        var agentsPath = Path.Combine(mindRoot, ".github", "agents");
        if (!Directory.Exists(agentsPath))
        {
            return soulContent;
        }

        var agentFiles = Directory.GetFiles(agentsPath, "*.agent.md", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (agentFiles.Length == 0)
        {
            return soulContent;
        }

        var parts = new List<string> { soulContent };
        foreach (var agentFile in agentFiles)
        {
            var content = await File.ReadAllTextAsync(agentFile, cancellationToken);
            parts.Add(StripFrontmatter(content));
        }

        return string.Join("\n\n---\n\n", parts);
    }

    private static string StripFrontmatter(string content)
    {
        if (!content.StartsWith("---", StringComparison.Ordinal))
        {
            return content;
        }

        var endIndex = content.IndexOf("---", 3, StringComparison.Ordinal);
        return endIndex > 0
            ? content[(endIndex + 3)..].TrimStart()
            : content;
    }
}
```

### Loading the Agent Identity

**IdentityLoader** builds the system prompt for the Copilot agent by combining:

1. **SOUL.md** — The mind's core identity and personality
2. **Agent files** — Individual agent definitions from 

The loader reads SOUL.md first, then appends each agent file (stripping their YAML frontmatter). This creates a single system prompt that defines both the agent's personality and the specific agent behaviors it should support.

This is how a mind can define multiple agent personalities—each one is an agent file that gets concatenated into the final system prompt.

## Putting It All Together: The Request Flow

Now let's trace what happens when a user sends a message to the Copilot agent.

```bash
sed -n '70,110p' src/MsClaw/Core/CopilotRuntimeClient.cs | head -30
```

```output
    {
        var session = await GetOrResumeSessionAsync(sessionId, cancellationToken);
        await _extensionManager.FireHookAsync(
            ExtensionEvents.MessageReceived,
            new ExtensionHookContext
            {
                EventName = ExtensionEvents.MessageReceived,
                SessionId = sessionId,
                Message = message
            },
            cancellationToken);

        var response = await session.SendAndWaitAsync(
            new MessageOptions { Prompt = message },
            timeout: TimeSpan.FromSeconds(120),
            cancellationToken: cancellationToken);

        var responseText = response?.Data?.Content
            ?? throw new InvalidOperationException("No assistant response received from Copilot session.");

        await _extensionManager.FireHookAsync(
            ExtensionEvents.MessageSent,
            new ExtensionHookContext
            {
                EventName = ExtensionEvents.MessageSent,
                SessionId = sessionId,
                Message = message,
                Response = responseText
            },
            cancellationToken);
```

### Request Handling in CopilotRuntimeClient

When a user sends a message through the Copilot Runtime:

1. **Fire 'message:received' hook** — Extensions are notified that a message arrived. A logging extension might record it; a memory extension might search its context.
2. **Send message to Copilot SDK** — The SDK session forwards the prompt to the LLM
3. **Wait for response** — The session waits up to 120 seconds for a response
4. **Fire 'message:sent' hook** — Extensions are notified of both the original message and the response. A memory extension might now update its working memory based on the exchange.
5. **Return response** — The response is sent back to the Copilot client

This design makes it easy for extensions to observe and react to conversations without modifying core MsClaw code. A new memory extension could be added without touching CopilotRuntimeClient—it just registers hooks for these two events.

## Extending MsClaw: Writing an Extension

Here's what it looks like to write a simple extension. Extensions are .NET assemblies that implement IExtension and inherit from ExtensionBase.

A typical extension:
- Inherits from ExtensionBase
- Overrides Register to subscribe to hooks and register tools/commands
- Implements hook handlers that react to lifecycle events

Extensions can be arbitrarily complex: they can register AI tools, expose HTTP endpoints, manage state, call external APIs, all through the IMsClawPluginApi interface. The plugin.json manifest describes the extension to MsClaw, specifying its entry type, dependencies, and optional configuration.

## Architecture Summary

MsClaw is built on these principles:

### 1. **Bootstrap First, Serve Second**
The app validates and loads the mind before starting the HTTP server. CLI argument parsing and mind discovery happen synchronously, upfront, in BootstrapOrchestrator.

### 2. **Extensions Are First-Class**
Extensions aren't an afterthought—they're baked into the core architecture. The extension system is initialized immediately after the mind loads and before Copilot agents start serving requests. This allows extensions to influence agent behavior from the very beginning.

### 3. **Hooks and Events, Not Direct Coupling**
Core MsClaw doesn't import or depend on specific extensions. Instead, it fires well-defined events (session:create, message:received, etc.). Extensions register hooks for these events. This is **loose coupling**—extensions can be swapped in and out without rebuilding MsClaw.

### 4. **Fail-Safe Extensions**
If an extension hook throws, it's caught and logged, but other extensions continue running. If an extension command fails, the error is returned to the user but the server stays up. This prevents a buggy extension from taking down the whole system.

### 5. **Runtime Reloadability**
External extensions can be reloaded via the ReloadExternalAsync() method. Core extensions (built into MsClaw) stay fixed, but a mind can update its external extensions without restarting the entire app.

This architecture makes it easy to build minds with custom behavior—just write extensions and drop them into the mind directory.

## File Structure

Here's the key files in the MsClaw codebase:

- **Program.cs** — Bootstrap, service registration, HTTP endpoints (/health, /session/new, /command)
- **Core/BootstrapOrchestrator.cs** — CLI parsing, mind discovery, mind scaffolding, validation
- **Core/MindValidator.cs** — Ensures a mind directory has required files (SOUL.md, etc.)
- **Core/MindDiscovery.cs** — Auto-discovers minds in standard locations
- **Core/MindScaffold.cs** — Scaffolds new minds from embedded templates
- **Core/IdentityLoader.cs** — Loads SOUL.md and agent definitions to build the system prompt
- **Core/MindReader.cs** — Reads files and directories from the mind
- **Core/CopilotRuntimeClient.cs** — Implements the Copilot Runtime protocol, fires hooks around messages
- **Core/ExtensionManager.cs** — Loads extensions, registers tools/hooks/commands, fires events
- **Core/ExtensionAbstractions.cs** — IExtension, IMsClawPluginApi, extension data models, event constants
- **Models/** — Data structures (MsClawOptions, ChatRequest, ChatResponse, etc.)

The code is organized by responsibility: bootstrap logic is separate from runtime logic is separate from extension management. This makes the codebase easy to navigate and modify.
