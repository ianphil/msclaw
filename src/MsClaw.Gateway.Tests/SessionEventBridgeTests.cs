using GitHub.Copilot.SDK;
using MsClaw.Gateway.Hosting;
using MsClaw.Gateway.Services;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class SessionEventBridgeTests
{
    [Fact]
    public async Task Bridge_PushedEvents_YieldsEventsAsAsyncEnumerable()
    {
        var session = new StubGatewaySession();
        var (subscription, events) = SessionEventBridge.Bridge(session, CancellationToken.None);

        var readTask = ReadAllAsync(events);
        session.Emit(new AssistantMessageDeltaEvent
        {
            Data = new AssistantMessageDeltaData
            {
                MessageId = "message-1",
                DeltaContent = "hello"
            }
        });
        session.Emit(new SessionIdleEvent
        {
            Data = new SessionIdleData()
        });

        var completedTask = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(5)));

        subscription.Dispose();

        Assert.Same(readTask, completedTask);
        Assert.Collection(
            await readTask,
            sessionEvent => Assert.IsType<AssistantMessageDeltaEvent>(sessionEvent),
            sessionEvent => Assert.IsType<SessionIdleEvent>(sessionEvent));
    }

    [Fact]
    public async Task Bridge_SessionIdleEvent_CompletesEnumerable()
    {
        var session = new StubGatewaySession();
        var (subscription, events) = SessionEventBridge.Bridge(session, CancellationToken.None);

        var readTask = ReadAllAsync(events);
        session.Emit(new SessionIdleEvent
        {
            Data = new SessionIdleData()
        });

        var completedTask = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(5)));

        subscription.Dispose();

        Assert.Same(readTask, completedTask);
        Assert.Collection(await readTask, sessionEvent => Assert.IsType<SessionIdleEvent>(sessionEvent));
    }

    [Fact]
    public async Task Bridge_CancelledToken_CompletesEnumerable()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var session = new StubGatewaySession();
        var (subscription, events) = SessionEventBridge.Bridge(session, cancellationTokenSource.Token);

        var readTask = ReadAllAsync(events);
        cancellationTokenSource.Cancel();

        var completedTask = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(5)));

        subscription.Dispose();

        Assert.Same(readTask, completedTask);
        Assert.Empty(await readTask);
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

    private sealed class StubGatewaySession : IGatewaySession
    {
        private readonly List<Action<SessionEvent>> handlers = [];

        public string SessionId => "session-1";

        public IDisposable On(Action<SessionEvent> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            handlers.Add(handler);

            return new Subscription(handlers, handler);
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
}
