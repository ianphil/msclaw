namespace MsClaw.Core;

public interface ISessionControl
{
    Task CycleSessionsAsync(CancellationToken cancellationToken = default);
}
