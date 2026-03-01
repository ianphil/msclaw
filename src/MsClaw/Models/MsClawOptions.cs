namespace MsClaw.Models;

public sealed class MsClawOptions
{
    public string MindRoot { get; set; } = "../miss-moneypenny";
    public string SessionStore { get; set; } = "./data/sessions";
    public int Port { get; set; } = 5000;
    public bool AutoGitPull { get; set; }
    public string AgentName { get; set; } = "miss-moneypenny";
    public string Model { get; set; } = "claude-sonnet-4.5";
}
