---
target:
  - src/MsClaw.Gateway/MsClaw.Gateway.csproj
  - src/MsClaw.Gateway/Program.cs
  - src/MsClaw.Gateway/GatewayOptions.cs
  - src/MsClaw.Gateway/Hosting/GatewayHostedService.cs
  - src/MsClaw.Gateway/Commands/StartCommand.cs
  - src/MsClaw.Gateway/Commands/Mind/ValidateCommand.cs
  - src/MsClaw.Gateway/Commands/Mind/ScaffoldCommand.cs
  - src/MsClaw.Gateway/Hubs/GatewayHub.cs
---

# Gateway MVP Plumbing Spec Tests

Validates that the MsClaw.Gateway project provides the hosting chassis for the gateway: CLI command tree, ASP.NET Core daemon, CopilotClient lifecycle, health probe, and SignalR hub.

## Project Structure

### Project exists and references required packages

The gateway cannot function without the correct project type and dependencies. A missing reference means no CLI, no hosting, or no rendering.

```
Given the src/MsClaw.Gateway/MsClaw.Gateway.csproj file
When examining the project configuration
Then it targets net10.0
And it references MsClaw.Core as a project reference
And it has a PackageReference to System.CommandLine
And it has a PackageReference to Spectre.Console
And the OutputType is Exe
```

### Project produces the msclaw binary

The binary name is the operator's CLI entry point. If it's wrong, no documented command works.

```
Given the src/MsClaw.Gateway/MsClaw.Gateway.csproj file
When examining the assembly name or tool name configuration
Then the output binary is named msclaw
```

## CLI Command Tree

### Root command registers start and mind subcommands

Without the command tree, the operator has no way to interact with the gateway. Every documented command must be wired.

```
Given the src/MsClaw.Gateway/Program.cs file
When examining the command registration
Then a root command is defined using System.CommandLine
And a "start" command is registered as a subcommand
And a "mind" command is registered with "validate" and "scaffold" subcommands
```

### Start command accepts --mind and --new-mind options

The operator must specify which mind to host. Without these options, the gateway cannot be pointed at a mind directory.

```
Given the src/MsClaw.Gateway/Commands/StartCommand.cs file
When examining the command definition
Then it defines a --mind option that accepts a path
And it defines a --new-mind option that accepts a path
```

### Mind validate command accepts a path argument

Mind authors need to validate their work from the CLI without starting the full gateway.

```
Given the src/MsClaw.Gateway/Commands/Mind/ValidateCommand.cs file
When examining the command definition
Then it defines a path argument for the mind directory
And it calls IMindValidator.Validate with the provided path
```

### Mind scaffold command accepts a path argument

Mind authors need to create new minds from the CLI with correct directory structure.

```
Given the src/MsClaw.Gateway/Commands/Mind/ScaffoldCommand.cs file
When examining the command definition
Then it defines a path argument for the mind directory
And it calls IMindScaffold.Scaffold with the provided path
```

## Configuration

### GatewayOptions defines required configuration fields

Without proper configuration, the gateway cannot know which mind to host or where to listen.

```
Given the src/MsClaw.Gateway/GatewayOptions.cs file
When examining the GatewayOptions class
Then it has a MindPath property of type string
And it has a Host property of type string with default "127.0.0.1"
And it has a Port property of type int with default 18789
```

## Hosted Service

### GatewayHostedService manages the CopilotClient lifecycle

If the hosted service doesn't validate the mind and start the client, the gateway is an empty shell with no agent capability.

```
Given the src/MsClaw.Gateway/Hosting/GatewayHostedService.cs file
When examining the class definition
Then it implements IHostedService
And it depends on IMindValidator and IIdentityLoader via constructor injection
And it has a method or property indicating readiness state
```

### GatewayHostedService validates mind before starting client

An invalid mind must be caught at startup. If validation is skipped, the client starts against a broken mind and produces confusing errors.

```
Given the src/MsClaw.Gateway/Hosting/GatewayHostedService.cs file
When examining the StartAsync method
Then it calls IMindValidator.Validate before creating or starting the CopilotClient
And if validation returns errors, it sets the state to failed without starting the client
```

### GatewayHostedService exposes readiness state

The health endpoint needs a way to query whether the gateway is ready. Without this, the health probe cannot distinguish startup from failure.

```
Given the src/MsClaw.Gateway/Hosting/GatewayHostedService.cs file
When examining the public API
Then it exposes a State or IsReady property
And the property reflects whether the CopilotClient started successfully
```

## Endpoints

### Health endpoint is mapped and queries readiness

Operators and orchestrators need a reliable way to check if the gateway is ready. Without a health endpoint, there is no observability.

```
Given the src/MsClaw.Gateway/Commands/StartCommand.cs file
When examining the endpoint mapping
Then a /healthz endpoint is mapped
And it returns 200 when the hosted service indicates ready
And it returns 503 when the hosted service indicates not ready
```

### GatewayHub extends SignalR Hub

Future epics need a real-time communication hub class. Without a Hub subclass, there is nothing to map to a route.

```
Given the src/MsClaw.Gateway/Hubs/GatewayHub.cs file
When examining the class definition
Then it extends Hub from Microsoft.AspNetCore.SignalR
```

### SignalR hub is mapped at /gateway route

The hub must be wired into the ASP.NET Core pipeline at a known route. Without the mapping, clients cannot connect to the real-time endpoint.

```
Given the src/MsClaw.Gateway/Commands/StartCommand.cs file
When examining the endpoint mapping
Then it maps GatewayHub at the /gateway route
```

## DI Registration

### Start command registers MsClaw.Core services

The hosted service and commands depend on MsClaw.Core interfaces. Without DI registration, constructor injection fails at runtime.

```
Given the src/MsClaw.Gateway/Commands/StartCommand.cs file
When examining the service registration
Then IMindValidator is registered in the DI container
And IIdentityLoader is registered in the DI container
And IMindScaffold is registered in the DI container
And GatewayHostedService is registered as a hosted service
```
