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

        try
        {
            var session = await GetOrCreateSessionAsync(callerKey, cancellationToken);

            var (subscription, events) = SessionEventBridge.Bridge(session, cancellationToken);
            try
            {
                await session.SendAsync(new MessageOptions { Prompt = prompt }, cancellationToken);
                await foreach (var sessionEvent in events.WithCancellation(cancellationToken))
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
            concurrencyGate.Release(callerKey);
        }
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

            return session;
        }, cancellationToken);
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
