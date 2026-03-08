# Entra Single-Tenant Release Checklist

Use this checklist before publishing MsClaw broadly (NuGet/tool release) with Entra auth and Dev Tunnel support.

## 1) Tenant and app registration boundaries

- [ ] App registration is **Single tenant** (Accounts in this organizational directory only).
- [ ] App name/owner/contact are correct and tied to an actively managed team.
- [ ] Tenant ID and Client ID in `appsettings.json` match the intended production app.
- [ ] App registration is not using test/dev IDs in release artifacts.

## 2) Token validation and API protections

- [ ] API validates issuer (`iss`) for the expected tenant.
- [ ] API validates audience (`aud`) for the expected app/API.
- [ ] Required scopes/roles are enforced for protected endpoints.
- [ ] Anonymous access is disabled for protected API and hub paths.
- [ ] SignalR endpoint auth remains enforced for remote (tunnel) access.

## 3) Permissions and consent hygiene

- [ ] Application permissions are minimized (least privilege).
- [ ] Delegated permissions are minimized (least privilege).
- [ ] Any admin consent granted is intentional and documented.
- [ ] No unused permissions remain in app registration.

## 4) Redirect URIs and client configuration

- [ ] Redirect URIs are explicit and valid for deployed usage patterns.
- [ ] No stale localhost/test redirect URIs remain unless intentionally supported.
- [ ] Public/native client settings are intentional and documented.

## 5) Secret and credential posture

- [ ] No client secrets are embedded in repo, package, scripts, or docs.
- [ ] Certificates/secrets (if any) are stored in approved secret management.
- [ ] Credential expiration/rotation ownership is assigned.

## 6) Dev Tunnel constraints and remote access

- [ ] `--tunnel` remains opt-in (default local-only behavior preserved).
- [ ] Tunnel status endpoint returns expected state and URL when enabled.
- [ ] Startup output clearly shows local and remote access methods.
- [ ] Missing `devtunnel` CLI path surfaces actionable guidance.
- [ ] Persistent tunnel startup is idempotent (existing port/tunnel reuse works).

## 7) Operational validation

- [ ] Fresh install test passes (`dotnet run` or packaged tool path).
- [ ] Startup with existing mind and `--tunnel` succeeds end-to-end.
- [ ] Health/readiness endpoints behave as expected (`/health`, `/health/ready`).
- [ ] Remote access works via tunnel URL for UI + API.
- [ ] Startup/shutdown leaves no orphan tunnel process.

## 8) Logging and privacy

- [ ] Logs do not emit tokens or sensitive headers.
- [ ] Error messages are actionable without leaking secrets.
- [ ] Production log level is appropriate (noise reduced, security preserved).

## 9) Release governance

- [ ] Security review sign-off completed.
- [ ] Rollback plan and owner defined.
- [ ] Version, release notes, and migration notes are complete.
- [ ] Post-release smoke test checklist is prepared and assigned.
