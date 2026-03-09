using MsClaw.Gateway.Services.Cron;
using Xunit;

namespace MsClaw.Gateway.Tests;

public sealed class CommandJobExecutorTests
{
    [Fact]
    public void PayloadType_ReturnsCommandPayloadType()
    {
        var sut = new CommandJobExecutor();

        Assert.Equal(typeof(CommandPayload), sut.PayloadType);
    }

    [Fact]
    public async Task ExecuteAsync_CommandPayload_StartsProcessAndCapturesStdout()
    {
        var sut = new CommandJobExecutor();
        var job = CreateCommandJob(CreateEchoPayload());

        var result = await sut.ExecuteAsync(job, "run-123", CancellationToken.None);

        Assert.Equal(CronRunOutcome.Success, result.Outcome);
        Assert.Contains("hello from cron", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ProcessExceedsTimeout_ReturnsFailureResult()
    {
        var sut = new CommandJobExecutor();
        var job = CreateCommandJob(CreateSleepPayload(timeoutSeconds: 1));

        var result = await sut.ExecuteAsync(job, "run-456", CancellationToken.None);

        Assert.Equal(CronRunOutcome.Failure, result.Outcome);
        Assert.Contains("timed out", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static CronJob CreateCommandJob(CommandPayload payload)
    {
        return new CronJob
        {
            Id = "job-1",
            Name = "Command Job",
            Schedule = new FixedIntervalSchedule(5_000),
            Payload = payload,
            Status = CronJobStatus.Enabled
        };
    }

    private static CommandPayload CreateEchoPayload()
    {
        return OperatingSystem.IsWindows()
            ? new CommandPayload("powershell", "-NoProfile -Command \"Write-Output 'hello from cron'\"", null, 10)
            : new CommandPayload("/bin/sh", "-c \"printf 'hello from cron\\n'\"", null, 10);
    }

    private static CommandPayload CreateSleepPayload(int timeoutSeconds)
    {
        return OperatingSystem.IsWindows()
            ? new CommandPayload("powershell", "-NoProfile -Command \"Start-Sleep -Seconds 5\"", null, timeoutSeconds)
            : new CommandPayload("/bin/sh", "-c \"sleep 5\"", null, timeoutSeconds);
    }
}
