---
title: "Inject auth token into index.html at serve time"
status: open
priority: high
created: 2026-03-08
---

# Inject Auth Token into index.html at Serve Time

## Summary

Replace the `/api/auth/context` API endpoint with server-side token injection directly into `index.html`, eliminating the unauthenticated endpoint and raw-token-in-response security concerns from PR #44 review.

## Motivation

PR #44 review flagged two critical security issues: `/api/auth/context` is unauthenticated and returns the raw Entra access token in a JSON response. Since the gateway already has access to `~/.msclaw/config.json` via `IUserConfigLoader`, the token can be injected directly into the HTML at serve time — no API call, no MSAL redirect, no fetch. Same security posture (whoever can reach the page gets the token), but no exposed endpoint to exploit.

## Proposal

### Goals

- Inject `accessToken`, `username`, and `expiresAtUtc` into `index.html` at serve time
- Remove `/api/auth/context` endpoint entirely
- Remove `loadAuthContext()` JS fetch call from `index.html`
- Keep SignalR `accessTokenFactory` working with the injected token

### Non-Goals

- Changing the auth login flow (`msclaw auth login` stays as-is)
- Adding MSAL or any browser-side token acquisition
- Changing how `UserConfigLoader` reads/writes config

## Design

Replace `UseDefaultFiles()` + `UseStaticFiles()` with a custom middleware (or a Razor/minimal-API handler) for `index.html` only. The handler reads `IUserConfigLoader.Load()`, injects a `<script>` block with the auth context into the HTML, and serves the result. All other static files continue through `UseStaticFiles()` as before.

The injected script sets a global (e.g., `window.__AUTH_CONTEXT`) that `index.html` reads instead of fetching `/api/auth/context`. The `accessTokenFactory` lambda references the injected value.

## Tasks

- [ ] Create middleware/handler that intercepts `GET /` and `GET /index.html`, reads the static file, injects auth context via `<script>window.__AUTH_CONTEXT = {...}</script>`, and returns the modified HTML
- [ ] Remove `/api/auth/context` endpoint mapping and `BuildAuthContextResult`/`TryGetValidAuth` methods from `GatewayEndpointExtensions.cs`
- [ ] Update `index.html`: remove `loadAuthContext()`, read from `window.__AUTH_CONTEXT` instead, simplify `initialize()`
- [ ] Update `UseGatewayPipeline` to use the new middleware before `UseStaticFiles()`
- [ ] Verify SignalR hub auth still works end-to-end with injected token
- [ ] Update any tests that reference `/api/auth/context`

## Open Questions

- Should the injected token be HTML-entity-encoded or placed in a `<meta>` tag instead of a `<script>` block? (Script block is simpler; meta tag is slightly more CSP-friendly)
