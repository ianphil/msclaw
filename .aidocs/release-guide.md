# MsClaw — Release & Publishing Guide

MsClaw ships as a .NET global tool via NuGet. Two workflows handle publishing: one for stable releases (nuget.org) and one for insider builds (GitHub Packages).

---

## How It Works

MsClaw's `.csproj` includes `PackAsTool=true`, which tells `dotnet pack` to produce a NuGet package that installs as a CLI tool. Users install it with:

```bash
dotnet tool install -g MsClaw
msclaw --mind ~/src/ernist
```

---

## Stable Release (`squad-release.yml`)

**Trigger:** Push a tag matching `v*` (e.g., `v0.3.0`).

**What it does:**
1. Checks out the repo
2. Sets up .NET 9
3. Runs `dotnet test` in Release configuration
4. Packs `MsClaw.csproj` into a `.nupkg`
5. Pushes the package to **nuget.org** using `NUGET_API_KEY` secret
6. Creates a GitHub release with auto-generated notes

**How to trigger a release:**

```bash
git tag v0.3.0
git push --tags
```

**Requirements:**
- `NUGET_API_KEY` repo secret — a nuget.org API key scoped to `MsClaw*`

### Versioning

The `<Version>` in `src/MsClaw/MsClaw.csproj` is the package version. Update it before tagging:

```xml
<Version>0.4.0</Version>
```

Then tag and push. The tag name and package version should match (e.g., tag `v0.4.0` → package version `0.4.0`).

---

## Insider Release (`squad-insider-release.yml`)

**Trigger:** Push to the `insider` branch.

**What it does:**
1. Checks out the repo
2. Sets up .NET 9
3. Generates a timestamped pre-release version (e.g., `0.3.0-insider.20260302054500`)
4. Runs `dotnet test` in Release configuration
5. Packs with the pre-release version
6. Pushes to **GitHub Packages** using the built-in `GITHUB_TOKEN`

**How to trigger:**

```bash
git push origin insider
```

**No extra secrets needed** — `GITHUB_TOKEN` has `packages:write` permission.

### Installing insider builds

Users must add the GitHub Packages feed first:

```bash
dotnet nuget add source https://nuget.pkg.github.com/ianphil/index.json \
  --name msclaw-insider --username USERNAME --password GH_PAT
dotnet tool install -g MsClaw --prerelease
```

---

## Local Testing

Build and install the tool locally without publishing:

```bash
# Pack
dotnet pack src/MsClaw/MsClaw.csproj -c Release -o ./nupkg

# Install from local package
dotnet tool install -g --add-source ./nupkg MsClaw

# Test it
msclaw --mind ~/src/ernist

# Uninstall when done
dotnet tool uninstall -g MsClaw
```

The `nupkg/` directory is in `.gitignore`.

---

## Secrets Reference

| Secret | Where | Purpose |
|---|---|---|
| `NUGET_API_KEY` | GitHub repo secrets | Push to nuget.org (glob: `MsClaw*`) |
| `GITHUB_TOKEN` | Built-in | Push to GitHub Packages (insider) |

---

## Connections

- **dotnet-tool-spec.md** — Original design spec for the global tool packaging
- **roadmap.md** — Phase 2 (Extension System) enables `msclaw extension install` commands
- **squad-release.yml** / **squad-insider-release.yml** — The workflow files themselves
