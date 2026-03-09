using MsClaw.Gateway.Services.Cron;
using Xunit;

namespace MsClaw.Gateway.Tests;

public sealed class CronJobStoreTests : IDisposable
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public CronJobStoreTests()
    {
        Directory.CreateDirectory(rootPath);
    }

    [Fact]
    public async Task InitializeAsync_MissingJobsFile_ReturnsEmptyList()
    {
        var sut = new CronJobStore(rootPath);

        await sut.InitializeAsync();
        var jobs = await sut.GetAllJobsAsync();

        Assert.Empty(jobs);
    }

    [Fact]
    public async Task AddJobAsync_NewStoreInstance_RoundTripsJobThroughDisk()
    {
        var job = CreateJob();
        var firstStore = new CronJobStore(rootPath);
        await firstStore.InitializeAsync();

        await firstStore.AddJobAsync(job);

        var secondStore = new CronJobStore(rootPath);
        await secondStore.InitializeAsync();
        var loadedJob = await secondStore.GetJobAsync(job.Id);

        var promptPayload = Assert.IsType<PromptPayload>(loadedJob?.Payload);

        Assert.NotNull(loadedJob);
        Assert.Equal(job.Id, loadedJob.Id);
        Assert.Equal(job.Name, loadedJob.Name);
        Assert.Equal(job.Schedule, loadedJob.Schedule);
        Assert.Equal(job.Status, loadedJob.Status);
        Assert.Equal(job.CreatedAtUtc, loadedJob.CreatedAtUtc);
        Assert.Equal(job.NextRunAtUtc, loadedJob.NextRunAtUtc);
        Assert.Equal("Summarize the day.", promptPayload.Prompt);
        Assert.Equal(["calendar"], Assert.IsType<string[]>(promptPayload.PreloadToolNames));
        Assert.Equal("gpt-5", promptPayload.Model);
    }

    [Fact]
    public async Task AddJobAsync_WritesJobsFileWithoutTemporaryArtifacts()
    {
        var sut = new CronJobStore(rootPath);
        await sut.InitializeAsync();

        await sut.AddJobAsync(CreateJob());

        var jobsPath = Path.Combine(rootPath, "jobs.json");
        var json = await File.ReadAllTextAsync(jobsPath);

        Assert.True(File.Exists(jobsPath));
        Assert.Contains("\"jobs\"", json);
        Assert.Contains("\"daily-summary\"", json);
        Assert.Empty(Directory.GetFiles(rootPath, "*.tmp"));
    }

    [Fact]
    public async Task AddJobAsync_AddsToCache_AndDuplicateThrows()
    {
        var job = CreateJob();
        var sut = new CronJobStore(rootPath);
        await sut.InitializeAsync();

        await sut.AddJobAsync(job);
        var loadedJob = await sut.GetJobAsync(job.Id);
        var duplicateException = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.AddJobAsync(job));

        Assert.Equal(job, loadedJob);
        Assert.Contains(job.Id, duplicateException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateJobAsync_ExistingJob_UpdatesCache_AndMissingThrows()
    {
        var originalJob = CreateJob();
        var updatedJob = originalJob with
        {
            Name = "Updated Summary",
            Payload = new PromptPayload("Updated prompt.", ["calendar", "mail"], "gpt-5")
        };
        var sut = new CronJobStore(rootPath);
        await sut.InitializeAsync();
        await sut.AddJobAsync(originalJob);

        await sut.UpdateJobAsync(updatedJob);
        var loadedJob = await sut.GetJobAsync(updatedJob.Id);
        var missingException = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.UpdateJobAsync(updatedJob with { Id = "missing-job" }));

        Assert.Equal(updatedJob, loadedJob);
        Assert.Contains("missing-job", missingException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveJobAsync_ExistingJob_RemovesFromCache_AndMissingIsNoOp()
    {
        var job = CreateJob();
        var sut = new CronJobStore(rootPath);
        await sut.InitializeAsync();
        await sut.AddJobAsync(job);

        await sut.RemoveJobAsync(job.Id);
        await sut.RemoveJobAsync("missing-job");
        var loadedJob = await sut.GetJobAsync(job.Id);

        Assert.Null(loadedJob);
        Assert.Empty(await sut.GetAllJobsAsync());
    }

    [Fact]
    public async Task AppendRunRecordAsync_CreatesHistoryFile_AndInterfaceSegregationIsPreserved()
    {
        var sut = new CronJobStore(rootPath);
        await sut.InitializeAsync();
        var runRecord = CreateRunRecord("run-001");

        await sut.AppendRunRecordAsync(runRecord);
        var history = await sut.GetRunHistoryAsync(runRecord.JobId);

        Assert.IsAssignableFrom<ICronJobStore>(sut);
        Assert.IsAssignableFrom<ICronRunHistoryStore>(sut);
        Assert.Null(typeof(ICronJobStore).GetMethod(nameof(ICronRunHistoryStore.AppendRunRecordAsync)));
        Assert.Null(typeof(ICronJobStore).GetMethod(nameof(ICronRunHistoryStore.GetRunHistoryAsync)));
        Assert.NotNull(typeof(ICronRunHistoryStore).GetMethod(nameof(ICronRunHistoryStore.AppendRunRecordAsync)));
        Assert.NotNull(typeof(ICronRunHistoryStore).GetMethod(nameof(ICronRunHistoryStore.GetRunHistoryAsync)));
        Assert.Equal([runRecord], history);
        Assert.True(File.Exists(Path.Combine(rootPath, "history", $"{runRecord.JobId}.json")));
    }

    [Fact]
    public async Task AppendRunRecordAsync_ExceedingHistoryLimit_PrunesOldestRecords()
    {
        var sut = new CronJobStore(rootPath, maxHistoryRecords: 2);
        await sut.InitializeAsync();

        await sut.AppendRunRecordAsync(CreateRunRecord("run-001"));
        await sut.AppendRunRecordAsync(CreateRunRecord("run-002"));
        await sut.AppendRunRecordAsync(CreateRunRecord("run-003"));

        var history = await sut.GetRunHistoryAsync("daily-summary");

        Assert.Collection(
            history,
            record => Assert.Equal("run-002", record.RunId),
            record => Assert.Equal("run-003", record.RunId));
    }

    public void Dispose()
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private static CronJob CreateJob()
    {
        return new CronJob
        {
            Id = "daily-summary",
            Name = "Daily Summary",
            Schedule = new FixedIntervalSchedule(5_000),
            Payload = new PromptPayload("Summarize the day.", ["calendar"], "gpt-5"),
            Status = CronJobStatus.Enabled,
            CreatedAtUtc = new DateTimeOffset(2026, 03, 09, 14, 00, 00, TimeSpan.Zero),
            NextRunAtUtc = new DateTimeOffset(2026, 03, 09, 14, 05, 00, TimeSpan.Zero)
        };
    }

    private static CronRunRecord CreateRunRecord(string runId)
    {
        var startedAtUtc = new DateTimeOffset(2026, 03, 09, 14, 00, 00, TimeSpan.Zero);

        return new CronRunRecord(
            runId,
            "daily-summary",
            startedAtUtc,
            startedAtUtc.AddSeconds(5),
            CronRunOutcome.Success,
            null,
            5_000);
    }
}
