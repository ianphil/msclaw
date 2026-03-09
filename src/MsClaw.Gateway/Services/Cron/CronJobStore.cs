using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MsClaw.Gateway.Services.Cron;

/// <summary>
/// Persists cron jobs and run history under the user's <c>.msclaw\cron</c> directory.
/// </summary>
public sealed class CronJobStore : ICronJobStore, ICronRunHistoryStore
{
    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ConcurrentDictionary<string, CronJob> jobs = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim flushLock = new(1, 1);
    private readonly string rootPath;
    private readonly string jobsPath;
    private readonly string historyDirectoryPath;
    private readonly long maxHistoryBytes;
    private readonly int maxHistoryRecords;
    private volatile bool initialized;

    /// <summary>
    /// Creates a store using the default cron directory under the user profile.
    /// </summary>
    public CronJobStore()
        : this(GetDefaultRootPath())
    {
    }

    /// <summary>
    /// Creates a store rooted at a specific directory.
    /// </summary>
    /// <param name="rootPath">The cron storage directory.</param>
    /// <param name="maxHistoryBytes">The maximum serialized history file size in bytes.</param>
    /// <param name="maxHistoryRecords">The maximum number of retained history records per job.</param>
    public CronJobStore(string rootPath, long maxHistoryBytes = 2 * 1024 * 1024, int maxHistoryRecords = 2_000)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Cron root path must be provided.", nameof(rootPath));
        }

        if (maxHistoryBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHistoryBytes), "History size limit must be positive.");
        }

        if (maxHistoryRecords <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHistoryRecords), "History record limit must be positive.");
        }

        this.rootPath = Path.GetFullPath(rootPath);
        jobsPath = Path.Combine(this.rootPath, "jobs.json");
        historyDirectoryPath = Path.Combine(this.rootPath, "history");
        this.maxHistoryBytes = maxHistoryBytes;
        this.maxHistoryRecords = maxHistoryRecords;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(rootPath);
        Directory.CreateDirectory(historyDirectoryPath);
        jobs.Clear();

        if (File.Exists(jobsPath) is false)
        {
            initialized = true;
            return;
        }

        var json = await File.ReadAllTextAsync(jobsPath, cancellationToken);

        try
        {
            var document = JsonSerializer.Deserialize<CronJobStoreDocument>(json, JsonReadOptions)
                ?? throw new InvalidOperationException($"Cron job store at '{jobsPath}' deserialized to null.");

            foreach (var job in document.Jobs)
            {
                jobs[job.Id] = job;
            }

            initialized = true;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse cron job store '{jobsPath}': {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CronJob>> GetAllJobsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();

        IReadOnlyList<CronJob> snapshot = jobs.Values
            .OrderBy(static job => job.Id, StringComparer.Ordinal)
            .ToArray();

        return Task.FromResult(snapshot);
    }

    /// <inheritdoc />
    public Task<CronJob?> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job ID must be provided.", nameof(jobId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        jobs.TryGetValue(jobId, out var job);

        return Task.FromResult(job);
    }

    /// <inheritdoc />
    public async Task AddJobAsync(CronJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        EnsureInitialized();

        if (jobs.TryAdd(job.Id, job) is false)
        {
            throw new InvalidOperationException($"A cron job with ID '{job.Id}' already exists.");
        }

        await FlushJobsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateJobAsync(CronJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        EnsureInitialized();

        if (jobs.ContainsKey(job.Id) is false)
        {
            throw new InvalidOperationException($"A cron job with ID '{job.Id}' does not exist.");
        }

        jobs[job.Id] = job;
        await FlushJobsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job ID must be provided.", nameof(jobId));
        }

        EnsureInitialized();
        jobs.TryRemove(jobId, out _);
        await FlushJobsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AppendRunRecordAsync(CronRunRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        EnsureInitialized();

        var history = (await LoadHistoryAsync(record.JobId, cancellationToken)).ToList();
        history.Add(record);

        while (history.Count > maxHistoryRecords)
        {
            history.RemoveAt(0);
        }

        while (history.Count > 1 && GetSerializedByteCount(history) > maxHistoryBytes)
        {
            history.RemoveAt(0);
        }

        await WriteJsonAtomicAsync(GetHistoryPath(record.JobId), history, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CronRunRecord>> GetRunHistoryAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job ID must be provided.", nameof(jobId));
        }

        EnsureInitialized();
        var history = await LoadHistoryAsync(jobId, cancellationToken);

        return history;
    }

    /// <summary>
    /// Writes the current in-memory job cache to disk using atomic replacement.
    /// </summary>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    private Task FlushJobsAsync(CancellationToken cancellationToken)
    {
        var document = new CronJobStoreDocument(
            jobs.Values
                .OrderBy(static job => job.Id, StringComparer.Ordinal)
                .ToArray());

        return WriteJsonAtomicAsync(jobsPath, document, cancellationToken);
    }

    /// <summary>
    /// Loads all history records for a job from disk.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    /// <returns>The persisted history records.</returns>
    private async Task<IReadOnlyList<CronRunRecord>> LoadHistoryAsync(string jobId, CancellationToken cancellationToken)
    {
        var historyPath = GetHistoryPath(jobId);
        if (File.Exists(historyPath) is false)
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(historyPath, cancellationToken);

        try
        {
            return JsonSerializer.Deserialize<List<CronRunRecord>>(json, JsonReadOptions) ?? [];
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse cron history file '{historyPath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Atomically writes a JSON document by writing a temporary file and then moving it into place.
    /// </summary>
    /// <typeparam name="T">The serialized value type.</typeparam>
    /// <param name="path">The destination path.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="cancellationToken">Cancels the asynchronous operation.</param>
    private async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Unable to determine the directory for '{path}'.");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $"{Path.GetFileName(path)}.{Path.GetRandomFileName()}.tmp");
        var json = JsonSerializer.Serialize(value, JsonWriteOptions);
        await flushLock.WaitAsync(cancellationToken);

        try
        {
            await File.WriteAllTextAsync(tempPath, json, cancellationToken);

            // Replacing the target with a fully written temp file avoids partially written JSON after crashes.
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            flushLock.Release();

            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <summary>
    /// Returns the history file path for a job.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <returns>The history file path.</returns>
    private string GetHistoryPath(string jobId)
    {
        return Path.Combine(historyDirectoryPath, $"{jobId}.json");
    }

    /// <summary>
    /// Computes the serialized UTF-8 byte count for a history payload.
    /// </summary>
    /// <param name="history">The history records to measure.</param>
    /// <returns>The serialized byte count.</returns>
    private static int GetSerializedByteCount(IReadOnlyList<CronRunRecord> history)
    {
        return Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(history, JsonWriteOptions));
    }

    /// <summary>
    /// Throws when the store has not been initialized yet.
    /// </summary>
    private void EnsureInitialized()
    {
        if (initialized is false)
        {
            throw new InvalidOperationException("CronJobStore must be initialized before use.");
        }
    }

    /// <summary>
    /// Resolves the default cron root path for the current user.
    /// </summary>
    /// <returns>The default cron directory path.</returns>
    private static string GetDefaultRootPath()
    {
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userHome))
        {
            throw new InvalidOperationException("Unable to resolve user profile directory for cron storage.");
        }

        return Path.Combine(userHome, ".msclaw", "cron");
    }

    /// <summary>
    /// Wraps the persisted jobs collection in a stable root object.
    /// </summary>
    /// <param name="Jobs">The jobs persisted to disk.</param>
    private sealed record CronJobStoreDocument(
        [property: JsonPropertyName("jobs")] IReadOnlyList<CronJob> Jobs);
}
