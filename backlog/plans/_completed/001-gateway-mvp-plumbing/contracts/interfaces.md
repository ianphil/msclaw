# Gateway MVP Plumbing — Interface Contracts

## GatewayOptions

```csharp
/// <summary>
/// Configuration for the MsClaw Gateway daemon.
/// Bound from CLI arguments and appsettings.json.
/// </summary>
public sealed class GatewayOptions
{
    /// <summary>Path to the mind directory.</summary>
    public required string MindPath { get; set; }

    /// <summary>Host address to bind to. Default: 127.0.0.1.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>Port to listen on. Default: 18789.</summary>
    public int Port { get; set; } = 18789;
}
```

## GatewayState Enum

```csharp
/// <summary>
/// Represents the lifecycle state of the gateway.
/// </summary>
public enum GatewayState
{
    Starting,
    Validating,
    Ready,
    Failed,
    Stopping,
    Stopped
}
```

## IGatewayHostedService

```csharp
/// <summary>
/// Manages the CopilotClient lifecycle within the ASP.NET Core host.
/// Validates the mind, loads identity, starts the client, and signals readiness.
/// </summary>
public interface IGatewayHostedService : IHostedService
{
    /// <summary>Current lifecycle state of the gateway.</summary>
    GatewayState State { get; }

    /// <summary>Error message when State is Failed; null otherwise.</summary>
    string? Error { get; }

    /// <summary>Whether the gateway is fully initialized and ready to serve.</summary>
    bool IsReady { get; }
}
```

## DI Registration Contract

The `StartCommand` registers the following services:

```csharp
// MsClaw.Core services (interface → concrete)
services.AddSingleton<IMindValidator, MindValidator>();
services.AddSingleton<IMindScaffold, MindScaffold>();
services.AddSingleton<IIdentityLoader, IdentityLoader>();
services.AddSingleton<IMindReader, MindReader>();

// Gateway configuration
services.Configure<GatewayOptions>(configuration.GetSection("Gateway"));

// Gateway hosted service
services.AddSingleton<GatewayHostedService>();
services.AddSingleton<IGatewayHostedService>(sp => sp.GetRequiredService<GatewayHostedService>());
services.AddHostedService(sp => sp.GetRequiredService<GatewayHostedService>());
```

## Health Endpoint Contract

```
GET /healthz

Response (200 OK):
{ "status": "Healthy" }

Response (503 Service Unavailable):
{ "status": "Unhealthy", "error": "Mind validation failed: SOUL.md not found" }
```

## SignalR Hub Contract

```csharp
/// <summary>
/// Real-time communication hub for the gateway.
/// Methods will be added by EPIC-03 (Gateway Protocol).
/// </summary>
public sealed class GatewayHub : Hub
{
    // Empty — methods added by future epics
}
```

Mapped at: `/gateway`

## CLI Command Contracts

```
msclaw start --mind <path> [--new-mind] [--host <address>] [--port <number>]
msclaw mind validate <path>
msclaw mind scaffold <path>
```

| Command | Exit Code | Meaning |
|---------|-----------|---------|
| `msclaw start --mind <path>` | 0 | Gateway shut down gracefully |
| `msclaw start --mind <path>` | 1 | Startup failed (invalid mind, CLI missing) |
| `msclaw mind validate <path>` | 0 | Mind is valid (may have warnings) |
| `msclaw mind validate <path>` | 1 | Mind has validation errors |
| `msclaw mind scaffold <path>` | 0 | Mind scaffolded successfully |
| `msclaw mind scaffold <path>` | 1 | Scaffold failed |
