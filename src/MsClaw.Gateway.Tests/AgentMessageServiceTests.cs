using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using MsClaw.Gateway.Hosting;
using MsClaw.Gateway.Services.Tools;
using MsClaw.Gateway.Services;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class AgentMessageServiceTests
{
    [Fact]
    public async Task SendAsync_AcquiredGate_ReleasesGateAfterStreamCompletes()
    {
        var gate = new StubConcurrencyGate();
        await using var pool = new SessionPool();
        var session = new StubGatewaySession("session-1");
        session.SendAsyncHandler = static stubSession =>
        {
            stubSession.Emit(new AssistantMessageDeltaEvent
            {
                Data = new AssistantMessageDeltaData
                {
                    MessageId = "message-1",
                    DeltaContent = "hello"
                }
            });
            stubSession.Emit(new SessionIdleEvent
            {
                Data = new SessionIdleData()
            });

            return Task.CompletedTask;
        };

        await using var client = new StubGatewayClient
        {
            CreateSessionResult = session
        };
        var hostedService = new StubGatewayHostedService { SystemMessage = "system message" };
        var sut = new AgentMessageService(gate, pool, client, hostedService);

        var results = await ReadAllAsync(sut.SendAsync("caller-1", "hello", CancellationToken.None));

        Assert.Single(gate.TryAcquireCallerKeys, "caller-1");
        Assert.Single(gate.ReleaseCallerKeys, "caller-1");
        Assert.Equal("hello", session.LastPrompt);
        Assert.False(session.Disposed);
        Assert.Collection(
            results,
            sessionEvent => Assert.IsType<AssistantMessageDeltaEvent>(sessionEvent),
            sessionEvent => Assert.IsType<SessionIdleEvent>(sessionEvent));
    }

    [Fact]
    public async Task SendAsync_GateBusy_ThrowsInvalidOperationException()
    {
        var gate = new StubConcurrencyGate { AcquireResult = false };
        await using var pool = new SessionPool();
        await using var client = new StubGatewayClient();
        var hostedService = new StubGatewayHostedService { SystemMessage = "system message" };
        var sut = new AgentMessageService(gate, pool, client, hostedService);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ReadAllAsync(sut.SendAsync("caller-1", "hello", CancellationToken.None)));

        Assert.Equal("Caller 'caller-1' already has an active run.", exception.Message);
        Assert.Empty(gate.ReleaseCallerKeys);
        Assert.Equal(0, client.CreateSessionCallCount);
    }

    [Fact]
    public async Task SendAsync_UnknownCaller_CreatesSessionViaPool()
    {
        var gate = new StubConcurrencyGate();
        await using var pool = new SessionPool();
        var session = new StubGatewaySession("session-created");
        session.SendAsyncHandler = static stubSession =>
        {
            stubSession.Emit(new SessionIdleEvent
            {
                Data = new SessionIdleData()
            });

            return Task.CompletedTask;
        };

        await using var client = new StubGatewayClient
        {
            CreateSessionResult = session
        };
        var hostedService = new StubGatewayHostedService { SystemMessage = "system message" };
        var sut = new AgentMessageService(gate, pool, client, hostedService);

        _ = await ReadAllAsync(sut.SendAsync("caller-1", "hello", CancellationToken.None));

        Assert.Equal(1, client.CreateSessionCallCount);
        Assert.Same(session, pool.TryGet("caller-1"));
        Assert.False(session.Disposed);
        var sessionConfig = Assert.IsType<SessionConfig>(client.LastCreateSessionConfig);
        var systemMessage = Assert.IsType<SystemMessageConfig>(sessionConfig.SystemMessage);
        Assert.Equal("system message", systemMessage.Content);
    }

    [Fact]
    public async Task SendAsync_KnownCaller_ReusesPooledSession()
    {
        var gate = new StubConcurrencyGate();
        await using var pool = new SessionPool();
        var session = new StubGatewaySession("session-existing");
        session.SendAsyncHandler = static stubSession =>
        {
            stubSession.Emit(new SessionIdleEvent
            {
                Data = new SessionIdleData()
            });

            return Task.CompletedTask;
        };

        // Pre-populate the pool with the session
        _ = await pool.GetOrCreateAsync("caller-1", (cancellationToken) => Task.FromResult<IGatewaySession>(session));
        await using var client = new StubGatewayClient();
        var hostedService = new StubGatewayHostedService { SystemMessage = "system message" };
        var sut = new AgentMessageService(gate, pool, client, hostedService);

        _ = await ReadAllAsync(sut.SendAsync("caller-1", "hello", CancellationToken.None));

        Assert.Equal(0, client.CreateSessionCallCount);
        Assert.Equal(0, client.ResumeSessionCallCount);
        Assert.Same(session, pool.TryGet("caller-1"));
        Assert.Equal("hello", session.LastPrompt);
    }

    [Fact]
    public async Task SendAsync_UnknownCaller_PopulatesSessionConfigToolsWithDefaultsAndExpandTools()
    {
        var defaultTool = CreateFunction("default_tool", "Default tool");
        var expandTool = CreateFunction("expand_tools", "Expand tools");
        var session = new StubGatewaySession("session-created");
        session.SendAsyncHandler = static stubSession =>
        {
            stubSession.Emit(new SessionIdleEvent
            {
                Data = new SessionIdleData()
            });

            return Task.CompletedTask;
        };

        await using var client = new StubGatewayClient
        {
            CreateSessionResult = session
        };
        await using var provider = CreateServiceProvider(client, new StubToolCatalog(defaultTool), new StubToolExpander(expandTool));
        var sut = provider.GetRequiredService<AgentMessageService>();

        _ = await ReadAllAsync(sut.SendAsync("caller-1", "hello", CancellationToken.None));

        var sessionConfig = Assert.IsType<SessionConfig>(client.LastCreateSessionConfig);
        Assert.NotNull(sessionConfig.Tools);
        Assert.Equal(["default_tool", "expand_tools"], sessionConfig.Tools.Select(static tool => tool.Name));
    }

    [Fact]
    public async Task SendAsync_UnknownCaller_LeavesCliToolWhitelistsUnsetWhenConfiguringCustomTools()
    {
        var defaultTool = CreateFunction("default_tool", "Default tool");
        var expandTool = CreateFunction("expand_tools", "Expand tools");
        var session = new StubGatewaySession("session-created");
        session.SendAsyncHandler = static stubSession =>
        {
            stubSession.Emit(new SessionIdleEvent
            {
                Data = new SessionIdleData()
            });

            return Task.CompletedTask;
        };

        await using var client = new StubGatewayClient
        {
            CreateSessionResult = session
        };
        await using var provider = CreateServiceProvider(client, new StubToolCatalog(defaultTool), new StubToolExpander(expandTool));
        var sut = provider.GetRequiredService<AgentMessageService>();

        _ = await ReadAllAsync(sut.SendAsync("caller-1", "hello", CancellationToken.None));

        var sessionConfig = Assert.IsType<SessionConfig>(client.LastCreateSessionConfig);
        Assert.Null(sessionConfig.AvailableTools);
        Assert.Null(sessionConfig.ExcludedTools);
        Assert.NotNull(sessionConfig.Tools);
        Assert.Contains(sessionConfig.Tools, static tool => tool.Name == "expand_tools");
    }

    private static async Task<List<SessionEvent>> ReadAllAsync(IAsyncEnumerable<SessionEvent> events)
    {
        var results = new List<SessionEvent>();
        await foreach (var sessionEvent in events)
        {
            results.Add(sessionEvent);
        }

        return results;
    }

    private static ServiceProvider CreateServiceProvider(IGatewayClient client, IToolCatalog toolCatalog, IToolExpander toolExpander)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConcurrencyGate, StubConcurrencyGate>();
        services.AddSingleton<ISessionPool, SessionPool>();
        services.AddSingleton(client);
        services.AddSingleton<IGatewayClient>(client);
        services.AddSingleton<IGatewayHostedService>(new StubGatewayHostedService { SystemMessage = "system message" });
        services.AddSingleton(toolCatalog);
        services.AddSingleton(toolExpander);
        services.AddSingleton<AgentMessageService>();

        return services.BuildServiceProvider();
    }

    private static AIFunction CreateFunction(string name, string description)
    {
        return AIFunctionFactory.Create(
            (string input) => input,
            name,
            description);
    }

    private sealed class StubConcurrencyGate : IConcurrencyGate
    {
        public bool AcquireResult { get; set; } = true;

        public List<string> TryAcquireCallerKeys { get; } = [];

        public List<string> ReleaseCallerKeys { get; } = [];

        public bool TryAcquire(string callerKey)
        {
            TryAcquireCallerKeys.Add(callerKey);

            return AcquireResult;
        }

        public void Release(string callerKey)
        {
            ReleaseCallerKeys.Add(callerKey);
        }
    }

    private sealed class StubGatewayClient : IGatewayClient
    {
        public StubGatewaySession? CreateSessionResult { get; set; }

        public StubGatewaySession? ResumeSessionResult { get; set; }

        public int CreateSessionCallCount { get; private set; }

        public int ResumeSessionCallCount { get; private set; }

        public string? LastResumedSessionId { get; private set; }

        public SessionConfig? LastCreateSessionConfig { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IGatewaySession> CreateSessionAsync(SessionConfig? config = null, CancellationToken cancellationToken = default)
        {
            CreateSessionCallCount++;
            LastCreateSessionConfig = config;

            return Task.FromResult<IGatewaySession>(CreateSessionResult ?? new StubGatewaySession("created-session"));
        }

        public Task<IGatewaySession> ResumeSessionAsync(string sessionId, ResumeSessionConfig? config = null, CancellationToken cancellationToken = default)
        {
            ResumeSessionCallCount++;
            LastResumedSessionId = sessionId;

            return Task.FromResult<IGatewaySession>(ResumeSessionResult ?? new StubGatewaySession(sessionId));
        }

        public Task<IReadOnlyList<SessionMetadata>> ListSessionsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SessionMetadata>>([]);
        }

        public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubGatewaySession(string sessionId) : IGatewaySession
    {
        private readonly List<Action<SessionEvent>> handlers = [];

        public Func<StubGatewaySession, Task>? SendAsyncHandler { get; set; }

        public string? LastPrompt { get; private set; }

        public bool Disposed { get; private set; }

        public string SessionId { get; } = sessionId;

        public IDisposable On(Action<SessionEvent> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            handlers.Add(handler);

            return new Subscription(handlers, handler);
        }

        public async Task SendAsync(MessageOptions options, CancellationToken cancellationToken = default)
        {
            LastPrompt = options.Prompt;
            if (SendAsyncHandler is not null)
            {
                await SendAsyncHandler(this);
            }
        }

        public Task AbortAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SessionEvent>> GetMessagesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SessionEvent>>([]);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;

            return ValueTask.CompletedTask;
        }

        public void Emit(SessionEvent sessionEvent)
        {
            foreach (var handler in handlers.ToArray())
            {
                handler(sessionEvent);
            }
        }

        private sealed class Subscription(List<Action<SessionEvent>> handlers, Action<SessionEvent> handler) : IDisposable
        {
            public void Dispose()
            {
                _ = handlers.Remove(handler);
            }
        }
    }

    private sealed class StubGatewayHostedService : IGatewayHostedService
    {
        public string? SystemMessage { get; init; }

        public GatewayState State { get; init; } = GatewayState.Ready;

        public string? Error { get; init; }

        public bool IsReady => State is GatewayState.Ready;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubToolCatalog(params AIFunction[] defaultTools) : IToolCatalog
    {
        public IReadOnlyList<AIFunction> GetDefaultTools()
        {
            return defaultTools;
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

    private sealed class StubToolExpander(AIFunction expandToolsFunction) : IToolExpander
    {
        public AIFunction CreateExpandToolsFunction(SessionHolder sessionHolder, IList<AIFunction> currentSessionTools)
        {
            return expandToolsFunction;
        }
    }
}
