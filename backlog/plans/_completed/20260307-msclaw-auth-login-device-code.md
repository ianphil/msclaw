---
title: "MsClaw Auth Login Command + Redirect-Free Tunnel Auth"
status: open
priority: high
created: 2026-03-07
---

# MsClaw Auth Login Command + Redirect-Free Tunnel Auth

## Summary

Add a first-class `msclaw auth login` flow so users can authenticate locally without depending on per-tunnel web redirect URIs.  
Replace the current browser redirect dependency with a token acquisition/storage model compatible with dynamic devtunnel DNS hosts.

## Motivation

Current UI auth in `wwwroot/index.html` uses MSAL browser redirect with `redirectUri = window.location.origin`, which requires each tunnel host to be pre-registered in Entra. This breaks the self-serve NuGet/tool install experience because users cannot update the shared app registration. A CLI-driven login flow avoids per-tunnel redirect registration and makes remote access practical for any tenant user.

## Proposal

### Goals

- Introduce `msclaw auth login` to acquire user tokens locally (device code or localhost loopback).
- Remove runtime dependence on dynamic web redirect URIs for tunnel-based usage.
- Keep SignalR/OpenResponses bearer auth enforcement unchanged on the gateway.

### Non-Goals

- Building a full multi-account profile manager in v1.
- Changing tenant model from single-tenant to multi-tenant.

## Design

Create an auth command group (`msclaw auth login`, later `status/logout`) in Gateway CLI and persist auth/session metadata in user config under `~/.msclaw/`. Move browser UI away from MSAL redirect bootstrap as the primary entry path and instead use a token sourced from local CLI login (or a gateway-issued session abstraction backed by that login). Keep API/hub validation logic unchanged, but update client connection bootstrap so tunnel host URL no longer acts as a redirect URI boundary. Add clear UX messaging for not-logged-in state with guidance to run `msclaw auth login`.

## Tasks

- [ ] Add `auth` command tree and implement `msclaw auth login` flow in Gateway.
- [ ] Add secure user-level token/session storage model under `~/.msclaw/` (non-secret metadata + secret handling approach).
- [ ] Refactor web client auth bootstrap to remove redirect-based `window.location.origin` dependency.
- [ ] Update gateway/client handshake path to consume CLI-authenticated context for SignalR and API calls.
- [ ] Add tests for auth login command, unauthenticated startup behavior, and authenticated tunnel usage.
- [ ] Update docs/quickstart with new auth flow and remove assumptions about tunnel redirect registration.

## Open Questions

- Should v1 use device code only, or support loopback browser auth as an optional mode?
- What is the preferred secure token storage mechanism for cross-platform CLI usage in this repo?
- Should `msclaw start --tunnel` fail hard when no prior `auth login` exists, or run with a guided degraded mode?
