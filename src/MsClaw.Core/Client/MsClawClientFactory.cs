using GitHub.Copilot.SDK;

namespace MsClaw.Core;

public static class MsClawClientFactory
{
    public static CopilotClient Create(string mindRoot)
    {
        return new CopilotClient(new CopilotClientOptions
        {
            Cwd = Path.GetFullPath(mindRoot),
            AutoStart = true,
            UseStdio = true,
            CliPath = CliLocator.ResolveCopilotCliPath()
        });
    }

    public static CopilotClient Create(string mindRoot, Action<CopilotClientOptions> configure)
    {
        var options = new CopilotClientOptions
        {
            Cwd = Path.GetFullPath(mindRoot),
            AutoStart = true,
            UseStdio = true,
            CliPath = CliLocator.ResolveCopilotCliPath()
        };
        configure(options);
        return new CopilotClient(options);
    }
}
