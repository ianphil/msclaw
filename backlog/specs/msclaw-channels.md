# MsClaw Messaging Channels

> Plugin-based adapters that bridge external messaging platforms into the MsClaw agent pipeline.

Reference: [MsClaw Gateway architecture](msclaw-gateway.md) · [MsClaw protocol spec](msclaw-gateway-protocol.md) · [OpenClaw architecture](../../.ai/docs/openclaw-architecture.md)

## Overview

The MsClaw Channel system defines how external messaging platforms (WhatsApp,
Telegram, Slack, Discord, etc.) connect to the Gateway's agent runtime. Each
platform is represented by a **channel adapter** — a self-contained plugin that
normalizes inbound messages into a canonical form, submits them to the agent
pipeline, and formats agent replies back into platform-native responses.

Inspired by [OpenClaw's channel plugins](https://github.com/openclaw/openclaw),
this spec replaces the Node.js plugin registry with a C# interface-based adapter
contract, DI-registered channel services, and ASP.NET Core hosted services for
channel lifecycle management.

## Goals

- **Unified adapter contract** — every channel implements the same set of C# interfaces regardless of transport (webhook, polling, persistent connection).
- **Independent lifecycle** — channels start, stop, reconnect, and health-check independently. A failed channel does not affect the Gateway or other channels.
- **Canonical message model** — inbound messages are normalized into a single `ChannelMessage` regardless of source platform. Agent replies go through a single `ChannelReply` regardless of destination.
- **Multi-account** — each channel supports multiple accounts (e.g., two Slack workspaces, three Telegram bots).
- **DM and group policies** — per-channel, per-account rules for who can message the agent (allowlists, open, pairing-required).
- **Delivery guarantees** — outbound replies go through a delivery queue with retry and dead-letter tracking.
- **Hub integration** — channel status is visible to operators via SignalR presence events; operators can start/stop channels at runtime via hub methods.

## Architecture

```
External Platforms                    MsClaw Gateway
                          ┌──────────────────────────────────────────────────────────┐
  WhatsApp ───webhook──►  │  ┌─────────────────────────────────────────────────────┐  │
  Telegram ───polling──►  │  │              CHANNEL MANAGER                        │  │
  Slack    ───socket───►  │  │           (IHostedService)                          │  │
  Discord  ───gateway──►  │  │                                                     │  │
  Signal   ───bridge───►  │  │  ┌──────────┐ ┌──────────┐ ┌──────────┐            │  │
  Email    ───imap─────►  │  │  │ WhatsApp │ │ Telegram │ │  Slack   │  ...       │  │
  WebChat  ───signalr──►  │  │  │ Adapter  │ │ Adapter  │ │ Adapter  │            │  │
                          │  │  └────┬─────┘ └────┬─────┘ └────┬─────┘            │  │
                          │  │       │            │            │                   │  │
                          │  │       ▼            ▼            ▼                   │  │
                          │  │  ┌─────────────────────────────────────────────┐    │  │
                          │  │  │           INBOUND PIPELINE                  │    │  │
                          │  │  │                                             │    │  │
                          │  │  │  normalize → policy check → create session │    │  │
                          │  │  │  → submit to agent → collect reply         │    │  │
                          │  │  └──────────────────────┬──────────────────────┘    │  │
                          │  │                         │                           │  │
                          │  │                         ▼                           │  │
                          │  │  ┌─────────────────────────────────────────────┐    │  │
                          │  │  │           OUTBOUND PIPELINE                 │    │  │
                          │  │  │                                             │    │  │
                          │  │  │  format → chunk → enqueue → deliver → ack  │    │  │
                          │  │  └─────────────────────────────────────────────┘    │  │
                          │  └─────────────────────────────────────────────────────┘  │
                          │                         │                                 │
                          │                         ▼                                 │
                          │  ┌──────────────────────────────────────────────────┐     │
                          │  │              AGENT RUNTIME                       │     │
                          │  │         (CopilotClient + Mind)                   │     │
                          │  └──────────────────────────────────────────────────┘     │
                          └──────────────────────────────────────────────────────────┘
```

## Core Contracts

### IChannelAdapter

The primary interface every channel must implement. Covers lifecycle, health,
and inbound/outbound message handling.

```csharp
/// <summary>
/// Adapter that bridges an external messaging platform into the agent pipeline.
/// Each channel registers one or more implementations via DI.
/// </summary>
public interface IChannelAdapter : IAsyncDisposable
{
    /// <summary>Unique channel identifier (e.g., "telegram", "slack").</summary>
    ChannelId Id { get; }

    /// <summary>Human-readable display name (e.g., "Telegram", "Slack").</summary>
    string DisplayName { get; }

    /// <summary>Current connection status of this adapter.</summary>
    ChannelStatus Status { get; }

    /// <summary>Capabilities this channel supports (text, media, reactions, threads, etc.).</summary>
    ChannelCapabilities Capabilities { get; }

    /// <summary>Start the channel — establish connections, begin receiving messages.</summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>Stop the channel — clean disconnect, drain pending deliveries.</summary>
    Task StopAsync(CancellationToken ct);

    /// <summary>Probe the channel's connection health.</summary>
    Task<ChannelHealthReport> HealthCheckAsync(CancellationToken ct);
}
```

### IChannelInboundHandler

Handles messages arriving from the external platform.

```csharp
/// <summary>
/// Processes inbound messages from an external platform and submits them
/// to the agent pipeline. Implemented internally by the channel infrastructure;
/// channel adapters call this to hand off normalized messages.
/// </summary>
public interface IChannelInboundHandler
{
    /// <summary>
    /// Submit a normalized inbound message to the agent pipeline.
    /// Returns the agent's reply (or an error) once processing completes.
    /// </summary>
    Task<ChannelReply> HandleInboundAsync(
        ChannelMessage message,
        CancellationToken ct);
}
```

### IChannelOutboundHandler

Handles formatting and delivering agent replies to the external platform.

```csharp
/// <summary>
/// Formats and delivers an agent reply to the external platform.
/// Each channel adapter provides its own outbound implementation.
/// </summary>
public interface IChannelOutboundHandler
{
    /// <summary>Channel this handler delivers to.</summary>
    ChannelId ChannelId { get; }

    /// <summary>
    /// Deliver a reply to the specified conversation on the external platform.
    /// Handles formatting, chunking, and platform-specific constraints.
    /// </summary>
    Task<OutboundDeliveryResult> DeliverAsync(
        ChannelReply reply,
        OutboundTarget target,
        CancellationToken ct);

    /// <summary>
    /// Deliver a media attachment (image, file, audio, video).
    /// </summary>
    Task<OutboundDeliveryResult> DeliverMediaAsync(
        ChannelMediaReply media,
        OutboundTarget target,
        CancellationToken ct);
}
```

### IChannelPolicyProvider

Controls who is allowed to message the agent through a given channel.

```csharp
/// <summary>
/// Evaluates DM and group access policies for inbound messages.
/// </summary>
public interface IChannelPolicyProvider
{
    /// <summary>Channel this provider applies to.</summary>
    ChannelId ChannelId { get; }

    /// <summary>
    /// Evaluate whether an inbound message is allowed by the channel's policy.
    /// Returns an allow/deny decision with an optional reason.
    /// </summary>
    Task<PolicyDecision> EvaluateAsync(
        ChannelMessage message,
        CancellationToken ct);
}
```

### IChannelConfigurator

Manages account configuration and onboarding for a channel.

```csharp
/// <summary>
/// Handles account resolution, validation, and onboarding for a channel.
/// </summary>
public interface IChannelConfigurator
{
    /// <summary>Channel this configurator manages.</summary>
    ChannelId ChannelId { get; }

    /// <summary>List configured account IDs for this channel.</summary>
    IReadOnlyList<string> ListAccountIds();

    /// <summary>Validate the configuration for a specific account.</summary>
    Task<ConfigValidationResult> ValidateAsync(
        string accountId,
        CancellationToken ct);

    /// <summary>Get the default account ID.</summary>
    string DefaultAccountId { get; }
}
```

## Canonical Message Model

All inbound messages are normalized into a `ChannelMessage` before reaching
the agent pipeline. All agent replies are expressed as a `ChannelReply` before
being formatted for the target platform.

### ChannelMessage (Inbound)

```csharp
/// <summary>
/// A normalized inbound message from any external platform.
/// This is the canonical form that the agent pipeline processes.
/// </summary>
public record ChannelMessage
{
    /// <summary>Unique message ID assigned by the source platform.</summary>
    public required string PlatformMessageId { get; init; }

    /// <summary>Channel this message arrived from.</summary>
    public required ChannelId ChannelId { get; init; }

    /// <summary>Account ID within the channel (for multi-account).</summary>
    public required string AccountId { get; init; }

    /// <summary>Conversation/chat/room identifier on the source platform.</summary>
    public required string ConversationId { get; init; }

    /// <summary>Sender identity on the source platform.</summary>
    public required ChannelSender Sender { get; init; }

    /// <summary>Text content of the message (may be empty for media-only).</summary>
    public string? Text { get; init; }

    /// <summary>Media attachments (images, files, audio, video).</summary>
    public IReadOnlyList<ChannelAttachment> Attachments { get; init; } = [];

    /// <summary>Whether this message is in a group conversation.</summary>
    public bool IsGroup { get; init; }

    /// <summary>Whether the agent was explicitly mentioned (@-mention, reply, etc.).</summary>
    public bool IsMentioned { get; init; }

    /// <summary>ID of the message this is replying to, if any.</summary>
    public string? ReplyToMessageId { get; init; }

    /// <summary>Thread/topic ID, if the platform supports threads.</summary>
    public string? ThreadId { get; init; }

    /// <summary>When the message was sent on the source platform.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Platform-specific raw payload for adapter-level access.
    /// Not used by the agent pipeline — available for outbound formatting context.
    /// </summary>
    public object? RawPayload { get; init; }
}

/// <summary>Identity of the message sender on the source platform.</summary>
public record ChannelSender
{
    /// <summary>Platform-specific user ID.</summary>
    public required string PlatformUserId { get; init; }

    /// <summary>Display name (best-effort, may be null).</summary>
    public string? DisplayName { get; init; }

    /// <summary>Username/handle (platform-dependent).</summary>
    public string? Username { get; init; }
}

/// <summary>A media attachment on an inbound or outbound message.</summary>
public record ChannelAttachment
{
    /// <summary>MIME type of the attachment.</summary>
    public required string MimeType { get; init; }

    /// <summary>URL or local path to the attachment content.</summary>
    public required string Url { get; init; }

    /// <summary>File name, if available.</summary>
    public string? FileName { get; init; }

    /// <summary>File size in bytes, if known.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>Caption/alt text for the attachment.</summary>
    public string? Caption { get; init; }
}
```

### ChannelReply (Outbound)

```csharp
/// <summary>
/// An agent reply formatted for delivery to an external platform.
/// </summary>
public record ChannelReply
{
    /// <summary>Agent run ID that produced this reply.</summary>
    public required string RunId { get; init; }

    /// <summary>Channel to deliver to.</summary>
    public required ChannelId ChannelId { get; init; }

    /// <summary>Account ID within the channel.</summary>
    public required string AccountId { get; init; }

    /// <summary>Conversation to deliver to.</summary>
    public required string ConversationId { get; init; }

    /// <summary>Text content of the reply (markdown).</summary>
    public string? Text { get; init; }

    /// <summary>Media attachments to include in the reply.</summary>
    public IReadOnlyList<ChannelAttachment> Attachments { get; init; } = [];

    /// <summary>Message ID to reply to (for threading), if applicable.</summary>
    public string? ReplyToMessageId { get; init; }

    /// <summary>Thread ID to post in, if applicable.</summary>
    public string? ThreadId { get; init; }
}

/// <summary>A media-specific reply (image, file, audio, video).</summary>
public record ChannelMediaReply
{
    public required string RunId { get; init; }
    public required ChannelId ChannelId { get; init; }
    public required string AccountId { get; init; }
    public required string ConversationId { get; init; }
    public required ChannelAttachment Attachment { get; init; }
    public string? Caption { get; init; }
    public string? ReplyToMessageId { get; init; }
}
```

## Supporting Types

```csharp
/// <summary>Strongly-typed channel identifier.</summary>
public readonly record struct ChannelId(string Value)
{
    public static readonly ChannelId WhatsApp = new("whatsapp");
    public static readonly ChannelId Telegram = new("telegram");
    public static readonly ChannelId Slack = new("slack");
    public static readonly ChannelId Discord = new("discord");
    public static readonly ChannelId Signal = new("signal");
    public static readonly ChannelId IMessage = new("imessage");
    public static readonly ChannelId Email = new("email");
    public static readonly ChannelId Matrix = new("matrix");
    public static readonly ChannelId Irc = new("irc");
    public static readonly ChannelId WebChat = new("webchat");

    public override string ToString() => Value;
}

/// <summary>Runtime status of a channel adapter.</summary>
public enum ChannelStatus
{
    Stopped,
    Starting,
    Connected,
    Degraded,
    Reconnecting,
    Disconnected,
    Failed
}

/// <summary>Capabilities a channel supports.</summary>
[Flags]
public enum ChannelCapabilities
{
    None = 0,
    Text = 1 << 0,
    Markdown = 1 << 1,
    Images = 1 << 2,
    Files = 1 << 3,
    Audio = 1 << 4,
    Video = 1 << 5,
    Reactions = 1 << 6,
    Threads = 1 << 7,
    Polls = 1 << 8,
    EditMessages = 1 << 9,
    DeleteMessages = 1 << 10,
    Groups = 1 << 11,
    Mentions = 1 << 12,
    ReadReceipts = 1 << 13,
    TypingIndicators = 1 << 14,
    RichFormatting = 1 << 15
}

/// <summary>Health report for a channel adapter.</summary>
public record ChannelHealthReport
{
    public required ChannelId ChannelId { get; init; }
    public required string AccountId { get; init; }
    public required ChannelStatus Status { get; init; }
    public string? StatusMessage { get; init; }
    public DateTimeOffset? LastMessageReceived { get; init; }
    public DateTimeOffset? LastMessageSent { get; init; }
    public int PendingDeliveries { get; init; }
    public string? Error { get; init; }
}

/// <summary>Target for outbound message delivery.</summary>
public record OutboundTarget
{
    public required ChannelId ChannelId { get; init; }
    public required string AccountId { get; init; }
    public required string ConversationId { get; init; }
    public string? ThreadId { get; init; }
    public string? ReplyToMessageId { get; init; }
}

/// <summary>Result of an outbound delivery attempt.</summary>
public record OutboundDeliveryResult
{
    public required bool Success { get; init; }
    public string? PlatformMessageId { get; init; }
    public string? Error { get; init; }
    public int AttemptCount { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>Policy decision for an inbound message.</summary>
public record PolicyDecision
{
    public required bool Allowed { get; init; }
    public string? Reason { get; init; }
    public PolicyDenyAction DenyAction { get; init; }
}

public enum PolicyDenyAction
{
    /// <summary>Silently drop the message.</summary>
    Drop,
    /// <summary>Reply with a denial message to the sender.</summary>
    Reply,
    /// <summary>Forward to an operator for manual review.</summary>
    Escalate
}

/// <summary>Result of validating a channel account configuration.</summary>
public record ConfigValidationResult
{
    public required bool Valid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
```

## Channel Lifecycle

Channels run as ASP.NET Core **hosted services** managed by a central
`ChannelManager`. Each channel adapter starts and stops independently.

### Startup Sequence

```
Gateway Boot
    │
    ▼
┌──────────────────────────────────────────────────────────────────┐
│  ChannelManager.StartAsync()  (IHostedService)                   │
│                                                                  │
│  1. Read MsClaw:Channels config section                          │
│  2. For each enabled channel:                                    │
│     a. Resolve IChannelAdapter from DI                           │
│     b. Validate config (IChannelConfigurator.ValidateAsync)      │
│     c. Call adapter.StartAsync()                                 │
│     d. On success → status = Connected                           │
│     e. On failure → status = Failed, schedule retry              │
│  3. Push PresenceEvent with channel statuses to operators        │
└──────────────────────────────────────────────────────────────────┘
```

### Runtime State Machine

```
                    ┌─────────┐
          ┌────────►│ Stopped │◄────────────────────────────────┐
          │         └────┬────┘                                 │
          │              │ StartAsync()                          │
          │              ▼                                       │
          │         ┌──────────┐                                │
          │         │ Starting │                                │
          │         └────┬─────┘                                │
          │              │                                      │
          │         ┌────┴──────────────┐                       │
          │         │                   │                       │
          │         ▼                   ▼                       │
          │    ┌───────────┐      ┌──────────┐                 │
          │    │ Connected │      │  Failed  │─── retry ──┐    │
          │    └─────┬─────┘      └──────────┘            │    │
          │          │                   ▲                 │    │
          │     connection lost          │                 │    │
          │          │            max retries              │    │
          │          ▼            exceeded                 │    │
          │    ┌──────────────┐      │                    │    │
          │    │ Reconnecting │──────┘                    │    │
          │    └──────┬───────┘                           │    │
          │           │ reconnected                       │    │
          │           ▼                                   │    │
          │    ┌───────────┐                              │    │
          │    │ Connected │◄─────────────────────────────┘    │
          │    └───────────┘                                   │
          │                                                    │
          │         StopAsync()                                │
          └────────────────────────────────────────────────────┘
```

### Retry Policy

- Failed channels retry with exponential backoff: 1s → 2s → 4s → 8s → … → 5 min cap.
- Maximum retry count is configurable per channel (default: unlimited).
- Each retry attempt emits a `ChannelStatusEvent` to operators.
- The `ChannelManager` tracks retry state per adapter instance.

### Graceful Shutdown

1. `ChannelManager.StopAsync()` is called by the host.
2. For each running channel adapter (in parallel):
   a. Cancel inbound processing (pass cancellation token).
   b. Drain the outbound delivery queue (wait up to configured timeout).
   c. Call `adapter.StopAsync()`.
   d. Call `adapter.DisposeAsync()`.
3. Push final `PresenceEvent` showing all channels stopped.

## Inbound Pipeline

The path a message takes from an external platform to the agent.

### Flow

```
EXTERNAL PLATFORM                   CHANNEL ADAPTER                    AGENT PIPELINE
       │                                  │                                  │
       │── platform event ──────────────►│                                  │
       │   (webhook / poll / push)        │                                  │
       │                                  │── normalize to ChannelMessage ──►│
       │                                  │                                  │
       │                                  │   ┌─────────────────────────┐    │
       │                                  │   │ Policy Check            │    │
       │                                  │   │ (IChannelPolicyProvider)│    │
       │                                  │   │                         │    │
       │                                  │   │ Allowed? ──────► yes ──┼───►│
       │                                  │   │            └──► no:    │    │
       │                                  │   │               drop /   │    │
       │                                  │   │               reply /  │    │
       │                                  │   │               escalate │    │
       │                                  │   └─────────────────────────┘    │
       │                                  │                                  │
       │                                  │                    resolve or    │
       │                                  │                    create session│
       │                                  │                                  │
       │                                  │                    submit to     │
       │                                  │                    CopilotSession│
       │                                  │                                  │
       │                                  │◄── ChannelReply ────────────────│
       │                                  │                                  │
       │◄── formatted platform reply ────│                                  │
       │                                  │                                  │
```

### Session Resolution

Each inbound message must be mapped to a `CopilotSession`:

- **Key**: `{channelId}:{accountId}:{conversationId}` — one session per conversation.
- **Create or resume**: If no session exists for the key, `CreateSessionAsync`. Otherwise, `ResumeSessionAsync`.
- **Group conversations**: Use the conversation ID (group/room ID), not the sender's user ID.
- **DM conversations**: Use the sender's platform user ID as the conversation ID.
- **Concurrency**: Only one agent invocation per session key at a time. Additional inbound messages for a busy session are queued.

### Group Conversation Rules

| Setting | Behavior |
|---------|----------|
| `RequireMention: true` (default) | Agent only responds when explicitly mentioned (@-mention, reply-to-agent, or name in text). |
| `RequireMention: false` | Agent responds to every message in the group. |
| `GroupIntroHint` | Optional system message hint appended when the agent enters a group conversation for the first time. |

## Outbound Pipeline

The path an agent reply takes from the agent to the external platform.

### Flow

```
AGENT PIPELINE                     OUTBOUND PIPELINE                   CHANNEL ADAPTER
       │                                  │                                  │
       │── ChannelReply ────────────────►│                                  │
       │                                  │                                  │
       │                                  │── format for platform ──────────►│
       │                                  │   (markdown → Slack blocks,      │
       │                                  │    markdown → Telegram HTML,     │
       │                                  │    markdown → Discord markdown,  │
       │                                  │    markdown → email HTML, etc.)  │
       │                                  │                                  │
       │                                  │── chunk if needed ──────────────►│
       │                                  │   (respect platform limits:      │
       │                                  │    WhatsApp 65536, Telegram      │
       │                                  │    4096, Discord 2000, etc.)     │
       │                                  │                                  │
       │                                  │── enqueue delivery ─────────────►│
       │                                  │                                  │
       │                                  │           ┌──────────────┐       │
       │                                  │           │ Delivery     │       │
       │                                  │           │ Queue        │       │
       │                                  │           │              │       │
       │                                  │           │ attempt 1 ───┼──────►│── send ──► Platform
       │                                  │           │ failure? ────┼──┐    │
       │                                  │           │ attempt 2 ◄─┼──┘    │
       │                                  │           │ success ────┼──────►│── ack
       │                                  │           └──────────────┘       │
       │                                  │                                  │
       │◄── OutboundDeliveryResult ──────│                                  │
       │                                  │                                  │
```

### Text Chunking

Long agent replies must be split to respect platform character limits:

| Channel | Max Text Length | Chunking Strategy |
|---------|----------------|-------------------|
| WhatsApp | 65,536 chars | Split on paragraph boundaries |
| Telegram | 4,096 chars | Split on paragraph boundaries, preserve HTML formatting |
| Discord | 2,000 chars | Split on paragraph boundaries, preserve markdown |
| Slack | 40,000 chars (block) | Split into multiple blocks |
| Signal | 2,000 chars | Split on paragraph boundaries |
| iMessage | ~20,000 chars | Split on paragraph boundaries |
| Email | No limit | Single message |
| IRC | 512 bytes per line | Split on newlines |
| Matrix | 65,536 chars | Split on paragraph boundaries |
| WebChat | No limit | Single message |

### Markdown Formatting

Agent replies are in markdown. Each channel adapter converts to the platform's
native format:

| Channel | Target Format | Notes |
|---------|---------------|-------|
| WhatsApp | WhatsApp formatting (`*bold*`, `_italic_`) | Subset of markdown |
| Telegram | HTML or MarkdownV2 | Telegram-specific escaping |
| Discord | Discord markdown | Nearly standard markdown |
| Slack | mrkdwn (Slack's markdown variant) | Block Kit for rich content |
| Signal | Plain text | Strip all formatting |
| iMessage | Plain text or attributed string | Limited formatting |
| Email | HTML | Full markdown → HTML conversion |
| IRC | IRC formatting codes | Bold, italic, color |
| Matrix | HTML (in `formatted_body`) | Full markdown → HTML |
| WebChat | Raw markdown | Client renders |

### Delivery Queue

Outbound messages are enqueued for reliable delivery:

```csharp
/// <summary>
/// Persistent outbound delivery queue with retry and dead-letter tracking.
/// </summary>
public interface IDeliveryQueue
{
    /// <summary>Enqueue a delivery task.</summary>
    Task<string> EnqueueAsync(DeliveryTask task, CancellationToken ct);

    /// <summary>Acknowledge successful delivery.</summary>
    Task AckAsync(string deliveryId, OutboundDeliveryResult result, CancellationToken ct);

    /// <summary>Report a failed delivery attempt.</summary>
    Task NackAsync(string deliveryId, string error, CancellationToken ct);

    /// <summary>Get pending deliveries for a channel.</summary>
    Task<IReadOnlyList<DeliveryTask>> GetPendingAsync(
        ChannelId channelId,
        CancellationToken ct);
}

/// <summary>A unit of work in the delivery queue.</summary>
public record DeliveryTask
{
    public required string DeliveryId { get; init; }
    public required ChannelReply Reply { get; init; }
    public required OutboundTarget Target { get; init; }
    public int AttemptCount { get; init; }
    public int MaxAttempts { get; init; } = 3;
    public DateTimeOffset? NextAttemptAfter { get; init; }
    public DateTimeOffset EnqueuedAt { get; init; }
}
```

- **In-memory for v1** — backed by a `System.Threading.Channels.Channel<T>`.
- **Future**: persistent queue (SQLite or file-backed) for crash recovery.
- **Retry**: 3 attempts with exponential backoff (1s, 5s, 15s).
- **Dead-letter**: failed deliveries after max attempts are logged and surfaced to operators via presence events.

## Channel Registry

The `ChannelRegistry` is a static catalog of known channel types. It does not
manage runtime state — that is the `ChannelManager`'s job.

```csharp
/// <summary>
/// Static catalog of known channel types and their metadata.
/// Used for configuration validation, UI rendering, and adapter resolution.
/// </summary>
public static class ChannelRegistry
{
    public static IReadOnlyList<ChannelDefinition> All { get; }
    public static ChannelDefinition? Get(ChannelId id);
    public static bool IsKnown(ChannelId id);
}

/// <summary>Metadata about a channel type.</summary>
public record ChannelDefinition
{
    public required ChannelId Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required ChannelCapabilities Capabilities { get; init; }
    public required ChannelTransport Transport { get; init; }
    public required string ConfigSection { get; init; }
    public int DisplayOrder { get; init; }
    public bool RequiresExternalService { get; init; }
}

/// <summary>How the channel connects to the external platform.</summary>
public enum ChannelTransport
{
    Webhook,
    LongPolling,
    PersistentConnection,
    Bridge,
    Embedded
}
```

## Channel Manager

The `ChannelManager` is the runtime orchestrator — an `IHostedService` that
manages adapter lifecycle, monitors health, and exposes control-plane methods
for the GatewayHub.

```csharp
/// <summary>
/// Runtime orchestrator for all channel adapters.
/// Registered as an IHostedService in the DI container.
/// </summary>
public interface IChannelManager
{
    /// <summary>Get the current status of all channels.</summary>
    IReadOnlyList<ChannelHealthReport> GetStatus();

    /// <summary>Get the status of a specific channel account.</summary>
    ChannelHealthReport? GetStatus(ChannelId channelId, string accountId);

    /// <summary>Start a specific channel account at runtime.</summary>
    Task StartChannelAsync(ChannelId channelId, string accountId, CancellationToken ct);

    /// <summary>Stop a specific channel account at runtime.</summary>
    Task StopChannelAsync(ChannelId channelId, string accountId, CancellationToken ct);

    /// <summary>Restart a specific channel account.</summary>
    Task RestartChannelAsync(ChannelId channelId, string accountId, CancellationToken ct);
}
```

## GatewayHub Integration

Channel status and control are exposed to operators through SignalR hub methods
and events defined in the [protocol spec](msclaw-gateway-protocol.md).

### Hub Methods (additions to GatewayHub)

```csharp
// ── Channels ─────────────────────────────────────────────────
// These methods extend the GatewayHub defined in the protocol spec.

/// <summary>List all channels and their current status.</summary>
[Authorize(Policy = "OperatorRead")]
Task<ChannelListResult> ChannelsList();

/// <summary>Get detailed status for a specific channel account.</summary>
[Authorize(Policy = "OperatorRead")]
Task<ChannelHealthReport> ChannelStatus(ChannelStatusRequest request);

/// <summary>Start a channel account.</summary>
[Authorize(Policy = "OperatorAdmin")]
Task ChannelStart(ChannelControlRequest request);

/// <summary>Stop a channel account.</summary>
[Authorize(Policy = "OperatorAdmin")]
Task ChannelStop(ChannelControlRequest request);

/// <summary>Restart a channel account.</summary>
[Authorize(Policy = "OperatorAdmin")]
Task ChannelRestart(ChannelControlRequest request);
```

### Hub Events (additions to IGatewayClient)

```csharp
// ── Channel events ───────────────────────────────────────────
// These events extend IGatewayClient defined in the protocol spec.

/// <summary>Pushed when a channel's status changes.</summary>
Task OnChannelStatus(ChannelStatusEvent e);

/// <summary>Pushed when a message is received from an external platform.</summary>
Task OnChannelMessageReceived(ChannelMessageReceivedEvent e);

/// <summary>Pushed when a reply is delivered to an external platform.</summary>
Task OnChannelMessageDelivered(ChannelMessageDeliveredEvent e);

/// <summary>Pushed when a delivery fails after all retries.</summary>
Task OnChannelDeliveryFailed(ChannelDeliveryFailedEvent e);
```

### Event Schemas

```csharp
public record ChannelStatusEvent
{
    public required ChannelId ChannelId { get; init; }
    public required string AccountId { get; init; }
    public required ChannelStatus Status { get; init; }
    public required ChannelStatus PreviousStatus { get; init; }
    public string? Message { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

public record ChannelMessageReceivedEvent
{
    public required ChannelId ChannelId { get; init; }
    public required string AccountId { get; init; }
    public required string ConversationId { get; init; }
    public required string SenderDisplayName { get; init; }
    public required string TextPreview { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

public record ChannelMessageDeliveredEvent
{
    public required string DeliveryId { get; init; }
    public required ChannelId ChannelId { get; init; }
    public required string AccountId { get; init; }
    public required string ConversationId { get; init; }
    public required string PlatformMessageId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

public record ChannelDeliveryFailedEvent
{
    public required string DeliveryId { get; init; }
    public required ChannelId ChannelId { get; init; }
    public required string AccountId { get; init; }
    public required string ConversationId { get; init; }
    public required string Error { get; init; }
    public required int AttemptCount { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

public record ChannelListResult
{
    public required IReadOnlyList<ChannelHealthReport> Channels { get; init; }
}

public record ChannelStatusRequest
{
    public required ChannelId ChannelId { get; init; }
    public required string AccountId { get; init; }
}

public record ChannelControlRequest
{
    public required ChannelId ChannelId { get; init; }
    public required string AccountId { get; init; }
}
```

## DM and Group Policies

Each channel account can define access policies controlling who may message the
agent and under what conditions.

### Policy Configuration

```json
{
  "MsClaw": {
    "Channels": {
      "Telegram": {
        "Accounts": {
          "default": {
            "BotToken": "...",
            "Policy": {
              "DmPolicy": "open",
              "GroupPolicy": "mention-only",
              "AllowList": [],
              "DenyList": [],
              "RequireMention": true,
              "GroupIntroHint": "I'm an AI assistant. Mention me to chat."
            }
          }
        }
      }
    }
  }
}
```

### Policy Types

| Policy | Behavior |
|--------|----------|
| `open` | Anyone can message the agent. |
| `allowlist` | Only users/groups on the allow list. Messages from others are dropped or denied. |
| `pairing` | Sender must complete a pairing flow (approve via operator) before messaging. |
| `disabled` | Channel accepts no inbound messages. Outbound-only. |

### Evaluation Order

1. Check if the channel is enabled and running.
2. Check deny list — if sender is denied, reject immediately.
3. Check policy type:
   - `open` → allow.
   - `allowlist` → check sender against allow list.
   - `pairing` → check if sender is paired. If not, trigger pairing flow.
   - `disabled` → reject.
4. For group messages with `RequireMention: true`, check if agent is mentioned.
5. Return `PolicyDecision` (allowed/denied + action).

## Target Channels

### Telegram

| Property | Value |
|----------|-------|
| Transport | Bot API (long-polling via `getUpdates`, or webhook) |
| Auth | Bot token from BotFather |
| Library | HTTP client to Telegram Bot API (no external SDK dependency) |
| Capabilities | Text, Markdown, Images, Files, Audio, Video, Polls, Reactions, Groups, Threads, EditMessages |
| Text Limit | 4,096 characters |
| Formatting | MarkdownV2 or HTML |

### Slack

| Property | Value |
|----------|-------|
| Transport | Socket Mode (WebSocket) or Events API (webhook) |
| Auth | Bot token + App token (Socket Mode) or OAuth (Events API) |
| Library | HTTP client to Slack Web API |
| Capabilities | Text, RichFormatting, Images, Files, Threads, Reactions, Mentions, Groups, EditMessages |
| Text Limit | 40,000 characters per block |
| Formatting | mrkdwn (Slack's markdown variant), Block Kit for rich content |

### Discord

| Property | Value |
|----------|-------|
| Transport | Discord Gateway (WebSocket) |
| Auth | Bot token |
| Library | HTTP client to Discord API + Gateway WebSocket |
| Capabilities | Text, Markdown, Images, Files, Audio, Video, Threads, Reactions, Mentions, Groups, EditMessages |
| Text Limit | 2,000 characters |
| Formatting | Discord-flavored markdown |

### WhatsApp

| Property | Value |
|----------|-------|
| Transport | WhatsApp Cloud API (webhook) or Baileys (unofficial WebSocket) |
| Auth | Cloud API token or QR-code pairing (Baileys) |
| Library | HTTP client to Cloud API, or Baileys interop (child process) |
| Capabilities | Text, Images, Files, Audio, Video, Reactions, Groups, Mentions, ReadReceipts |
| Text Limit | 65,536 characters |
| Formatting | WhatsApp formatting (`*bold*`, `_italic_`, `~strike~`, `` `code` ``) |

### Signal

| Property | Value |
|----------|-------|
| Transport | signal-cli (linked device mode) or libsignal bridge |
| Auth | Linked device pairing |
| Library | signal-cli child process (JSON-RPC) |
| Capabilities | Text, Images, Files, Audio, Video, Groups, Reactions, Mentions, ReadReceipts |
| Text Limit | ~2,000 characters |
| Formatting | Plain text (no rich formatting) |

### iMessage

| Property | Value |
|----------|-------|
| Transport | BlueBubbles or similar macOS bridge |
| Auth | Local API (no external auth) |
| Library | HTTP client to bridge API |
| Capabilities | Text, Images, Files, Audio, Video, Groups, Reactions, Threads, ReadReceipts, TypingIndicators |
| Text Limit | ~20,000 characters |
| Formatting | Plain text or attributed strings |

### Email

| Property | Value |
|----------|-------|
| Transport | IMAP (polling) for inbound, SMTP for outbound |
| Auth | OAuth or app password |
| Library | MailKit |
| Capabilities | Text, RichFormatting, Images, Files, Threads |
| Text Limit | No practical limit |
| Formatting | HTML (full markdown → HTML conversion) |

### Matrix

| Property | Value |
|----------|-------|
| Transport | Client-Server API (long-polling `/sync`) |
| Auth | Access token |
| Library | HTTP client to Matrix CS API |
| Capabilities | Text, Markdown, Images, Files, Audio, Video, Threads, Reactions, Groups, EditMessages, ReadReceipts |
| Text Limit | 65,536 characters |
| Formatting | HTML in `formatted_body` field |

### IRC

| Property | Value |
|----------|-------|
| Transport | Persistent TCP connection |
| Auth | SASL or NickServ |
| Library | Raw TCP/TLS socket |
| Capabilities | Text, Groups, Mentions |
| Text Limit | 512 bytes per IRC message (including protocol overhead) |
| Formatting | IRC formatting codes (bold, italic, color) |

### WebChat

| Property | Value |
|----------|-------|
| Transport | Embedded — uses the Gateway's SignalR hub directly |
| Auth | Gateway token (same as operator auth) |
| Library | None — WebChat is a thin layer over the existing `Send`/`Agent` hub methods |
| Capabilities | Text, Markdown, Images, Files |
| Text Limit | No limit |
| Formatting | Raw markdown (client renders) |

WebChat is a special case — it is not an external platform adapter but a
built-in channel that routes through the existing SignalR hub methods. It
exists in the channel registry for consistency (status, policy, config) but
its transport is the hub itself.

## Configuration

Channel configuration lives under `MsClaw:Channels` in `appsettings.json`.

```json
{
  "MsClaw": {
    "Channels": {
      "Defaults": {
        "GroupPolicy": "mention-only",
        "RequireMention": true,
        "DeliveryRetries": 3,
        "DeliveryRetryBackoffSeconds": [1, 5, 15]
      },
      "Telegram": {
        "Enabled": true,
        "Accounts": {
          "default": {
            "BotToken": null,
            "Transport": "polling",
            "Policy": {
              "DmPolicy": "open",
              "GroupPolicy": "mention-only",
              "AllowList": [],
              "DenyList": []
            }
          }
        }
      },
      "Slack": {
        "Enabled": false,
        "Accounts": {
          "default": {
            "BotToken": null,
            "AppToken": null,
            "Transport": "socket-mode",
            "Policy": {
              "DmPolicy": "open",
              "GroupPolicy": "mention-only"
            }
          }
        }
      },
      "Discord": {
        "Enabled": false,
        "Accounts": {
          "default": {
            "BotToken": null,
            "Policy": {
              "DmPolicy": "open",
              "GroupPolicy": "mention-only"
            }
          }
        }
      },
      "WhatsApp": {
        "Enabled": false,
        "Accounts": {
          "default": {
            "Transport": "cloud-api",
            "ApiToken": null,
            "PhoneNumberId": null,
            "Policy": {
              "DmPolicy": "open"
            }
          }
        }
      },
      "Signal": {
        "Enabled": false,
        "Accounts": {
          "default": {
            "SignalCliPath": "signal-cli",
            "PhoneNumber": null,
            "Policy": {
              "DmPolicy": "allowlist",
              "AllowList": []
            }
          }
        }
      },
      "Email": {
        "Enabled": false,
        "Accounts": {
          "default": {
            "ImapHost": null,
            "ImapPort": 993,
            "SmtpHost": null,
            "SmtpPort": 587,
            "Username": null,
            "Password": null,
            "PollIntervalSeconds": 60,
            "Policy": {
              "DmPolicy": "allowlist",
              "AllowList": []
            }
          }
        }
      },
      "WebChat": {
        "Enabled": true
      }
    }
  }
}
```

### Secrets Management

Channel credentials (bot tokens, API keys, passwords) **must not** be stored
in `appsettings.json` in production. Use:

- **Environment variables**: `MsClaw__Channels__Telegram__Accounts__default__BotToken`
- **User secrets**: `dotnet user-secrets set "MsClaw:Channels:Telegram:Accounts:default:BotToken" "<token>"`
- **Azure Key Vault** or other secret managers for deployed environments.

The configuration section names use .NET's standard configuration binding with
the `__` separator for environment variables and `:` separator for JSON paths.

## Mapping to OpenClaw

| OpenClaw | MsClaw | Notes |
|----------|--------|-------|
| `ChannelPlugin<T>` (TypeScript) | `IChannelAdapter` (C#) | Same concept, DI-registered |
| `ChannelOutboundAdapter` | `IChannelOutboundHandler` | Same role, C# interface |
| `ChannelGatewayAdapter.startAccount()` | `IChannelAdapter.StartAsync()` | Lifecycle via `IHostedService` |
| `ChannelConfigAdapter` | `IChannelConfigurator` | Account resolution |
| `ChannelSecurityAdapter` | `IChannelPolicyProvider` | DM/group policies |
| `src/channels/plugins/normalize/*.ts` | Normalization in each adapter's inbound handler | Per-adapter, not centralized |
| `src/channels/plugins/index.ts` (cached registry) | `ChannelRegistry` (static catalog) | Same pattern, C# static class |
| `src/infra/outbound/deliver.ts` | `IChannelOutboundHandler` + `IDeliveryQueue` | Split into handler + queue |
| `src/infra/outbound/delivery-queue.ts` | `IDeliveryQueue` | Same concept |
| `src/gateway/server-channels.ts` | `IChannelManager` (`IHostedService`) | Same role |
| `extensions/*/src/channel.ts` | Per-channel adapter projects or classes | Same modular pattern |
| Plugin lazy-loading from registry | DI-registered `IChannelAdapter` implementations | Same concept, DI-based |
| `triggerInternalHook("message.received")` | `OnChannelMessageReceived` (SignalR event) | Hub events replace hooks |
| `triggerInternalHook("message.sent")` | `OnChannelMessageDelivered` (SignalR event) | Hub events replace hooks |
| `CHAT_CHANNEL_ORDER` | `ChannelDefinition.DisplayOrder` | Same concept |
| `ChannelCapabilities` (TypeScript object) | `ChannelCapabilities` (C# flags enum) | Same data, different shape |

## Open Questions

- Should channel adapters run in-process or as separate sidecar processes?
  - In-process is simpler and shares the DI container. Sidecars provide isolation
    but add IPC complexity. Recommend in-process for v1.
- Should we support hot-reload of channel configuration without gateway restart?
  - `IOptionsMonitor<T>` provides this for free in .NET. The `ChannelManager`
    would need to diff the old and new configs and start/stop adapters accordingly.
- Should channels that require external runtimes (signal-cli, Baileys/Node.js)
  be managed child processes or require manual external setup?
  - Recommend: managed child processes for v1, with a config escape hatch to
    point at an existing external service.
- Should the delivery queue be persistent (survive gateway crashes)?
  - In-memory for v1 with an interface that allows swapping to SQLite later.
- How should we handle rate limiting from external platforms?
  - Each adapter should implement platform-specific rate limiting. The outbound
    handler should respect `Retry-After` headers and backoff accordingly.
- Should we support channel-specific agent tools (e.g., Slack-specific tools
  like `slack.create_channel` or `telegram.pin_message`)?
  - OpenClaw supports this via `agentTools` on the plugin. Defer to v2.
