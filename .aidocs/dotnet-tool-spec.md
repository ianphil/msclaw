# MsClaw as a .NET Global Tool — Specification

## Problem

MsClaw currently runs via `dotnet run` from a cloned repo. Users must clone the source, build it, and run from the project directory. This is fine for development but not for distribution. There's no install story, no versioning from the user's perspective, and no clean CLI entry point.

## Goal

Ship MsClaw as a .NET global tool so users install and run it like any other CLI:

```bash
dotnet tool install -g msclaw
msclaw --mind ~/src/ernist
```

One command to install. `msclaw` on the PATH. Updates via `dotnet tool update -g msclaw`. The same packaging infrastructure used for extension distribution (NuGet) also distributes the tool itself.

---

## What is a .NET Global Tool?

A .NET global tool is a NuGet package containing a console application that gets installed to `~/.dotnet/tools/` and added to the user's PATH. The `dotnet tool` commands manage installation, updates, and removal.

```bash
dotnet tool install -g msclaw          # install
dotnet tool update -g msclaw           # update to latest
dotnet tool uninstall -g msclaw        # remove
dotnet tool list -g                    # list installed tools
```

The tool is self-contained — no need to clone the repo, no `dotnet run`, no build step.

---

## Project Changes

### .csproj Modifications

The `MsClaw.csproj` needs tool packaging properties:

```xml
<PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <!-- Global tool packaging -->
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>msclaw</ToolCommandName>
    <PackageId>MsClaw</PackageId>
    <Version>0.1.0</Version>
    <Authors>Ian</Authors>
    <Description>MsClaw — an agentic runtime for AI minds</Description>
    <PackageProjectUrl>https://github.com/yourusername/msclaw</PackageProjectUrl>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageReadmeFile>README.md</PackageReadmeFile>
</PropertyGroup>
```

Key properties:
- `PackAsTool` — tells the SDK to produce a tool package
- `ToolCommandName` — the command name on PATH (`msclaw`)
- `PackageId` — the NuGet package name users install

### Output Type

MsClaw is currently an ASP.NET Core web app (`Microsoft.NET.Sdk.Web`), which produces an executable. This is compatible with `PackAsTool` — the tool IS the web server. When you run `msclaw`, it starts Kestrel and listens for requests.

---

## CLI Surface

Today MsClaw has `--mind`, `--new-mind`, and `--reset-config`. As a global tool, the full CLI surface becomes:

### Core Commands

```
msclaw                                  # Start with last-used mind (from config)
msclaw --mind <path>                    # Start with explicit mind
msclaw --new-mind <path>                # Scaffold new mind and start
msclaw --reset-config                   # Clear saved config
```

### Extension Commands (Phase 2)

```
msclaw extension install <package-id> [--version <ver>]
msclaw extension uninstall <extension-id>
msclaw extension list
msclaw extension update [<extension-id>]
msclaw extension restore
```

### Server Options

```
msclaw --port <port>                    # Override listen port (default: 5000)
msclaw --model <model-id>              # Override model
msclaw --verbose                        # Debug logging
```

### Info Commands

```
msclaw --version                        # Print version
msclaw --help                           # Usage info
msclaw info                             # Show resolved mind, loaded extensions, config paths
```

The existing `--mind` / `--new-mind` args already work this way. The shift is just that `dotnet run --` becomes `msclaw`.

---

## Configuration Hierarchy

With a global tool, config resolution becomes important. Precedence (highest to lowest):

1. **CLI arguments** — `--mind`, `--port`, `--model` (always win)
2. **Environment variables** — `MSCLAW_MIND_ROOT`, `MSCLAW_PORT`, `MSCLAW_MODEL`
3. **User config** — `~/.msclaw/config.json` (persisted by `ConfigPersistence`)
4. **App defaults** — `appsettings.json` embedded in the tool

Today only #1 and #3 exist. Adding #2 is cheap (standard .NET configuration) and useful for containers, CI, and scripted setups.

---

## Packaging and Publishing

### Build the Package

```bash
dotnet pack src/MsClaw/MsClaw.csproj -c Release -o ./nupkg
```

Produces `MsClaw.0.1.0.nupkg`.

### Local Testing

```bash
dotnet tool install -g --add-source ./nupkg MsClaw
msclaw --mind ~/src/ernist
```

### Publish to NuGet

```bash
dotnet nuget push ./nupkg/MsClaw.0.1.0.nupkg --api-key <key> --source https://api.nuget.org/v3/index.json
```

After publishing:

```bash
dotnet tool install -g MsClaw
```

### GitHub Packages (Alternative)

For pre-release / insider distribution:

```bash
dotnet nuget push ./nupkg/MsClaw.0.1.0.nupkg --source https://nuget.pkg.github.com/OWNER/index.json --api-key <ghp_token>
```

Users add the feed:

```bash
dotnet nuget add source https://nuget.pkg.github.com/OWNER/index.json --name msclaw-insider --username USER --password <ghp_token>
dotnet tool install -g MsClaw --prerelease
```

---

## ASP.NET Web SDK Considerations

A .NET global tool is typically a plain console app. MsClaw uses `Microsoft.NET.Sdk.Web` because it's a Kestrel web server. This works with `PackAsTool` but has implications:

1. **No `appsettings.json` file resolution** — Global tools run from `~/.dotnet/tools/`, not the project directory. `appsettings.json` must be embedded as a resource or config must come from `~/.msclaw/config.json` and CLI args. The current `appsettings.json` approach needs to shift to embedded defaults + user config.

2. **No `launchSettings.json`** — Launch profiles don't apply to installed tools. Port and URL configuration comes from CLI args or config.

3. **Working directory** — The tool runs in whatever directory the user invokes it from. Mind root must always be an absolute resolved path (already the case today via `Path.GetFullPath()`).

4. **Self-contained vs framework-dependent** — Framework-dependent (default) requires the user to have .NET 9 installed. Self-contained bundles the runtime but produces a larger package. Start framework-dependent; offer self-contained as an option later.

---

## Migration Path

The transition from `dotnet run` to `msclaw` is non-breaking:

| Phase | How you run it | How you install it |
|-------|---------------|-------------------|
| Today | `cd src/MsClaw && dotnet run -- --mind ~/src/ernist` | Clone repo, build |
| After | `msclaw --mind ~/src/ernist` | `dotnet tool install -g MsClaw` |
| Both work | `dotnet run` still works from the repo for development | Tool install for usage |

Developers use the repo. Users use the tool. Both exercise the same binary.

---

## Versioning Strategy

The tool version tracks MsClaw releases:

- `0.x.y` — pre-1.0, breaking changes expected
- `1.0.0` — stable extension API, stable CLI surface
- SemVer from there

The `Version` property in `.csproj` is the single source of truth. CI sets it from tags or build numbers.

---

## Connections

- **Phase 2 (Extension System)** — `msclaw extension install` commands require the tool to be on PATH. The extension NuGet distribution story and the tool distribution story use the same infrastructure.
- **Phase 3 (Gateway)** — `msclaw` as a long-running service (Telegram bot, etc.) benefits from being a proper installed tool rather than `dotnet run` from a dev directory.
- **CI/CD** — Publish the tool package from CI. GitHub Actions: `dotnet pack` → `dotnet nuget push` on tagged releases.

## Open Questions

1. **Package name** — `MsClaw`, `msclaw`, or `MsClaw.Tool`? NuGet convention is PascalCase, but the command name is lowercase. `MsClaw` (package) → `msclaw` (command) feels right.
2. **Self-contained builds** — Should we offer platform-specific self-contained packages (no .NET install required)? Bigger package but removes the .NET 9 prerequisite. Relevant for the always-on hosting story in Phase 3.
3. **Update notifications** — Should `msclaw` check for updates on startup and notify the user? Low priority but nice UX.
4. **Local tool manifest** — Should we also support `dotnet tool install MsClaw` (local, not global) via a `.config/dotnet-tools.json` manifest? Useful for teams that want pinned versions per-repo.
