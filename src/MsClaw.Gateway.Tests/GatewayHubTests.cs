using System.Security.Claims;
using GitHub.Copilot.SDK;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using MsClaw.Gateway.Hosting;
using MsClaw.Gateway.Hubs;
using MsClaw.Gateway.Services;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class GatewayHubTests
{
    [Fact]
    public void GatewayHub_ExtendsStronglyTypedHub()
    {
        Assert.Equal(typeof(Hub<IGatewayHubClient>), typeof(GatewayHub).BaseType);
    }

    [Fact]
    public async Task SendMessage_ConnectionHasNoSession_DelegatesUsingConnectionId()
    {
        var gate = new StubConcurrencyGate();
        await using var pool = new SessionPool();
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
        var messageService = new AgentMessageService(gate, pool, client, new StubGatewayHostedService { SystemMessage = "system" });
        var sut = new GatewayHub(messageService, pool)
        {
            Context = new StubHubCallerContext("connection-1")
        };

        var results = await ReadAllAsync(sut.SendMessage("hello", CancellationToken.None));

        Assert.Single(gate.TryAcquireCallerKeys, "connection-1");
        Assert.Single(gate.ReleaseCallerKeys, "connection-1");
        Assert.Equal("hello", session.LastPrompt);
        Assert.Collection(results, sessionEvent => Assert.IsType<SessionIdleEvent>(sessionEvent));
    }

    [Fact]
    public async Task ListSessions_ReturnsPooledCallerSessionPairs()
    {
        await using var pool = new SessionPool();
        _ = await pool.GetOrCreateAsync("caller-1", (cancellationToken) => Task.FromResult<IGatewaySession>(new StubGatewaySession("session-1")));
        _ = await pool.GetOrCreateAsync("caller-2", (cancellationToken) => Task.FromResult<IGatewaySession>(new StubGatewaySession("session-2")));

        var gate = new StubConcurrencyGate();
        await using var client = new StubGatewayClient();
        var messageService = new AgentMessageService(gate, pool, client, new StubGatewayHostedService { SystemMessage = "system" });
        var sut = new GatewayHub(messageService, pool)
        {
            Context = new StubHubCallerContext("connection-1")
        };

        var sessions = await sut.ListSessions(CancellationToken.None);

        Assert.Equal(2, sessions.Count);
    }

    [Fact]
    public async Task GetHistory_CallerHasPooledSession_ReturnsSessionEvents()
    {
        await using var pool = new SessionPool();
        var history = new List<SessionEvent>
        {
            new UserMessageEvent
            {
                Data = new UserMessageData
                {
                    Content = "hello"
                }
            }
        };
        var session = new StubGatewaySession("session-1")
        {
            History = history
        };
        _ = await pool.GetOrCreateAsync("connection-1", (cancellationToken) => Task.FromResult<IGatewaySession>(session));

        var gate = new StubConcurrencyGate();
        await using var client = new StubGatewayClient();
        var messageService = new AgentMessageService(gate, pool, client, new StubGatewayHostedService { SystemMessage = "system" });
        var sut = new GatewayHub(messageService, pool)
        {
            Context = new StubHubCallerContext("connection-1")
        };

        var results = await sut.GetHistory(CancellationToken.None);

        Assert.Single(results);
        Assert.IsType<UserMessageEvent>(results[0]);
        Assert.Equal(0, client.ResumeSessionCallCount);
    }

    [Fact]
    public async Task AbortResponse_CallerHasPooledSession_AbortsSession()
    {
        await using var pool = new SessionPool();
        var session = new StubGatewaySession("session-1");
        _ = await pool.GetOrCreateAsync("connection-1", (cancellationToken) => Task.FromResult<IGatewaySession>(session));

        var gate = new StubConcurrencyGate();
        await using var client = new StubGatewayClient();
        var messageService = new AgentMessageService(gate, pool, client, new StubGatewayHostedService { SystemMessage = "system" });
        var sut = new GatewayHub(messageService, pool)
        {
            Context = new StubHubCallerContext("connection-1")
        };

        await sut.AbortResponse(CancellationToken.None);

        Assert.True(session.AbortCalled);
        Assert.Equal(0, client.ResumeSessionCallCount);
    }

    [Fact]
    public async Task OnDisconnectedAsync_RemovesCallerSessionFromPool()
    {
        await using var pool = new SessionPool();
        var session = new StubGatewaySession("session-1");
        _ = await pool.GetOrCreateAsync("connection-1", (cancellationToken) => Task.FromResult<IGatewaySession>(session));

        var gate = new StubConcurrencyGate();
        await using var client = new StubGatewayClient();
        var messageService = new AgentMessageService(gate, pool, client, new StubGatewayHostedService { SystemMessage = "system" });
        var sut = new GatewayHub(messageService, pool)
        {
            Context = new StubHubCallerContext("connection-1")
        };

        await sut.OnDisconnectedAsync(null);

        Assert.Null(pool.TryGet("connection-1"));
        Assert.True(session.Disposed);
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
        public List<string> TryAcquireCallerKeys { get; } = [];

        public List<string> ReleaseCallerKeys { get; } = [];

        public bool TryAcquire(string callerKey)
        {
            TryAcquireCallerKeys.Add(callerKey);

            return true;
        }

        public void Release(string callerKey)
        {
            ReleaseCallerKeys.Add(callerKey);
        }

        public bool TryRelease(string callerKey)
        {
            ReleaseCallerKeys.Add(callerKey);

            return true;
        }
    }

    private sealed class StubGatewayClient : IGatewayClient
    {
        public StubGatewaySession? CreateSessionResult { get; set; }

        public StubGatewaySession? ResumeSessionResult { get; set; }

        public IReadOnlyList<SessionMetadata> ListedSessions { get; set; } = [];

        public int ResumeSessionCallCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IGatewaySession> CreateSessionAsync(SessionConfig? config = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IGatewaySession>(CreateSessionResult ?? new StubGatewaySession("session-created"));
        }

        public Task<IGatewaySession> ResumeSessionAsync(string sessionId, ResumeSessionConfig? config = null, CancellationToken cancellationToken = default)
        {
            ResumeSessionCallCount++;

            return Task.FromResult<IGatewaySession>(ResumeSessionResult ?? new StubGatewaySession(sessionId));
        }

        public Task<IReadOnlyList<SessionMetadata>> ListSessionsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ListedSessions);
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

        public IReadOnlyList<SessionEvent> History { get; init; } = [];

        public string? LastPrompt { get; private set; }

        public bool AbortCalled { get; private set; }

        public bool Disposed { get; private set; }

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
            AbortCalled = true;

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SessionEvent>> GetMessagesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(History);
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

    private sealed class StubHubCallerContext(string connectionId) : HubCallerContext
    {
        public override string ConnectionId { get; } = connectionId;

        public override string? UserIdentifier => null;

        public override ClaimsPrincipal? User => null;

        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

        public override IFeatureCollection Features { get; } = new FeatureCollection();

        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort()
        {
        }
    }
}
