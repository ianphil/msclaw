---
title: "MSAL silent token refresh"
status: open
priority: high
created: 2026-03-08
---

# MSAL Silent Token Refresh

## Summary

Persist MSAL's token cache to disk and add background silent refresh so the gateway keeps a valid access token without requiring restart + `msclaw auth login`.

## Motivation

Today `LoginCommand` creates a throwaway `PublicClientApplication` — the refresh token MSAL receives (via `offline_access` scope) is discarded. Access tokens expire in ~1 hour, forcing the user to stop the gateway, re-login, and restart. This is especially painful over dev tunnels from a phone.

## Proposal

### Goals

- Persist MSAL token cache so `AcquireTokenSilent` works across process restarts
- Silently refresh the access token in the running gateway before expiry
- Update `config.json` with refreshed tokens so the browser UI stays authenticated
- Fall back gracefully — if silent refresh fails, log a warning (don't crash)

### Non-Goals

- Changing the Entra app registration or scopes
- Adding a web-based login flow (interactive browser login stays as-is)
- Multi-user / multi-account support

## Design

**Shared MSAL app builder** — Extract `PublicClientApplication` construction into a shared helper (e.g., `MsalAppFactory`) that both `LoginCommand` and the gateway can use with the same authority/clientId/redirect.

**Persistent token cache** — Attach a file-based token cache at `~/.msclaw/msal-cache.bin` using MSAL's `SetBeforeAccessAsync`/`SetAfterAccessAsync` hooks. This preserves refresh tokens across process restarts.

**`LoginCommand` update** — Use the shared app builder. After interactive login, the cache is automatically persisted. No other changes needed.

**Background refresh service** — Add a `TokenRefreshService : BackgroundService` in the gateway that wakes up ~5 minutes before token expiry, calls `AcquireTokenSilent`, and writes the new access token + expiry to `config.json` via `IUserConfigLoader`. On failure, log and retry with backoff.

**Browser UI token push** — After refresh, push the new token to connected SignalR clients so `window.__AUTH_CONTEXT` stays current without page reload.

## Tasks

- [ ] Create `MsalAppFactory` helper with persistent file-based token cache
- [ ] Update `LoginCommand` / `MsalInteractiveBrowserAuthenticator` to use shared app builder
- [ ] Add `TokenRefreshService` background service in gateway
- [ ] Push refreshed token to connected SignalR clients via hub
- [ ] Update `UserAuthConfig` in `config.json` on each silent refresh
- [ ] Add unit tests for refresh service and cache integration

## Open Questions

- Should the cache file be encrypted with DPAPI on Windows (MSAL extensions library), or is plaintext acceptable for a local-only dev tool?
