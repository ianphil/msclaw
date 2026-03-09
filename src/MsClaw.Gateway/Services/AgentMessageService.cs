using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;
using MsClaw.Gateway.Hosting;
using MsClaw.Gateway.Services.Tools;

namespace MsClaw.Gateway.Services;

/// <summary>
/// Coordinates gateway message delivery across concurrency, sessions, and streaming.
/// </summary>
public sealed class AgentMessageService
{
    private readonly IConcurrencyGate concurrencyGate;
    private readonly ISessionPool sessionPool;
    private readonly IGatewayClient client;
    private readonly IGatewayHostedService hostedService;
    private readonly IToolCatalog toolCatalog;
    private readonly IToolExpander toolExpander;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> activeRuns = new();
    private readonly ConcurrentDictionary<string, ToolSyncState> toolSyncStates = new();

    /// <summary>
    /// Initializes the service with the shared coordination, session pool, and hosting dependencies.
    /// </summary>
    public AgentMessageService(
        IConcurrencyGate concurrencyGate,
        ISessionPool sessionPool,
        IGatewayClient client,
        IGatewayHostedService hostedService)
        : this(concurrencyGate, sessionPool, client, hostedService, EmptyToolCatalog.Instance, EmptyToolExpander.Instance)
    {
    }

    /// <summary>
    /// Initializes the service with tool catalog dependencies used to populate per-session custom tools.
    /// </summary>
    public AgentMessageService(
        IConcurrencyGate concurrencyGate,
        ISessionPool sessionPool,
        IGatewayClient client,
        IGatewayHostedService hostedService,
        IToolCatalog toolCatalog,
        IToolExpander toolExpander)
    {
        this.concurrencyGate = concurrencyGate;
        this.sessionPool = sessionPool;
        this.client = client;
        this.hostedService = hostedService;
        this.toolCatalog = toolCatalog;
        this.toolExpander = toolExpander;
    }

    /// <summary>
    /// Sends a prompt for the specified caller and streams the resulting SDK events.
    /// </summary>
    public async IAsyncEnumerable<SessionEvent> SendAsync(
        string callerKey,
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        if (concurrencyGate.TryAcquire(callerKey) is false)
        {
            throw new InvalidOperationException($"Caller '{callerKey}' already has an active run.");
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        activeRuns[callerKey] = linkedCts;
        try
        {
            var session = await GetOrCreateSessionAsync(callerKey, linkedCts.Token);
            session = await SyncToolsIfNeededAsync(callerKey, session, linkedCts.Token);

            var (subscription, events) = SessionEventBridge.Bridge(session, linkedCts.Token);
            try
            {
                await session.SendAsync(new MessageOptions { Prompt = prompt }, linkedCts.Token);
                await foreach (var sessionEvent in events.WithCancellation(linkedCts.Token))
                {
                    yield return sessionEvent;
                }
            }
            finally
            {
                subscription.Dispose();
            }
        }
        finally
        {
            activeRuns.TryRemove(callerKey, out _);
            concurrencyGate.TryRelease(callerKey);
        }
    }

    /// <summary>
    /// Aborts the active run for the specified caller, cancelling the event stream and releasing the concurrency gate.
    /// </summary>
    public async Task AbortAsync(string callerKey, CancellationToken cancellationToken = default)
    {
        var session = sessionPool.TryGet(callerKey);
        if (session is not null)
        {
            await session.AbortAsync(cancellationToken);
        }

        if (activeRuns.TryRemove(callerKey, out var cts))
        {
            await cts.CancelAsync();
        }

        concurrencyGate.TryRelease(callerKey);
    }

    /// <summary>
    /// Retrieves an existing session for the caller or creates a new streaming session with the hosted system message.
    /// </summary>
    private Task<IGatewaySession> GetOrCreateSessionAsync(string callerKey, CancellationToken cancellationToken)
    {
        return sessionPool.GetOrCreateAsync(callerKey, async ct =>
        {
            var sessionConfig = new SessionConfig { Streaming = true };
            var tools = new List<AIFunction>(toolCatalog.GetDefaultTools());
            var sessionHolder = new SessionHolder();
            tools.Add(toolExpander.CreateExpandToolsFunction(sessionHolder, tools));
            sessionConfig.Tools = tools;
            if (string.IsNullOrWhiteSpace(hostedService.SystemMessage) is false)
            {
                sessionConfig.SystemMessage = new SystemMessageConfig
                {
                    Mode = SystemMessageMode.Append,
                    Content = hostedService.SystemMessage
                };
            }

            var session = await client.CreateSessionAsync(sessionConfig, ct);
            sessionHolder.Bind(session);
            toolSyncStates[callerKey] = new ToolSyncState(tools, tools.Count);

            return session;
        }, cancellationToken);
    }

    /// <summary>
    /// Resumes the session with an updated tool list when expand_tools has added tools since the last sync.
    /// </summary>
    private async Task<IGatewaySession> SyncToolsIfNeededAsync(string callerKey, IGatewaySession session, CancellationToken cancellationToken)
    {
        if (toolSyncStates.TryGetValue(callerKey, out var state) is false || state.Tools.Count <= state.SyncedCount)
        {
            return session;
        }

        var resumed = await client.ResumeSessionAsync(
            session.SessionId,
            new ResumeSessionConfig { Tools = state.Tools },
            cancellationToken);

        state.SyncedCount = state.Tools.Count;
        await sessionPool.ReplaceAsync(callerKey, resumed);

        return resumed;
    }

    /// <summary>
    /// Tracks the mutable tool list and how many tools the CLI knows about.
    /// </summary>
    private sealed class ToolSyncState(IList<AIFunction> tools, int syncedCount)
    {
        public IList<AIFunction> Tools { get; } = tools;

        public int SyncedCount { get; set; } = syncedCount;
    }

    /// <summary>
    /// Provides an empty tool catalog for tests that exercise message flow without tool wiring.
    /// </summary>
    private sealed class EmptyToolCatalog : IToolCatalog
    {
        public static EmptyToolCatalog Instance { get; } = new();

        public IReadOnlyList<AIFunction> GetDefaultTools()
        {
            return [];
        }

        public IReadOnlyList<AIFunction> GetToolsByName(IEnumerable<string> names)
        {
            return [];
        }

        public IReadOnlyList<string> GetCatalogToolNames()
        {
            return [];
        }

        public IReadOnlyList<string> GetToolNamesByProvider(string providerName)
        {
            return [];
        }

        public IReadOnlyList<string> SearchTools(string query)
        {
            return [];
        }

        public ToolDescriptor? GetDescriptor(string toolName)
        {
            return null;
        }
    }

    /// <summary>
    /// Provides a no-op expander for tests that do not care about dynamic tool loading.
    /// </summary>
    private sealed class EmptyToolExpander : IToolExpander
    {
        public static EmptyToolExpander Instance { get; } = new();

        public AIFunction CreateExpandToolsFunction(SessionHolder sessionHolder, IList<AIFunction> currentSessionTools)
        {
            return AIFunctionFactory.Create(
                static () => new { },
                "expand_tools",
                "No-op expand_tools function used by message-flow tests.");
        }
    }
}
