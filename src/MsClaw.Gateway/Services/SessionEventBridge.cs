using System.Collections.Generic;
using System.Threading.Channels;
using GitHub.Copilot.SDK;
using MsClaw.Gateway.Hosting;

namespace MsClaw.Gateway.Services;

/// <summary>
/// Bridges push-based session events to an async stream for hub and HTTP consumers.
/// </summary>
public static class SessionEventBridge
{
    /// <summary>
    /// Creates an event stream backed by a session subscription.
    /// </summary>
    public static (IDisposable Subscription, IAsyncEnumerable<SessionEvent> Events) Bridge(
        IGatewaySession session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var channel = Channel.CreateBounded<SessionEvent>(new BoundedChannelOptions(500)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        var cancellationRegistration = cancellationToken.Register(() => channel.Writer.TryComplete());
        var sessionSubscription = session.On(sessionEvent =>
        {
            if (channel.Writer.TryWrite(sessionEvent) is false)
            {
                return;
            }

            if (sessionEvent is SessionIdleEvent or SessionErrorEvent)
            {
                channel.Writer.TryComplete();
            }
        });

        return (new CompositeDisposable(sessionSubscription, cancellationRegistration), ReadAllAsync(channel.Reader));
    }

    /// <summary>
    /// Reads all available session events from the channel-backed stream.
    /// </summary>
    private static async IAsyncEnumerable<SessionEvent> ReadAllAsync(ChannelReader<SessionEvent> reader)
    {
        await foreach (var sessionEvent in reader.ReadAllAsync())
        {
            yield return sessionEvent;
        }
    }

    /// <summary>
    /// Disposes the session subscription and cancellation registration together.
    /// </summary>
    private sealed class CompositeDisposable(IDisposable sessionSubscription, CancellationTokenRegistration cancellationRegistration) : IDisposable
    {
        /// <summary>
        /// Disposes the composite resources used by the bridge.
        /// </summary>
        public void Dispose()
        {
            sessionSubscription.Dispose();
            cancellationRegistration.Dispose();
        }
    }
}
