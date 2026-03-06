# Gateway MVP Plumbing Conformance Research

**Date**: 2026-03-05
**Spec Version Reviewed**: gateway.md v3.1, 20260305-gateway-mvp-plumbing.md
**Plan Version**: plan.md v1.0

## Summary

This feature implements the hosting chassis described in the gateway spec. It does not implement any epic's feature-level requirements. The conformance check validates that the plumbing decisions align with the gateway spec's non-functional requirements, architecture assumptions, and the constraints imposed by each epic.

## Conformance Analysis

### 1. Binding and Transport

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Default bind address | `127.0.0.1:18789` | "default: `127.0.0.1:18789`" (§4) | CONFORMANT |
| Configurable host/port | GatewayOptions with CLI override | "configurable host and port" (§4) | CONFORMANT |
| SignalR hub path | `/gateway` | "SignalR hub" (EPIC-03) | CONFORMANT |
| HTTP endpoints | `/healthz` mapped | "health probes" (EPIC-07) | CONFORMANT |

### 2. Lifecycle and Reliability

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Process stays alive on client failure | GatewayHostedService sets readiness=failed, process continues | "gateway process MUST remain alive" (§3.3) | CONFORMANT |
| Graceful shutdown | ASP.NET Core host handles SIGINT/SIGTERM | EPIC-03 defines full shutdown; plumbing provides the host signal | CONFORMANT |
| Health probe semantics | 200 when ready, 503 when not | "readiness probe (non-200 status)" (§3.3) | CONFORMANT |
| Health probe latency | Synchronous readiness check | "< 200ms" (§4) | CONFORMANT |

### 3. Mind System Integration

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Validate before start | GatewayHostedService validates on StartAsync | "refuse to start" on invalid mind (§3.3) | CONFORMANT |
| Identity loading | GatewayHostedService loads system message | EPIC-01: "load identity" | CONFORMANT |
| Scaffold support | `--new-mind` flag on start | Not in spec but in quick plan decisions | N/A (plan extension) |
| CLI surface for mind ops | `mind validate`, `mind scaffold` | Not in spec but in quick plan decisions | N/A (plan extension) |

### 4. Platform and Dependencies

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Target framework | net10.0 | "only the .NET runtime" (§4) | CONFORMANT |
| CLI dependency | Copilot CLI on PATH (via CliLocator) | "GitHub Copilot CLI on PATH" (§4) | CONFORMANT |
| Cross-platform | ASP.NET Core + System.CommandLine | "Windows, macOS, and Linux" (§4) | CONFORMANT |

### 5. Authentication

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Baseline auth | Loopback-only, no token auth | "loopback bypass" listed in EPIC-03 | CONFORMANT |
| Token auth | Deferred to EPIC-03 | Correct — EPIC-03 scope | CONFORMANT |

## New Features in Spec (Not in Plan)

The following spec features are intentionally deferred — they belong to specific epics, not the plumbing layer:

- Session management (EPIC-02)
- Full gateway protocol with roles and auth (EPIC-03)
- Channel adapters (EPIC-09)
- Heartbeat, cron, canvas, skills (EPIC-05/06/04/08)
- OpenAI-compatible API (EPIC-07)

All are acceptable deferrals for an MVP plumbing feature.

## Recommendations

### Critical Updates

None. The plumbing plan is fully conformant with the gateway spec's non-functional requirements and does not prematurely implement any epic's scope.

### Minor Updates

1. **Health endpoint body format** — The spec says "readiness probe" but doesn't prescribe a body format. Plan should define a simple JSON response (`{ "status": "Healthy" }` / `{ "status": "Unhealthy" }`) for consistency with the ASP.NET Core health checks convention.

### Future Enhancements

1. **Structured logging** — The spec mentions "log a message" and "log the error" in multiple edge cases. The plumbing should wire up `ILogger` consistently so epics inherit structured logging. (This is automatic with ASP.NET Core's default logging.)
2. **Configuration file** — `appsettings.json` should be supported for host/port overrides, not just CLI args. (ASP.NET Core provides this by default.)

## Sources

- [Gateway Spec](../../specs/gateway.md) v3.1
- [Gateway MVP Plumbing Quick Plan](../20260305-gateway-mvp-plumbing.md)
- [ASP.NET Core Health Checks](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks)
- [System.CommandLine](https://learn.microsoft.com/dotnet/standard/commandline/)

## Conclusion

The gateway MVP plumbing plan is fully conformant with the gateway spec. All deferred features correctly belong to their respective epics. The plumbing provides exactly the hosting chassis needed for epics to build on. No critical changes required.
