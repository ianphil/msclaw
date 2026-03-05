# Data Model: Gateway MVP Plumbing

## Entities

### GatewayOptions

Configuration POCO bound from CLI arguments and appsettings.json. Represents the gateway's runtime configuration.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| MindPath | string | Yes | — | Absolute path to the mind directory |
| Host | string | No | "127.0.0.1" | IP address or hostname to bind to |
| Port | int | No | 18789 | TCP port to listen on |

**Invariants:**
- MindPath MUST be a non-empty, valid filesystem path
- Port MUST be between 1 and 65535
- Host MUST be a valid IP address or hostname

### GatewayReadiness

Represents the readiness state of the gateway, managed by `GatewayHostedService`.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| State | GatewayState (enum) | Yes | Starting | Current lifecycle state |
| Error | string? | No | null | Error message if state is Failed |

**Relationships:**
- Queried by `/healthz` endpoint
- Owned by GatewayHostedService

**Invariants:**
- Error MUST be non-null when State is Failed
- Error MUST be null when State is Ready

### MindValidationResult (existing in MsClaw.Core)

Already defined in MsClaw.Core. Used as-is by the gateway.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| Errors | IReadOnlyList\<string\> | Yes | empty | Validation errors (missing required files) |
| Warnings | IReadOnlyList\<string\> | Yes | empty | Validation warnings (missing optional files) |
| Found | IReadOnlyList\<string\> | Yes | empty | Successfully validated items |

## State Transitions

### Gateway Lifecycle

```
         ┌───────────┐
         │  Starting  │
         └─────┬─────┘
               │
        ┌──────▼──────┐
        │  Validating  │
        └──────┬──────┘
               │
          ┌────┴────┐
          │         │
    ┌─────▼───┐  ┌──▼─────┐
    │  Ready  │  │ Failed │
    └─────┬───┘  └────────┘
          │
    ┌─────▼──────┐
    │  Stopping  │
    └─────┬──────┘
          │
    ┌─────▼──────┐
    │  Stopped   │
    └────────────┘
```

| State | Description |
|-------|-------------|
| Starting | Host is booting, hosted service not yet called |
| Validating | GatewayHostedService is validating mind + loading identity + starting client |
| Ready | CopilotClient is started, health returns 200 |
| Failed | Validation or client startup failed, health returns 503, process stays alive |
| Stopping | Shutdown signal received, disposing CopilotClient |
| Stopped | All resources disposed |

## Data Flow

### Startup Sequence

```
Program.cs
  │ Parse CLI args
  ▼
StartCommand.Execute()
  │ Build WebApplication
  │ Register DI services
  │ Configure Kestrel
  │ Map endpoints
  ▼
WebApplication.RunAsync()
  │
  ├──► GatewayHostedService.StartAsync()
  │      │
  │      ├─ IMindValidator.Validate(MindPath)
  │      │    ├─ Errors? → State=Failed, log
  │      │    └─ OK? → continue
  │      │
  │      ├─ IIdentityLoader.LoadSystemMessageAsync(MindPath)
  │      │    └─ Store system message
  │      │
  │      ├─ MsClawClientFactory.Create(MindPath)
  │      │    └─ CopilotClient created
  │      │
  │      ├─ CopilotClient.StartAsync()
  │      │    ├─ Failure? → State=Failed, log
  │      │    └─ OK? → State=Ready
  │      │
  │      └─ return
  │
  ├──► /healthz ready to serve
  └──► /gateway (SignalR) ready to serve
```

### Health Check Flow

```
HTTP GET /healthz
  │
  ├─ Query GatewayHostedService.State
  │
  ├─ State == Ready?
  │    └─ 200 { "status": "Healthy" }
  │
  └─ State != Ready?
       └─ 503 { "status": "Unhealthy", "error": "..." }
```

## Validation Summary

| Entity | Rule | Error |
|--------|------|-------|
| GatewayOptions | MindPath must not be empty | ArgumentException |
| GatewayOptions | Port must be 1-65535 | ArgumentOutOfRangeException |
| GatewayOptions | MindPath must exist on disk | DirectoryNotFoundException |
| GatewayReadiness | State=Failed requires Error message | InvalidOperationException |
