---
title: "Gateway Shared Room — Agent as Chat Participant"
status: open
priority: medium
created: 2026-03-06
depends-on: 20260306-gateway-signalr-openresponses
---

# Gateway Shared Room — Agent as Chat Participant

## Summary

Add a shared chat room mode where multiple users join a common SignalR group and the mind appears as a named participant. Users @mention the agent to queue messages; the agent processes them in order and streams responses to the whole room. Users can also talk to each other without triggering the agent.

## Motivation

The 1:1 private session model (Plan 1) proves the plumbing but doesn't reflect how teams actually work with an agent. A shared room lets multiple operators see the agent's responses, build on each other's questions, and maintain shared context — closer to a Slack channel with a bot than a private ChatGPT window.

## Proposal

### Goals

- Shared room via SignalR group — all connected users see all messages (user-to-user and user-to-agent)
- Agent identity derived from SOUL.md — display name, distinct visual treatment in UI
- @mention routing — agent only responds when @mentioned, but sees all messages for context
- Queue-mode concurrency (#17) — multiple @mentions line up, agent processes sequentially
- Shared session context — agent sees full room history, not just the @mentioning user's messages

### Non-Goals

- Multiple rooms / channels (single room for now)
- Per-user private side-channels with the agent within the room
- Agent proactively speaking without being @mentioned
- Role-based permissions (who can @mention, who can read)

## Design

Builds on `IAgentRuntime` from Plan 1. Add a `RoomService` that manages a single shared SignalR group ("room"). All hub connections join the group on connect. User messages are broadcast to the group immediately. Messages containing `@{agent-name}` are additionally enqueued to the agent runtime. The runtime uses #17 queue mode — `SemaphoreSlim(1)` gates one agent run at a time, additional @mentions wait in a `Channel<T>` queue. The agent's session accumulates the full room history (all messages, not just @mentions) so it has conversational context. Agent responses stream to the entire group via `Clients.Group("room")`. The agent's display name comes from parsing SOUL.md (first heading or a `name:` frontmatter field).

## Tasks

- [ ] Add `name` field parsing to `IdentityLoader` — extract agent display name from SOUL.md
- [ ] Create `RoomService` managing shared SignalR group membership (join on connect, leave on disconnect)
- [ ] Add hub method: `SendRoomMessage` — broadcasts user message to group, checks for @mention, enqueues to agent if matched
- [ ] Implement queue-mode concurrency in `IAgentRuntime` — `Channel<T>` message queue with sequential processing (#17 queue mode)
- [ ] Wire shared session: agent session receives full room history as context, not per-user isolation
- [ ] Stream agent responses to `Clients.Group("room")` with agent identity metadata (name, role marker)
- [ ] Update chat UI: render agent messages with distinct name/avatar, add @mention autocomplete or prefix
- [ ] Update chat UI: show user-to-user messages (no agent involvement) in the same stream
- [ ] Add presence: broadcast join/leave events to the room group
- [ ] Add integration test: two users in room, one @mentions agent, both receive streamed response
- [ ] Add integration test: two concurrent @mentions queue and execute sequentially

## Open Questions

- Should the agent see all room messages in its session context, or only messages since the last @mention? Full context is better but costs more tokens.
- How do we handle session history growth? Truncation strategy, sliding window, or summarization?
- Should the agent name in SOUL.md be a frontmatter field (`name: Ernist`) or parsed from the first heading?
