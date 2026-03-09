using Microsoft.Extensions.Hosting;
using MsClaw.Gateway.Services.Cron;
using Xunit;

namespace MsClaw.Gateway.Tests;

public sealed class CronEngineTests
{
    [Fact]
    public async Task StartAsync_InitializesStoreAndMarksEngineRunning()
    {
        var store = new RecordingCronStore();
        var sut = CreateEngine(store);

        Assert.IsAssignableFrom<IHostedService>(sut);

        await sut.StartAsync(CancellationToken.None);

        Assert.Equal(1, store.InitializeCallCount);
        Assert.True(sut.IsRunning);

        await sut.StopAsync(CancellationToken.None);

        Assert.False(sut.IsRunning);
    }

    [Fact]
    public async Task OnTickAsync_EnabledDueJob_DispatchesMatchingExecutor()
    {
        var now = new DateTimeOffset(2026, 03, 09, 16, 00, 00, TimeSpan.Zero);
        var job = CreateRecurringJob("job-1", now.AddSeconds(-1));
        var store = new RecordingCronStore(job);
        var executor = new RecordingExecutor();
        var sut = CreateEngine(store, executor, timeProvider: new StubTimeProvider(now));

        await sut.OnTickAsync(CancellationToken.None);
        await executor.WaitForCallCountAsync(1);

        Assert.Equal(["job-1"], executor.ExecutedJobIds);

        await sut.DrainAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OnTickAsync_DisabledJob_DoesNotDispatchExecutor()
    {
        var now = new DateTimeOffset(2026, 03, 09, 16, 00, 00, TimeSpan.Zero);
        var disabledJob = CreateRecurringJob("job-1", now.AddSeconds(-1)) with { Status = CronJobStatus.Disabled };
        var store = new RecordingCronStore(disabledJob);
        var executor = new RecordingExecutor();
        var sut = CreateEngine(store, executor, timeProvider: new StubTimeProvider(now));

        await sut.OnTickAsync(CancellationToken.None);
        await sut.DrainAsync(CancellationToken.None);

        Assert.Empty(executor.ExecutedJobIds);
    }

    [Fact]
    public async Task OnTickAsync_ActiveJob_DoesNotDispatchSecondExecution()
    {
        var now = new DateTimeOffset(2026, 03, 09, 16, 00, 00, TimeSpan.Zero);
        var job = CreateRecurringJob("job-1", now.AddSeconds(-1));
        var store = new RecordingCronStore(job);
        var executor = new RecordingExecutor
        {
            Completion = new TaskCompletionSource<CronRunResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var sut = CreateEngine(store, executor, timeProvider: new StubTimeProvider(now));

        await sut.OnTickAsync(CancellationToken.None);
        await executor.WaitForCallCountAsync(1);

        Assert.True(sut.IsJobActive("job-1"));

        await sut.OnTickAsync(CancellationToken.None);

        Assert.Equal(["job-1"], executor.ExecutedJobIds);

        executor.Completion.SetResult(new CronRunResult("done", CronRunOutcome.Success, null, 25, false));
        await sut.DrainAsync(CancellationToken.None);

        Assert.False(sut.IsJobActive("job-1"));
    }

    [Fact]
    public async Task OnTickAsync_ConcurrencyLimitOne_OnlyDispatchesFirstDueJob()
    {
        var now = new DateTimeOffset(2026, 03, 09, 16, 00, 00, TimeSpan.Zero);
        var firstJob = CreateRecurringJob("job-1", now.AddSeconds(-2));
        var secondJob = CreateRecurringJob("job-2", now.AddSeconds(-1));
        var store = new RecordingCronStore(firstJob, secondJob);
        var executor = new RecordingExecutor
        {
            Completion = new TaskCompletionSource<CronRunResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var sut = CreateEngine(store, executor, maxConcurrentExecutions: 1, timeProvider: new StubTimeProvider(now));

        await sut.OnTickAsync(CancellationToken.None);
        await executor.WaitForCallCountAsync(1);

        Assert.Equal(["job-1"], executor.ExecutedJobIds);
        Assert.Equal(1, sut.ActiveJobCount);

        executor.Completion.SetResult(new CronRunResult("done", CronRunOutcome.Success, null, 25, false));
        await sut.DrainAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OnTickAsync_RecurringSuccess_UpdatesLifecycleAndPublishesResult()
    {
        var now = new DateTimeOffset(2026, 03, 09, 16, 00, 00, TimeSpan.Zero);
        var job = CreateRecurringJob("job-1", now.AddSeconds(-1)) with
        {
            Backoff = new BackoffState(1, now.AddMinutes(-1), "previous failure")
        };
        var store = new RecordingCronStore(job);
        var executor = new RecordingExecutor
        {
            Result = new CronRunResult("Cron run complete.", CronRunOutcome.Success, null, 42, false)
        };
        var sink = new RecordingCronOutputSink();
        var sut = CreateEngine(store, executor, sink, timeProvider: new StubTimeProvider(now));

        await sut.OnTickAsync(CancellationToken.None);
        await sut.DrainAsync(CancellationToken.None);

        var updatedJob = Assert.Single(store.UpdatedJobs);
        Assert.Equal(now, updatedJob.LastRunAtUtc);
        Assert.Equal(now.AddSeconds(5), updatedJob.NextRunAtUtc);
        Assert.Null(updatedJob.Backoff);
        Assert.False(sut.IsJobActive("job-1"));
        var publishedEvent = Assert.Single(sink.PublishedEvents);
        Assert.Equal("job-1", publishedEvent.JobId);
        Assert.Equal(CronRunOutcome.Success, publishedEvent.Outcome);
        Assert.Equal("Cron run complete.", publishedEvent.Content);
        var historyRecord = Assert.Single(store.RunHistory);
        Assert.Equal("job-1", historyRecord.JobId);
        Assert.Equal(CronRunOutcome.Success, historyRecord.Outcome);
    }

    [Fact]
    public async Task OnTickAsync_RecurringFailure_AppliesBackoffAndClassifiesError()
    {
        var now = new DateTimeOffset(2026, 03, 09, 16, 00, 00, TimeSpan.Zero);
        var job = CreateRecurringJob("job-1", now.AddSeconds(-1));
        var store = new RecordingCronStore(job);
        var executor = new RecordingExecutor
        {
            Exception = new IOException("temporary network issue")
        };
        var classifier = new RecordingCronErrorClassifier(isTransient: true);
        var sink = new RecordingCronOutputSink();
        var sut = CreateEngine(store, executor, sink, classifier, timeProvider: new StubTimeProvider(now));

        await sut.OnTickAsync(CancellationToken.None);
        await sut.DrainAsync(CancellationToken.None);

        var updatedJob = Assert.Single(store.UpdatedJobs);
        Assert.Equal(CronJobStatus.Enabled, updatedJob.Status);
        Assert.NotNull(updatedJob.Backoff);
        Assert.Equal(1, updatedJob.Backoff!.ConsecutiveFailures);
        Assert.Equal(now.AddSeconds(30), updatedJob.Backoff.NextRetryAtUtc);
        _ = Assert.Single(classifier.Exceptions);
        Assert.Contains("temporary network issue", Assert.Single(sink.PublishedEvents).ErrorMessage, StringComparison.Ordinal);
        Assert.False(sut.IsJobActive("job-1"));
    }

    [Fact]
    public async Task OnTickAsync_OneShotSuccess_DisablesJob()
    {
        var now = new DateTimeOffset(2026, 03, 09, 16, 00, 00, TimeSpan.Zero);
        var job = CreateOneShotJob("job-1", now.AddSeconds(-1));
        var store = new RecordingCronStore(job);
        var executor = new RecordingExecutor
        {
            Result = new CronRunResult("Reminder delivered.", CronRunOutcome.Success, null, 12, false)
        };
        var sut = CreateEngine(store, executor, timeProvider: new StubTimeProvider(now));

        await sut.OnTickAsync(CancellationToken.None);
        await sut.DrainAsync(CancellationToken.None);

        var updatedJob = Assert.Single(store.UpdatedJobs);
        Assert.Equal(CronJobStatus.Disabled, updatedJob.Status);
        Assert.Null(updatedJob.NextRunAtUtc);
        Assert.False(sut.IsJobActive("job-1"));
    }

    [Fact]
    public async Task OnTickAsync_OneShotTransientFailure_KeepsJobEnabledWithBackoff()
    {
        var now = new DateTimeOffset(2026, 03, 09, 16, 00, 00, TimeSpan.Zero);
        var job = CreateOneShotJob("job-1", now.AddSeconds(-1));
        var store = new RecordingCronStore(job);
        var executor = new RecordingExecutor
        {
            Exception = new IOException("temporary network issue")
        };
        var classifier = new RecordingCronErrorClassifier(isTransient: true);
        var sut = CreateEngine(store, executor, classifier: classifier, timeProvider: new StubTimeProvider(now));

        await sut.OnTickAsync(CancellationToken.None);
        await sut.DrainAsync(CancellationToken.None);

        var updatedJob = Assert.Single(store.UpdatedJobs);
        Assert.Equal(CronJobStatus.Enabled, updatedJob.Status);
        Assert.NotNull(updatedJob.Backoff);
        Assert.Equal(now.AddSeconds(30), updatedJob.Backoff!.NextRetryAtUtc);
    }

    [Fact]
    public async Task OnTickAsync_OneShotPermanentFailure_DisablesJobImmediately()
    {
        var now = new DateTimeOffset(2026, 03, 09, 16, 00, 00, TimeSpan.Zero);
        var job = CreateOneShotJob("job-1", now.AddSeconds(-1));
        var store = new RecordingCronStore(job);
        var executor = new RecordingExecutor
        {
            Exception = new ArgumentException("invalid prompt")
        };
        var classifier = new RecordingCronErrorClassifier(isTransient: false);
        var sut = CreateEngine(store, executor, classifier: classifier, timeProvider: new StubTimeProvider(now));

        await sut.OnTickAsync(CancellationToken.None);
        await sut.DrainAsync(CancellationToken.None);

        var updatedJob = Assert.Single(store.UpdatedJobs);
        Assert.Equal(CronJobStatus.Disabled, updatedJob.Status);
        Assert.Null(updatedJob.Backoff);
    }

    [Fact]
    public async Task OnTickAsync_OverdueJob_FiresOnFirstTick()
    {
        var now = new DateTimeOffset(2026, 03, 09, 16, 00, 00, TimeSpan.Zero);
        var overdueJob = CreateRecurringJob("job-1", now.AddHours(-2));
        var store = new RecordingCronStore(overdueJob);
        var executor = new RecordingExecutor();
        var sut = CreateEngine(store, executor, timeProvider: new StubTimeProvider(now));

        await sut.OnTickAsync(CancellationToken.None);
        await executor.WaitForCallCountAsync(1);

        Assert.Equal(["job-1"], executor.ExecutedJobIds);

        await sut.DrainAsync(CancellationToken.None);
    }

    private static CronEngine CreateEngine(
        RecordingCronStore store,
        RecordingExecutor? executor = null,
        RecordingCronOutputSink? sink = null,
        RecordingCronErrorClassifier? classifier = null,
        int maxConcurrentExecutions = 1,
        TimeProvider? timeProvider = null)
    {
        return new CronEngine(
            store,
            store,
            [executor ?? new RecordingExecutor()],
            classifier ?? new RecordingCronErrorClassifier(isTransient: false),
            sink ?? new RecordingCronOutputSink(),
            maxConcurrentExecutions,
            timeProvider ?? TimeProvider.System);
    }

    private static CronJob CreateRecurringJob(string jobId, DateTimeOffset nextRunAtUtc)
    {
        return new CronJob
        {
            Id = jobId,
            Name = $"Job {jobId}",
            Schedule = new FixedIntervalSchedule(5_000),
            Payload = new PromptPayload("Check status.", null, null),
            Status = CronJobStatus.Enabled,
            CreatedAtUtc = nextRunAtUtc.AddMinutes(-10),
            NextRunAtUtc = nextRunAtUtc
        };
    }

    private static CronJob CreateOneShotJob(string jobId, DateTimeOffset fireAtUtc)
    {
        return new CronJob
        {
            Id = jobId,
            Name = $"Job {jobId}",
            Schedule = new OneShotSchedule(fireAtUtc),
            Payload = new PromptPayload("Remind me.", null, null),
            Status = CronJobStatus.Enabled,
            CreatedAtUtc = fireAtUtc.AddMinutes(-10),
            NextRunAtUtc = fireAtUtc
        };
    }

    private sealed class RecordingCronStore(params CronJob[] jobs) : ICronJobStore, ICronRunHistoryStore
    {
        private readonly Dictionary<string, CronJob> jobMap = jobs.ToDictionary(static job => job.Id, StringComparer.Ordinal);

        public int InitializeCallCount { get; private set; }

        public List<CronJob> UpdatedJobs { get; } = [];

        public List<CronRunRecord> RunHistory { get; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            InitializeCallCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CronJob>> GetAllJobsAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<CronJob> snapshot = jobMap.Values
                .OrderBy(static job => job.Id, StringComparer.Ordinal)
                .ToArray();

            return Task.FromResult(snapshot);
        }

        public Task<CronJob?> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
        {
            jobMap.TryGetValue(jobId, out var job);

            return Task.FromResult(job);
        }

        public Task AddJobAsync(CronJob job, CancellationToken cancellationToken = default)
        {
            jobMap[job.Id] = job;
            return Task.CompletedTask;
        }

        public Task UpdateJobAsync(CronJob job, CancellationToken cancellationToken = default)
        {
            jobMap[job.Id] = job;
            UpdatedJobs.Add(job);
            return Task.CompletedTask;
        }

        public Task RemoveJobAsync(string jobId, CancellationToken cancellationToken = default)
        {
            _ = jobMap.Remove(jobId);
            return Task.CompletedTask;
        }

        public Task AppendRunRecordAsync(CronRunRecord record, CancellationToken cancellationToken = default)
        {
            RunHistory.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CronRunRecord>> GetRunHistoryAsync(string jobId, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<CronRunRecord> history = RunHistory
                .Where(record => string.Equals(record.JobId, jobId, StringComparison.Ordinal))
                .ToArray();

            return Task.FromResult(history);
        }
    }

    private sealed class RecordingExecutor : ICronJobExecutor
    {
        private readonly object gate = new();
        private readonly List<string> executedJobIds = [];
        private readonly TaskCompletionSource callObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Type PayloadType => typeof(PromptPayload);

        public CronRunResult Result { get; init; } = new("done", CronRunOutcome.Success, null, 25, false);

        public Exception? Exception { get; init; }

        public TaskCompletionSource<CronRunResult>? Completion { get; init; }

        public IReadOnlyList<string> ExecutedJobIds
        {
            get
            {
                lock (gate)
                {
                    return executedJobIds.ToArray();
                }
            }
        }

        public async Task<CronRunResult> ExecuteAsync(CronJob job, string runId, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                executedJobIds.Add(job.Id);
            }

            callObserved.TrySetResult();

            if (Exception is not null)
            {
                throw Exception;
            }

            if (Completion is not null)
            {
                return await Completion.Task.WaitAsync(cancellationToken);
            }

            return Result;
        }

        public async Task WaitForCallCountAsync(int expectedCount)
        {
            await callObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline)
            {
                if (ExecutedJobIds.Count >= expectedCount)
                {
                    return;
                }

                await Task.Delay(10);
            }

            throw new TimeoutException($"Expected {expectedCount} executor calls but observed {ExecutedJobIds.Count}.");
        }
    }

    private sealed class RecordingCronErrorClassifier(bool isTransient) : ICronErrorClassifier
    {
        public List<Exception> Exceptions { get; } = [];

        public bool IsTransient(Exception exception)
        {
            Exceptions.Add(exception);
            return isTransient;
        }
    }

    private sealed class RecordingCronOutputSink : ICronOutputSink
    {
        public List<CronRunEvent> PublishedEvents { get; } = [];

        public Task PublishResultAsync(CronRunEvent result, CancellationToken cancellationToken)
        {
            PublishedEvents.Add(result);
            return Task.CompletedTask;
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
