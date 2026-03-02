namespace MsClaw.Models;

public sealed class MsClawConfig
{
    public string? MindRoot { get; set; }
    public DateTime? LastUsed { get; set; }
    public List<string> DisabledExtensions { get; set; } = [];
}
