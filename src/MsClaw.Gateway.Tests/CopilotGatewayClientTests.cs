using GitHub.Copilot.SDK;
using MsClaw.Gateway.Hosting;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class CopilotGatewayClientTests
{
    [Fact]
    public async Task CreateSessionAsync_DelegateReturnsSession_ReturnsGatewaySession()
    {
        var expectedSession = new StubGatewaySession("session-1");
        await using var sut = new CopilotGatewayClient(
            startAsync: _ => Task.CompletedTask,
            createSessionAsync: (_, _) => Task.FromResult<IGatewaySession>(expectedSession),
            resumeSessionAsync: (_, _, _) => Task.FromResult<IGatewaySession>(new StubGatewaySession("session-2")),
            listSessionsAsync: _ => Task.FromResult<IReadOnlyList<SessionMetadata>>([]),
            deleteSessionAsync: (_, _) => Task.CompletedTask,
            disposeAsync: () => ValueTask.CompletedTask);

        var session = await sut.CreateSessionAsync();

        Assert.Equal("session-1", session.SessionId);
    }

    [Fact]
    public async Task ResumeSessionAsync_DelegateReturnsSession_ReturnsGatewaySession()
    {
        var expectedSession = new StubGatewaySession("session-2");
        await using var sut = new CopilotGatewayClient(
            startAsync: _ => Task.CompletedTask,
            createSessionAsync: (_, _) => Task.FromResult<IGatewaySession>(new StubGatewaySession("session-1")),
            resumeSessionAsync: (_, _, _) => Task.FromResult<IGatewaySession>(expectedSession),
            listSessionsAsync: _ => Task.FromResult<IReadOnlyList<SessionMetadata>>([]),
            deleteSessionAsync: (_, _) => Task.CompletedTask,
            disposeAsync: () => ValueTask.CompletedTask);

        var session = await sut.ResumeSessionAsync("session-2");

        Assert.Equal("session-2", session.SessionId);
    }

    [Fact]
    public async Task ListSessionsAsync_DelegateReturnsMetadata_ReturnsMetadata()
    {
        var expectedSessions = new List<SessionMetadata>
        {
            new() { SessionId = "session-1", Summary = "First session" },
            new() { SessionId = "session-2", Summary = "Second session" }
        };

        await using var sut = new CopilotGatewayClient(
            startAsync: _ => Task.CompletedTask,
            createSessionAsync: (_, _) => Task.FromResult<IGatewaySession>(new StubGatewaySession("session-1")),
            resumeSessionAsync: (_, _, _) => Task.FromResult<IGatewaySession>(new StubGatewaySession("session-2")),
            listSessionsAsync: _ => Task.FromResult<IReadOnlyList<SessionMetadata>>(expectedSessions),
            deleteSessionAsync: (_, _) => Task.CompletedTask,
            disposeAsync: () => ValueTask.CompletedTask);

        var sessions = await sut.ListSessionsAsync();

        Assert.Collection(
            sessions,
            metadata =>
            {
                Assert.Equal("session-1", metadata.SessionId);
                Assert.Equal("First session", metadata.Summary);
            },
            metadata =>
            {
                Assert.Equal("session-2", metadata.SessionId);
                Assert.Equal("Second session", metadata.Summary);
            });
    }

    private sealed class StubGatewaySession(string sessionId) : IGatewaySession
    {
        public string SessionId { get; } = sessionId;

        public IDisposable On(Action<SessionEvent> handler)
        {
            return new StubDisposable();
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
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
