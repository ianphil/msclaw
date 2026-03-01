using MsClaw.Models;

namespace MsClaw.Core;

public interface ISessionManager
{
    Task<SessionState> GetOrCreateAsync(CancellationToken cancellationToken = default);
    Task<SessionState> CreateNewAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(SessionState session, CancellationToken cancellationToken = default);
}
