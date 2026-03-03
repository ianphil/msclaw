namespace MsClaw.Core;

public interface IMindReader
{
    Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListDirectoryAsync(string path, CancellationToken cancellationToken = default);
    Task EnsureSyncedAsync(CancellationToken cancellationToken = default);
}
