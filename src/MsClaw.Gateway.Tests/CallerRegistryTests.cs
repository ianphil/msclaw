using MsClaw.Gateway.Services;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class CallerRegistryTests
{
    [Fact]
    public void TryAcquire_NoActiveRunForCaller_ReturnsTrue()
    {
        IConcurrencyGate sut = new CallerRegistry();

        var acquired = sut.TryAcquire("caller-1");

        Assert.True(acquired);
    }

    [Fact]
    public void TryAcquire_ActiveRunForCaller_ReturnsFalse()
    {
        IConcurrencyGate sut = new CallerRegistry();
        _ = sut.TryAcquire("caller-1");

        var acquired = sut.TryAcquire("caller-1");

        Assert.False(acquired);
    }

    [Fact]
    public void Release_AfterAcquire_AllowsAcquireAgain()
    {
        IConcurrencyGate sut = new CallerRegistry();
        _ = sut.TryAcquire("caller-1");

        sut.Release("caller-1");
        var acquired = sut.TryAcquire("caller-1");

        Assert.True(acquired);
    }

    [Fact]
    public void TryAcquire_DifferentCallers_ReturnTrueIndependently()
    {
        IConcurrencyGate sut = new CallerRegistry();

        var firstAcquired = sut.TryAcquire("caller-1");
        var secondAcquired = sut.TryAcquire("caller-2");

        Assert.True(firstAcquired);
        Assert.True(secondAcquired);
    }

    [Fact]
    public void SetSessionId_GetSessionId_RoundTripsSessionId()
    {
        ISessionMap sut = new CallerRegistry();

        sut.SetSessionId("caller-1", "session-1");
        var sessionId = sut.GetSessionId("caller-1");

        Assert.Equal("session-1", sessionId);
    }

    [Fact]
    public void GetSessionId_UnknownCaller_ReturnsNull()
    {
        ISessionMap sut = new CallerRegistry();

        var sessionId = sut.GetSessionId("caller-unknown");

        Assert.Null(sessionId);
    }

    [Fact]
    public void ListCallers_WithRegisteredSessions_ReturnsCallerSessionPairs()
    {
        ISessionMap sut = new CallerRegistry();
        sut.SetSessionId("caller-1", "session-1");
        sut.SetSessionId("caller-2", "session-2");

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
}
