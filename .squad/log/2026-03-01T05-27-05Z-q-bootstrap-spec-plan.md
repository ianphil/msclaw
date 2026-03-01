# Implementation Plan — Bootstrap Spec v5.0

**Author:** Q (Lead / Architect)
**Date:** 2026-03-01
**Source:** `.aidocs/bootstrap-spec.md` (Rev 5.0)
**Status:** Ready for execution

---

## Resolved Decisions

### D1: Failure Behavior → **Exit with error (fail-fast)**

If no mind is found and no `--new-mind` flag is provided, the process exits with a non-zero exit code and clear usage instructions. No degraded mode, no 503. Bootstrap must succeed before Kestrel starts.

**Rationale:** A headless API server with no identity is useless. Fail-fast forces correct configuration up front. Users get a clear error message with next steps, not mysterious runtime failures.

### D2: Config Location → **`~/.msclaw/config.json` (user-global)**

Config lives in the user's home directory, not the project.

**Rationale:** A mind can serve multiple projects. Project-local config creates confusion when the same mind is referenced from different working directories. User-global config survives project moves and matches the pattern of other CLI tools (`~/.docker/`, `~/.azure/`).

### D3: Discovery Priority → **Explicit → Cached → Convention**

Search order when no CLI flag is given:
1. Cached config (`~/.msclaw/config.json` → `MindRoot`)
2. Convention search: `.` → `~/.msclaw/mind` → `~/src/miss-moneypenny`

When `--mind` or `--new-mind` is provided, skip discovery entirely.

**Rationale:** Explicit flags always win. Cached config means "last successful run" — the most common case after first setup. Convention search is the fallback for fresh installs where someone followed the README.

---

## Architecture Overview

```
CLI args
  │
  ▼
BootstrapOrchestrator
  ├── --new-mind <path> → MindScaffold.Scaffold(path) → MindValidator.Validate(path) → ConfigPersistence.Save(path) → serve
  ├── --mind <path>     → MindValidator.Validate(path) → ConfigPersistence.Save(path) → serve
  ├── --reset-config    → ConfigPersistence.Clear() → exit
  └── (no flags)        → ConfigPersistence.Load() → MindDiscovery.Discover() → MindValidator.Validate(found) → serve
                           (if nothing found → exit with error + usage)

serve:
  Program.cs starts Kestrel with resolved MindRoot
  IdentityLoader composes SOUL.md + .github/agents/*.agent.md → system message
  /chat checks for bootstrap.md → if present, agent uses it as conversation instructions
```

---

## New Interfaces

All interfaces go in `src/MsClaw/Core/`.

### IMindValidator

```csharp
// src/MsClaw/Core/IMindValidator.cs
namespace MsClaw.Core;

public interface IMindValidator
{
    MindValidationResult Validate(string mindRoot);
}
```

### IMindDiscovery

```csharp
// src/MsClaw/Core/IMindDiscovery.cs
namespace MsClaw.Core;

public interface IMindDiscovery
{
    string? Discover();
}
```

### IMindScaffold

```csharp
// src/MsClaw/Core/IMindScaffold.cs
namespace MsClaw.Core;

public interface IMindScaffold
{
    void Scaffold(string mindRoot);
}
```

### IConfigPersistence

```csharp
// src/MsClaw/Core/IConfigPersistence.cs
namespace MsClaw.Core;

public interface IConfigPersistence
{
    MsClawConfig? Load();
    void Save(MsClawConfig config);
    void Clear();
}
```

### IBootstrapOrchestrator

```csharp
// src/MsClaw/Core/IBootstrapOrchestrator.cs
namespace MsClaw.Core;

public interface IBootstrapOrchestrator
{
    BootstrapResult Run(string[] args);
}
```

### IIdentityLoader

```csharp
// src/MsClaw/Core/IIdentityLoader.cs
namespace MsClaw.Core;

public interface IIdentityLoader
{
    Task<string> LoadSystemMessageAsync(string mindRoot, CancellationToken cancellationToken = default);
}
```

---

## New Models

All models go in `src/MsClaw/Models/`.

### MindValidationResult

```csharp
// src/MsClaw/Models/MindValidationResult.cs
namespace MsClaw.Models;

public sealed class MindValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Found { get; init; } = [];
}
```

### MsClawConfig

```csharp
// src/MsClaw/Models/MsClawConfig.cs
namespace MsClaw.Models;

public sealed class MsClawConfig
{
    public string? MindRoot { get; set; }
    public DateTime? LastUsed { get; set; }
}
```

### BootstrapResult

```csharp
// src/MsClaw/Models/BootstrapResult.cs
namespace MsClaw.Models;

public sealed class BootstrapResult
{
    public required string MindRoot { get; init; }
    public bool IsNewMind { get; init; }
    public bool HasBootstrapMarker { get; init; }
}
```

---

## Validation Rules

The validator checks the mind root directory. Classification:

**Errors (invalid mind — cannot serve):**
- `SOUL.md` does not exist
- `.working-memory/` directory does not exist

**Warnings (functional but incomplete):**
- `.working-memory/memory.md` missing
- `.working-memory/rules.md` missing
- `.working-memory/log.md` missing
- `.github/agents/` directory missing
- `.github/skills/` directory missing
- `domains/` directory missing
- `initiatives/` directory missing
- `expertise/` directory missing
- `inbox/` directory missing
- `Archive/` directory missing

**Found (informational — what exists):**
- Every file/directory that was checked and exists gets listed

---

## Task Breakdown

### T1: Embedded Resources — Vesper (1h)

**No dependencies. Can start immediately.**

Update `src/MsClaw/MsClaw.csproj` to embed `Templates/*` as embedded resources:

```xml
<ItemGroup>
  <EmbeddedResource Include="Templates\**\*" />
</ItemGroup>
```

Create a static helper to read embedded resources:

**New file:** `src/MsClaw/Core/EmbeddedResources.cs`

```csharp
namespace MsClaw.Core;

internal static class EmbeddedResources
{
    public static string ReadTemplate(string fileName)
    {
        var assembly = typeof(EmbeddedResources).Assembly;
        var resourceName = $"MsClaw.Templates.{fileName}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

**Deliverable:** Templates accessible at runtime without filesystem path assumptions.

---

### T2: Models & Interfaces — Felix (2h)

**No dependencies. Can start immediately.**

Create the six interface files and three model files listed in the "New Interfaces" and "New Models" sections above. Exact file paths and signatures are specified.

**New files (9 total):**
- `src/MsClaw/Core/IMindValidator.cs`
- `src/MsClaw/Core/IMindDiscovery.cs`
- `src/MsClaw/Core/IMindScaffold.cs`
- `src/MsClaw/Core/IConfigPersistence.cs`
- `src/MsClaw/Core/IBootstrapOrchestrator.cs`
- `src/MsClaw/Core/IIdentityLoader.cs`
- `src/MsClaw/Models/MindValidationResult.cs`
- `src/MsClaw/Models/MsClawConfig.cs`
- `src/MsClaw/Models/BootstrapResult.cs`

**Deliverable:** All contracts defined. Everything else depends on these.

---

### T3: Mind Validator — Felix (3h)

**Depends on: T2**

**New file:** `src/MsClaw/Core/MindValidator.cs`

Implement `IMindValidator`. The `Validate` method:
1. Checks `mindRoot` directory exists (error if not)
2. Checks `SOUL.md` exists (error if not)
3. Checks `.working-memory/` exists (error if not)
4. Checks `.working-memory/memory.md`, `rules.md`, `log.md` (warnings if missing)
5. Checks `.github/agents/`, `.github/skills/` (warnings if missing)
6. Checks IDEA dirs: `domains/`, `initiatives/`, `expertise/`, `inbox/`, `Archive/` (warnings if missing)
7. Builds `Found` list of everything that exists
8. Returns `MindValidationResult`

All checks are synchronous filesystem operations — no async needed.

**Deliverable:** Structural validation with clear error/warning separation.

---

### T4: Config Persistence — Felix (2h)

**Depends on: T2**

**New file:** `src/MsClaw/Core/ConfigPersistence.cs`

Implement `IConfigPersistence`:
- **`Load()`**: Read `~/.msclaw/config.json`, deserialize to `MsClawConfig`, return null if file doesn't exist
- **`Save(MsClawConfig)`**: Serialize to JSON, write to `~/.msclaw/config.json`, create `~/.msclaw/` directory if needed, update `LastUsed` timestamp
- **`Clear()`**: Delete `~/.msclaw/config.json` if it exists

Use `System.Text.Json` with `JsonSerializerOptions { WriteIndented = true }`. The config file path is `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".msclaw", "config.json")`.

**Deliverable:** Persistent mind root caching across runs.

---

### T5: Mind Discovery — Vesper (2h)

**Depends on: T2, T3, T4**

**New file:** `src/MsClaw/Core/MindDiscovery.cs`

Implement `IMindDiscovery`. Constructor takes `IConfigPersistence` and `IMindValidator`.

`Discover()` method:
1. Try cached: `configPersistence.Load()?.MindRoot` → if exists and `validator.Validate()` returns valid, return it
2. Try `.` (current directory) → validate
3. Try `~/.msclaw/mind` → validate
4. Try `~/src/miss-moneypenny` → validate
5. Return null if nothing found

A candidate is valid if the directory exists AND `validator.Validate()` returns `IsValid == true`.

**Deliverable:** Convention-based mind location without user intervention.

---

### T6: Mind Scaffold — Felix (3h)

**Depends on: T1, T2**

**New file:** `src/MsClaw/Core/MindScaffold.cs`

Implement `IMindScaffold`. `Scaffold(string mindRoot)`:

1. Create `mindRoot` directory if it doesn't exist
2. Copy `SOUL.md` from embedded resource → `{mindRoot}/SOUL.md`
3. Copy `bootstrap.md` from embedded resource → `{mindRoot}/bootstrap.md`
4. Create `.working-memory/` with empty starter files:
   - `memory.md` → `"# AI Notes — Memory\n"`
   - `rules.md` → `"# AI Notes — Rules\n"`
   - `log.md` → `"# AI Notes — Log\n"`
5. Create empty directories:
   - `.github/agents/`
   - `.github/skills/`
   - `domains/`
   - `initiatives/`
   - `expertise/`
   - `inbox/`
   - `Archive/`

If `mindRoot` already exists and contains files, throw `InvalidOperationException`. Scaffold is for new minds only.

**Deliverable:** Full mind structure from templates in one call.

---

### T7: Identity Loader — Felix (2h)

**Depends on: T2**

**New file:** `src/MsClaw/Core/IdentityLoader.cs`

Implement `IIdentityLoader`. `LoadSystemMessageAsync(string mindRoot, ...)`:

1. Read `{mindRoot}/SOUL.md` → personality/voice
2. Glob `{mindRoot}/.github/agents/*.agent.md` → operational instructions
3. For each agent file, strip YAML frontmatter (reuse existing pattern from `CopilotRuntimeClient.LoadAgentInstructionsAsync`)
4. Compose: `SOUL.md content + "\n\n---\n\n" + agent file contents` (joined with `\n\n---\n\n`)
5. If no agent files exist (pre-bootstrap), return SOUL.md content only

This **replaces** the private `LoadAgentInstructionsAsync` in `CopilotRuntimeClient`. That method gets removed, and `CopilotRuntimeClient` takes `IIdentityLoader` as a dependency.

**Deliverable:** Composite system message from identity + operational instructions.

---

### T8: Bootstrap Orchestrator — Vesper (4h)

**Depends on: T3, T4, T5, T6**

**New file:** `src/MsClaw/Core/BootstrapOrchestrator.cs`

Implement `IBootstrapOrchestrator`. Constructor takes `IMindValidator`, `IMindDiscovery`, `IMindScaffold`, `IConfigPersistence`.

`Run(string[] args)`:

1. Parse CLI args (simple switch — no library needed for 3 flags):
   - `--reset-config` → `configPersistence.Clear()`, write "Config cleared." to stdout, `Environment.Exit(0)`
   - `--new-mind <path>` → resolve to absolute path, scaffold, validate, persist, return `BootstrapResult { MindRoot = path, IsNewMind = true, HasBootstrapMarker = true }`
   - `--mind <path>` → resolve to absolute path, validate, persist, return `BootstrapResult { MindRoot = path, IsNewMind = false, HasBootstrapMarker = File.Exists(bootstrap.md) }`
   - (no flags) → discover, validate, persist, return result
   - (nothing found) → write usage message to stderr, `Environment.Exit(1)`

2. Validation failure at any point → write errors to stderr, `Environment.Exit(1)`

3. On success → `configPersistence.Save(new MsClawConfig { MindRoot = resolvedPath })`

CLI parsing rules:
- `--mind` and `--new-mind` are mutually exclusive
- `--reset-config` ignores all other flags
- Unknown flags → error + usage

**Deliverable:** Single entry point that resolves the mind root before the server starts.

---

### T9: Program.cs Integration — Vesper (3h)

**Depends on: T7, T8**

Modify `src/MsClaw/Program.cs`:

1. **Before `builder.Build()`:** Call `BootstrapOrchestrator.Run(args)` to resolve mind root
2. **Replace hardcoded `MsClawOptions.MindRoot`:** Override config with resolved path from bootstrap result
3. **Register new services:** `IMindValidator`, `IMindDiscovery`, `IMindScaffold`, `IConfigPersistence`, `IBootstrapOrchestrator`, `IIdentityLoader`
4. **Modify `/chat` endpoint:** Before calling `copilotClient.GetAssistantResponseAsync`, check if `{mindRoot}/bootstrap.md` exists. If yes, read it and prepend to the system message as bootstrap instructions.
5. **Wire `IIdentityLoader` into `CopilotRuntimeClient`:** Replace the private `LoadAgentInstructionsAsync` call with `identityLoader.LoadSystemMessageAsync()`

**Key changes to `CopilotRuntimeClient`:**
- Add `IIdentityLoader` constructor parameter
- Remove private `LoadAgentInstructionsAsync` method
- Use `identityLoader.LoadSystemMessageAsync(mindRoot)` for the system message
- When `bootstrap.md` exists, append its content to the system message

**Bootstrap detection pattern in `/chat`:**
```csharp
var bootstrapPath = Path.Combine(mindRoot, "bootstrap.md");
var isBootstrapping = File.Exists(bootstrapPath);
// If bootstrapping, the identity loader already includes SOUL.md
// The bootstrap.md content gets prepended to the system message
// so the agent knows it's in bootstrap mode
```

**Deliverable:** The server starts with a validated mind root and handles bootstrap conversations.

---

### T10: Test Project Setup — Natalya (2h)

**No dependencies. Can start immediately.**

1. Create `tests/MsClaw.Tests/MsClaw.Tests.csproj` (xUnit, .NET 9)
2. Add project reference to `src/MsClaw/MsClaw.csproj`
3. Add to `MsClaw.sln`
4. Create `tests/MsClaw.Tests/TestHelpers/TempMindFixture.cs` — creates temp directories with mind structures for testing, cleans up on dispose

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="NSubstitute" Version="5.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MsClaw\MsClaw.csproj" />
  </ItemGroup>
</Project>
```

**Deliverable:** Test infrastructure ready for all unit and integration tests.

---

### T11: Unit Tests — Natalya (6h)

**Depends on: T3, T4, T5, T6, T7 (tests each component as it lands)**

Test files:
- `tests/MsClaw.Tests/Core/MindValidatorTests.cs`
- `tests/MsClaw.Tests/Core/ConfigPersistenceTests.cs`
- `tests/MsClaw.Tests/Core/MindDiscoveryTests.cs`
- `tests/MsClaw.Tests/Core/MindScaffoldTests.cs`
- `tests/MsClaw.Tests/Core/IdentityLoaderTests.cs`

**MindValidatorTests:**
- Valid mind → IsValid true, no errors
- Missing SOUL.md → error
- Missing .working-memory/ → error
- Missing optional dirs → warnings only, still valid
- Empty directory → errors for SOUL.md and .working-memory/
- Found list includes everything that exists

**ConfigPersistenceTests:**
- Save then Load round-trips correctly
- Load returns null when no file exists
- Clear removes file
- Save creates ~/.msclaw/ directory
- LastUsed is set on save

**MindDiscoveryTests (use NSubstitute for IConfigPersistence and IMindValidator):**
- Returns cached path when valid
- Skips invalid cached path, falls through to conventions
- Returns null when nothing found
- Convention order is correct (test with multiple valid locations)

**MindScaffoldTests:**
- Creates full directory structure
- SOUL.md content matches embedded resource
- bootstrap.md content matches embedded resource
- .working-memory/ files have correct headers
- Throws on existing non-empty directory

**IdentityLoaderTests:**
- SOUL.md only (no agent files) → returns SOUL.md content
- SOUL.md + agent file → composed with separator
- Agent file frontmatter stripped
- Multiple agent files all included

**Deliverable:** Full coverage of all new components.

---

### T12: Integration Tests — Natalya (4h)

**Depends on: T8, T9**

Test files:
- `tests/MsClaw.Tests/Integration/BootstrapOrchestratorTests.cs`
- `tests/MsClaw.Tests/Integration/BootstrapDetectionTests.cs`

**BootstrapOrchestratorTests:**
- `--new-mind <tmpdir>` → scaffolds, validates, persists, returns IsNewMind=true
- `--mind <valid-mind>` → validates, persists, returns IsNewMind=false
- `--mind <invalid-path>` → exits with error
- No flags + cached config → uses cached
- No flags + no config + no conventions → exits with error
- `--reset-config` → clears config and exits

**BootstrapDetectionTests (using WebApplicationFactory or manual HTTP):**
- `/chat` with bootstrap.md present → response includes bootstrap conversation behavior
- `/chat` without bootstrap.md → normal conversation behavior
- `/health` returns ok regardless of bootstrap state

**Deliverable:** Confidence that the full flow works end-to-end.

---

## Execution Schedule

### Day 1 — Foundation (parallel)

| Task | Owner | Hours | Blocks |
|------|-------|-------|--------|
| T1: Embedded Resources | Vesper | 1h | T6 |
| T2: Models & Interfaces | Felix | 2h | T3, T4, T5, T6, T7, T8 |
| T10: Test Project Setup | Natalya | 2h | T11, T12 |

All three tasks have no dependencies — start immediately, in parallel.

### Day 2 — Core Services (parallel after T2 lands)

| Task | Owner | Hours | Blocks |
|------|-------|-------|--------|
| T3: Mind Validator | Felix | 3h | T5, T8 |
| T4: Config Persistence | Felix | 2h | T5, T8 |
| T6: Mind Scaffold | Felix | 3h | T8 |
| T7: Identity Loader | Felix | 2h | T9 |
| T5: Mind Discovery | Vesper | 2h | T8 |

Felix works T3 → T4 → T6 → T7 sequentially (10h, may spill to Day 3).
Vesper works T5 after T3 and T4 land. Natalya starts writing T11 tests as each service lands.

### Day 3 — Integration

| Task | Owner | Hours | Blocks |
|------|-------|-------|--------|
| T8: Bootstrap Orchestrator | Vesper | 4h | T9 |
| T9: Program.cs Integration | Vesper | 3h | T12 |
| T11: Unit Tests (continued) | Natalya | 6h | — |

### Day 4 — Verification

| Task | Owner | Hours | Blocks |
|------|-------|-------|--------|
| T12: Integration Tests | Natalya | 4h | — |
| Bug fixes from testing | Felix + Vesper | 2h | — |

---

## Dependency Graph

```
T1 (Embedded Resources) ──────────────────────────────┐
                                                       │
T2 (Models & Interfaces) ──┬── T3 (Validator) ────┐   │
                           │                       │   │
                           ├── T4 (Config) ────────┤   │
                           │                       │   │
                           ├── T5 (Discovery) ─────┤   │
                           │   (needs T3, T4)      │   │
                           │                       │   │
                           ├── T6 (Scaffold) ──────┤───┘
                           │   (needs T1)          │
                           │                       │
                           ├── T7 (Identity) ──┐   │
                           │                   │   │
                           │                   ▼   ▼
                           │              T8 (Orchestrator)
                           │                   │
                           │                   ▼
                           │              T9 (Program.cs)
                           │                   │
T10 (Test Setup) ──────── T11 (Unit Tests) ── T12 (Integration)
```

---

## Files Changed (Summary)

### New Files (19)

| File | Owner | Task |
|------|-------|------|
| `src/MsClaw/Core/IMindValidator.cs` | Felix | T2 |
| `src/MsClaw/Core/IMindDiscovery.cs` | Felix | T2 |
| `src/MsClaw/Core/IMindScaffold.cs` | Felix | T2 |
| `src/MsClaw/Core/IConfigPersistence.cs` | Felix | T2 |
| `src/MsClaw/Core/IBootstrapOrchestrator.cs` | Felix | T2 |
| `src/MsClaw/Core/IIdentityLoader.cs` | Felix | T2 |
| `src/MsClaw/Models/MindValidationResult.cs` | Felix | T2 |
| `src/MsClaw/Models/MsClawConfig.cs` | Felix | T2 |
| `src/MsClaw/Models/BootstrapResult.cs` | Felix | T2 |
| `src/MsClaw/Core/EmbeddedResources.cs` | Vesper | T1 |
| `src/MsClaw/Core/MindValidator.cs` | Felix | T3 |
| `src/MsClaw/Core/ConfigPersistence.cs` | Felix | T4 |
| `src/MsClaw/Core/MindDiscovery.cs` | Vesper | T5 |
| `src/MsClaw/Core/MindScaffold.cs` | Felix | T6 |
| `src/MsClaw/Core/IdentityLoader.cs` | Felix | T7 |
| `src/MsClaw/Core/BootstrapOrchestrator.cs` | Vesper | T8 |
| `tests/MsClaw.Tests/MsClaw.Tests.csproj` | Natalya | T10 |
| `tests/MsClaw.Tests/TestHelpers/TempMindFixture.cs` | Natalya | T10 |
| `tests/MsClaw.Tests/Core/*.cs` (5 test files) | Natalya | T11 |
| `tests/MsClaw.Tests/Integration/*.cs` (2 test files) | Natalya | T12 |

### Modified Files (3)

| File | Owner | Task | Change |
|------|-------|------|--------|
| `src/MsClaw/MsClaw.csproj` | Vesper | T1 | Add `<EmbeddedResource>` for Templates |
| `src/MsClaw/Program.cs` | Vesper | T9 | Wire orchestrator, identity loader, bootstrap detection |
| `src/MsClaw/Core/CopilotRuntimeClient.cs` | Felix | T7 | Remove `LoadAgentInstructionsAsync`, inject `IIdentityLoader` |
| `MsClaw.sln` | Natalya | T10 | Add test project |

---

## Team Directives

1. **SOUL.md template is verbatim.** Do not modify `src/MsClaw/Templates/SOUL.md`. It's an OpenClaw reference pinned to commit `0f72000c`. The scaffold copies it as-is.

2. **bootstrap.md template is verbatim.** Do not modify `src/MsClaw/Templates/bootstrap.md`. It drives the 3-phase bootstrap conversation. The scaffold copies it as-is.

3. **`.working-memory/` is canonical.** Not `.ainotes/`, not `.ai-notes/`. The roadmap naming is authoritative per Ian's directive (2026-03-01T03:52:41Z).

4. **No interactive prompts at startup.** The server is headless. Bootstrap is an LLM conversation through `/chat`, not a CLI wizard.

5. **Fail fast on validation errors.** Warnings are logged but don't prevent startup. Errors terminate the process.

6. **Tests use temp directories.** Never modify real filesystem locations. `TempMindFixture` creates and cleans up test mind structures.
