using System.Text.Json;
using MsClaw.Gateway.Services.Cron;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class CronModelTests
{
    [Fact]
    public void CronJob_DefaultsAreCorrect()
    {
        var job = new CronJob
        {
            Id = "daily-summary",
            Name = "Daily Summary",
            Schedule = new FixedIntervalSchedule(5_000),
            Payload = new PromptPayload("Summarize today's work.", ["tool-a"], "gpt-5"),
            Status = CronJobStatus.Enabled
        };

        Assert.Equal(1, job.MaxConcurrency);
        Assert.Null(job.LastRunAtUtc);
        Assert.Null(job.NextRunAtUtc);
        Assert.Null(job.Backoff);
        Assert.Equal(CronJobStatus.Enabled, job.Status);
    }

    [Fact]
    public void JobSchedule_SerializesPolymorphicallyWithTypeDiscriminator()
    {
        var oneShot = new OneShotSchedule(new DateTimeOffset(2026, 03, 09, 15, 00, 00, TimeSpan.Zero));
        var fixedInterval = new FixedIntervalSchedule(30_000);
        var cron = new CronExpressionSchedule("0 9 * * *", "America/New_York");

        var oneShotJson = JsonSerializer.Serialize<JobSchedule>(oneShot);
        var fixedIntervalJson = JsonSerializer.Serialize<JobSchedule>(fixedInterval);
        var cronJson = JsonSerializer.Serialize<JobSchedule>(cron);

        Assert.Contains("\"type\":\"oneShot\"", oneShotJson);
        Assert.Contains("\"fireAtUtc\"", oneShotJson);
        Assert.Equal(oneShot, JsonSerializer.Deserialize<JobSchedule>(oneShotJson));

        Assert.Contains("\"type\":\"fixedInterval\"", fixedIntervalJson);
        Assert.Contains("\"intervalMs\":30000", fixedIntervalJson);
        Assert.Equal(fixedInterval, JsonSerializer.Deserialize<JobSchedule>(fixedIntervalJson));

        Assert.Contains("\"type\":\"cron\"", cronJson);
        Assert.Contains("\"expression\":\"0 9 * * *\"", cronJson);
        Assert.Contains("\"timezone\":\"America/New_York\"", cronJson);
        Assert.Equal(cron, JsonSerializer.Deserialize<JobSchedule>(cronJson));
    }

    [Fact]
    public void JobPayload_SerializesPolymorphicallyWithTypeDiscriminator()
    {
        var prompt = new PromptPayload("Check the inbox.", ["mail", "calendar"], "gpt-5");
        var command = new CommandPayload("git", "status --short", "C:\\src\\msclaw", 45);

        var promptJson = JsonSerializer.Serialize<JobPayload>(prompt);
        var commandJson = JsonSerializer.Serialize<JobPayload>(command);

        Assert.Contains("\"type\":\"prompt\"", promptJson);
        Assert.Contains("\"prompt\":\"Check the inbox.\"", promptJson);
        Assert.Contains("\"preloadToolNames\":[\"mail\",\"calendar\"]", promptJson);
        Assert.Contains("\"model\":\"gpt-5\"", promptJson);

        var promptRoundTrip = Assert.IsType<PromptPayload>(JsonSerializer.Deserialize<JobPayload>(promptJson));
        Assert.Equal(prompt.Prompt, promptRoundTrip.Prompt);
        Assert.Equal(prompt.Model, promptRoundTrip.Model);
        Assert.Equal(prompt.PreloadToolNames, promptRoundTrip.PreloadToolNames);

        Assert.Contains("\"type\":\"command\"", commandJson);
        Assert.Contains("\"command\":\"git\"", commandJson);
        Assert.Contains("\"arguments\":\"status --short\"", commandJson);
        Assert.Contains("\"workingDirectory\":\"C:\\\\src\\\\msclaw\"", commandJson);
        Assert.Contains("\"timeoutSeconds\":45", commandJson);

        var commandRoundTrip = Assert.IsType<CommandPayload>(JsonSerializer.Deserialize<JobPayload>(commandJson));
        Assert.Equal(command, commandRoundTrip);
    }

    [Fact]
    public void CronRunResult_FieldsAreAccessible()
    {
        var success = new CronRunResult("completed", CronRunOutcome.Success, null, 150, false);
        var failure = new CronRunResult("", CronRunOutcome.Failure, "timeout", 300, true);

        Assert.Equal("completed", success.Content);
        Assert.Equal(CronRunOutcome.Success, success.Outcome);
        Assert.Null(success.ErrorMessage);
        Assert.Equal(150, success.DurationMs);
        Assert.False(success.IsTransient);

        Assert.Equal(CronRunOutcome.Failure, failure.Outcome);
        Assert.Equal("timeout", failure.ErrorMessage);
        Assert.True(failure.IsTransient);
    }

    [Fact]
    public void CronRunRecord_RoundTripsThroughJson()
    {
        var startedAtUtc = new DateTimeOffset(2026, 03, 09, 15, 30, 00, TimeSpan.Zero);
        var completedAtUtc = startedAtUtc.AddSeconds(4);
        var record = new CronRunRecord(
            "run-123",
            "daily-summary",
            startedAtUtc,
            completedAtUtc,
            CronRunOutcome.Success,
            null,
            4_000);

        var json = JsonSerializer.Serialize(record);

        Assert.Contains("\"runId\":\"run-123\"", json);
        Assert.Contains("\"jobId\":\"daily-summary\"", json);
        Assert.Equal(record, JsonSerializer.Deserialize<CronRunRecord>(json));
    }
}
