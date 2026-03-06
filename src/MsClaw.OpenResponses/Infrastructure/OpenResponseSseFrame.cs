namespace MsClaw.OpenResponses.Infrastructure;

/// <summary>
/// Represents a single SSE frame emitted by the OpenResponses endpoint.
/// </summary>
internal sealed record OpenResponseSseFrame(string? EventName, string Data)
{
    /// <summary>
    /// Gets the terminal SSE frame used to close the stream.
    /// </summary>
    public static OpenResponseSseFrame Done { get; } = new(null, "[DONE]");
}
