using System.Text.Json;
using MsClaw.Gateway.Services.Cron;
using Xunit;

namespace MsClaw.Gateway.Tests;

public sealed class DefaultCronErrorClassifierTests
{
    [Fact]
    public void DefaultCronErrorClassifier_ImplementsInterface()
    {
        var sut = new DefaultCronErrorClassifier();

        Assert.IsAssignableFrom<ICronErrorClassifier>(sut);
    }

    [Theory]
    [MemberData(nameof(TransientExceptions))]
    public void IsTransient_TransientExceptions_ReturnsTrue(Exception exception)
    {
        var sut = new DefaultCronErrorClassifier();

        Assert.True(sut.IsTransient(exception));
    }

    [Theory]
    [MemberData(nameof(PermanentExceptions))]
    public void IsTransient_PermanentExceptions_ReturnsFalse(Exception exception)
    {
        var sut = new DefaultCronErrorClassifier();

        Assert.False(sut.IsTransient(exception));
    }

    public static TheoryData<Exception> TransientExceptions =>
        [
            new HttpRequestException("network"),
            new TaskCanceledException("timeout"),
            new IOException("io")
        ];

    public static TheoryData<Exception> PermanentExceptions =>
        [
            new UnauthorizedAccessException("auth"),
            new ArgumentException("bad input"),
            new JsonException("json")
        ];
}
