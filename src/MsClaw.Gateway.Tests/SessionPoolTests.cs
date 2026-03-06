using GitHub.Copilot.SDK;
using MsClaw.Gateway.Hosting;
using MsClaw.Gateway.Services;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class SessionPoolTests
{
    [Fact]
    public async Task GetOrCreateAsync_NewCaller_CreatesSessionViaFactory()
    {
        await using var sut = new SessionPool();
        var session = new StubGatewaySession("session-1");
        var factoryCalled = false;

        var result = await sut.GetOrCreateAsync("caller-1", (cancellationToken) =>
        {
            factoryCalled = true;

            return Task.FromResult<IGatewaySession>(session);
        });

        Assert.True(factoryCalled);
        Assert.Same(session, result);
    }

    [Fact]
    public async Task GetOrCreateAsync_ExistingCaller_ReturnsCachedSession()
    {
        await using var sut = new SessionPool();
        var session = new StubGatewaySession("session-1");
        _ = await sut.GetOrCreateAsync("caller-1", (cancellationToken) => Task.FromResult<IGatewaySession>(session));
        var factoryCalledAgain = false;

        var result = await sut.GetOrCreateAsync("caller-1", (cancellationToken) =>
        {
            factoryCalledAgain = true;

            return Task.FromResult<IGatewaySession>(new StubGatewaySession("session-2"));
        });

        Assert.False(factoryCalledAgain);
        Assert.Same(session, result);
    }

    [Fact]
    public async Task TryGet_UnknownCaller_ReturnsNull()
    {
        await using var sut = new SessionPool();

        var result = sut.TryGet("caller-unknown");

        Assert.Null(result);
    }

    [Fact]
    public async Task TryGet_KnownCaller_ReturnsSession()
    {
        await using var sut = new SessionPool();
        var session = new StubGatewaySession("session-1");
        _ = await sut.GetOrCreateAsync("caller-1", (cancellationToken) => Task.FromResult<IGatewaySession>(session));

        var result = sut.TryGet("caller-1");

        Assert.Same(session, result);
    }

    [Fact]
    public async Task RemoveAsync_ExistingCaller_DisposesAndRemovesSession()
    {
        await using var sut = new SessionPool();
        var session = new StubGatewaySession("session-1");
        _ = await sut.GetOrCreateAsync("caller-1", (cancellationToken) => Task.FromResult<IGatewaySession>(session));

        await sut.RemoveAsync("caller-1");

        Assert.True(session.Disposed);
        Assert.Null(sut.TryGet("caller-1"));
    }

    [Fact]
    public async Task RemoveAsync_UnknownCaller_DoesNotThrow()
    {
        await using var sut = new SessionPool();

        var exception = await Record.ExceptionAsync(() => sut.RemoveAsync("caller-unknown"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task DisposeAsync_DisposesAllTrackedSessions()
    {
        var session1 = new StubGatewaySession("session-1");
        var session2 = new StubGatewaySession("session-2");
        var sut = new SessionPool();
        _ = await sut.GetOrCreateAsync("caller-1", (cancellationToken) => Task.FromResult<IGatewaySession>(session1));
        _ = await sut.GetOrCreateAsync("caller-2", (cancellationToken) => Task.FromResult<IGatewaySession>(session2));

        await sut.DisposeAsync();

        Assert.True(session1.Disposed);
        Assert.True(session2.Disposed);
    }

    [Fact]
    public async Task ListCallers_ReturnsCallerSessionIdPairs()
    {
        await using var sut = new SessionPool();
        _ = await sut.GetOrCreateAsync("caller-1", (cancellationToken) => Task.FromResult<IGatewaySession>(new StubGatewaySession("session-1")));
        _ = await sut.GetOrCreateAsync("caller-2", (cancellationToken) => Task.FromResult<IGatewaySession>(new StubGatewaySession("session-2")));

        var callers = sut.ListCallers();

        Assert.Collection(
            callers.OrderBy(static pair => pair.CallerKey, StringComparer.Ordinal),
            pair =>
            {
                Assert.Equal("caller-1", pair.CallerKey);
                Assert.Equal("session-1", pair.SessionId);
            },
            pair =>
            {
                Assert.Equal("caller-2", pair.CallerKey);
                Assert.Equal("session-2", pair.SessionId);
            });
    }

    private sealed class StubGatewaySession(string sessionId) : IGatewaySession
    {
        public bool Disposed { get; private set; }

        public string SessionId { get; } = sessionId;

        public IDisposable On(Action<SessionEvent> handler)
        {
            return new NoOpDisposable();
        }

        public Task SendAsync(MessageOptions options, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
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

        private sealed class NoOpDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
