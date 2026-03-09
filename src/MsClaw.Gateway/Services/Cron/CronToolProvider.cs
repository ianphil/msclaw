using System.ComponentModel;
using Microsoft.Extensions.AI;
using MsClaw.Gateway.Services.Tools;

namespace MsClaw.Gateway.Services.Cron;

/// <summary>
/// Exposes cron management tools to the gateway tool bridge.
/// </summary>
public sealed class CronToolProvider : IToolProvider
{
    private readonly ICronJobStore jobStore;
    private readonly ICronRunHistoryStore runHistoryStore;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Creates a cron tool provider backed by the cron job store and run history store.
    /// </summary>
    /// <param name="jobStore">The cron job store used for CRUD operations.</param>
    /// <param name="runHistoryStore">The cron run history store used for inspection.</param>
    /// <param name="timeProvider">The time source used for timestamps and next-run calculations.</param>
    public CronToolProvider(
        ICronJobStore jobStore,
        ICronRunHistoryStore runHistoryStore,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(jobStore);
        ArgumentNullException.ThrowIfNull(runHistoryStore);

        this.jobStore = jobStore;
        this.runHistoryStore = runHistoryStore;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public string Name => "cron";

    /// <inheritdoc />
    public ToolSourceTier Tier => ToolSourceTier.Bundled;

    /// <inheritdoc />
    public Task<IReadOnlyList<ToolDescriptor>> DiscoverAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ToolDescriptor> descriptors =
        [
            CreateDescriptor(CreateCronCreateTool()),
            CreateDescriptor(CreateCronListTool()),
            CreateDescriptor(CreateCronGetTool()),
            CreateDescriptor(CreateCronUpdateTool()),
            CreateDescriptor(CreateCronDeleteTool()),
            CreateDescriptor(CreateCronPauseTool()),
            CreateDescriptor(CreateCronResumeTool())
        ];

        return Task.FromResult(descriptors);
    }

    /// <inheritdoc />
    public Task WaitForSurfaceChangeAsync(CancellationToken cancellationToken)
    {
        return Task.Delay(Timeout.Infinite, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Creates the tool used to add a new cron job.
    /// </summary>
    private AIFunction CreateCronCreateTool()
    {
        return AIFunctionFactory.Create(CreateCronJobAsync, "cron_create", "Creates a persisted cron job.");
    }

    /// <summary>
    /// Creates the tool used to list persisted cron jobs.
    /// </summary>
    private AIFunction CreateCronListTool()
    {
        return AIFunctionFactory.Create(ListCronJobsAsync, "cron_list", "Lists persisted cron jobs.");
    }

    /// <summary>
    /// Creates the tool used to inspect one cron job and its history.
    /// </summary>
    private AIFunction CreateCronGetTool()
    {
        return AIFunctionFactory.Create(GetCronJobAsync, "cron_get", "Gets one cron job and recent run history.");
    }

    /// <summary>
    /// Creates the tool used to update an existing cron job.
    /// </summary>
    private AIFunction CreateCronUpdateTool()
    {
        return AIFunctionFactory.Create(UpdateCronJobAsync, "cron_update", "Updates an existing cron job.");
    }

    /// <summary>
    /// Creates the tool used to delete a cron job.
    /// </summary>
    private AIFunction CreateCronDeleteTool()
    {
        return AIFunctionFactory.Create(DeleteCronJobAsync, "cron_delete", "Deletes a cron job.");
    }

    /// <summary>
    /// Creates the tool used to pause a cron job.
    /// </summary>
    private AIFunction CreateCronPauseTool()
    {
        return AIFunctionFactory.Create(PauseCronJobAsync, "cron_pause", "Pauses a cron job without deleting it.");
    }

    /// <summary>
    /// Creates the tool used to resume a paused cron job.
    /// </summary>
    private AIFunction CreateCronResumeTool()
    {
        return AIFunctionFactory.Create(ResumeCronJobAsync, "cron_resume", "Resumes a paused cron job.");
    }

    /// <summary>
    /// Creates a tool descriptor owned by this provider.
    /// </summary>
    /// <param name="function">The tool function.</param>
    private ToolDescriptor CreateDescriptor(AIFunction function)
    {
        return new ToolDescriptor
        {
            Function = function,
            ProviderName = Name,
            Tier = Tier,
            AlwaysVisible = true
        };
    }

    /// <summary>
    /// Creates and persists a cron job from tool arguments.
    /// </summary>
    private async Task<object> CreateCronJobAsync(
        [Description("Human-readable job name.")] string name,
        [Description("Schedule type: oneShot, fixedInterval, or cron.")] string scheduleType,
        [Description("Schedule value: ISO timestamp, interval milliseconds, or cron expression.")] string scheduleValue,
        [Description("Optional IANA or system timezone for cron schedules.")] string? timezone = null,
        [Description("Payload type: prompt or command.")] string payloadType = "prompt",
        [Description("Prompt text for prompt payloads.")] string? prompt = null,
        [Description("Command name for command payloads.")] string? command = null,
        [Description("Optional preload tool names for prompt payloads.")] string[]? preloadToolNames = null,
        [Description("Optional model override for prompt payloads.")] string? model = null,
        [Description("Optional command arguments.")] string? arguments = null,
        [Description("Optional command working directory.")] string? workingDirectory = null,
        [Description("Optional command timeout in seconds.")] int? timeoutSeconds = null,
        [Description("Optional maximum concurrency for the job.")] int? maxConcurrency = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var now = timeProvider.GetUtcNow();
        var schedule = CreateSchedule(scheduleType, scheduleValue, timezone);
        var payload = CreatePayload(payloadType, prompt, command, preloadToolNames, model, arguments, workingDirectory, timeoutSeconds);
        var job = new CronJob
        {
            Id = ToKebabCase(name),
            Name = name.Trim(),
            Schedule = schedule,
            Payload = payload,
            Status = CronJobStatus.Enabled,
            MaxConcurrency = maxConcurrency ?? 1,
            CreatedAtUtc = now,
            NextRunAtUtc = CronScheduleCalculator.ComputeNextRun(schedule, null, now)
        };

        await jobStore.AddJobAsync(job);

        return new
        {
            Job = job,
            Summary = $"Created cron job '{job.Name}' ({job.Id}) with next run at {job.NextRunAtUtc:O}."
        };
    }

    /// <summary>
    /// Returns a structured summary of all persisted cron jobs.
    /// </summary>
    private async Task<object> ListCronJobsAsync()
    {
        var jobs = await jobStore.GetAllJobsAsync();
        var summaries = jobs
            .Select(CreateJobSummary)
            .ToArray();

        var summaryText = summaries.Length is 0
            ? "No cron jobs are configured."
            : string.Join(Environment.NewLine, summaries.Select(static summary =>
                $"- {summary.Name} ({summary.Id}) [{summary.Status}] next={summary.NextRunAtUtc?.ToString("O") ?? "none"}"));

        return new
        {
            Count = summaries.Length,
            Jobs = summaries,
            Summary = summaryText
        };
    }

    /// <summary>
    /// Returns one cron job and its run history.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    private async Task<object> GetCronJobAsync(
        [Description("The cron job identifier.")] string jobId)
    {
        var job = await RequireJobAsync(jobId);
        var history = await runHistoryStore.GetRunHistoryAsync(job.Id);

        return new
        {
            Job = CreateJobSummary(job),
            HistoryCount = history.Count,
            History = history,
            Summary = $"Cron job '{job.Name}' has {history.Count} recorded runs."
        };
    }

    /// <summary>
    /// Updates an existing cron job with any supplied fields.
    /// </summary>
    private async Task<object> UpdateCronJobAsync(
        [Description("The cron job identifier.")] string jobId,
        [Description("Optional replacement display name.")] string? name = null,
        [Description("Optional replacement schedule type: oneShot, fixedInterval, or cron.")] string? scheduleType = null,
        [Description("Optional replacement schedule value.")] string? scheduleValue = null,
        [Description("Optional replacement timezone for cron schedules.")] string? timezone = null,
        [Description("Optional replacement payload type: prompt or command.")] string? payloadType = null,
        [Description("Optional replacement prompt text.")] string? prompt = null,
        [Description("Optional replacement command name.")] string? command = null,
        [Description("Optional replacement preload tool names.")] string[]? preloadToolNames = null,
        [Description("Optional replacement model override.")] string? model = null,
        [Description("Optional replacement command arguments.")] string? arguments = null,
        [Description("Optional replacement command working directory.")] string? workingDirectory = null,
        [Description("Optional replacement command timeout in seconds.")] int? timeoutSeconds = null,
        [Description("Optional replacement max concurrency.")] int? maxConcurrency = null)
    {
        var existingJob = await RequireJobAsync(jobId);
        var updatedSchedule = scheduleType is null
            ? existingJob.Schedule
            : CreateSchedule(scheduleType, scheduleValue ?? throw new ArgumentException("scheduleValue must be provided when scheduleType is specified.", nameof(scheduleValue)), timezone);
        var updatedPayload = payloadType is null
            ? existingJob.Payload
            : CreatePayload(payloadType, prompt, command, preloadToolNames, model, arguments, workingDirectory, timeoutSeconds);
        var updatedJob = existingJob with
        {
            Name = string.IsNullOrWhiteSpace(name) ? existingJob.Name : name.Trim(),
            Schedule = updatedSchedule,
            Payload = updatedPayload,
            MaxConcurrency = maxConcurrency ?? existingJob.MaxConcurrency,
            NextRunAtUtc = CronScheduleCalculator.ComputeNextRun(updatedSchedule, existingJob.LastRunAtUtc, timeProvider.GetUtcNow())
        };

        await jobStore.UpdateJobAsync(updatedJob);

        return new
        {
            Job = updatedJob,
            Summary = $"Updated cron job '{updatedJob.Name}' ({updatedJob.Id})."
        };
    }

    /// <summary>
    /// Deletes an existing cron job.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    private async Task<object> DeleteCronJobAsync(
        [Description("The cron job identifier.")] string jobId)
    {
        await jobStore.RemoveJobAsync(jobId);

        return new
        {
            JobId = jobId,
            Summary = $"Deleted cron job '{jobId}'."
        };
    }

    /// <summary>
    /// Disables a cron job without deleting it.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    private async Task<object> PauseCronJobAsync(
        [Description("The cron job identifier.")] string jobId)
    {
        var existingJob = await RequireJobAsync(jobId);
        var updatedJob = existingJob with
        {
            Status = CronJobStatus.Disabled
        };

        await jobStore.UpdateJobAsync(updatedJob);

        return new
        {
            Job = updatedJob,
            Summary = $"Paused cron job '{updatedJob.Name}' ({updatedJob.Id})."
        };
    }

    /// <summary>
    /// Re-enables a paused cron job and recomputes its next run.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    private async Task<object> ResumeCronJobAsync(
        [Description("The cron job identifier.")] string jobId)
    {
        var existingJob = await RequireJobAsync(jobId);
        var updatedJob = existingJob with
        {
            Status = CronJobStatus.Enabled,
            NextRunAtUtc = CronScheduleCalculator.ComputeNextRun(existingJob.Schedule, existingJob.LastRunAtUtc, timeProvider.GetUtcNow())
        };

        await jobStore.UpdateJobAsync(updatedJob);

        return new
        {
            Job = updatedJob,
            Summary = $"Resumed cron job '{updatedJob.Name}' ({updatedJob.Id})."
        };
    }

    /// <summary>
    /// Loads a job or throws when the identifier is unknown.
    /// </summary>
    /// <param name="jobId">The requested job identifier.</param>
    private async Task<CronJob> RequireJobAsync(string jobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        var job = await jobStore.GetJobAsync(jobId);
        return job ?? throw new InvalidOperationException($"Cron job '{jobId}' was not found.");
    }

    /// <summary>
    /// Builds a schedule from the supplied tool arguments.
    /// </summary>
    private static JobSchedule CreateSchedule(string scheduleType, string scheduleValue, string? timezone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleType);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleValue);

        return scheduleType.Trim().ToLowerInvariant() switch
        {
            "oneshot" => new OneShotSchedule(DateTimeOffset.Parse(scheduleValue)),
            "fixedinterval" => new FixedIntervalSchedule(long.Parse(scheduleValue)),
            "cron" => new CronExpressionSchedule(scheduleValue.Trim(), timezone),
            _ => throw new ArgumentOutOfRangeException(nameof(scheduleType), $"Unsupported schedule type '{scheduleType}'.")
        };
    }

    /// <summary>
    /// Builds a payload from the supplied tool arguments.
    /// </summary>
    private static JobPayload CreatePayload(
        string payloadType,
        string? prompt,
        string? command,
        string[]? preloadToolNames,
        string? model,
        string? arguments,
        string? workingDirectory,
        int? timeoutSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadType);

        return payloadType.Trim().ToLowerInvariant() switch
        {
            "prompt" => new PromptPayload(
                prompt ?? throw new ArgumentException("prompt must be provided for prompt payloads.", nameof(prompt)),
                preloadToolNames,
                model),
            "command" => new CommandPayload(
                command ?? throw new ArgumentException("command must be provided for command payloads.", nameof(command)),
                arguments,
                workingDirectory,
                timeoutSeconds ?? 300),
            _ => throw new ArgumentOutOfRangeException(nameof(payloadType), $"Unsupported payload type '{payloadType}'.")
        };
    }

    /// <summary>
    /// Projects a cron job into a stable summary shape for tool responses.
    /// </summary>
    /// <param name="job">The job to summarize.</param>
    private static JobSummary CreateJobSummary(CronJob job)
    {
        return new JobSummary(
            job.Id,
            job.Name,
            job.Status.ToString(),
            FormatSchedule(job.Schedule),
            job.Payload switch
            {
                PromptPayload => "prompt",
                CommandPayload => "command",
                _ => job.Payload.GetType().Name
            },
            job.LastRunAtUtc,
            job.NextRunAtUtc,
            job.MaxConcurrency);
    }

    /// <summary>
    /// Formats a schedule into compact human-readable text.
    /// </summary>
    /// <param name="schedule">The schedule to format.</param>
    private static string FormatSchedule(JobSchedule schedule)
    {
        return schedule switch
        {
            OneShotSchedule oneShotSchedule => $"oneShot:{oneShotSchedule.FireAtUtc:O}",
            FixedIntervalSchedule fixedIntervalSchedule => $"fixedInterval:{fixedIntervalSchedule.IntervalMs}",
            CronExpressionSchedule cronExpressionSchedule => $"cron:{cronExpressionSchedule.Expression}@{cronExpressionSchedule.Timezone ?? "UTC"}",
            _ => schedule.GetType().Name
        };
    }

    /// <summary>
    /// Normalizes a display name into a kebab-case job identifier.
    /// </summary>
    /// <param name="value">The source display name.</param>
    private static string ToKebabCase(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var builder = new System.Text.StringBuilder(value.Length);
        var previousWasSeparator = false;

        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                if (char.IsUpper(character) && builder.Length > 0 && previousWasSeparator is false)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
                continue;
            }

            if (previousWasSeparator || builder.Length is 0)
            {
                continue;
            }

            builder.Append('-');
            previousWasSeparator = true;
        }

        return builder.ToString().Trim('-');
    }

    /// <summary>
    /// Represents the structured summary returned for a cron job.
    /// </summary>
    /// <param name="Id">Gets the job identifier.</param>
    /// <param name="Name">Gets the display name.</param>
    /// <param name="Status">Gets the lifecycle state.</param>
    /// <param name="Schedule">Gets the human-readable schedule.</param>
    /// <param name="PayloadType">Gets the payload kind.</param>
    /// <param name="LastRunAtUtc">Gets the last execution time.</param>
    /// <param name="NextRunAtUtc">Gets the next scheduled execution time.</param>
    /// <param name="MaxConcurrency">Gets the job concurrency limit.</param>
    private sealed record JobSummary(
        string Id,
        string Name,
        string Status,
        string Schedule,
        string PayloadType,
        DateTimeOffset? LastRunAtUtc,
        DateTimeOffset? NextRunAtUtc,
        int MaxConcurrency);
}
