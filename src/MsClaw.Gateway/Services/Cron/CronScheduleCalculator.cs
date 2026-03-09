using Cronos;

namespace MsClaw.Gateway.Services.Cron;

/// <summary>
/// Computes the next scheduled run time for a cron job.
/// Uses the Cronos library for cron-expression parsing and timezone-aware scheduling.
/// </summary>
public static class CronScheduleCalculator
{
    /// <summary>
    /// Returns the next scheduled run time, or <see langword="null"/> when the schedule is exhausted.
    /// </summary>
    /// <param name="schedule">The schedule definition to evaluate.</param>
    /// <param name="lastRunAtUtc">The most recent execution time, if any.</param>
    /// <param name="now">The current evaluation time.</param>
    public static DateTimeOffset? ComputeNextRun(
        JobSchedule schedule,
        DateTimeOffset? lastRunAtUtc,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        return schedule switch
        {
            OneShotSchedule oneShotSchedule => ComputeOneShotNextRun(oneShotSchedule, lastRunAtUtc),
            FixedIntervalSchedule fixedIntervalSchedule => ComputeFixedIntervalNextRun(fixedIntervalSchedule, lastRunAtUtc, now),
            CronExpressionSchedule cronExpressionSchedule => ComputeCronNextRun(cronExpressionSchedule, now),
            _ => throw new ArgumentOutOfRangeException(nameof(schedule), $"Unsupported schedule type '{schedule.GetType().Name}'.")
        };
    }

    /// <summary>
    /// Returns the one-shot timestamp until the job has executed once.
    /// </summary>
    /// <param name="schedule">The one-shot schedule.</param>
    /// <param name="lastRunAtUtc">The prior execution time, if any.</param>
    private static DateTimeOffset? ComputeOneShotNextRun(OneShotSchedule schedule, DateTimeOffset? lastRunAtUtc)
    {
        return lastRunAtUtc is null ? schedule.FireAtUtc : null;
    }

    /// <summary>
    /// Returns the current time for a first execution or the last run plus the configured interval.
    /// </summary>
    /// <param name="schedule">The fixed-interval schedule.</param>
    /// <param name="lastRunAtUtc">The prior execution time, if any.</param>
    /// <param name="now">The current evaluation time.</param>
    private static DateTimeOffset ComputeFixedIntervalNextRun(
        FixedIntervalSchedule schedule,
        DateTimeOffset? lastRunAtUtc,
        DateTimeOffset now)
    {
        return lastRunAtUtc is null
            ? now
            : lastRunAtUtc.Value.AddMilliseconds(schedule.IntervalMs);
    }

    /// <summary>
    /// Returns the next cron occurrence using Cronos and the requested timezone.
    /// </summary>
    /// <param name="schedule">The cron-expression schedule.</param>
    /// <param name="now">The current evaluation time.</param>
    private static DateTimeOffset? ComputeCronNextRun(CronExpressionSchedule schedule, DateTimeOffset now)
    {
        var fieldCount = schedule.Expression
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;
        var cronFormat = fieldCount switch
        {
            5 => CronFormat.Standard,
            6 => CronFormat.IncludeSeconds,
            _ => throw new CronFormatException($"Cron expression '{schedule.Expression}' must contain 5 or 6 fields.")
        };
        var cronExpression = CronExpression.Parse(schedule.Expression, cronFormat);
        var timezone = string.IsNullOrWhiteSpace(schedule.Timezone)
            ? TimeZoneInfo.Utc
            : TimeZoneInfo.FindSystemTimeZoneById(schedule.Timezone);
        var nextOccurrenceUtc = cronExpression.GetNextOccurrence(now.UtcDateTime, timezone);

        return nextOccurrenceUtc is null
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(nextOccurrenceUtc.Value, DateTimeKind.Utc));
    }
}
