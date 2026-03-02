# MsClaw — Code Walkthrough

*2026-03-01T23:47:15Z by Showboat 0.6.1*
<!-- showboat-id: 58fe5f57-308e-4f16-b71f-636279914a04 -->

# What is MsClaw?

MsClaw is a GitHub Copilot Extension that gives AI agents a **persistent identity** — a "mind". Instead of starting fresh with each conversation, MsClaw loads a mind directory containing an identity file (SOUL.md), agent definitions, and working memory, then serves it as a Copilot agent through the GitHub Copilot Runtime API.

Think of it like this:
- **Without MsClaw**: Every conversation with Copilot is stateless. The agent has no memory of previous interactions, no persistent identity, no ongoing context.
- **With MsClaw**: An agent has a mind with a defined SOUL, can load its own agents, maintains working memory, and persists across sessions.

The core responsibility is simple: **load a mind directory, validate it, and expose it through the Copilot Runtime API on localhost:5050**.

## Architecture Overview

MsClaw is a .NET 9 C# application with these main pieces:

1. **Program.cs** — CLI entry point, argument parsing, service registration
2. **Core/** — Business logic: mind discovery, mind validation, mind reading, orchestration
3. **Models/** — Data structures for configuration and chat request/response payloads
4. **Templates/** — Embedded templates for scaffolding new minds

The app boots in this sequence:
1. Parse CLI arguments (--mind, --new-mind, or auto-discover)
2. Scaffold a mind if --new-mind was specified
3. Validate the mind directory
4. Load the mind's identity, agents, and working memory
5. Register a Copilot agent with the Runtime
6. Start an HTTP server listening on :5050
7. Handle incoming chat requests in the Copilot Runtime protocol

Let's walk through the code to understand each piece.

## Entry Point: Program.cs

Program.cs is the first code that runs. It parses CLI arguments, sets up dependency injection, and starts the HTTP server.

```bash
sed -n '1,40p' src/MsClaw/Program.cs
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
```

The bootstrap phase happens **before** the HTTP server starts. MsClaw uses a BootstrapOrchestrator to:
1. Validate the mind directory
2. Auto-discover or scaffold it based on CLI arguments
3. Persist the configuration for next runs

This is important because all downstream services (IMindReader, IMindValidator, etc.) depend on knowing the mind root path upfront. If the mind is invalid, the app exits with an error message before the server even starts.

```bash
sed -n '40,96p' src/MsClaw/Program.cs
```

```output
builder.Services.AddSingleton<IConfigPersistence>(configPersistence);
builder.Services.AddSingleton<IMindDiscovery>(discovery);
builder.Services.AddSingleton<IMindScaffold>(scaffold);
builder.Services.AddSingleton<IIdentityLoader, IdentityLoader>();

// Register CopilotClient as singleton
builder.Services.AddSingleton<CopilotClient>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MsClawOptions>>().Value;
    return new CopilotClient(new CopilotClientOptions
    {
        Cwd = Path.GetFullPath(options.MindRoot),
        AutoStart = true,
        UseStdio = true
    });
});

builder.Services.AddSingleton<IMindReader, MindReader>();
builder.Services.AddSingleton<ICopilotRuntimeClient, CopilotRuntimeClient>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/session/new", async (ICopilotRuntimeClient copilotClient, CancellationToken cancellationToken) =>
{
    var sessionId = await copilotClient.CreateSessionAsync(cancellationToken);
    return Results.Ok(new { sessionId });
});

app.MapPost("/chat", async (
    ChatRequest request,
    ICopilotRuntimeClient copilotClient,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "message is required" });
    }

    var sessionId = request.SessionId;

    if (string.IsNullOrWhiteSpace(sessionId))
    {
        sessionId = await copilotClient.CreateSessionAsync(cancellationToken);
    }

    var response = await copilotClient.SendMessageAsync(sessionId, request.Message, cancellationToken);

    return Results.Ok(new ChatResponse
    {
        Response = response,
        SessionId = sessionId
    });
});

app.Run();
```

After bootstrap, the app registers all dependencies as singletons, including a singleton `CopilotClient`, and exposes three HTTP endpoints:

- **GET /health** — Simple liveness probe.
- **POST /session/new** — Calls `ICopilotRuntimeClient.CreateSessionAsync` and returns a new session ID from the SDK.
- **POST /chat** — Accepts `message` plus optional `sessionId`; if no session ID is provided, it creates one, then sends the message with `SendMessageAsync`.

This shifts session state ownership to the SDK runtime instead of custom app-level persistence, while keeping the HTTP surface small and explicit.

## Mind Discovery and Validation

Before we can serve a mind, we need to find it and verify it's valid. This is where MindDiscovery and MindValidator come in.

MindDiscovery has three strategies:
1. **Explicit path via --mind** — User specifies the mind directory directly.
2. **Scaffolded path via --new-mind** — Create a new mind from embedded templates.
3. **Auto-discovery** — Check convention paths or a saved config from a previous run.

Let's look at the discovery logic:

```bash
sed -n '1,35p' src/MsClaw/Core/MindDiscovery.cs
```

```output
namespace MsClaw.Core;

public sealed class MindDiscovery : IMindDiscovery
{
    private readonly IConfigPersistence _configPersistence;
    private readonly IMindValidator _validator;

    public MindDiscovery(IConfigPersistence configPersistence, IMindValidator validator)
    {
        _configPersistence = configPersistence;
        _validator = validator;
    }

    public string? Discover()
    {
        var cachedMindRoot = _configPersistence.Load()?.MindRoot;
        if (IsValidCandidate(cachedMindRoot))
        {
            return cachedMindRoot;
        }

        var currentDirectory = Directory.GetCurrentDirectory();
        if (IsValidCandidate(currentDirectory))
        {
            return currentDirectory;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var msclawMind = Path.Combine(home, ".msclaw", "mind");
        if (IsValidCandidate(msclawMind))
        {
            return msclawMind;
        }

        return null;
```

Discovery follows a priority order:
1. **Cached config** — Check if a previous run saved a mind path (in ConfigPersistence).
2. **Current directory** — Check if the current working directory is a valid mind.
3. **~/.msclaw/mind** — Convention path for the default mind.

Each candidate is validated by calling `IsValidCandidate()`, which uses the MindValidator to check if the directory structure is correct.

Discovery follows a priority order: (1) Cached config from previous run, (2) Current directory, (3) Convention path ~/.msclaw/mind. Each candidate is validated by checking if the directory structure is correct.

## Mind Validation

Once a mind is discovered (or explicitly provided via --mind), it must be validated. MindValidator checks that the mind directory has the required structure.

```bash
sed -n '1,50p' src/MsClaw/Core/MindValidator.cs
```

```output
using MsClaw.Models;

namespace MsClaw.Core;

public sealed class MindValidator : IMindValidator
{
    public MindValidationResult Validate(string mindRoot)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var found = new List<string>();

        if (!Directory.Exists(mindRoot))
        {
            errors.Add($"Mind root directory does not exist: {mindRoot}");
            return new MindValidationResult
            {
                Errors = errors,
                Warnings = warnings,
                Found = found
            };
        }

        found.Add(mindRoot);

        var soulPath = Path.Combine(mindRoot, "SOUL.md");
        if (File.Exists(soulPath))
        {
            found.Add(soulPath);
        }
        else
        {
            errors.Add($"Required file missing: {soulPath}");
        }

        var workingMemoryPath = Path.Combine(mindRoot, ".working-memory");
        if (Directory.Exists(workingMemoryPath))
        {
            found.Add(workingMemoryPath);

            CheckOptionalFile(workingMemoryPath, "memory.md", warnings, found);
            CheckOptionalFile(workingMemoryPath, "rules.md", warnings, found);
            CheckOptionalFile(workingMemoryPath, "log.md", warnings, found);
        }
        else
        {
            errors.Add($"Required directory missing: {workingMemoryPath}");
        }

        CheckOptionalDirectory(mindRoot, ".github", "agents", warnings, found);
```

MindValidator is strict about **required** files and directories:
- **SOUL.md** — The identity file (must exist)
- **.working-memory/** — The memory directory (must exist)

It also checks for optional directories like .github/agents/, domains/, initiatives/, and expertise/. If optional files are missing, it logs warnings instead of errors. The validator tracks three lists: errors (which fail validation), warnings (optional missing parts), and found (what exists).

## Loading the Mind: IdentityLoader and MindReader

Once the mind is validated, MsClaw loads its contents. There are two key components: IdentityLoader (reads SOUL.md) and MindReader (reads agents and memory).

```bash
sed -n '1,35p' src/MsClaw/Core/IdentityLoader.cs
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
```

IdentityLoader is responsible for composing the system message that will be sent to the Copilot Runtime API. It:
1. Reads SOUL.md (the core identity)
2. Discovers all *.agent.md files in .github/agents/ (sorted by name for deterministic ordering)
3. Strips YAML frontmatter from agent files (if present)
4. Joins them all together with separators, creating a single system prompt

The result is a cohesive system message that includes the mind's identity and all its agents. This is crucial: the system message is what tells the Copilot Runtime who this agent is and how it should behave.

```bash
sed -n '1,40p' src/MsClaw/Core/MindReader.cs
```

```output
using System.Diagnostics;
using MsClaw.Models;
using Microsoft.Extensions.Options;

namespace MsClaw.Core;

public sealed class MindReader : IMindReader
{
    private readonly string _mindRoot;
    private readonly bool _autoGitPull;

    public MindReader(IOptions<MsClawOptions> options)
    {
        _mindRoot = Path.GetFullPath(options.Value.MindRoot);
        _autoGitPull = options.Value.AutoGitPull;
    }

    public async Task EnsureSyncedAsync(CancellationToken cancellationToken = default)
    {
        if (!_autoGitPull)
        {
            return;
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"-C \"{_mindRoot}\" pull --ff-only --quiet",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };

        process.Start();
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
```

MindReader has two responsibilities: (1) Keep the mind synced by pulling the latest from git (if AutoGitPull is enabled), and (2) Provide access to the mind's working memory. The git pull is a convenience feature — if the mind directory is a git repo, MsClaw can auto-update it before each chat. This is useful when the mind is stored in GitHub and you want the latest version automatically.

## Session Management — SDK-Native

Session tracking is now owned by the GitHub Copilot SDK, not by a custom `SessionManager`. MsClaw creates or resumes SDK sessions by session ID and forwards one message at a time; the SDK stores and compacts conversation history internally when `InfiniteSessions` is enabled.

```bash
cat src/MsClaw/Core/ICopilotRuntimeClient.cs
```

```output
namespace MsClaw.Core;

public interface ICopilotRuntimeClient
{
    /// <summary>
    /// Creates a new SDK session. Returns the session ID.
    /// </summary>
    Task<string> CreateSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a single message to an existing session. Returns the assistant's response.
    /// The SDK maintains conversation history internally.
    /// </summary>
    Task<string> SendMessageAsync(
        string sessionId,
        string message,
        CancellationToken cancellationToken = default);
}
```

At runtime, `CopilotRuntimeClient` keeps an in-memory `ConcurrentDictionary<string, CopilotSession>` to reuse active sessions efficiently. If a session isn't cached, it calls `ResumeSessionAsync`, caches the result, and continues the conversation without reconstructing history in app models.

## The Copilot Runtime Client

This is where the runtime bridge lives. `CopilotRuntimeClient` creates SDK sessions with the mind identity as system message, then sends one prompt per request against a session ID.

```bash
cat src/MsClaw/Core/CopilotRuntimeClient.cs
```

```output
using GitHub.Copilot.SDK;
using MsClaw.Models;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace MsClaw.Core;

public sealed class CopilotRuntimeClient : ICopilotRuntimeClient
{
    private readonly CopilotClient _client;
    private readonly MsClawOptions _options;
    private readonly IIdentityLoader _identityLoader;
    private readonly ConcurrentDictionary<string, CopilotSession> _sessions = new();

    public CopilotRuntimeClient(
        CopilotClient client,
        IOptions<MsClawOptions> options,
        IIdentityLoader identityLoader)
    {
        _client = client;
        _options = options.Value;
        _identityLoader = identityLoader;
    }

    public async Task<string> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        var mindRoot = Path.GetFullPath(_options.MindRoot);
        var systemMessage = await _identityLoader.LoadSystemMessageAsync(mindRoot, cancellationToken);

        var bootstrapPath = Path.Combine(mindRoot, "bootstrap.md");
        if (File.Exists(bootstrapPath))
        {
            var bootstrapInstructions = await File.ReadAllTextAsync(bootstrapPath, cancellationToken);
            systemMessage = bootstrapInstructions + "\n\n---\n\n" + systemMessage;
        }

        var session = await _client.CreateSessionAsync(new SessionConfig
        {
            Model = _options.Model,
            InfiniteSessions = new InfiniteSessionConfig { Enabled = true },
            OnPermissionRequest = PermissionHandler.ApproveAll,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Replace,
                Content = systemMessage
            }
        }, cancellationToken);

        _sessions[session.SessionId] = session;
        return session.SessionId;
    }

    public async Task<string> SendMessageAsync(
        string sessionId,
        string message,
        CancellationToken cancellationToken = default)
    {
        var session = await GetOrResumeSessionAsync(sessionId, cancellationToken);

        var response = await session.SendAndWaitAsync(
            new MessageOptions { Prompt = message },
            timeout: TimeSpan.FromSeconds(120),
            cancellationToken: cancellationToken);

        return response?.Data?.Content
            ?? throw new InvalidOperationException("No assistant response received from Copilot session.");
    }

    private async Task<CopilotSession> GetOrResumeSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            return session;
        }

        var resumedSession = await _client.ResumeSessionAsync(sessionId, new ResumeSessionConfig
        {
            OnPermissionRequest = PermissionHandler.ApproveAll
        }, cancellationToken);

        return _sessions.GetOrAdd(sessionId, resumedSession);
    }
}
```

Key behavior in the new implementation:
1. **Singleton client lifecycle** — `CopilotClient` is injected once and reused across requests.
2. **Explicit session lifecycle** — `CreateSessionAsync` sets model/system message and enables `InfiniteSessions`.
3. **Message routing by session ID** — `SendMessageAsync` sends a single prompt to a resumed or cached SDK session.
4. **In-memory session cache** — `_sessions` avoids repeated resume calls for active sessions.

The identity injection point is unchanged (SOUL + agent files, optionally prefixed by bootstrap.md), but session ownership moved fully into the SDK.

## Mind Scaffolding

If the user runs with --new-mind, MsClaw needs to create a mind from templates. MindScaffold handles this. It extracts embedded template files and writes them to the new mind directory.

```bash
sed -n '1,40p' src/MsClaw/Core/MindScaffold.cs
```

```output
namespace MsClaw.Core;

public sealed class MindScaffold : IMindScaffold
{
    public void Scaffold(string mindRoot)
    {
        if (Directory.Exists(mindRoot) && Directory.EnumerateFileSystemEntries(mindRoot).Any())
        {
            throw new InvalidOperationException($"Cannot scaffold into non-empty directory: {mindRoot}");
        }

        Directory.CreateDirectory(mindRoot);

        File.WriteAllText(Path.Combine(mindRoot, "SOUL.md"), EmbeddedResources.ReadTemplate("SOUL.md"));
        File.WriteAllText(Path.Combine(mindRoot, "bootstrap.md"), EmbeddedResources.ReadTemplate("bootstrap.md"));

        var workingMemoryPath = Path.Combine(mindRoot, ".working-memory");
        Directory.CreateDirectory(workingMemoryPath);
        File.WriteAllText(Path.Combine(workingMemoryPath, "memory.md"), "# AI Notes — Memory\n");
        File.WriteAllText(Path.Combine(workingMemoryPath, "rules.md"), "# AI Notes — Rules\n");
        File.WriteAllText(Path.Combine(workingMemoryPath, "log.md"), "# AI Notes — Log\n");

        Directory.CreateDirectory(Path.Combine(mindRoot, ".github", "agents"));
        Directory.CreateDirectory(Path.Combine(mindRoot, ".github", "skills"));
        Directory.CreateDirectory(Path.Combine(mindRoot, "domains"));
        Directory.CreateDirectory(Path.Combine(mindRoot, "initiatives"));
        Directory.CreateDirectory(Path.Combine(mindRoot, "expertise"));
        Directory.CreateDirectory(Path.Combine(mindRoot, "inbox"));
        Directory.CreateDirectory(Path.Combine(mindRoot, "Archive"));
    }
}
```

MindScaffold creates a complete mind directory structure. It:
1. Checks that the target directory is empty (or doesn't exist yet)
2. Creates the directory
3. Writes SOUL.md and bootstrap.md from embedded templates
4. Creates .working-memory/ with memory.md, rules.md, and log.md
5. Creates all the organizational directories: .github/agents, .github/skills, domains, initiatives, expertise, inbox, Archive

The embedded templates come from EmbeddedResources, which reads files compiled into the binary. This means users can run --new-mind without needing a separate template directory — everything is self-contained.

## Configuration Persistence

MsClaw persists its configuration between runs so users don't have to specify --mind every time. ConfigPersistence handles this.

```bash
cat src/MsClaw/Core/ConfigPersistence.cs
```

```output
using System.Text.Json;
using MsClaw.Models;

namespace MsClaw.Core;

public sealed class ConfigPersistence : IConfigPersistence
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _configPath;

    public ConfigPersistence()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".msclaw",
            "config.json"))
    {
    }

    public ConfigPersistence(string configPath)
    {
        _configPath = configPath;
    }

    public MsClawConfig? Load()
    {
        if (!File.Exists(_configPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            return JsonSerializer.Deserialize<MsClawConfig>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(MsClawConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var directoryPath = Path.GetDirectoryName(_configPath)
            ?? throw new InvalidOperationException($"Could not resolve config directory for path: {_configPath}");

        Directory.CreateDirectory(directoryPath);

        config.LastUsed = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(_configPath, json);
    }

    public void Clear()
    {
        if (File.Exists(_configPath))
        {
            File.Delete(_configPath);
        }
    }
}
```

ConfigPersistence stores `MsClawConfig` at `~/.msclaw/config.json` with `MindRoot` and `LastUsed`. `Load()` now fails open for corrupted JSON by catching `JsonException` and returning `null`, so startup can continue with discovery instead of crashing. `--reset-config` still clears the file and exits.

## Data Models

Let's look at the key data structures that flow through the system.

```bash
cat src/MsClaw/Models/ChatRequest.cs && echo && cat src/MsClaw/Models/ChatResponse.cs
```

```output
namespace MsClaw.Models;

public sealed class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public string? SessionId { get; set; }
}

namespace MsClaw.Models;

public sealed class ChatResponse
{
    public required string Response { get; init; }
    public required string SessionId { get; init; }
}
```

The chat payload models are minimal:
- **ChatRequest** — Incoming HTTP request with required `Message` and optional `SessionId` (for continuing a prior SDK session).
- **ChatResponse** — HTTP response containing `Response` and `SessionId`.

MsClaw stays a thin HTTP adapter: clients provide a prompt (and optionally a session ID), and the SDK handles the conversation state.

## Bootstrap Orchestration

When MsClaw starts, the BootstrapOrchestrator coordinates all the setup steps. Let's see how it orchestrates the boot sequence:

```bash
sed -n '1,80p' src/MsClaw/Core/BootstrapOrchestrator.cs
```

```output
using MsClaw.Models;

namespace MsClaw.Core;

public sealed class BootstrapOrchestrator : IBootstrapOrchestrator
{
    private const string Usage = "Usage: msclaw [--reset-config] [--mind <path> | --new-mind <path>]";

    private readonly IMindValidator _mindValidator;
    private readonly IMindDiscovery _mindDiscovery;
    private readonly IMindScaffold _mindScaffold;
    private readonly IConfigPersistence _configPersistence;

    public BootstrapOrchestrator(
        IMindValidator mindValidator,
        IMindDiscovery mindDiscovery,
        IMindScaffold mindScaffold,
        IConfigPersistence configPersistence)
    {
        _mindValidator = mindValidator;
        _mindDiscovery = mindDiscovery;
        _mindScaffold = mindScaffold;
        _configPersistence = configPersistence;
    }

    public BootstrapResult? Run(string[] args)
    {
        if (args.Contains("--reset-config", StringComparer.Ordinal))
        {
            _configPersistence.Clear();
            Console.Out.WriteLine("Config cleared.");
            return null;
        }

        string? explicitMindPath = null;
        string? newMindPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mind":
                    explicitMindPath = ReadPathValue(args, ref i, "--mind", explicitMindPath);
                    break;
                case "--new-mind":
                    newMindPath = ReadPathValue(args, ref i, "--new-mind", newMindPath);
                    break;
                default:
                    // Skip unknown args — they may be ASP.NET Core host flags (--urls, --environment, etc.)
                    break;
            }
        }

        if (explicitMindPath is not null && newMindPath is not null)
        {
            ThrowUsage("--mind and --new-mind cannot be used together.");
        }

        if (newMindPath is not null)
        {
            var resolvedPath = Path.GetFullPath(newMindPath);
            _mindScaffold.Scaffold(resolvedPath);
            ValidateOrThrow(resolvedPath);
            Persist(resolvedPath);

            return new BootstrapResult
            {
                MindRoot = resolvedPath,
                IsNewMind = true,
                HasBootstrapMarker = true
            };
        }

        if (explicitMindPath is not null)
        {
            var resolvedPath = Path.GetFullPath(explicitMindPath);
            ValidateOrThrow(resolvedPath);
            Persist(resolvedPath);

            return new BootstrapResult
```

BootstrapOrchestrator is the command-line entry point coordinator. It:
1. **Parses arguments** — Recognizes --reset-config, --mind, and --new-mind
2. **Enforces constraints** — Ensures --mind and --new-mind are never used together
3. **Handles --reset-config** — Clears the saved config and exits
4. **Scaffolds if needed** — If --new-mind is given, creates a new mind directory
5. **Validates** — Calls MindValidator to ensure the mind is valid
6. **Persists** — Saves the mind path to the config file
7. **Returns a BootstrapResult** — Contains the resolved mind path and metadata

This ensures that by the time the HTTP server starts, the mind is valid, the path is known, and the config is saved.

## The Complete Request Flow

Now let's trace what happens when a user sends a chat message:

1. HTTP POST to `/chat` with `{ message: "Hello", sessionId?: "..." }`
2. If `sessionId` is missing, `ICopilotRuntimeClient.CreateSessionAsync()` creates an SDK session and returns its ID
3. `ICopilotRuntimeClient.SendMessageAsync(sessionId, message)` runs:
   - Reuse cached `CopilotSession` or call `ResumeSessionAsync(sessionId)`
   - Send one prompt via `SendAndWaitAsync`
   - Return assistant content
4. API returns `{ response: "...", sessionId: "..." }`

System message composition (SOUL + agents + optional bootstrap.md) happens when sessions are created; subsequent messages continue that session in the SDK.

## Architecture Summary

MsClaw is an elegant solution to a specific problem: **How do you give a persistent identity to an AI agent in a stateless system?**

**The answer:**
- Store the identity (SOUL.md + agent files) on disk
- Inject it as a system message when creating SDK sessions
- Route chat turns over lightweight HTTP endpoints
- Let the SDK persist and compact conversation history

**Key design choices:**
- **Minimal HTTP API** — Just three endpoints: /health, /session/new, /chat
- **Lightweight persistence** — Config is stored in JSON; chat session state is SDK-managed
- **Loose coupling** — Each component has a single responsibility and a clear interface
- **Convention over configuration** — Mind directories follow a standard structure; paths are discovered automatically
- **Identity-at-session-creation** — System message is loaded when creating a session
- **SDK-native session management** — Session IDs flow through HTTP, while the SDK owns turn history

This design makes MsClaw:
- Easy to run (just a single binary + a mind directory)
- Easy to understand (no complex state machines or databases)
- Easy to extend (add agents, skills, and domains just by writing markdown files)

The entire system is orchestrated through dependency injection and a thin HTTP layer — a classical microservice architecture, but for AI agents.
