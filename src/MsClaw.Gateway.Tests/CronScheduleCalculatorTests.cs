using MsClaw.Gateway.Services.Cron;
using Xunit;

namespace MsClaw.Gateway.Tests;

public sealed class CronScheduleCalculatorTests
{
    [Fact]
    public void ComputeNextRun_OneShotSchedule_ReturnsFireAtUtcBeforeExecution_AndNullAfterExecution()
    {
        var fireAtUtc = new DateTimeOffset(2026, 03, 09, 16, 00, 00, TimeSpan.Zero);
        var schedule = new OneShotSchedule(fireAtUtc);
        var now = fireAtUtc.AddMinutes(-5);

        var beforeExecution = CronScheduleCalculator.ComputeNextRun(schedule, null, now);
        var afterExecution = CronScheduleCalculator.ComputeNextRun(schedule, fireAtUtc, now);

        Assert.Equal(fireAtUtc, beforeExecution);
        Assert.Null(afterExecution);
    }

    [Fact]
    public void ComputeNextRun_FixedIntervalSchedule_ReturnsNowWhenNeverRun_AndLastRunPlusIntervalOtherwise()
    {
        var schedule = new FixedIntervalSchedule(30_000);
        var now = new DateTimeOffset(2026, 03, 09, 16, 00, 00, TimeSpan.Zero);
        var lastRunAtUtc = now.AddMinutes(-2);

        var firstRun = CronScheduleCalculator.ComputeNextRun(schedule, null, now);
        var subsequentRun = CronScheduleCalculator.ComputeNextRun(schedule, lastRunAtUtc, now);

        Assert.Equal(now, firstRun);
        Assert.Equal(lastRunAtUtc.AddSeconds(30), subsequentRun);
    }

    [Fact]
    public void ComputeNextRun_CronExpressionSchedule_ReturnsNextOccurrenceUsingTimezone()
    {
        var schedule = new CronExpressionSchedule("0 9 * * *", "Etc/UTC");
        var now = new DateTimeOffset(2026, 03, 09, 08, 15, 00, TimeSpan.Zero);

        var result = CronScheduleCalculator.ComputeNextRun(schedule, null, now);

        Assert.Equal(new DateTimeOffset(2026, 03, 09, 09, 00, 00, TimeSpan.Zero), result);
    }
}
