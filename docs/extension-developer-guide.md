# MS-Claw Extension Developer Guide

A hands-on guide for developers building extensions for MsClaw agents.

## Overview

MsClaw extensions are .NET assemblies that hook into the MsClaw lifecycle, register tools, handle commands, and extend the agent's capabilities—all without modifying the core codebase. This guide walks you through building your first extension.

## What You'll Build

By the end of this guide, you'll understand:
- The extension architecture and lifecycle
- How to implement IExtension and ExtensionBase
- How to register tools, commands, and hooks
- How to structure your extension with a plugin.json manifest
- How to test your extension with a MsClaw mind

## Prerequisites

- .NET 9 SDK
- Basic C# knowledge
- Understanding of MsClaw minds (see [msclaw-walkthrough.md](./msclaw-walkthrough.md))
- A MsClaw mind directory to test with

## Part 1: Understanding the Extension Architecture

### The Extension Lifecycle

Every extension goes through a predictable lifecycle:

1. **Load** — MsClaw discovers the extension via plugin.json
2. **Register** — The extension's `Register()` method is called. The extension tells MsClaw what it provides (tools, hooks, commands)
3. **Start** — The extension's `StartAsync()` method is called. It can initialize resources
4. **Hook Events** — Extensions listen for MsClaw lifecycle events and react
5. **Handle Commands** — Extensions process slash commands from users
6. **Stop** — The extension's `StopAsync()` method is called during shutdown
7. **Dispose** — The extension cleans up async resources

### Extension Tiers

Extensions fall into two categories:

- **Core** — Built into MsClaw, always loaded. Useful for essential system functionality.
- **External** — Loaded from the mind directory. Can be reloaded without restarting the app. This is what most developers will create.

### The IMsClawPluginApi Contract

When your extension calls `Register(IMsClawPluginApi api)`, the `api` parameter is your gateway to MsClaw. It provides these capabilities:

```csharp
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
```

- **RegisterTool** — Add an AI function the agent can call
- **RegisterHook** — Listen for MsClaw events (session:create, message:received, etc.)
- **RegisterCommand** — Handle user slash commands (/memory, /reset, etc.)
- **RegisterService** — Register a background service (e.g., a file watcher, polling agent)
- **RegisterHttpRoute** — Add custom HTTP endpoints

## Part 2: Creating Your First Extension

### Step 1: Create a .NET Class Library

```bash
dotnet new classlib -n MyExtension
cd MyExtension
```

### Step 2: Add MsClaw NuGet References

You need to reference MsClaw.Core and Microsoft.Extensions.AI:

```bash
dotnet add package Microsoft.Extensions.AI
# And reference MsClaw locally (or via NuGet if published)
```

### Step 3: Implement ExtensionBase

Create a file `MyExtension.cs`:

```csharp
using MsClaw.Core;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MyExtension;

public class MyExtensionImpl : ExtensionBase
{
    private readonly ILogger<MyExtensionImpl> _logger;
    
    public MyExtensionImpl(ILogger<MyExtensionImpl> logger)
    {
        _logger = logger;
    }
    
    public override void Register(IMsClawPluginApi api)
    {
        _logger.LogInformation("Registering MyExtension v1.0");
        
        // Register a hook to listen for messages
        api.RegisterHook(ExtensionEvents.MessageReceived, OnMessageReceived);
        
        // Register a command
        api.RegisterCommand("hello", OnHelloCommand);
    }
    
    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MyExtension started");
        await Task.CompletedTask;
    }
    
    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MyExtension stopped");
        await Task.CompletedTask;
    }
    
    private async Task OnMessageReceived(ExtensionHookContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Message received: {Message}", context.Message);
        await Task.CompletedTask;
    }
    
    private async Task<string> OnHelloCommand(ExtensionCommandContext context, CancellationToken cancellationToken)
    {
        var name = context.Arguments?.Trim() ?? "World";
        return await Task.FromResult($"Hello, {name}!");
    }
}
```

### Step 4: Create a plugin.json Manifest

In your extension assembly output directory, create `plugin.json`:

```json
{
  "id": "my-extension",
  "name": "My Extension",
  "version": "1.0.0",
  "entryAssembly": "MyExtension.dll",
  "entryType": "MyExtension.MyExtensionImpl",
  "dependencies": [],
  "config": null
}
```

The manifest tells MsClaw:
- **id** — Unique identifier for your extension
- **name** — Display name
- **version** — Semantic version
- **entryAssembly** — The DLL containing your extension class
- **entryType** — Fully qualified name of your ExtensionBase subclass
- **dependencies** — Array of extensions this one depends on (optional)
- **config** — JSON configuration passed to your extension (optional)

### Step 5: Register the Extension in Your Mind

Place your compiled extension in your mind directory:

```
my-mind/
├── SOUL.md
├── .working-memory/
├── .extensions/
│   └── my-extension/
│       ├── MyExtension.dll
│       ├── MyExtension.pdb
│       └── plugin.json
└── .github/
    └── agents/
```

When you start MsClaw with this mind, it will:
1. Discover `plugin.json` files in `.extensions/*/`
2. Load each extension's assembly
3. Call `Register()` on your extension
4. Call `StartAsync()` after all extensions load
5. Fire the 'extension:loaded' and 'agent:bootstrap' events

## Part 3: Registering Tools

Tools are AI functions that the Copilot agent can call. They're defined using `Microsoft.Extensions.AI.AIFunction`.

### Example: Weather Tool

```csharp
using Microsoft.Extensions.AI;

private void RegisterWeatherTool(IMsClawPluginApi api)
{
    var weatherTool = new AIFunction(
        "get_weather",
        "Get the current weather for a location",
        invoke: async (args, cancellationToken) =>
        {
            var location = args["location"]?.ToString() ?? "Unknown";
            // Call a weather API here
            var weather = $"Weather in {location}: Sunny, 72°F";
            return weather;
        }
    );
    
    // Add a string parameter
    weatherTool.Parameters.Add(
        "location",
        new AIFunctionParameterMetadata
        {
            Name = "location",
            Description = "City name or coordinates",
            IsRequired = true,
            ParameterType = typeof(string)
        }
    );
    
    api.RegisterTool(weatherTool);
}
```

Then call this from your `Register()` method:

```csharp
public override void Register(IMsClawPluginApi api)
{
    RegisterWeatherTool(api);
}
```

### Parameters

Tools can have multiple parameters. Each parameter is defined with:
- **Name** — The parameter name
- **Description** — What it does
- **IsRequired** — Whether it's mandatory
- **ParameterType** — The .NET type (string, int, bool, etc.)

The agent will call your tool with the user's intent, and you receive the arguments as a dictionary.

## Part 4: Handling Commands

Commands are slash-prefixed inputs from users. Examples: `/memory`, `/reset`, `/status`.

### Example: Memory Command

```csharp
public override void Register(IMsClawPluginApi api)
{
    api.RegisterCommand("memory", OnMemoryCommand);
}

private async Task<string> OnMemoryCommand(ExtensionCommandContext context, CancellationToken cancellationToken)
{
    // context.RawInput = "/memory search foo"
    // context.Command = "memory"
    // context.Arguments = "search foo"
    // context.SessionId = session ID (if available)
    
    var parts = context.Arguments?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
    
    if (parts.Length == 0)
    {
        return "Usage: /memory <search|update|clear>";
    }
    
    var subcommand = parts[0];
    return subcommand switch
    {
        "search" => SearchMemory(string.Join(" ", parts.Skip(1))),
        "update" => UpdateMemory(context.SessionId, string.Join(" ", parts.Skip(1))),
        "clear" => ClearMemory(context.SessionId),
        _ => $"Unknown subcommand: {subcommand}"
    };
}

private string SearchMemory(string query)
{
    // Your logic here
    return $"Found memories matching '{query}'";
}

private string UpdateMemory(string? sessionId, string content)
{
    // Your logic here
    return "Memory updated";
}

private string ClearMemory(string? sessionId)
{
    // Your logic here
    return "Memory cleared";
}
```

The command result is returned as a string and sent back to the user.

## Part 5: Registering Hooks

Hooks let you react to MsClaw lifecycle events. Available events:

```csharp
public static class ExtensionEvents
{
    public const string SessionCreate = "session:create";    // New session created
    public const string SessionResume = "session:resume";    // Session resumed
    public const string SessionEnd = "session:end";          // Session ended
    public const string MessageReceived = "message:received"; // User sent message
    public const string MessageSent = "message:sent";        // Agent responded
    public const string AgentBootstrap = "agent:bootstrap";  // Agent initializing
    public const string ExtensionLoaded = "extension:loaded"; // Extensions ready
}
```

### Example: Logging Extension

```csharp
public override void Register(IMsClawPluginApi api)
{
    api.RegisterHook(ExtensionEvents.MessageReceived, LogMessageReceived);
    api.RegisterHook(ExtensionEvents.MessageSent, LogMessageSent);
    api.RegisterHook(ExtensionEvents.SessionCreate, LogSessionCreate);
}

private async Task LogMessageReceived(ExtensionHookContext context, CancellationToken cancellationToken)
{
    _logger.LogInformation(
        "Session {SessionId} received message: {Message}",
        context.SessionId,
        context.Message
    );
    await Task.CompletedTask;
}

private async Task LogMessageSent(ExtensionHookContext context, CancellationToken cancellationToken)
{
    _logger.LogInformation(
        "Session {SessionId} sent response: {Response}",
        context.SessionId,
        context.Response
    );
    await Task.CompletedTask;
}

private async Task LogSessionCreate(ExtensionHookContext context, CancellationToken cancellationToken)
{
    _logger.LogInformation("New session created: {SessionId}", context.SessionId);
    await Task.CompletedTask;
}
```

### Hook Safety

If your hook handler throws an exception:
1. The exception is caught and logged
2. Other extensions' hooks for the same event continue
3. The application stays running

This is **fail-safe by design**. A buggy extension won't crash MsClaw.

## Part 6: Registering Services

Background services run alongside your extension. Examples: polling, file watching, background cleanup.

```csharp
using Microsoft.Extensions.Hosting;

public class MyBackgroundService : BackgroundService
{
    private readonly ILogger<MyBackgroundService> _logger;
    
    public MyBackgroundService(ILogger<MyBackgroundService> logger)
    {
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Background work running");
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}
```

Then register it:

```csharp
public override void Register(IMsClawPluginApi api)
{
    api.RegisterService(new MyBackgroundService(_logger));
}
```

## Part 7: Registering HTTP Routes

Add custom HTTP endpoints to MsClaw:

```csharp
public override void Register(IMsClawPluginApi api)
{
    api.RegisterHttpRoute(builder =>
    {
        builder.MapGet("/extension/status", () =>
        {
            return Results.Ok(new { status = "ok", message = "My extension is healthy" });
        });
        
        builder.MapPost("/extension/action", async (HttpContext context) =>
        {
            var body = await context.Request.ReadAsStringAsync();
            return Results.Ok(new { result = "Action performed", input = body });
        });
    });
}
```

Test your endpoint:

```bash
curl http://localhost:5050/extension/status
# {"status":"ok","message":"My extension is healthy"}
```

### Important: HTTP Routes Are Only Available After Startup

**⚠️ ASP.NET Core Limitation:** HTTP routes registered during `Register()` are added to MsClaw's route table at application startup. However, **routes do NOT re-register after extension reload**.

This means:
- ✅ HTTP routes work fine if your extension is part of the initial startup
- ✅ Tools, hooks, and commands reload correctly when extensions are reloaded
- ❌ If you reload external extensions (via `/reload` command or `ReloadExternalAsync`), any new HTTP routes won't be available

**Workaround:** Use commands and tools instead of HTTP routes for functionality needed after reload. Commands and tools are fully reload-compatible.

**Example:** Instead of an HTTP endpoint, use a command:

```csharp
// Don't do this (won't work after reload):
api.RegisterHttpRoute(builder =>
{
    builder.MapGet("/extension/data", () => Results.Ok(GetData()));
});

// Do this instead (works after reload):
api.RegisterCommand("data", async (context, ct) =>
{
    return GetData();
});
```

Then access it via:
```bash
curl -X POST http://localhost:5050/command \
  -H "Content-Type: application/json" \
  -d '{"message": "/data", "sessionId": "session-123"}'
```

## Part 8: Extension Configuration

Extensions can accept JSON configuration via `plugin.json`:

```json
{
  "id": "my-extension",
  "name": "My Extension",
  "version": "1.0.0",
  "entryAssembly": "MyExtension.dll",
  "entryType": "MyExtension.MyExtensionImpl",
  "config": {
    "logLevel": "Information",
    "maxRetries": 3,
    "apiKey": "your-key-here"
  }
}
```

Access the config in your extension:

```csharp
public override void Register(IMsClawPluginApi api)
{
    if (api.Config.HasValue)
    {
        var config = api.Config.Value;
        var logLevel = config.GetProperty("logLevel").GetString();
        var maxRetries = config.GetProperty("maxRetries").GetInt32();
        _logger.LogInformation("Config: logLevel={LogLevel}, maxRetries={MaxRetries}", logLevel, maxRetries);
    }
}
```

## Part 9: Extension Dependencies

If your extension depends on another, declare the dependency in `plugin.json`:

```json
{
  "id": "my-extension",
  "dependencies": [
    {
      "id": "other-extension",
      "versionRange": "^1.0.0"
    }
  ]
}
```

MsClaw ensures dependencies are loaded before your extension.

## Part 10: Testing Your Extension

### Setup a Test Mind

```bash
# Create a test mind (or use an existing one)
mkdir -p ~/test-mind/.extensions/my-extension
cp bin/Release/net9.0/MyExtension.dll ~/test-mind/.extensions/my-extension/
cp plugin.json ~/test-mind/.extensions/my-extension/
```

### Start MsClaw with Your Mind

```bash
# From the MsClaw source directory
dotnet run -- --mind ~/test-mind
```

You should see:

```
Registering MyExtension v1.0
MyExtension started
```

### Test a Command

```bash
curl -X POST http://localhost:5050/command \
  -H "Content-Type: application/json" \
  -d '{"message": "/hello Alice", "sessionId": "test-session"}'

# Response: "Hello, Alice!"
```

### Test a Tool

The tool is automatically registered with the Copilot agent. Send a message through the IDE or CLI, and the agent can invoke your tool.

### Test Hooks

Use logging to verify hooks are firing:

```bash
# Start MsClaw and watch the console
# Send messages through the IDE
# You should see logs like: "Message received: ..."
```

## Part 11: Advanced Topics

### Accessing Extension Configuration

Extensions can be configured at runtime. Use dependency injection to provide configuration:

```csharp
public class MyExtensionImpl : ExtensionBase
{
    private readonly IConfiguration _configuration;
    
    public MyExtensionImpl(IConfiguration configuration, ILogger<MyExtensionImpl> logger)
    {
        _configuration = configuration;
    }
}
```

Then in your mind's `.extensions/my-extension/` directory, add an `appsettings.json`:

```json
{
  "MyExtension": {
    "ApiUrl": "https://api.example.com",
    "Timeout": 30
  }
}
```

MsClaw loads this configuration automatically.

### Async Initialization

If your extension needs to fetch data or initialize resources asynchronously:

```csharp
public override async Task StartAsync(CancellationToken cancellationToken = default)
{
    _logger.LogInformation("Initializing resources...");
    
    // Fetch from an API
    using var client = new HttpClient();
    var response = await client.GetAsync("https://api.example.com/init", cancellationToken);
    response.EnsureSuccessStatusCode();
    
    _logger.LogInformation("Initialization complete");
}
```

### Extension State Management

Keep state in instance variables (thread-safe):

```csharp
public class MyExtensionImpl : ExtensionBase
{
    private readonly Dictionary<string, object> _cache = [];
    private readonly object _cacheLock = new();
    
    private void SetCache(string key, object value)
    {
        lock (_cacheLock)
        {
            _cache[key] = value;
        }
    }
    
    private object? GetCache(string key)
    {
        lock (_cacheLock)
        {
            return _cache.TryGetValue(key, out var value) ? value : null;
        }
    }
}
```

## Debugging Tips

### Enable Logging

In your mind's `.github/` directory, create an `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "MyExtension": "Debug"
    }
  }
}
```

### Check the Extension Manager Status

Create a debug command:

```csharp
api.RegisterCommand("extensions", OnExtensionsCommand);

private async Task<string> OnExtensionsCommand(ExtensionCommandContext context, CancellationToken cancellationToken)
{
    // Return a list of loaded extensions (you'd need the IExtensionManager injected)
    return "Extensions loaded";
}
```

### Unhandled Exception Handling

Always wrap async operations:

```csharp
private async Task OnMessageReceived(ExtensionHookContext context, CancellationToken cancellationToken)
{
    try
    {
        // Your code
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Hook failed");
        // Don't re-throw; let MsClaw handle gracefully
    }
}
```

## Common Patterns

### Memory Extension Pattern

```csharp
public override void Register(IMsClawPluginApi api)
{
    api.RegisterHook(ExtensionEvents.MessageSent, UpdateMemory);
    api.RegisterCommand("memory", OnMemoryCommand);
    
    var memoryTool = new AIFunction(
        "recall_memory",
        "Recall information from your working memory",
        invoke: RecallMemory
    );
    api.RegisterTool(memoryTool);
}

private async Task UpdateMemory(ExtensionHookContext context, CancellationToken cancellationToken)
{
    // Save the exchange to working memory
}

private async Task<string> RecallMemory(Dictionary<string, object?> args, CancellationToken cancellationToken)
{
    var query = args["query"]?.ToString() ?? "";
    // Retrieve from memory
    return $"Memory for '{query}': ...";
}

private async Task<string> OnMemoryCommand(ExtensionCommandContext context, CancellationToken cancellationToken)
{
    // Handle /memory commands
    return "Memory status: ...";
}
```

### Tool Wrapper Pattern

```csharp
public override void Register(IMsClawPluginApi api)
{
    var externalApiTool = new AIFunction(
        "call_external_api",
        "Call an external API",
        invoke: async (args, ct) =>
        {
            try
            {
                var endpoint = args["endpoint"]?.ToString() ?? throw new ArgumentException("endpoint required");
                using var client = new HttpClient();
                var response = await client.GetStringAsync(endpoint, ct);
                return response;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
    );
    api.RegisterTool(externalApiTool);
}
```

## Summary

You now have everything you need to build MsClaw extensions:

1. **Implement ExtensionBase** with Register(), StartAsync(), StopAsync()
2. **Register tools** for the agent to call via AIFunction
3. **Register commands** for user slash-command interaction
4. **Register hooks** to react to MsClaw lifecycle events
5. **Create plugin.json** to describe your extension
6. **Place in .extensions/** for MsClaw to discover
7. **Test with a mind** by starting MsClaw and interacting

Extensions are designed to be **loosely coupled**, **fail-safe**, and **runtime-reloadable**. Build extensions that enhance minds without modifying core MsClaw code.

Happy extending!
