namespace MsClaw.Models;

public sealed class SessionMessage
{
    public required string Role { get; init; }
    public required string Content { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
