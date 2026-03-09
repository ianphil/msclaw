using System.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace MsClaw.Gateway.Services.Cron;

/// <summary>
/// Evaluates due cron jobs on a timer and dispatches them to the matching executor.
/// </summary>
public sealed class CronEngine : ICronEngine
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(2);

    private readonly ICronJobStore jobStore;
    private readonly ICronRunHistoryStore runHistoryStore;
    private readonly IReadOnlyDictionary<Type, ICronJobExecutor> executorsByPayloadType;
    private readonly ICronErrorClassifier errorClassifier;
    private readonly ICronOutputSink outputSink;
    private readonly SemaphoreSlim concurrencyGate;
    private readonly TimeProvider timeProvider;
    private readonly object sync = new();
    private readonly HashSet<string> activeJobIds = new(StringComparer.Ordinal);
    private readonly List<Task> activeExecutions = [];

    private CancellationTokenSource? loopCancellationSource;
    private PeriodicTimer? timer;
    private Task? runLoopTask;

    /// <summary>
    /// Creates a cron engine with the provided store, executors, and output publisher.
    /// </summary>
    /// <param name="jobStore">The cron job store used for reads and updates.</param>
    /// <param name="runHistoryStore">The run history store used to persist execution records.</param>
    /// <param name="executors">The available executors keyed by payload type.</param>
    /// <param name="errorClassifier">The failure classifier used for retry decisions.</param>
    /// <param name="outputSink">The publisher used for completed run events.</param>
    /// <param name="maxConcurrentExecutions">The maximum number of jobs that may run at once.</param>
    /// <param name="timeProvider">The time source used for timer creation and due-job evaluation.</param>
    public CronEngine(
        ICronJobStore jobStore,
        ICronRunHistoryStore runHistoryStore,
        IEnumerable<ICronJobExecutor> executors,
        ICronErrorClassifier errorClassifier,
        ICronOutputSink outputSink,
        int maxConcurrentExecutions = 1,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(jobStore);
        ArgumentNullException.ThrowIfNull(runHistoryStore);
        ArgumentNullException.ThrowIfNull(executors);
        ArgumentNullException.ThrowIfNull(errorClassifier);
        ArgumentNullException.ThrowIfNull(outputSink);

        if (maxConcurrentExecutions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentExecutions), "Maximum concurrent executions must be positive.");
        }

        this.jobStore = jobStore;
        this.runHistoryStore = runHistoryStore;
        this.errorClassifier = errorClassifier;
        this.outputSink = outputSink;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        concurrencyGate = new SemaphoreSlim(maxConcurrentExecutions, maxConcurrentExecutions);
        executorsByPayloadType = executors.ToDictionary(
            static executor => executor.PayloadType,
            static executor => executor);
    }

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public int ActiveJobCount
    {
        get
        {
            lock (sync)
            {
                return activeJobIds.Count;
            }
        }
    }

    /// <inheritdoc />
    public bool IsJobActive(string jobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        lock (sync)
        {
            return activeJobIds.Contains(jobId);
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (runLoopTask is not null)
        {
            return;
        }

        await jobStore.InitializeAsync(cancellationToken);
        loopCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timer = new PeriodicTimer(TickInterval, timeProvider);
        IsRunning = true;
        runLoopTask = RunLoopAsync(loopCancellationSource.Token);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        IsRunning = false;

        loopCancellationSource?.Cancel();
        timer?.Dispose();

        if (runLoopTask is not null)
        {
            await runLoopTask;
        }

        await DrainAsync(cancellationToken);

        runLoopTask = null;
        loopCancellationSource?.Dispose();
        loopCancellationSource = null;
        timer = null;
    }

    /// <summary>
    /// Evaluates the current job set and dispatches due work without waiting for completion.
    /// </summary>
    /// <param name="cancellationToken">Cancels the evaluation.</param>
    internal async Task OnTickAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var jobs = await jobStore.GetAllJobsAsync(cancellationToken);
        var dueJobs = jobs
            .Where(job => IsJobDue(job, now))
            .OrderBy(static job => job.NextRunAtUtc)
            .ThenBy(static job => job.Id, StringComparer.Ordinal);

        foreach (var job in dueJobs)
        {
            if (await TryDispatchJobAsync(job, cancellationToken) is false)
            {
                continue;
            }
        }
    }

    /// <summary>
    /// Waits for all active executions currently known to the engine.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    internal async Task DrainAsync(CancellationToken cancellationToken)
    {
        Task[] executions;

        lock (sync)
        {
            executions = activeExecutions.ToArray();
        }

        if (executions.Length is 0)
        {
            return;
        }

        await Task.WhenAll(executions).WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Runs the timer loop until cancellation is requested.
    /// </summary>
    /// <param name="cancellationToken">Cancels the timer loop.</param>
    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        if (timer is null)
        {
            return;
        }

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await OnTickAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// Attempts to dispatch a due job when concurrency and active-state rules allow it.
    /// </summary>
    /// <param name="job">The job to dispatch.</param>
    /// <param name="cancellationToken">Cancels the dispatch attempt.</param>
    /// <returns><see langword="true"/> when the job was dispatched; otherwise, <see langword="false"/>.</returns>
    private async Task<bool> TryDispatchJobAsync(CronJob job, CancellationToken cancellationToken)
    {
        if (await concurrencyGate.WaitAsync(0, cancellationToken) is false)
        {
            return false;
        }

        lock (sync)
        {
            if (activeJobIds.Add(job.Id) is false)
            {
                concurrencyGate.Release();
                return false;
            }
        }

        var executionTask = ExecuteTrackedJobAsync(job, cancellationToken);
        lock (sync)
        {
            activeExecutions.Add(executionTask);
        }

        _ = executionTask.ContinueWith(
            static (task, state) => ((CronEngine)state!).RemoveTrackedExecution(task),
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return true;
    }

    /// <summary>
    /// Executes a job and always releases its active slot and concurrency permit.
    /// </summary>
    /// <param name="job">The dispatched job.</param>
    /// <param name="cancellationToken">Cancels the execution.</param>
    private async Task ExecuteTrackedJobAsync(CronJob job, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteJobAsync(job, cancellationToken);
        }
        finally
        {
            lock (sync)
            {
                _ = activeJobIds.Remove(job.Id);
            }

            concurrencyGate.Release();
        }
    }

    /// <summary>
    /// Removes a completed execution task from the tracked task set.
    /// </summary>
    /// <param name="task">The completed task to remove.</param>
    private void RemoveTrackedExecution(Task task)
    {
        lock (sync)
        {
            _ = activeExecutions.Remove(task);
        }
    }

    /// <summary>
    /// Executes a single job and persists the resulting lifecycle changes.
    /// </summary>
    /// <param name="job">The dispatched job.</param>
    /// <param name="cancellationToken">Cancels the execution.</param>
    private async Task ExecuteJobAsync(CronJob job, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("n");
        var startedAtUtc = timeProvider.GetUtcNow();
        var result = await ExecuteWithClassificationAsync(job, runId, cancellationToken);
        var completedAtUtc = timeProvider.GetUtcNow();
        var updatedJob = CronJobLifecycle.ApplyResult(job, result, startedAtUtc, completedAtUtc);

        await runHistoryStore.AppendRunRecordAsync(
            new CronRunRecord(
                runId,
                job.Id,
                startedAtUtc,
                completedAtUtc,
                result.Outcome,
                result.ErrorMessage,
                result.DurationMs),
            cancellationToken);
        await jobStore.UpdateJobAsync(updatedJob, cancellationToken);
        await outputSink.PublishResultAsync(
            new CronRunEvent(
                job.Id,
                job.Name,
                runId,
                result.Outcome,
                result.Content,
                result.ErrorMessage,
                result.DurationMs),
            cancellationToken);
    }

    /// <summary>
    /// Executes the payload-specific executor and converts thrown exceptions into classified run results.
    /// </summary>
    /// <param name="job">The job to execute.</param>
    /// <param name="runId">The run identifier.</param>
    /// <param name="cancellationToken">Cancels the execution.</param>
    /// <returns>The classified run result.</returns>
    private async Task<CronRunResult> ExecuteWithClassificationAsync(CronJob job, string runId, CancellationToken cancellationToken)
    {
        if (executorsByPayloadType.TryGetValue(job.Payload.GetType(), out var executor) is false)
        {
            throw new InvalidOperationException($"No cron executor is registered for payload type '{job.Payload.GetType().Name}'.");
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            return await executor.ExecuteAsync(job, runId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var isTransient = errorClassifier.IsTransient(ex);

            return new CronRunResult(string.Empty, CronRunOutcome.Failure, ex.Message, stopwatch.ElapsedMilliseconds, isTransient);
        }
    }

    /// <summary>
    /// Returns whether the job is due for execution at the provided instant.
    /// </summary>
    /// <param name="job">The job being evaluated.</param>
    /// <param name="now">The current UTC time.</param>
    /// <returns><see langword="true"/> when the job should be considered for dispatch.</returns>
    private bool IsJobDue(CronJob job, DateTimeOffset now)
    {
        if (job.Status is not CronJobStatus.Enabled || job.NextRunAtUtc is null)
        {
            return false;
        }

        if (job.Backoff is not null && job.Backoff.NextRetryAtUtc > now)
        {
            return false;
        }

        lock (sync)
        {
            if (activeJobIds.Contains(job.Id))
            {
                return false;
            }
        }

        return job.NextRunAtUtc <= now;
    }
}
