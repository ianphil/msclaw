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
3. **Models/** — Data structures for configuration, chat requests/responses, session state
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

var builder = WebApplication.CreateBuilder(args);

string resolvedMindRoot;
try
{
    // Bootstrap: resolve mind root before starting the server
    var validator = new MindValidator();
    var configPersistence = new ConfigPersistence();
    var discovery = new MindDiscovery(configPersistence, validator);
    var scaffold = new MindScaffold();
    var orchestrator = new BootstrapOrchestrator(validator, discovery, scaffold, configPersistence);

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

builder.Services.AddSingleton<IMindValidator, MindValidator>();
builder.Services.AddSingleton<IConfigPersistence, ConfigPersistence>();
builder.Services.AddSingleton<IMindDiscovery, MindDiscovery>();
builder.Services.AddSingleton<IMindScaffold, MindScaffold>();
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
builder.Services.AddSingleton<IMindScaffold, MindScaffold>();
builder.Services.AddSingleton<IIdentityLoader, IdentityLoader>();

builder.Services.AddSingleton<ISessionManager, SessionManager>();
builder.Services.AddSingleton<IMindReader, MindReader>();
builder.Services.AddSingleton<ICopilotRuntimeClient, CopilotRuntimeClient>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/session/new", async (ISessionManager sessionManager, CancellationToken cancellationToken) =>
{
    var session = await sessionManager.CreateNewAsync(cancellationToken);
    return Results.Ok(new { sessionId = session.SessionId });
});

app.MapPost("/chat", async (
    ChatRequest request,
    ISessionManager sessionManager,
    ICopilotRuntimeClient copilotClient,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "message is required" });
    }

    var session = await sessionManager.GetOrCreateAsync(cancellationToken);

    session.Messages.Add(new SessionMessage
    {
        Role = "user",
        Content = request.Message,
        Timestamp = DateTime.UtcNow
    });

    var assistantResponse = await copilotClient.GetAssistantResponseAsync(session.Messages, cancellationToken);

    session.Messages.Add(new SessionMessage
    {
        Role = "assistant",
        Content = assistantResponse,
        Timestamp = DateTime.UtcNow
    });

    session.UpdatedAt = DateTime.UtcNow;
    await sessionManager.SaveAsync(session, cancellationToken);

    return Results.Ok(new ChatResponse
    {
        Response = assistantResponse,
        SessionId = session.SessionId
    });
});

app.Run();
```

After bootstrap, the app registers all dependencies as singletons and exposes three HTTP endpoints:

- **GET /health** — Simple liveness probe. Used by Copilot Runtime to check if the agent is alive.
- **POST /session/new** — Creates a new session with a fresh SessionId. The session is stored in memory and persists across requests.
- **POST /chat** — The main chat endpoint. Takes a message, gets the assistant response from the Copilot Runtime, stores it in the session, and returns it to the caller.

The chat endpoint is the core loop:
1. Get or create a session
2. Add the user message to the session history
3. Call the Copilot Runtime client with the full message history
4. Add the assistant response to the session
5. Save the session
6. Return the response

This is the simple case. The real magic happens in the Copilot Runtime client and mind loading — let's look at those next.

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

        var missMoneypenny = Path.Combine(home, "src", "miss-moneypenny");
```

Discovery follows a priority order:
1. **Cached config** — Check if a previous run saved a mind path (in ConfigPersistence).
2. **Current directory** — Check if the current working directory is a valid mind.
3. **~/.msclaw/mind** — Convention path for the default mind.
4. **~/src/miss-moneypenny** — Another convention path (legacy or specific use case).

Each candidate is validated by calling , which uses the MindValidator to check if the directory structure is correct.

Discovery follows a priority order: (1) Cached config from previous run, (2) Current directory, (3) Convention paths like ~/.msclaw/mind, (4) Legacy paths like ~/src/miss-moneypenny. Each candidate is validated by checking if the directory structure is correct.

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

        if (Directory.Exists(mindRoot))
        {
            found.Add(mindRoot);
        }
        else
        {
            errors.Add($"Mind root directory does not exist: {mindRoot}");
        }

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
        CheckOptionalDirectory(mindRoot, ".github", "skills", warnings, found);
        CheckOptionalDirectory(mindRoot, "domains", warnings, found);
        CheckOptionalDirectory(mindRoot, "initiatives", warnings, found);
        CheckOptionalDirectory(mindRoot, "expertise", warnings, found);
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

## Session Management

When a chat request comes in, MsClaw needs to track the conversation. SessionManager handles this. Let's look at how sessions work:

```bash
sed -n '1,60p' src/MsClaw/Core/SessionManager.cs
```

```output
using System.Text.Json;
using MsClaw.Models;
using Microsoft.Extensions.Options;

namespace MsClaw.Core;

public sealed class SessionManager : ISessionManager
{
    private const string ActiveSessionFile = "active-session-id.txt";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _sessionStore;

    public SessionManager(IOptions<MsClawOptions> options)
    {
        _sessionStore = Path.GetFullPath(options.Value.SessionStore);
    }

    public async Task<SessionState> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_sessionStore);
        var activeSessionPath = Path.Combine(_sessionStore, ActiveSessionFile);

        if (File.Exists(activeSessionPath))
        {
            var sessionId = (await File.ReadAllTextAsync(activeSessionPath, cancellationToken)).Trim();
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                var filePath = GetSessionPath(sessionId);
                if (File.Exists(filePath))
                {
                    await using var stream = File.OpenRead(filePath);
                    var session = await JsonSerializer.DeserializeAsync<SessionState>(stream, cancellationToken: cancellationToken);
                    if (session is not null)
                    {
                        return session;
                    }
                }
            }
        }

        return await CreateNewAsync(cancellationToken);
    }

    public async Task<SessionState> CreateNewAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_sessionStore);

        var session = new SessionState
        {
            SessionId = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await SaveAsync(session, cancellationToken);
        return session;
    }

    public async Task SaveAsync(SessionState session, CancellationToken cancellationToken = default)
```

SessionManager persists conversations to disk. Here's the flow:
1. **GetOrCreateAsync** — Check if there's an active session. If so, load it. If not, create a new one.
2. **CreateNewAsync** — Generate a new SessionId, initialize timestamps, and save it.
3. **SaveAsync** — Serialize the session to JSON and write it to the session store (usually in the mind directory under data/).

Sessions are stored as individual JSON files per SessionId. An active-session-id.txt file tracks which session is currently active, so subsequent requests can load the same session. This gives continuity across multiple chat messages — the model remembers the entire conversation history because it's stored and reloaded with each request.

## The Copilot Runtime Client

This is where the magic happens. CopilotRuntimeClient sends the conversation history to the Copilot Runtime API along with the system message (the mind's identity). Let's see how it works:

```bash
sed -n '1,70p' src/MsClaw/Core/CopilotRuntimeClient.cs
```

```output
using System.Text;
using GitHub.Copilot.SDK;
using MsClaw.Models;
using Microsoft.Extensions.Options;

namespace MsClaw.Core;

public sealed class CopilotRuntimeClient : ICopilotRuntimeClient
{
    private readonly MsClawOptions _options;
    private readonly IIdentityLoader _identityLoader;

    public CopilotRuntimeClient(IOptions<MsClawOptions> options, IIdentityLoader identityLoader)
    {
        _options = options.Value;
        _identityLoader = identityLoader;
    }

    public async Task<string> GetAssistantResponseAsync(IReadOnlyList<SessionMessage> messages, CancellationToken cancellationToken = default)
    {
        var mindRoot = Path.GetFullPath(_options.MindRoot);
        var systemMessage = await _identityLoader.LoadSystemMessageAsync(mindRoot, cancellationToken);
        var bootstrapPath = Path.Combine(mindRoot, "bootstrap.md");
        if (File.Exists(bootstrapPath))
        {
            var bootstrapInstructions = await File.ReadAllTextAsync(bootstrapPath, cancellationToken);
            systemMessage = bootstrapInstructions + "\n\n---\n\n" + systemMessage;
        }

        await using var client = new CopilotClient(new CopilotClientOptions
        {
            Cwd = mindRoot,
            AutoStart = true,
            UseStdio = true
        });

        await client.StartAsync(cancellationToken);

        await using var session = await client.CreateSessionAsync(new SessionConfig
        {
            Model = _options.Model,
            Streaming = false,
            InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            OnPermissionRequest = PermissionHandler.ApproveAll,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Replace,
                Content = systemMessage
            }
        }, cancellationToken);

        string? assistantMessage = null;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = session.On(evt =>
        {
            switch (evt)
            {
                case AssistantMessageEvent msg when !string.IsNullOrWhiteSpace(msg.Data.Content):
                    assistantMessage = msg.Data.Content;
                    break;
                case SessionErrorEvent err:
                    finished.TrySetException(new InvalidOperationException(err.Data.Message));
                    break;
                case SessionIdleEvent:
                    finished.TrySetResult();
                    break;
            }
        });

```

This is the critical part. CopilotRuntimeClient:
1. **Loads the system message** — Calls IdentityLoader to get SOUL.md + agents
2. **Checks for bootstrap.md** — If it exists, prepends bootstrap instructions to the system message
3. **Creates a CopilotClient** — Connects to the Copilot Runtime via stdio
4. **Creates a session** — Specifies the model, system message mode (Replace), and disables infinite sessions
5. **Sends messages** — Passes the conversation history
6. **Listens for events** — Waits for AssistantMessageEvent (the response), SessionErrorEvent (errors), or SessionIdleEvent (done)

The key insight: The system message (the mind's identity) is injected here. The Copilot Runtime receives both the identity and the conversation history, so the model responds as this specific agent with this specific mind. The session is closed after the response, so the actual chat state is managed by MsClaw (in SessionManager), not by the Copilot Runtime.

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

        var json = File.ReadAllText(_configPath);
        return JsonSerializer.Deserialize<MsClawConfig>(json, JsonOptions);
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

ConfigPersistence is simple: it stores a MsClawConfig to ~/.msclaw/config.json. The config contains the MindRoot path and a LastUsed timestamp. On startup, MindDiscovery checks the saved config first before trying convention paths. If the user runs --reset-config, it clears the saved config and exits — useful when you want MsClaw to re-discover the mind on the next run.

## Data Models

Let's look at the key data structures that flow through the system.

```bash
cat src/MsClaw/Models/SessionState.cs
```

```output
namespace MsClaw.Models;

public sealed class SessionState
{
    public required string SessionId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<SessionMessage> Messages { get; init; } = [];
}
```

```bash
cat src/MsClaw/Models/SessionMessage.cs
```

```output
namespace MsClaw.Models;

public sealed class SessionMessage
{
    public required string Role { get; init; }
    public required string Content { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
```

```bash
cat src/MsClaw/Models/ChatRequest.cs && echo && cat src/MsClaw/Models/ChatResponse.cs
```

```output
namespace MsClaw.Models;

public sealed class ChatRequest
{
    public string Message { get; set; } = string.Empty;
}

namespace MsClaw.Models;

public sealed class ChatResponse
{
    public required string Response { get; init; }
    public required string SessionId { get; init; }
}
```

The data models are minimal by design:
- **SessionState** — Holds a unique SessionId, creation/update timestamps, and the list of messages in the conversation.
- **SessionMessage** — A single message with a role ("user" or "assistant"), content, and timestamp.
- **ChatRequest** — Incoming HTTP request with just a Message field.
- **ChatResponse** — HTTP response with the Response text and the SessionId (so the client knows which session was used).

This simplicity is intentional. MsClaw is not a chat library; it's a thin adapter between HTTP requests and the Copilot Runtime API.

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
                    ThrowUsage($"Unknown argument: {args[i]}");
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

1. HTTP POST to /chat with {message: "Hello"}
2. SessionManager.GetOrCreateAsync() — Load the active session or create a new one
3. Add the user message to session.Messages
4. CopilotRuntimeClient.GetAssistantResponseAsync():
   - IdentityLoader.LoadSystemMessageAsync() — Read SOUL.md + agent files
   - Check for bootstrap.md and prepend it if it exists
   - Create a CopilotClient connection to the Copilot Runtime
   - Create a session with SystemMessage = (bootstrap + SOUL + agents)
   - Send the conversation history (system message + all messages)
   - Listen for AssistantMessageEvent
   - Return the response
5. Add the assistant message to session.Messages
6. SessionManager.SaveAsync() — Write the session to disk
7. Return {response: "...", sessionId: "..."} to the client

The mind is loaded fresh for each chat request, so if SOUL.md or agent files change, the next request sees the updates. The session history is maintained separately on disk.

## Architecture Summary

MsClaw is an elegant solution to a specific problem: **How do you give a persistent identity to an AI agent in a stateless system?**

**The answer:**
- Store the identity (SOUL.md) on disk
- Load it at request time
- Inject it as a system message to the Copilot Runtime
- Maintain conversation history separately from the model

**Key design choices:**
- **Minimal HTTP API** — Just three endpoints: /health, /session/new, /chat
- **File-based persistence** — No databases. Configuration and sessions are JSON files.
- **Loose coupling** — Each component has a single responsibility and a clear interface
- **Convention over configuration** — Mind directories follow a standard structure; paths are discovered automatically
- **Fresh loads** — The mind is loaded from disk on each request, so changes are immediately visible
- **Session management** — Conversations are stored in JSON, enabling long-running discussions across multiple requests

This design makes MsClaw:
- Easy to run (just a single binary + a mind directory)
- Easy to understand (no complex state machines or databases)
- Easy to extend (add agents, skills, and domains just by writing markdown files)

The entire system is orchestrated through dependency injection and a thin HTTP layer — a classical microservice architecture, but for AI agents.
