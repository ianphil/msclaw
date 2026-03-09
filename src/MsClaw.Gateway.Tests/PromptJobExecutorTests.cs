using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;
using MsClaw.Gateway.Hosting;
using MsClaw.Gateway.Services;
using MsClaw.Gateway.Services.Cron;
using MsClaw.Gateway.Services.Tools;
using Xunit;

namespace MsClaw.Gateway.Tests;

public sealed class PromptJobExecutorTests
{
    [Fact]
    public void PayloadType_ReturnsPromptPayloadType()
    {
        var sut = new PromptJobExecutor(
            new RecordingSessionPool(),
            new StubGatewayClient(),
            new StubGatewayHostedService(),
            new StubToolCatalog());

        Assert.Equal(typeof(PromptPayload), sut.PayloadType);
    }

    [Fact]
    public async Task ExecuteAsync_PromptPayload_CreatesIsolatedSessionAndReturnsSuccess()
    {
        var session = new StubGatewaySession("session-1");
        session.SendAsyncHandler = static stubSession =>
        {
            stubSession.Emit(new AssistantMessageEvent
            {
                Data = new AssistantMessageData
                {
                    MessageId = "message-1",
                    Content = "Cron run complete."
                }
            });
            stubSession.Emit(new SessionIdleEvent
            {
                Data = new SessionIdleData()
            });

            return Task.CompletedTask;
        };

        var sessionPool = new RecordingSessionPool();
        await using var client = new StubGatewayClient
        {
            CreateSessionResult = session
        };
        var sut = new PromptJobExecutor(
            sessionPool,
            client,
            new StubGatewayHostedService { SystemMessage = "system message" },
            new StubToolCatalog());
        var job = CreatePromptJob(new PromptPayload("Check my inbox.", null, null));

        var result = await sut.ExecuteAsync(job, "run-123", CancellationToken.None);

        Assert.Equal("cron:job-1:run-123", sessionPool.LastGetOrCreateCallerKey);
        Assert.Equal(["cron:job-1:run-123"], sessionPool.RemovedCallerKeys);
        Assert.Equal("Check my inbox.", session.LastPrompt);
        Assert.Equal(CronRunOutcome.Success, result.Outcome);
        Assert.Equal("Cron run complete.", result.Content);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(1, client.CreateSessionCallCount);
        var sessionConfig = Assert.IsType<SessionConfig>(client.LastCreateSessionConfig);
        Assert.True(sessionConfig.Streaming);
        Assert.Equal("system message", Assert.IsType<SystemMessageConfig>(sessionConfig.SystemMessage).Content);
    }

    [Fact]
    public async Task ExecuteAsync_PreloadToolNames_PopulatesSessionConfigToolsWithCatalogTools()
    {
        var defaultTool = CreateFunction("default_tool", "Default tool");
        var preloadTool = CreateFunction("mail_tool", "Mail tool");
        var session = new StubGatewaySession("session-1");
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
        var sut = new PromptJobExecutor(
            new RecordingSessionPool(),
            client,
            new StubGatewayHostedService(),
            new StubToolCatalog([defaultTool], new Dictionary<string, AIFunction>(StringComparer.Ordinal)
            {
                ["mail_tool"] = preloadTool
            }));
        var job = CreatePromptJob(new PromptPayload("Check mail.", ["mail_tool"], "gpt-5"));

        _ = await sut.ExecuteAsync(job, "run-456", CancellationToken.None);

        var sessionConfig = Assert.IsType<SessionConfig>(client.LastCreateSessionConfig);
        Assert.NotNull(sessionConfig.Tools);
        Assert.Equal(["default_tool", "mail_tool"], sessionConfig.Tools.Select(static tool => tool.Name));
        Assert.Equal("gpt-5", sessionConfig.Model);
    }

    [Fact]
    public async Task ExecuteAsync_SessionSendThrows_ReturnsFailureResult()
    {
        var session = new StubGatewaySession("session-1")
        {
            SendAsyncHandler = static _ => throw new InvalidOperationException("boom")
        };

        await using var client = new StubGatewayClient
        {
            CreateSessionResult = session
        };
        var sessionPool = new RecordingSessionPool();
        var sut = new PromptJobExecutor(
            sessionPool,
            client,
            new StubGatewayHostedService(),
            new StubToolCatalog());

        var result = await sut.ExecuteAsync(CreatePromptJob(new PromptPayload("Check mail.", null, null)), "run-789", CancellationToken.None);

        Assert.Equal(CronRunOutcome.Failure, result.Outcome);
        Assert.Contains("boom", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(["cron:job-1:run-789"], sessionPool.RemovedCallerKeys);
    }

    private static CronJob CreatePromptJob(PromptPayload payload)
    {
        return new CronJob
        {
            Id = "job-1",
            Name = "Prompt Job",
            Schedule = new FixedIntervalSchedule(5_000),
            Payload = payload,
            Status = CronJobStatus.Enabled
        };
    }

    private static AIFunction CreateFunction(string name, string description)
    {
        return AIFunctionFactory.Create(
            static (string input) => input,
            name,
            description);
    }

    private sealed class RecordingSessionPool : ISessionPool
    {
        private readonly Dictionary<string, IGatewaySession> sessions = new(StringComparer.Ordinal);

        public string? LastGetOrCreateCallerKey { get; private set; }

        public List<string> RemovedCallerKeys { get; } = [];

        public async Task<IGatewaySession> GetOrCreateAsync(string callerKey, Func<CancellationToken, Task<IGatewaySession>> factory, CancellationToken cancellationToken = default)
        {
            LastGetOrCreateCallerKey = callerKey;
            if (sessions.TryGetValue(callerKey, out var existingSession))
            {
                return existingSession;
            }

            var session = await factory(cancellationToken);
            sessions[callerKey] = session;

            return session;
        }

        public IGatewaySession? TryGet(string callerKey)
        {
            sessions.TryGetValue(callerKey, out var session);

            return session;
        }

        public Task ReplaceAsync(string callerKey, IGatewaySession newSession)
        {
            sessions[callerKey] = newSession;

            return Task.CompletedTask;
        }

        public async Task RemoveAsync(string callerKey)
        {
            RemovedCallerKeys.Add(callerKey);
            if (sessions.Remove(callerKey, out var session))
            {
                await session.DisposeAsync();
            }
        }

        public IReadOnlyList<(string CallerKey, string SessionId)> ListCallers()
        {
            return sessions.Select(static pair => (pair.Key, pair.Value.SessionId)).ToArray();
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var session in sessions.Values)
            {
                await session.DisposeAsync();
            }

            sessions.Clear();
        }
    }

    private sealed class StubGatewayClient : IGatewayClient
    {
        public IGatewaySession? CreateSessionResult { get; set; }

        public int CreateSessionCallCount { get; private set; }

        public SessionConfig? LastCreateSessionConfig { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IGatewaySession> CreateSessionAsync(SessionConfig? config = null, CancellationToken cancellationToken = default)
        {
            CreateSessionCallCount++;
            LastCreateSessionConfig = config;

            return Task.FromResult(CreateSessionResult ?? new StubGatewaySession("created-session"));
        }

        public Task<IGatewaySession> ResumeSessionAsync(string sessionId, ResumeSessionConfig? config = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
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

        public string SessionId { get; } = sessionId;

        public IDisposable On(Action<SessionEvent> handler)
        {
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

    private sealed class StubToolCatalog(
        IReadOnlyList<AIFunction>? defaultTools = null,
        IReadOnlyDictionary<string, AIFunction>? namedTools = null) : IToolCatalog
    {
        public IReadOnlyList<AIFunction> GetDefaultTools()
        {
            return defaultTools ?? [];
        }

        public IReadOnlyList<AIFunction> GetToolsByName(IEnumerable<string> names)
        {
            if (namedTools is null)
            {
                return [];
            }

            return names
                .Where(namedTools.ContainsKey)
                .Select(name => namedTools[name])
                .ToArray();
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
}
