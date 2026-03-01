namespace MsClaw.Models;

public sealed class ChatResponse
{
    public required string Response { get; init; }
    public required string SessionId { get; init; }
}
