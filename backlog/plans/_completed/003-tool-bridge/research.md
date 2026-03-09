# Tool Bridge Conformance Research

**Date**: 2026-03-09
**Spec Versions Reviewed**: `specs/gateway-skills.md`, `specs/gateway-agent-runtime.md`
**Plan Version**: `backlog/plans/20260308-tool-bridge.md` (quick plan)

## Summary

The Tool Bridge plan is **substantially conformant** with the gateway specifications. It correctly implements the three-tier sourcing model (REQ-002–006), priority-based collision resolution (REQ-006–007), status tracking (REQ-010), hot discovery (REQ-004/REQ-011), and lifecycle events (REQ-015). The plan intentionally defers several requirements (approval gates, managed tier, node routing, operator admin) as out-of-scope non-goals. All critical requirements for the bridge abstraction layer are addressed.

## Conformance Analysis

### 1. Tool Sourcing Tiers (REQ-002–005)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Three-tier model | `ToolSourceTier { Bundled, Workspace, Managed }` | Bundled > Workspace > Managed | CONFORMANT |
| Bundled always present | AlwaysVisible tools from bundled providers | REQ-003: All bundled skills MUST appear on startup | CONFORMANT |
| Workspace hot discovery | Providers signal via `WaitForSurfaceChangeAsync` | REQ-004: Discovery MUST NOT require restart | CONFORMANT |
| Managed tier reserved | Enum value exists, no implementation | REQ-005: Pipeline MUST reserve lowest tier | CONFORMANT |

### 2. Priority & Collision Resolution (REQ-006–007)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Cross-tier priority | Higher-tier tool wins | REQ-006: Keep higher-priority, skip lower | CONFORMANT |
| Same-tier collision | Hard error (`InvalidOperationException`) | REQ-007: Log warning | UPDATE NEEDED |
| Collision logging | Exception message identifies providers | REQ-007: Log warning with both names and tiers | CONFORMANT (via exception) |

**Recommendation**: The spec says same-tier collision should **log a warning and skip the duplicate**, not throw. The plan's hard-error approach is **stricter** than the spec — this is a deliberate design choice to surface conflicts at startup rather than silently depending on DI registration order. Document this as intentional deviation.

### 3. Descriptor Validation (REQ-001, REQ-008)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Required fields | `ToolDescriptor` requires `Function`, `ProviderName`, `Tier` | REQ-001: name, description, version, input params, execution mode | DEFERRED — provider responsibility |
| Validation rules | Not in bridge scope | REQ-008: Reject missing name/description/params/mode | DEFERRED — provider responsibility |

**Recommendation**: The bridge delegates descriptor validation to providers. This is acceptable because `ToolDescriptor.Function` (an `AIFunction`) already carries name, description, and parameter schema. The bridge validates that `Function.Name` is non-empty and unique. Field-level validation (per REQ-008) is the provider's concern during `DiscoverAsync`.

### 4. Status Tracking (REQ-010)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Status values | `ToolStatus { Ready, Degraded, Unavailable }` | Ready / Degraded / Unavailable | CONFORMANT |
| Only Ready tools served | `GetDefaultTools()` and `GetToolsByName()` filter by Ready | Skills not ready excluded from registry | CONFORMANT |
| Status update on refresh | `RefreshProviderAsync` re-evaluates status | Status SHOULD update when conditions change | CONFORMANT |

### 5. Hot Discovery (REQ-004, REQ-011)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| No restart required | `WaitForSurfaceChangeAsync` + background loop | REQ-004: Discovery MUST NOT require restart | CONFORMANT |
| Refresh mechanism | `RefreshProviderAsync` re-discovers | REQ-011: Re-scan on session creation | PARTIALLY CONFORMANT |
| Active session isolation | New tools only on new/expanded sessions | REQ-011 edge case: New registrations apply after re-discovery | CONFORMANT |

**Recommendation**: The spec says workspace skills are discovered on each new session creation. The plan uses a background loop instead (provider signals change → registrar refreshes). This is equivalent or better — the catalog is always up-to-date, not just at session creation time. No update needed.

### 6. Lifecycle Events (REQ-015)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Start event | SDK's `ToolExecutionStartEvent` | Skill name, source tier, node requirement | PARTIALLY CONFORMANT |
| End event | SDK's `ToolExecutionCompleteEvent` | Skill name, duration, success/failure | PARTIALLY CONFORMANT |
| Provider metadata | "Decorate in event handler if needed" | Tier and node info required | FUTURE WORK |

**Recommendation**: The SDK events include tool name and execution status but not provider-specific metadata (tier, node routing). The plan correctly identifies this as a decoration concern. When REQ-015 metadata is needed, the `AgentMessageService` event handler can map tool name → provider via `IToolCatalog.GetDescriptor()` to enrich events.

### 7. Execution Modes (REQ-012)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| In-process | Via `AIFunction` handler | REQ-012: In-process execution | CONFORMANT |
| Shell command | Provider implements | REQ-012: Shell execution with injection prevention | DEFERRED to provider |
| Script | Provider implements | REQ-012: Interpreter-based execution | DEFERRED to provider |
| Node routing | Interface supports, not implemented | REQ-012: Device node routing | DEFERRED (non-goal) |
| HTTP endpoint | Provider implements | REQ-012: External HTTP invocation | DEFERRED to provider |

**Recommendation**: The bridge doesn't implement execution modes — providers do. This is correct. The bridge's job is lifecycle (register, catalog, expand, teardown), not execution.

### 8. Security Requirements (REQ-023–025)

| Aspect | Plan | Spec | Status |
|--------|------|------|--------|
| Path traversal | Not in bridge scope | REQ-023: Restrict to mind directory | DEFERRED to provider |
| Environment allowlist | Not in bridge scope | REQ-024: Only allowlisted vars | DEFERRED to provider |
| Argument injection | Not in bridge scope | REQ-025: No shell expansion | DEFERRED to provider |

**Recommendation**: Security validation is provider-specific. Bundled providers use `MindReader` (which already has path-traversal protection). Shell-executing providers must implement REQ-025. This is correct delegation.

## New Features in Spec (Not in Plan)

| Feature | Spec | Status |
|---------|------|--------|
| Approval gates (REQ-016, REQ-027) | Global + per-skill approval | Reserved in interface, not implemented |
| Operator skill listing (REQ-018) | List all registered skills | Future work |
| Operator skill detail (REQ-019) | Full descriptor by name | Future work — `GetDescriptor` enables this |
| Manual re-discovery (REQ-020) | Admin trigger | Future work — `RefreshProviderAsync` enables this |
| Direct invocation (REQ-021) | Admin test/debug | Future work |
| Change notifications (REQ-022) | Push to clients on registry change | Future work |
| Skill disabling (REQ-026) | Config-based disable | Future work |
| Timeout enforcement (REQ-017) | Configurable per-tool timeout | Deferred to provider/runtime |
| Context-aware parameterization (REQ-028) | Mind dir, working memory refs | Deferred to provider |

## Recommendations

### Critical Updates

None — the plan addresses all critical requirements for the bridge abstraction layer.

### Minor Updates

1. **Document same-tier collision deviation** — The plan throws on same-tier collision; the spec says log and skip. Document this as an intentional stricter policy in `plan.md`.

### Future Enhancements

1. **REQ-015 event decoration** — Add provider metadata to tool lifecycle events via catalog lookup
2. **REQ-018–021 operator endpoints** — Expose catalog and registrar operations via admin API
3. **REQ-022 change notifications** — Push `SkillRegistryChanged` events via SignalR when catalog mutates
4. **REQ-016/027 approval gates** — Add approval checkpoint in `expand_tools` or tool invocation handler
5. **REQ-017 timeout enforcement** — Wrap `AIFunction` handlers with configurable `CancellationTokenSource`

## Sources

- `specs/gateway-skills.md` — Full skill system specification (REQ-001 through REQ-028)
- `specs/gateway-agent-runtime.md` — Runtime requirements (REQ-012 through REQ-018)
- `backlog/plans/20260308-tool-bridge.md` — Quick plan with detailed design

## Conclusion

The Tool Bridge plan is conformant with all requirements in scope. It correctly delegates provider-specific concerns (descriptor validation, execution modes, security) to individual providers while implementing the shared infrastructure (catalog, registry, expander, lifecycle). The intentional same-tier collision strictness is the only deviation from the spec, and it's a defensible improvement. All deferred requirements have clear extension points in the defined interfaces.
