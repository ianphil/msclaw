# AI Notes — Rules

- Never sync-over-async in Timer callbacks — use `Task.Run` with per-session try/catch
- Never use `AvailableTools`/`ExcludedTools` on `SessionConfig` — creates whitelist hiding CLI built-ins
- Always set `SessionConfig.Streaming = true` to receive delta/message events with content
- Always set `OnPermissionRequest` on both `SessionConfig` and `ResumeSessionConfig` or get `ArgumentException`
- Always filter `assistant.reasoning.delta` events from UI display or users see model thinking
- Treat `devtunnel port create` conflicts as success — command is not idempotent
- MSBuild embedded `.github` resources use `..github.` prefix; folder hyphens become underscores, filename hyphens preserved
- Use interactive browser auth (localhost loopback), never device-code flow — CA policy 530033 blocks it
- `MindPaths.ArchiveDir` must be "Archive" (capital A) to match IDEA taxonomy
- Same-tier tool name collision must be a hard error — silent skip hides DI ordering bugs
