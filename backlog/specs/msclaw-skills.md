# MsClaw Skills System

> Reusable tool definitions the agent can invoke — discovery, registration, invocation, and lifecycle.

## Overview

The MsClaw Skills System defines how the Gateway discovers, registers, and invokes
**skills** — typed tool definitions that extend the agent's capabilities beyond the
Copilot SDK's built-in tools. Skills bridge the gap between what the LLM can reason
about and what the agent can actually _do_: read memory, search the web, capture
photos, run scripts, call APIs.

Skills are the agent's hands. The mind is who it is; skills are what it can do.

Inspired by [OpenClaw's three-tier skill model](https://github.com/openclaw/openclaw)
(bundled / managed / workspace), this spec defines how MsClaw implements that pattern
on top of ASP.NET Core and the GitHub Copilot SDK.

Reference: [MsClaw Gateway architecture](msclaw-gateway.md) §4 · [Protocol spec](msclaw-gateway-protocol.md)

## Goals

- **Declarative descriptors** — skills are defined in YAML manifests, not compiled into the gateway.
- **Three-tier sourcing** — bundled skills ship with the gateway, workspace skills live in the mind, managed skills are installed from registries.
- **Copilot SDK integration** — skills are registered as tools on `CopilotSession` via `AIFunctionFactory`, invoked automatically by the model.
- **Node routing** — skills that require hardware capabilities are transparently routed to connected device nodes.
- **Dependency checking** — skills declare their requirements; the gateway validates availability before registration.
- **Hot discovery** — workspace skills are discovered per-session from the mind directory without gateway restart.

## Architecture

```
                    ┌──────────────────────────────────────────────────────────────┐
                    │                      MSCLAW GATEWAY                         │
                    │                                                              │
                    │  ┌────────────────────────────────────────────────────────┐  │
                    │  │                    SKILL SYSTEM                        │  │
                    │  │                                                        │  │
                    │  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │  │
                    │  │  │   BUNDLED    │  │  WORKSPACE   │  │   MANAGED    │  │  │
                    │  │  │              │  │              │  │              │  │  │
                    │  │  │  memory.*    │  │  skills/     │  │  ~/.msclaw/  │  │  │
                    │  │  │  mind.*      │  │  .github/    │  │   skills/    │  │  │
                    │  │  │  web.*       │  │   skills/    │  │              │  │  │
                    │  │  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘  │  │
                    │  │         │                  │                  │         │  │
                    │  │         └──────────┬───────┘──────────────────┘         │  │
                    │  │                    ▼                                    │  │
                    │  │           ┌────────────────┐                            │  │
                    │  │           │ SKILL REGISTRY │  in-memory, per-session    │  │
                    │  │           │                │                            │  │
                    │  │           │  resolve name  │                            │  │
                    │  │           │  check deps    │                            │  │
                    │  │           │  build tools   │                            │  │
                    │  │           └───────┬────────┘                            │  │
                    │  │                   │ AIFunction[]                        │  │
                    │  └───────────────────┼────────────────────────────────────┘  │
                    │                      │                                       │
                    │  ┌───────────────────▼────────────────────────────────────┐  │
                    │  │               AGENT RUNTIME                            │  │
                    │  │                                                        │  │
                    │  │  CopilotClient → CreateSessionAsync(SessionConfig      │  │
                    │  │    { Tools = registry.BuildTools() })                   │  │
                    │  │                                                        │  │
                    │  │  Model invokes tool → registry resolves handler →      │  │
                    │  │    execute locally or route to node                     │  │
                    │  └────────────────────────────────────────────────────────┘  │
                    └──────────────────────────────────────────────────────────────┘
```

## Skill Descriptor Format

Skills are defined in YAML manifest files. Each file describes one skill.

### Schema

```yaml
# skill.yaml — descriptor for a single skill
name: memory.read                          # REQUIRED — dot-separated unique identifier
description: >                             # REQUIRED — natural language for model tool selection
  Read a file from the agent's persistent working memory directory.
version: 1.0.0                             # REQUIRED — semver

parameters:                                # REQUIRED — JSON Schema for tool input
  type: object
  properties:
    path:
      type: string
      description: Relative path within .working-memory/ to read.
  required: [path]

returns:                                   # OPTIONAL — description of output
  type: string
  description: The file contents as a string, or an error message.

handler:                                   # REQUIRED — how to execute the skill
  type: delegate                           # delegate | cli | script | node | http
  # type-specific fields below

requirements:                              # OPTIONAL — prerequisites
  binaries: []                             # CLI commands that must be on PATH
  capabilities: []                         # Node capabilities required (e.g., camera, screen)
  platform: []                             # OS constraints (windows, linux, macos)

tags: [memory, builtin, read]              # OPTIONAL — for filtering and discovery
```

### Handler Types

| Type | Description | Runs Where | Fields |
|------|-------------|------------|--------|
| `delegate` | In-process C# function | Gateway | `class`, `method` |
| `cli` | Shell command execution | Gateway host | `command`, `args`, `workingDir`, `timeout` |
| `script` | Script file execution | Gateway host | `interpreter`, `file`, `timeout` |
| `node` | Routed to a device node | Connected node | `capability`, `command`, `targetPolicy` |
| `http` | HTTP API call | Gateway host | `url`, `method`, `headers`, `bodyTemplate` |

### Handler Examples

**Delegate handler** (bundled skills):

```yaml
handler:
  type: delegate
  class: MsClaw.Gateway.Skills.MemoryReadHandler
  method: ExecuteAsync
```

**CLI handler** (workspace skill):

```yaml
handler:
  type: cli
  command: rg
  args: ["--json", "--max-count", "10", "{{query}}", "{{path}}"]
  workingDir: "{{mind_root}}"
  timeout: 30
```

**Script handler** (workspace skill):

```yaml
handler:
  type: script
  interpreter: python3
  file: scripts/analyze.py
  timeout: 60
```

**Node handler** (device-routed skill):

```yaml
handler:
  type: node
  capability: camera
  command: camera.capture
  targetPolicy: any          # any | preferred | specific
```

**HTTP handler** (API integration):

```yaml
handler:
  type: http
  url: "https://api.example.com/search"
  method: POST
  headers:
    Authorization: "Bearer {{env:API_KEY}}"
    Content-Type: application/json
  bodyTemplate: |
    { "query": "{{query}}", "limit": {{limit}} }
  timeout: 15
```

### Template Variables

Handler fields support Mustache-style `{{variable}}` interpolation:

| Variable | Value |
|----------|-------|
| `{{mind_root}}` | Absolute path to the mind directory |
| `{{working_memory}}` | Absolute path to `.working-memory/` |
| `{{param_name}}` | Value of a parameter from the `parameters` schema |
| `{{env:VAR_NAME}}` | Environment variable |

## Skill Sources

### Bundled Skills

Shipped with the gateway binary. Implemented as C# delegates registered at startup.
Cannot be removed or overridden by workspace skills (name collision is an error).

**Location**: Compiled into the gateway assembly.

**Lifecycle**: Available immediately on startup. Updated only by upgrading the gateway.

### Workspace Skills

Defined in the mind directory by the mind author. Discovered per-session from disk.

**Locations** (searched in order, first match wins):

```
{mind_root}/skills/{name}/skill.yaml        ← primary location
{mind_root}/.github/skills/{name}/skill.yaml ← alternative (GitHub convention)
```

**Directory structure**:

```
skills/
├── ripgrep-search/
│   ├── skill.yaml           ← descriptor
│   └── scripts/             ← supporting files (optional)
│       └── search.sh
├── summarize-url/
│   ├── skill.yaml
│   └── templates/
│       └── prompt.md
└── daily-digest/
    └── skill.yaml
```

**Lifecycle**: Discovered each time a session is created. No gateway restart needed.
The gateway reads the `skills/` directory, parses descriptors, validates requirements,
and registers valid skills as tools on the session.

### Managed Skills

Installed from external registries into a shared directory. Available to all minds
hosted by the gateway.

**Location**: `~/.msclaw/skills/{name}/skill.yaml`

**Install sources** (future):

| Source | Command |
|--------|---------|
| Git repo | `msclaw skill install https://github.com/user/skill-name` |
| NuGet | `msclaw skill install --nuget SkillPackageName` |
| npm | `msclaw skill install --npm @scope/skill-name` |
| Local path | `msclaw skill install --path ./my-skill` |

**Lifecycle**: Installed once, available across sessions and gateway restarts.
Updated via `msclaw skill update {name}`.

> **v1 scope**: Managed skills are out of scope for the initial release.
> The registry, install, and update commands are future work.

## Skill Discovery

### Discovery Order

Skills are discovered and merged in priority order. Name collisions are errors
(the gateway logs a warning and skips the lower-priority duplicate).

```
1. Bundled skills       (highest priority — cannot be overridden)
2. Workspace skills     (mind-specific)
3. Managed skills       (shared across minds)
```

### Discovery Flow

```
GATEWAY STARTUP / SESSION CREATION
        │
        ├── 1. Load bundled skill manifests (compiled-in)
        │       → Register in SkillRegistry
        │
        ├── 2. Scan {mind_root}/skills/*/skill.yaml
        │   └── Scan {mind_root}/.github/skills/*/skill.yaml
        │       → Parse YAML → Validate schema → Check requirements
        │       → Register in SkillRegistry (skip if name collision)
        │
        └── 3. Scan ~/.msclaw/skills/*/skill.yaml  (future)
                → Parse YAML → Validate schema → Check requirements
                → Register in SkillRegistry (skip if name collision)
```

### Validation Rules

| Rule | Severity | Description |
|------|----------|-------------|
| `name` is present and dot-separated | Error | Skill is skipped |
| `name` is unique across all sources | Error | Lower-priority duplicate is skipped |
| `description` is present | Error | Skill is skipped (model needs description for tool selection) |
| `parameters` is valid JSON Schema | Error | Skill is skipped |
| `handler` is present and type is known | Error | Skill is skipped |
| Required binaries are on PATH | Warning | Skill registered but marked `degraded` |
| Required capabilities not available | Warning | Skill registered but only invocable when a matching node connects |
| `version` is valid semver | Warning | Skill registered with version `0.0.0` |

## Skill Registry

The `ISkillRegistry` is an in-memory registry that holds all discovered skills
for the current gateway instance. It is the single source of truth for what
skills are available.

### Contract

```csharp
/// <summary>
/// Central registry of all discovered skills.
/// Resolves skill names to descriptors and builds Copilot SDK tool arrays.
/// </summary>
public interface ISkillRegistry
{
    /// <summary>
    /// Discovers and registers skills from all sources for the given mind root.
    /// Clears any previously registered workspace/managed skills.
    /// Bundled skills are always present.
    /// </summary>
    Task DiscoverAsync(string mindRoot, CancellationToken ct = default);

    /// <summary>
    /// Returns all registered skills.
    /// </summary>
    IReadOnlyList<SkillDescriptor> GetAll();

    /// <summary>
    /// Returns skills filtered by source.
    /// </summary>
    IReadOnlyList<SkillDescriptor> GetBySource(SkillSource source);

    /// <summary>
    /// Resolves a skill by name. Returns null if not found.
    /// </summary>
    SkillDescriptor? GetByName(string name);

    /// <summary>
    /// Builds an array of AIFunction tools for registration with the Copilot SDK session.
    /// Only includes skills whose requirements are satisfied.
    /// </summary>
    /// <param name="availableCapabilities">
    /// Node capabilities currently available (from connected nodes).
    /// Skills requiring unavailable capabilities are excluded.
    /// </param>
    AIFunction[] BuildTools(IReadOnlySet<string>? availableCapabilities = null);
}

/// <summary>
/// Where a skill was loaded from.
/// </summary>
public enum SkillSource
{
    Bundled,
    Workspace,
    Managed
}

/// <summary>
/// Parsed and validated skill descriptor.
/// </summary>
public record SkillDescriptor
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Version { get; init; }
    public required SkillSource Source { get; init; }
    public required JsonElement Parameters { get; init; }
    public required SkillHandler Handler { get; init; }
    public SkillRequirements? Requirements { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public SkillStatus Status { get; init; } = SkillStatus.Ready;
    public string? StatusReason { get; init; }
}

/// <summary>
/// Skill readiness status.
/// </summary>
public enum SkillStatus
{
    Ready,       // All requirements met, fully operational
    Degraded,    // Missing optional requirements (e.g., binary not on PATH)
    Unavailable  // Missing required capability (no matching node connected)
}
```

### Registration with Copilot SDK

The registry builds `AIFunction[]` from skill descriptors and passes them to
`CreateSessionAsync`. Each skill becomes a tool the model can invoke.

```csharp
// During session creation
var tools = skillRegistry.BuildTools(connectedNodeCapabilities);

await using var session = await copilotClient.CreateSessionAsync(new SessionConfig
{
    Model = request.Model ?? defaultModel,
    Streaming = true,
    Tools = tools,
    SystemMessage = new SystemMessageConfig
    {
        Mode = SystemMessageMode.Append,
        Content = systemMessage
    }
});
```

### Tool Function Generation

Each skill descriptor is compiled into an `AIFunction` via `AIFunctionFactory.Create`:

```csharp
// Simplified — actual implementation handles all handler types
AIFunction BuildToolFromSkill(SkillDescriptor skill)
{
    return AIFunctionFactory.Create(
        async (JsonElement input) =>
        {
            return skill.Handler.Type switch
            {
                HandlerType.Delegate => await InvokeDelegateAsync(skill, input),
                HandlerType.Cli => await InvokeCliAsync(skill, input),
                HandlerType.Script => await InvokeScriptAsync(skill, input),
                HandlerType.Node => await InvokeNodeAsync(skill, input),
                HandlerType.Http => await InvokeHttpAsync(skill, input),
                _ => throw new InvalidOperationException(
                    $"Unknown handler type: {skill.Handler.Type}")
            };
        },
        skill.Name,
        skill.Description
    );
}
```

## Invocation Flow

### Local Skill (delegate, cli, script, http)

```
MODEL                    GATEWAY                     HOST
  │                        │                           │
  │── tool_call ──────────►│                           │
  │   name: "memory.read"  │                           │
  │   args: {path: "..."}  │                           │
  │                        │── resolve skill ──────────│
  │                        │   SkillRegistry.GetByName │
  │                        │                           │
  │                        │── execute handler ────────│
  │                        │   delegate: call method   │
  │                        │   cli: spawn process      │
  │                        │   script: spawn + interp  │
  │                        │   http: HttpClient.Send   │
  │                        │                           │
  │                        │◄── result ────────────────│
  │◄── tool_result ────────│                           │
  │                        │                           │
```

### Node-Routed Skill (camera, screen, location)

```
MODEL                    GATEWAY                     NODE
  │                        │                           │
  │── tool_call ──────────►│                           │
  │   name: "camera.capture│                           │
  │   args: {...}          │                           │
  │                        │── resolve skill ──────────│
  │                        │   handler.type = node     │
  │                        │   capability = camera     │
  │                        │                           │
  │                        │── select target node ─────│
  │                        │   targetPolicy: any       │
  │                        │   find node with "camera" │
  │                        │                           │
  │                        │── OnNodeInvokeRequest ───►│  (SignalR push)
  │                        │   {skill, args, invokeId} │
  │                        │                           │── execute on device
  │                        │                           │
  │                        │◄── NodeInvokeResult ──────│  (SignalR hub call)
  │                        │   {invokeId, result}      │
  │                        │                           │
  │◄── tool_result ────────│                           │
  │                        │                           │
```

### Node Target Policy

When multiple nodes offer the same capability, the `targetPolicy` determines
which node receives the invocation.

| Policy | Behavior |
|--------|----------|
| `any` | First available node with the capability. No preference. |
| `preferred` | Use the node marked as preferred for this capability. Fall back to `any`. |
| `specific` | Route to a named device. Fails if the device is not connected. |

### Invocation Events

Skill invocations generate `AgentEvent` entries in the streaming response.

```csharp
// Tool execution start
yield return new AgentEvent
{
    RunId = runId,
    Seq = seq++,
    Stream = AgentEventStream.Tool,
    Timestamp = DateTimeOffset.UtcNow,
    Data = new ToolEvent
    {
        Phase = ToolPhase.Start,
        Skill = skill.Name,
        Source = skill.Source,
        RequiresNode = skill.Handler.Type == HandlerType.Node
    }
};

// Tool execution complete
yield return new AgentEvent
{
    RunId = runId,
    Seq = seq++,
    Stream = AgentEventStream.Tool,
    Timestamp = DateTimeOffset.UtcNow,
    Data = new ToolEvent
    {
        Phase = ToolPhase.End,
        Skill = skill.Name,
        DurationMs = elapsed.TotalMilliseconds,
        Success = result.Success
    }
};
```

### Execution Approval

Some skills may require operator approval before execution (e.g., destructive
operations, external API calls with side effects). This integrates with the
Gateway's existing exec approval flow.

```yaml
# In skill.yaml
approval: required           # required | optional | none (default)
approvalMessage: >
  This skill will send an email. Approve?
```

When `approval: required`, the gateway:

1. Pauses tool execution.
2. Pushes `OnApprovalRequested` to operators via SignalR.
3. Waits for `ExecApprovalResolve` from an operator.
4. If approved, executes the handler. If rejected, returns an error to the model.

## Built-in Skills (v1)

### `memory.read`

Read a file from the agent's persistent working memory.

```yaml
name: memory.read
description: >
  Read a file from the agent's persistent working memory directory.
  Use this to recall information the agent has previously saved.
version: 1.0.0
parameters:
  type: object
  properties:
    path:
      type: string
      description: >
        Relative path within .working-memory/ to read.
        Examples: "memory.md", "rules.md", "log.md"
  required: [path]
returns:
  type: string
  description: File contents as UTF-8 text, or error message if file not found.
handler:
  type: delegate
  class: MsClaw.Gateway.Skills.Builtin.MemoryReadHandler
  method: ExecuteAsync
tags: [memory, builtin, read]
```

### `memory.write`

Write to the agent's persistent working memory.

```yaml
name: memory.write
description: >
  Write content to a file in the agent's persistent working memory directory.
  Use this to save information the agent wants to remember across sessions.
version: 1.0.0
parameters:
  type: object
  properties:
    path:
      type: string
      description: >
        Relative path within .working-memory/ to write.
        Examples: "memory.md", "rules.md", "log.md"
    content:
      type: string
      description: The content to write to the file.
    mode:
      type: string
      enum: [overwrite, append]
      description: Write mode. Default is "overwrite".
  required: [path, content]
handler:
  type: delegate
  class: MsClaw.Gateway.Skills.Builtin.MemoryWriteHandler
  method: ExecuteAsync
tags: [memory, builtin, write]
```

### `mind.list`

List files and directories in the mind.

```yaml
name: mind.list
description: >
  List files and directories in the agent's mind directory.
  Use this to discover what knowledge, domains, and initiatives are available.
version: 1.0.0
parameters:
  type: object
  properties:
    path:
      type: string
      description: >
        Relative path within the mind directory to list. Defaults to root.
    depth:
      type: integer
      description: Maximum directory depth. Default 1, max 5.
      minimum: 1
      maximum: 5
  required: []
handler:
  type: delegate
  class: MsClaw.Gateway.Skills.Builtin.MindListHandler
  method: ExecuteAsync
tags: [mind, builtin, read]
```

### `mind.read`

Read a file from the mind directory.

```yaml
name: mind.read
description: >
  Read a file from the agent's mind directory. Use this to access domain knowledge,
  initiative details, expertise references, or any other mind content.
  Path traversal outside the mind root is blocked.
version: 1.0.0
parameters:
  type: object
  properties:
    path:
      type: string
      description: Relative path within the mind directory to read.
  required: [path]
returns:
  type: string
  description: File contents as UTF-8 text.
handler:
  type: delegate
  class: MsClaw.Gateway.Skills.Builtin.MindReadHandler
  method: ExecuteAsync
tags: [mind, builtin, read]
```

### `web.search`

Search the web.

```yaml
name: web.search
description: >
  Search the web for information. Returns a summary of relevant results.
  Use this when the agent needs current information not in its training data or mind.
version: 1.0.0
parameters:
  type: object
  properties:
    query:
      type: string
      description: Search query.
    maxResults:
      type: integer
      description: Maximum number of results to return. Default 5.
      minimum: 1
      maximum: 20
  required: [query]
handler:
  type: delegate
  class: MsClaw.Gateway.Skills.Builtin.WebSearchHandler
  method: ExecuteAsync
tags: [web, builtin, search]
```

### `camera.capture`

Capture a photo from a connected device.

```yaml
name: camera.capture
description: >
  Take a photo using a connected device's camera.
  Requires a node with camera capability to be connected.
version: 1.0.0
parameters:
  type: object
  properties:
    camera:
      type: string
      enum: [front, back]
      description: Which camera to use. Default "back".
    device:
      type: string
      description: >
        Specific device ID to target. If omitted, uses any available device
        with camera capability.
  required: []
handler:
  type: node
  capability: camera
  command: camera.capture
  targetPolicy: any
requirements:
  capabilities: [camera]
tags: [camera, node, capture]
```

### `screen.record`

Record the screen of a connected device.

```yaml
name: screen.record
description: >
  Record the screen of a connected device for a specified duration.
  Requires a node with screen recording capability.
version: 1.0.0
parameters:
  type: object
  properties:
    duration:
      type: integer
      description: Recording duration in seconds. Default 10, max 120.
      minimum: 1
      maximum: 120
    device:
      type: string
      description: Specific device ID to target.
  required: []
handler:
  type: node
  capability: screen
  command: screen.record
  targetPolicy: any
requirements:
  capabilities: [screen]
tags: [screen, node, record]
```

### `location.get`

Get the location of a connected device.

```yaml
name: location.get
description: >
  Get the current geographic location of a connected device.
  Requires a node with location capability.
version: 1.0.0
parameters:
  type: object
  properties:
    device:
      type: string
      description: Specific device ID to target.
  required: []
handler:
  type: node
  capability: location
  command: location.get
  targetPolicy: any
requirements:
  capabilities: [location]
tags: [location, node, gps]
```

## Handler Execution

### Delegate Handler

In-process C# method invocation. Used by bundled skills.

```csharp
/// <summary>
/// Contract for delegate skill handlers.
/// </summary>
public interface ISkillHandler
{
    /// <summary>
    /// Executes the skill with the given parameters.
    /// </summary>
    /// <param name="parameters">Deserialized tool input from the model.</param>
    /// <param name="context">Execution context with mind root, session info, etc.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result to return to the model as the tool response.</returns>
    Task<SkillResult> ExecuteAsync(
        JsonElement parameters,
        SkillExecutionContext context,
        CancellationToken ct = default);
}

/// <summary>
/// Context available to skill handlers during execution.
/// </summary>
public record SkillExecutionContext
{
    public required string MindRoot { get; init; }
    public required string WorkingMemoryPath { get; init; }
    public required string SessionId { get; init; }
    public required string RunId { get; init; }
    public IReadOnlyDictionary<string, string> Environment { get; init; }
        = new Dictionary<string, string>();
}

/// <summary>
/// Result of a skill execution.
/// </summary>
public record SkillResult
{
    public required bool Success { get; init; }
    public required object Data { get; init; }
    public string? Error { get; init; }
}
```

### CLI Handler

Spawns a child process. Supports template variable interpolation in `command`,
`args`, and `workingDir`.

**Execution rules**:
- Working directory defaults to `{{mind_root}}` if not specified.
- `stdout` is captured as the tool result.
- `stderr` is included in the result on failure.
- Process is killed after `timeout` seconds (default: 30).
- Exit code 0 = success; non-zero = failure.
- Shell expansion is **not** performed — arguments are passed directly.

### Script Handler

Spawns the interpreter with the script file. The script file path is relative
to the skill directory.

**Execution rules**:
- Interpreter must be on PATH (checked during requirement validation).
- Parameters are passed as JSON via `stdin`.
- `stdout` is captured as the tool result.
- Same timeout and exit code rules as CLI handler.

### Node Handler

Routes the invocation to a connected device node via SignalR.

**Execution rules**:
- Gateway selects a target node based on `targetPolicy` and required `capability`.
- Sends `OnNodeInvokeRequest` to the target node.
- Waits for `NodeInvokeResult` with a matching `invokeId`.
- Timeout is configurable per skill (default: 60 seconds for node skills).
- If no node with the required capability is connected, returns an error immediately.

### HTTP Handler

Makes an HTTP request to an external API.

**Execution rules**:
- URL, headers, and body support template variable interpolation.
- `env:VAR_NAME` references are resolved from the gateway's environment.
- Response body is returned as the tool result.
- HTTP 2xx = success; other status codes = failure.
- Default timeout: 15 seconds.
- HTTPS is required for non-localhost URLs.

## Security

### Path Traversal Protection

Skills that access the filesystem (memory.*, mind.*) use `MindReader`'s
path-traversal protection. Paths are resolved relative to the mind root and
validated to stay within bounds.

### Environment Variable Access

Only explicitly allowlisted environment variables are available via `{{env:VAR}}`.
The allowlist is configured in `appsettings.json`.

```json
{
  "MsClaw": {
    "Skills": {
      "AllowedEnvVars": ["API_KEY", "SEARCH_API_KEY"]
    }
  }
}
```

### CLI / Script Sandboxing

- CLI and script handlers run as the same user as the gateway process.
- No shell expansion — arguments are passed directly to avoid injection.
- Timeout enforcement prevents runaway processes.
- `approval: required` can gate dangerous skills behind operator confirmation.
- Future: container-based sandboxing for untrusted skills.

### Node Invocation Auth

Node invocations go through the Gateway's existing device pairing and
authorization model. Only paired, authenticated nodes receive invoke requests.

## Configuration

```json
{
  "MsClaw": {
    "Skills": {
      "WorkspaceDiscovery": true,
      "ManagedSkillsPath": "~/.msclaw/skills",
      "DefaultTimeout": 30,
      "NodeInvokeTimeout": 60,
      "AllowedEnvVars": [],
      "Disabled": [],
      "ApprovalPolicy": "per-skill"
    }
  }
}
```

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `WorkspaceDiscovery` | bool | `true` | Enable discovery of skills from the mind's `skills/` directory |
| `ManagedSkillsPath` | string | `~/.msclaw/skills` | Directory for managed (installed) skills |
| `DefaultTimeout` | int | `30` | Default timeout in seconds for CLI/script/HTTP handlers |
| `NodeInvokeTimeout` | int | `60` | Default timeout in seconds for node-routed skills |
| `AllowedEnvVars` | string[] | `[]` | Environment variables accessible via `{{env:VAR}}` |
| `Disabled` | string[] | `[]` | Skill names to disable (e.g., `["web.search"]`) |
| `ApprovalPolicy` | string | `per-skill` | `per-skill` (use skill's `approval` field) or `all` (require approval for every skill) |

## Hub Integration

### Skill-Related Hub Methods

The `GatewayHub` exposes methods for operators to inspect and manage skills.

```csharp
public class GatewayHub : Hub<IGatewayClient>
{
    // ── Skills ──────────────────────────────────────────────────
    /// <summary>
    /// Lists all registered skills with their source, status, and metadata.
    /// </summary>
    [Authorize(Policy = "OperatorRead")]
    Task<SkillListResult> SkillsList();

    /// <summary>
    /// Gets the full descriptor for a skill by name.
    /// </summary>
    [Authorize(Policy = "OperatorRead")]
    Task<SkillDescriptor?> SkillGet(SkillGetRequest request);

    /// <summary>
    /// Forces re-discovery of workspace and managed skills.
    /// </summary>
    [Authorize(Policy = "OperatorAdmin")]
    Task<SkillDiscoveryResult> SkillsRediscover();

    /// <summary>
    /// Invokes a skill directly (bypassing the model).
    /// For testing and debugging.
    /// </summary>
    [Authorize(Policy = "OperatorAdmin")]
    Task<SkillResult> SkillInvoke(SkillInvokeRequest request);
}
```

### Skill Events (Server → Client)

```csharp
public interface IGatewayClient
{
    // ... existing events ...

    /// <summary>
    /// Pushed when the skill registry changes (discovery, status change).
    /// </summary>
    Task OnSkillsChanged(SkillsChangedEvent e);
}
```

### Event and Request Schemas

```csharp
public record SkillListResult
{
    public required IReadOnlyList<SkillSummary> Skills { get; init; }
}

public record SkillSummary
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Version { get; init; }
    public required SkillSource Source { get; init; }
    public required SkillStatus Status { get; init; }
    public string? StatusReason { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public record SkillGetRequest
{
    public required string Name { get; init; }
}

public record SkillDiscoveryResult
{
    public required int Total { get; init; }
    public required int Bundled { get; init; }
    public required int Workspace { get; init; }
    public required int Managed { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public record SkillInvokeRequest
{
    public required string Name { get; init; }
    public required JsonElement Parameters { get; init; }
}

public record SkillsChangedEvent
{
    public required string Reason { get; init; }
    public required IReadOnlyList<SkillSummary> Skills { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

public record ToolEvent
{
    public required ToolPhase Phase { get; init; }
    public required string Skill { get; init; }
    public SkillSource? Source { get; init; }
    public bool RequiresNode { get; init; }
    public double? DurationMs { get; init; }
    public bool? Success { get; init; }
}

public enum ToolPhase
{
    Start,
    End
}
```

## Mapping to OpenClaw

| OpenClaw | MsClaw | Notes |
|----------|--------|-------|
| Skills (JS/TS functions) | Skills (YAML descriptors + handlers) | Declarative-first, not code-first |
| Bundled in gateway process | Bundled as C# delegates | Same concept, different runtime |
| Workspace `skills/` directory | Workspace `skills/` directory | Same location convention |
| Managed via npm/git | Managed via registry (future) | Similar concept, .NET ecosystem |
| Tool definitions passed to pi-mono | AIFunction[] passed to CopilotClient | Same SDK pattern |
| Node capability routing | Node capability routing via SignalR | Same pattern, SignalR transport |
| JSON Schema for parameters | JSON Schema in YAML descriptor | Same schema language |
| Tool execution in agent loop | Tool execution via skill handlers | SDK manages the loop |

## Open Questions

- Should workspace skills support a `skills.yaml` index file (listing all skills in one file) as an alternative to one-directory-per-skill?
- Should skill descriptors support multiple handler types as fallbacks (e.g., try `delegate`, fall back to `cli`)?
- How should skill versioning work when a workspace skill and a managed skill have the same name but different versions?
- Should skills be able to declare dependencies on other skills (composite skills)?
- Should the gateway expose a skill marketplace/registry API in the future?
- How should large binary outputs from node skills (photos, recordings) be handled — inline base64, temporary file URI, or blob storage?
- Should skills support streaming output (e.g., for long-running CLI commands)?

## Future Considerations

- **Composite skills** — skills that orchestrate other skills (e.g., `daily-briefing` calls `web.search` + `memory.read`).
- **Skill marketplace** — central registry for sharing skills across the community.
- **Container sandboxing** — run untrusted CLI/script skills in isolated containers.
- **Skill analytics** — track invocation frequency, latency, and failure rates.
- **Skill testing framework** — unit test harness for skill development.
- **Hot reload** — file watcher on `skills/` for instant skill updates during development.
- **Skill permissions** — fine-grained access control (read-only skills vs. write skills vs. network skills).
