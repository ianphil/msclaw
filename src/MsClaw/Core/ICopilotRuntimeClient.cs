using MsClaw.Models;

namespace MsClaw.Core;

public interface ICopilotRuntimeClient
{
    Task<string> GetAssistantResponseAsync(IReadOnlyList<SessionMessage> messages, CancellationToken cancellationToken = default);
}
