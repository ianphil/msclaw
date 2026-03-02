namespace MsClaw.Models;

public sealed class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public string? SessionId { get; set; }
}
