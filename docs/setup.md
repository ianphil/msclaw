# Setup Guide

Prerequisites and installation for developing with MsClaw.

## Prerequisites

| Tool | Install | Purpose |
|------|---------|---------|
| .NET 10 SDK | `winget install Microsoft.DotNet.SDK.10` | Build and run the gateway |
| Dev Tunnel CLI | `winget install Microsoft.devtunnel` | Remote access via `--tunnel` flag |
| Node.js | `winget install OpenJS.NodeJS.LTS` | Required for MCPorter (Teams integration) |
| Agency CLI | `iex "& { $(irm aka.ms/InstallTool.ps1)} agency"` | MCP server management |

After installing Dev Tunnel CLI, authenticate:

```pwsh
devtunnel user login
```

## Install MsClaw

```pwsh
dotnet tool install --global MsClaw --version 0.7.2
```

## Scaffold and Start a Mind

```pwsh
# Create a new mind directory
msclaw mind scaffold .\my-mind

# Validate the structure
msclaw mind validate .\my-mind

# Authenticate (device code flow — opens browser)
msclaw auth login

# Start the gateway
msclaw start --mind .\my-mind
```

Open [http://127.0.0.1:18789](http://127.0.0.1:18789) in your browser to chat.

## Build from Source

```pwsh
git clone https://github.com/ianphil/msclaw.git
cd msclaw
dotnet build src/MsClaw.slnx --nologo
```

The binary lands at `src/MsClaw.Gateway/bin/Debug/net10.0/msclaw`.

## Teams Integration (Optional)

To connect your agent to Microsoft Teams via MCPorter, see [Bootstrap Teams](bootstrap-teams.md).

```pwsh
# Verify MCPorter is available
npx mcporter list
```

## Next Steps

- [Gateway Quickstart](gateway-quickstart.md) — Full manual testing walkthrough
- [Tools Developer Guide](tools-dev-guide.md) — Build custom tool providers
- [Bootstrap Flows](bootstrap-mind-flow.md) — How minds start up