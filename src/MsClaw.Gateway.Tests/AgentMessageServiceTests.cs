using GitHub.Copilot.SDK;
using MsClaw.Gateway.Hosting;
using MsClaw.Gateway.Services;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class AgentMessageServiceTests
{
    [Fact]
    public async Task SendAsync_AcquiredGate_ReleasesGateAfterStreamCompletes()
    {
        var gate = new StubConcurrencyGate();
        ISessionMap sessionMap = new CallerRegistry();
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
        var sut = new AgentMessageService(gate, sessionMap, client, hostedService);

        var results = await ReadAllAsync(sut.SendAsync("caller-1", "hello", CancellationToken.None));

        Assert.Single(gate.TryAcquireCallerKeys, "caller-1");
        Assert.Single(gate.ReleaseCallerKeys, "caller-1");
        Assert.Equal("hello", session.LastPrompt);
        Assert.True(session.Disposed);
        Assert.Collection(
            results,
            sessionEvent => Assert.IsType<AssistantMessageDeltaEvent>(sessionEvent),
            sessionEvent => Assert.IsType<SessionIdleEvent>(sessionEvent));
    }

    [Fact]
    public async Task SendAsync_GateBusy_ThrowsInvalidOperationException()
    {
        var gate = new StubConcurrencyGate { AcquireResult = false };
        ISessionMap sessionMap = new CallerRegistry();
        await using var client = new StubGatewayClient();
        var hostedService = new StubGatewayHostedService { SystemMessage = "system message" };
        var sut = new AgentMessageService(gate, sessionMap, client, hostedService);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ReadAllAsync(sut.SendAsync("caller-1", "hello", CancellationToken.None)));

        Assert.Equal("Caller 'caller-1' already has an active run.", exception.Message);
        Assert.Empty(gate.ReleaseCallerKeys);
        Assert.Equal(0, client.CreateSessionCallCount);
    }

    [Fact]
    public async Task SendAsync_UnknownCaller_CreatesSessionAndStoresSessionId()
    {
        var gate = new StubConcurrencyGate();
        ISessionMap sessionMap = new CallerRegistry();
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
        var sut = new AgentMessageService(gate, sessionMap, client, hostedService);

        _ = await ReadAllAsync(sut.SendAsync("caller-1", "hello", CancellationToken.None));

        Assert.Equal(1, client.CreateSessionCallCount);
        Assert.Equal(0, client.ResumeSessionCallCount);
        Assert.Equal("session-created", sessionMap.GetSessionId("caller-1"));
        var sessionConfig = Assert.IsType<SessionConfig>(client.LastCreateSessionConfig);
        var systemMessage = Assert.IsType<SystemMessageConfig>(sessionConfig.SystemMessage);
        Assert.Equal("system message", systemMessage.Content);
    }

    [Fact]
    public async Task SendAsync_KnownCaller_ResumesExistingSession()
    {
        var gate = new StubConcurrencyGate();
        ISessionMap sessionMap = new CallerRegistry();
        sessionMap.SetSessionId("caller-1", "session-existing");
        var session = new StubGatewaySession("session-existing");
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
            ResumeSessionResult = session
        };
        var hostedService = new StubGatewayHostedService { SystemMessage = "system message" };
        var sut = new AgentMessageService(gate, sessionMap, client, hostedService);

        _ = await ReadAllAsync(sut.SendAsync("caller-1", "hello", CancellationToken.None));

        Assert.Equal(0, client.CreateSessionCallCount);
        Assert.Equal(1, client.ResumeSessionCallCount);
        Assert.Equal("session-existing", client.LastResumedSessionId);
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
}
