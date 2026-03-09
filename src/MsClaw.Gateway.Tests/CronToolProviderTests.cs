using System.Text.Json;
using Microsoft.Extensions.AI;
using MsClaw.Gateway.Services.Cron;
using MsClaw.Gateway.Services.Tools;
using Xunit;

namespace MsClaw.Gateway.Tests;

public sealed class CronToolProviderTests
{
    [Fact]
    public async Task Contract_ReturnsCronProviderMetadataAndStaticSurface()
    {
        var sut = new CronToolProvider(new RecordingCronStore(), new RecordingCronStore(), new StubTimeProvider(DateTimeOffset.UtcNow));

        Assert.IsAssignableFrom<IToolProvider>(sut);
        Assert.Equal("cron", sut.Name);
        Assert.Equal(ToolSourceTier.Bundled, sut.Tier);

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.WaitForSurfaceChangeAsync(cancellationSource.Token));
    }

    [Fact]
    public async Task DiscoverAsync_ReturnsSevenAlwaysVisibleCronTools()
    {
        var store = new RecordingCronStore();
        var sut = new CronToolProvider(store, store, new StubTimeProvider(DateTimeOffset.UtcNow));

        var descriptors = await sut.DiscoverAsync(CancellationToken.None);

        Assert.Equal(
            [
                "cron_create",
                "cron_delete",
                "cron_get",
                "cron_list",
                "cron_pause",
                "cron_resume",
                "cron_update"
            ],
            descriptors.Select(static descriptor => descriptor.Function.Name).OrderBy(static name => name, StringComparer.Ordinal));
        Assert.All(descriptors, static descriptor => Assert.True(descriptor.AlwaysVisible));
    }

    [Fact]
    public async Task CronCreate_WithCronPromptPayload_AddsJobAndComputesNextRun()
    {
        var now = new DateTimeOffset(2026, 03, 09, 16, 00, 00, TimeSpan.Zero);
        var store = new RecordingCronStore();
        var sut = new CronToolProvider(store, store, new StubTimeProvider(now));
        var function = await GetToolAsync(sut, "cron_create");

        _ = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["name"] = "Daily Inbox Check",
                ["scheduleType"] = "cron",
                ["scheduleValue"] = "0 9 * * *",
                ["timezone"] = "UTC",
                ["payloadType"] = "prompt",
                ["prompt"] = "Check my inbox.",
                ["preloadToolNames"] = new[] { "mail_tool" },
                ["model"] = "gpt-5"
            }),
            CancellationToken.None);

        var addedJob = Assert.Single(store.AddedJobs);
        Assert.Equal("daily-inbox-check", addedJob.Id);
        Assert.Equal("Daily Inbox Check", addedJob.Name);
        Assert.Equal(CronJobStatus.Enabled, addedJob.Status);
        Assert.Equal(now, addedJob.CreatedAtUtc);

        var schedule = Assert.IsType<CronExpressionSchedule>(addedJob.Schedule);
        Assert.Equal("0 9 * * *", schedule.Expression);
        Assert.Equal("UTC", schedule.Timezone);

        var payload = Assert.IsType<PromptPayload>(addedJob.Payload);
        Assert.Equal("Check my inbox.", payload.Prompt);
        Assert.NotNull(payload.PreloadToolNames);
        Assert.Equal(["mail_tool"], payload.PreloadToolNames);
        Assert.Equal("gpt-5", payload.Model);

        Assert.Equal(
            CronScheduleCalculator.ComputeNextRun(addedJob.Schedule, null, now),
            addedJob.NextRunAtUtc);
    }

    [Fact]
    public async Task CronList_ReturnsSummaryForAllJobs()
    {
        var jobs = new[]
        {
            CreateJob("job-1", "Inbox Check", CronJobStatus.Enabled),
            CreateJob("job-2", "Reminder", CronJobStatus.Disabled)
        };
        var store = new RecordingCronStore(jobs);
        var sut = new CronToolProvider(store, store, new StubTimeProvider(DateTimeOffset.UtcNow));
        var function = await GetToolAsync(sut, "cron_list");

        var result = await function.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()), CancellationToken.None);

        Assert.Equal(1, store.GetAllJobsCallCount);
        var json = Assert.IsType<JsonElement>(result);
        Assert.Equal(2, GetProperty(json, "Count").GetInt32());
        var summary = GetProperty(json, "Summary").GetString();
        Assert.Contains("Inbox Check", summary, StringComparison.Ordinal);
        Assert.Contains("Reminder", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CronGet_LoadsJobAndRunHistoryFromSeparateStores()
    {
        var store = new RecordingCronStore(CreateJob("job-1", "Inbox Check", CronJobStatus.Enabled))
        {
            History =
            [
                new CronRunRecord("run-1", "job-1", DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddMinutes(-1), CronRunOutcome.Success, null, 300)
            ]
        };
        var sut = new CronToolProvider(store, store, new StubTimeProvider(DateTimeOffset.UtcNow));
        var function = await GetToolAsync(sut, "cron_get");

        var result = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["jobId"] = "job-1" }),
            CancellationToken.None);

        Assert.Equal(["job-1"], store.GetJobCalls);
        Assert.Equal(["job-1"], store.GetRunHistoryCalls);
        var json = Assert.IsType<JsonElement>(result);
        Assert.Equal("job-1", GetProperty(GetProperty(json, "Job"), "Id").GetString());
        Assert.Equal(1, GetProperty(json, "HistoryCount").GetInt32());
    }

    [Fact]
    public async Task CronUpdate_ExistingJob_UpdatesScheduleAndPayload()
    {
        var now = new DateTimeOffset(2026, 03, 09, 16, 00, 00, TimeSpan.Zero);
        var existingJob = CreateJob("job-1", "Inbox Check", CronJobStatus.Enabled);
        var store = new RecordingCronStore(existingJob);
        var sut = new CronToolProvider(store, store, new StubTimeProvider(now));
        var function = await GetToolAsync(sut, "cron_update");

        _ = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["jobId"] = "job-1",
                ["name"] = "Repository Status",
                ["scheduleType"] = "fixedInterval",
                ["scheduleValue"] = "60000",
                ["payloadType"] = "command",
                ["command"] = "git",
                ["arguments"] = "status --short",
                ["workingDirectory"] = "C:\\src\\msclaw",
                ["timeoutSeconds"] = 30,
                ["maxConcurrency"] = 2
            }),
            CancellationToken.None);

        var updatedJob = Assert.Single(store.UpdatedJobs);
        Assert.Equal("job-1", updatedJob.Id);
        Assert.Equal("Repository Status", updatedJob.Name);
        Assert.Equal(2, updatedJob.MaxConcurrency);
        Assert.Equal(now, updatedJob.NextRunAtUtc);

        var schedule = Assert.IsType<FixedIntervalSchedule>(updatedJob.Schedule);
        Assert.Equal(60_000, schedule.IntervalMs);

        var payload = Assert.IsType<CommandPayload>(updatedJob.Payload);
        Assert.Equal("git", payload.Command);
        Assert.Equal("status --short", payload.Arguments);
        Assert.Equal("C:\\src\\msclaw", payload.WorkingDirectory);
        Assert.Equal(30, payload.TimeoutSeconds);
    }

    [Fact]
    public async Task CronDelete_RemovesJob()
    {
        var store = new RecordingCronStore(CreateJob("job-1", "Inbox Check", CronJobStatus.Enabled));
        var sut = new CronToolProvider(store, store, new StubTimeProvider(DateTimeOffset.UtcNow));
        var function = await GetToolAsync(sut, "cron_delete");

        _ = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["jobId"] = "job-1" }),
            CancellationToken.None);

        Assert.Equal(["job-1"], store.RemovedJobIds);
    }

    [Fact]
    public async Task CronPause_LoadsJobAndDisablesIt()
    {
        var store = new RecordingCronStore(CreateJob("job-1", "Inbox Check", CronJobStatus.Enabled));
        var sut = new CronToolProvider(store, store, new StubTimeProvider(DateTimeOffset.UtcNow));
        var function = await GetToolAsync(sut, "cron_pause");

        _ = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["jobId"] = "job-1" }),
            CancellationToken.None);

        var updatedJob = Assert.Single(store.UpdatedJobs);
        Assert.Equal(CronJobStatus.Disabled, updatedJob.Status);
    }

    [Fact]
    public async Task CronResume_LoadsJobAndEnablesIt()
    {
        var store = new RecordingCronStore(CreateJob("job-1", "Inbox Check", CronJobStatus.Disabled));
        var sut = new CronToolProvider(store, store, new StubTimeProvider(DateTimeOffset.UtcNow));
        var function = await GetToolAsync(sut, "cron_resume");

        _ = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["jobId"] = "job-1" }),
            CancellationToken.None);

        var updatedJob = Assert.Single(store.UpdatedJobs);
        Assert.Equal(CronJobStatus.Enabled, updatedJob.Status);
    }

    private static async Task<AIFunction> GetToolAsync(CronToolProvider provider, string toolName)
    {
        var descriptor = Assert.Single(
            await provider.DiscoverAsync(CancellationToken.None),
            descriptor => string.Equals(descriptor.Function.Name, toolName, StringComparison.Ordinal));

        return descriptor.Function;
    }

    private static JsonElement GetProperty(JsonElement json, string propertyName)
    {
        if (json.TryGetProperty(propertyName, out var exactMatch))
        {
            return exactMatch;
        }

        var camelCaseName = char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
        if (json.TryGetProperty(camelCaseName, out var camelCaseMatch))
        {
            return camelCaseMatch;
        }

        throw new KeyNotFoundException($"Property '{propertyName}' was not found in {json.GetRawText()}.");
    }

    private static CronJob CreateJob(string jobId, string name, CronJobStatus status)
    {
        return new CronJob
        {
            Id = jobId,
            Name = name,
            Schedule = new FixedIntervalSchedule(5_000),
            Payload = new PromptPayload("Check status.", null, null),
            Status = status,
            CreatedAtUtc = new DateTimeOffset(2026, 03, 09, 15, 00, 00, TimeSpan.Zero),
            NextRunAtUtc = new DateTimeOffset(2026, 03, 09, 16, 00, 00, TimeSpan.Zero)
        };
    }

    private sealed class RecordingCronStore(params CronJob[] jobs) : ICronJobStore, ICronRunHistoryStore
    {
        private readonly Dictionary<string, CronJob> jobMap = jobs.ToDictionary(static job => job.Id, StringComparer.Ordinal);

        public int GetAllJobsCallCount { get; private set; }

        public List<string> GetJobCalls { get; } = [];

        public List<string> GetRunHistoryCalls { get; } = [];

        public List<CronJob> AddedJobs { get; } = [];

        public List<CronJob> UpdatedJobs { get; } = [];

        public List<string> RemovedJobIds { get; } = [];

        public IReadOnlyList<CronRunRecord> History { get; init; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CronJob>> GetAllJobsAsync(CancellationToken cancellationToken = default)
        {
            GetAllJobsCallCount++;

            IReadOnlyList<CronJob> snapshot = jobMap.Values
                .OrderBy(static job => job.Id, StringComparer.Ordinal)
                .ToArray();

            return Task.FromResult(snapshot);
        }

        public Task<CronJob?> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
        {
            GetJobCalls.Add(jobId);
            jobMap.TryGetValue(jobId, out var job);

            return Task.FromResult(job);
        }

        public Task AddJobAsync(CronJob job, CancellationToken cancellationToken = default)
        {
            AddedJobs.Add(job);
            jobMap[job.Id] = job;

            return Task.CompletedTask;
        }

        public Task UpdateJobAsync(CronJob job, CancellationToken cancellationToken = default)
        {
            UpdatedJobs.Add(job);
            jobMap[job.Id] = job;

            return Task.CompletedTask;
        }

        public Task RemoveJobAsync(string jobId, CancellationToken cancellationToken = default)
        {
            RemovedJobIds.Add(jobId);
            _ = jobMap.Remove(jobId);

            return Task.CompletedTask;
        }

        public Task AppendRunRecordAsync(CronRunRecord record, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<CronRunRecord>> GetRunHistoryAsync(string jobId, CancellationToken cancellationToken = default)
        {
            GetRunHistoryCalls.Add(jobId);
            return Task.FromResult(History);
        }
    }

    private sealed class StubTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
