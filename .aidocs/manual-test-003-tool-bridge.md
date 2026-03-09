# Manual E2E Test — Tool Bridge (Feature 003)

## Prerequisites

- `copilot` CLI installed and on PATH
- A valid mind directory (use `~/src/ernist` for testing)
- .NET 10 SDK installed
- Authenticated with GitHub (`gh auth login`)

## Start the Gateway

```powershell
dotnet run --project src\MsClaw.Gateway -- start --mind C:\src\ernist
```

Verify the banner prints with all endpoints:

```
MSCLAW GATEWAY READY
  UI (browser)      http://127.0.0.1:18789/
  SignalR Hub       http://127.0.0.1:18789/gateway
  Health            http://127.0.0.1:18789/health
```

## Test 1 — Health & Readiness

```powershell
curl http://127.0.0.1:18789/health
curl http://127.0.0.1:18789/health/ready
```

**Expected:** Both return 200 OK.

## Test 2 — Session Gets expand_tools

Open `http://127.0.0.1:18789/` in a browser and send a message:

> What tools do you have? List every tool name you can see.

**Expected:** The response lists `expand_tools` among the available tools. This confirms `AgentMessageService` wired the tool into `SessionConfig.Tools`.

## Test 3 — expand_tools Query Mode (Empty Catalog)

Send:

> Use the expand_tools tool in query mode to search for "file" tools.

**Expected:** The agent calls `expand_tools(query: "file")` and reports zero matches. With no providers registered, the catalog is empty — this is correct baseline behavior.

## Test 4 — expand_tools Load Mode (No Tools to Load)

Send:

> Use expand_tools to load a tool called "read_file".

**Expected:** The agent calls `expand_tools(names: ["read_file"])` and reports the tool was skipped (not found in catalog). No crash, no unhandled error.

## Test 5 — Multi-Turn Session Persistence

Continue in the same browser tab:

> What was the first thing I asked you?

**Expected:** The agent recalls your earlier messages. This confirms the session is persistent across turns via `SessionPool` keyed by SignalR connection ID.

## Test 6 — Abort In-Flight Response

1. Send a long prompt: "Write a 2000-word essay about software architecture."
2. While the response is streaming, click the stop/abort button in the UI (or call `AbortResponse` via SignalR).

**Expected:** The stream stops. The next message you send works normally — the concurrency gate is released.

## Test 7 — ToolBridgeHostedService Startup (Logs)

Check the gateway console output at startup for log lines like:

```
Tool bridge hosted service starting
Registered 0 tool providers
```

**Expected:** The hosted service starts cleanly even with zero providers. No exceptions in the log.

---

## Testing with a Custom Provider (Advanced)

To fully test query→load flow, create a trivial `IToolProvider` and register it in DI.

### 1. Add a Test Provider

Create `src/MsClaw.Gateway/Services/Tools/Providers/EchoToolProvider.cs`:

```csharp
using Microsoft.Extensions.AI;

namespace MsClaw.Core;

public sealed class EchoToolProvider : IToolProvider
{
    public string Name => "echo";
    public ToolSourceTier Tier => ToolSourceTier.Workspace;

    public Task<IReadOnlyList<ToolDescriptor>> DiscoverAsync(CancellationToken ct)
    {
        var fn = AIFunctionFactory.Create(
            (string text) => $"Echo: {text}",
            "echo_text",
            "Echoes the input text back");

        ToolDescriptor descriptor = new(fn, Name, Tier, AlwaysVisible: false);
        return Task.FromResult<IReadOnlyList<ToolDescriptor>>([descriptor]);
    }

    public Task WaitForSurfaceChangeAsync(CancellationToken ct)
    {
        return Task.Delay(Timeout.Infinite, ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

### 2. Register It

In `GatewayServiceExtensions.cs`, add after the existing tool bridge registrations:

```csharp
services.AddSingleton<IToolProvider, EchoToolProvider>();
```

### 3. Rebuild and Start

```powershell
dotnet run --project src\MsClaw.Gateway -- start --mind ~/src/ernist
```

### 4. Test the Full Flow

**Query:**

> Use expand_tools to search for "echo".

**Expected:** Returns `["echo_text"]` with count 1.

**Load:**

> Use expand_tools to load "echo_text".

**Expected:** Returns `{ enabled: ["echo_text"], skipped: [], count: 1 }`.

**Use:**

> Use the echo_text tool with the text "hello world".

**Expected:** Returns `"Echo: hello world"`.

### 5. Clean Up

Remove `EchoToolProvider.cs` and the DI registration when done — this is test-only code.

---

## What's NOT Testable Yet

| Area | Why |
|------|-----|
| Provider surface refresh | No real providers emit surface change signals yet |
| Tier collision resolution | Requires two providers registering same-named tool |
| Spec tests | Run `specs\tests\Invoke-SpecTests.ps1 specs\tests\003-tool-bridge.md` when copilot CLI is on PATH |
