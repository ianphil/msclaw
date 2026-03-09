using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MsClaw.Gateway.Services.Cron;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class CronModelTests
{
    [Fact]
    public void CronJob_HasRequiredPropertiesAndDefaults()
    {
        var cronJobType = typeof(CronJob);
        var instance = new CronJob
        {
            Id = "daily-summary",
            Name = "Daily Summary",
            Schedule = new FixedIntervalSchedule(5_000),
            Payload = new PromptPayload("Summarize today's work.", ["tool-a"], "gpt-5"),
            Status = CronJobStatus.Enabled
        };

        Assert.True(cronJobType.IsSealed);

        var idProperty = cronJobType.GetProperty(nameof(CronJob.Id));
        Assert.NotNull(idProperty);
        Assert.Equal(typeof(string), idProperty.PropertyType);
        Assert.NotNull(idProperty.GetCustomAttribute<RequiredMemberAttribute>());

        var nameProperty = cronJobType.GetProperty(nameof(CronJob.Name));
        Assert.NotNull(nameProperty);
        Assert.Equal(typeof(string), nameProperty.PropertyType);
        Assert.NotNull(nameProperty.GetCustomAttribute<RequiredMemberAttribute>());

        var scheduleProperty = cronJobType.GetProperty(nameof(CronJob.Schedule));
        Assert.NotNull(scheduleProperty);
        Assert.Equal(typeof(JobSchedule), scheduleProperty.PropertyType);
        Assert.NotNull(scheduleProperty.GetCustomAttribute<RequiredMemberAttribute>());

        var payloadProperty = cronJobType.GetProperty(nameof(CronJob.Payload));
        Assert.NotNull(payloadProperty);
        Assert.Equal(typeof(JobPayload), payloadProperty.PropertyType);
        Assert.NotNull(payloadProperty.GetCustomAttribute<RequiredMemberAttribute>());

        var statusProperty = cronJobType.GetProperty(nameof(CronJob.Status));
        Assert.NotNull(statusProperty);
        Assert.Equal(typeof(CronJobStatus), statusProperty.PropertyType);
        Assert.NotNull(statusProperty.GetCustomAttribute<RequiredMemberAttribute>());

        var maxConcurrencyProperty = cronJobType.GetProperty(nameof(CronJob.MaxConcurrency));
        Assert.NotNull(maxConcurrencyProperty);
        Assert.Equal(typeof(int), maxConcurrencyProperty.PropertyType);
        Assert.Equal(1, instance.MaxConcurrency);

        var createdAtUtcProperty = cronJobType.GetProperty(nameof(CronJob.CreatedAtUtc));
        Assert.NotNull(createdAtUtcProperty);
        Assert.Equal(typeof(DateTimeOffset), createdAtUtcProperty.PropertyType);

        var lastRunAtUtcProperty = cronJobType.GetProperty(nameof(CronJob.LastRunAtUtc));
        Assert.NotNull(lastRunAtUtcProperty);
        Assert.Equal(typeof(DateTimeOffset?), lastRunAtUtcProperty.PropertyType);

        var nextRunAtUtcProperty = cronJobType.GetProperty(nameof(CronJob.NextRunAtUtc));
        Assert.NotNull(nextRunAtUtcProperty);
        Assert.Equal(typeof(DateTimeOffset?), nextRunAtUtcProperty.PropertyType);

        var backoffProperty = cronJobType.GetProperty(nameof(CronJob.Backoff));
        Assert.NotNull(backoffProperty);
        Assert.Equal(typeof(BackoffState), backoffProperty.PropertyType);

        Assert.Equal(CronJobStatus.Enabled, instance.Status);
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
    public void CronRunResult_HasRequiredPropertiesAndOutcomeEnum()
    {
        var resultType = typeof(CronRunResult);
        var instance = new CronRunResult("completed", CronRunOutcome.Success, null, 150, false);

        Assert.True(resultType.IsSealed);

        var contentProperty = resultType.GetProperty(nameof(CronRunResult.Content));
        Assert.NotNull(contentProperty);
        Assert.Equal(typeof(string), contentProperty.PropertyType);

        var outcomeProperty = resultType.GetProperty(nameof(CronRunResult.Outcome));
        Assert.NotNull(outcomeProperty);
        Assert.Equal(typeof(CronRunOutcome), outcomeProperty.PropertyType);

        var errorMessageProperty = resultType.GetProperty(nameof(CronRunResult.ErrorMessage));
        Assert.NotNull(errorMessageProperty);
        Assert.Equal(typeof(string), errorMessageProperty.PropertyType);

        var durationMsProperty = resultType.GetProperty(nameof(CronRunResult.DurationMs));
        Assert.NotNull(durationMsProperty);
        Assert.Equal(typeof(long), durationMsProperty.PropertyType);

        var isTransientProperty = resultType.GetProperty(nameof(CronRunResult.IsTransient));
        Assert.NotNull(isTransientProperty);
        Assert.Equal(typeof(bool), isTransientProperty.PropertyType);

        Assert.Equal(CronRunOutcome.Success, instance.Outcome);
        Assert.Contains(CronRunOutcome.Failure, Enum.GetValues<CronRunOutcome>());
    }

    [Fact]
    public void CronRunRecord_RoundTripsThroughJson()
    {
        var recordType = typeof(CronRunRecord);
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

        Assert.True(recordType.IsSealed);

        Assert.Equal(typeof(string), recordType.GetProperty(nameof(CronRunRecord.RunId))?.PropertyType);
        Assert.Equal(typeof(string), recordType.GetProperty(nameof(CronRunRecord.JobId))?.PropertyType);
        Assert.Equal(typeof(DateTimeOffset), recordType.GetProperty(nameof(CronRunRecord.StartedAtUtc))?.PropertyType);
        Assert.Equal(typeof(DateTimeOffset), recordType.GetProperty(nameof(CronRunRecord.CompletedAtUtc))?.PropertyType);
        Assert.Equal(typeof(CronRunOutcome), recordType.GetProperty(nameof(CronRunRecord.Outcome))?.PropertyType);
        Assert.Equal(typeof(string), recordType.GetProperty(nameof(CronRunRecord.ErrorMessage))?.PropertyType);
        Assert.Equal(typeof(long), recordType.GetProperty(nameof(CronRunRecord.DurationMs))?.PropertyType);

        var json = JsonSerializer.Serialize(record);

        Assert.Contains("\"runId\":\"run-123\"", json);
        Assert.Contains("\"jobId\":\"daily-summary\"", json);
        Assert.Equal(record, JsonSerializer.Deserialize<CronRunRecord>(json));
    }
}
