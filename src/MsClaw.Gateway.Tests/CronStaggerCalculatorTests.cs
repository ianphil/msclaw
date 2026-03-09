using MsClaw.Gateway.Services.Cron;
using Xunit;

namespace MsClaw.Gateway.Tests;

public sealed class CronStaggerCalculatorTests
{
    [Fact]
    public void ComputeOffset_DifferentJobs_GetDifferentOffsets_AndSameJobIsDeterministic()
    {
        var window = TimeSpan.FromMinutes(5);

        var firstJobOffset = CronStaggerCalculator.ComputeOffset("job-a", window);
        var secondJobOffset = CronStaggerCalculator.ComputeOffset("job-b", window);
        var repeatedJobOffset = CronStaggerCalculator.ComputeOffset("job-a", window);

        Assert.NotEqual(firstJobOffset, secondJobOffset);
        Assert.Equal(firstJobOffset, repeatedJobOffset);
        Assert.InRange(firstJobOffset, TimeSpan.Zero, window);
        Assert.InRange(secondJobOffset, TimeSpan.Zero, window);
    }
}
