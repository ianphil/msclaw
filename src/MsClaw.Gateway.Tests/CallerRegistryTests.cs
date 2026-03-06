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
}
