namespace MsClaw.Models;

public sealed class BootstrapResult
{
    public required string MindRoot { get; init; }
    public bool IsNewMind { get; init; }
    public bool HasBootstrapMarker { get; init; }
}
