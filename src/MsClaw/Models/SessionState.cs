namespace MsClaw.Models;

public sealed class SessionState
{
    public required string SessionId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<SessionMessage> Messages { get; init; } = [];
}
