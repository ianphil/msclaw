using System.Text;
using GitHub.Copilot.SDK;
using MsClaw.Models;
using Microsoft.Extensions.Options;

namespace MsClaw.Core;

public sealed class CopilotRuntimeClient : ICopilotRuntimeClient
{
    private readonly MsClawOptions _options;

    public CopilotRuntimeClient(IOptions<MsClawOptions> options)
    {
        _options = options.Value;
    }

    public async Task<string> GetAssistantResponseAsync(IReadOnlyList<SessionMessage> messages, CancellationToken cancellationToken = default)
    {
        var mindRoot = Path.GetFullPath(_options.MindRoot);
        var agentInstructions = await LoadAgentInstructionsAsync(mindRoot, _options.AgentName, cancellationToken);

        await using var client = new CopilotClient(new CopilotClientOptions
        {
            Cwd = mindRoot,
            AutoStart = true,
            UseStdio = true
        });

        await client.StartAsync(cancellationToken);

        await using var session = await client.CreateSessionAsync(new SessionConfig
        {
            Model = _options.Model,
            Streaming = false,
            InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            OnPermissionRequest = PermissionHandler.ApproveAll,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Replace,
                Content = agentInstructions
            }
        }, cancellationToken);

        string? assistantMessage = null;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = session.On(evt =>
        {
            switch (evt)
            {
                case AssistantMessageEvent msg when !string.IsNullOrWhiteSpace(msg.Data.Content):
                    assistantMessage = msg.Data.Content;
                    break;
                case SessionErrorEvent err:
                    finished.TrySetException(new InvalidOperationException(err.Data.Message));
                    break;
                case SessionIdleEvent:
                    finished.TrySetResult();
                    break;
            }
        });

        await session.SendAsync(new MessageOptions
        {
            Prompt = BuildPrompt(messages)
        }, cancellationToken);

        await using var _ = cancellationToken.Register(() => finished.TrySetCanceled(cancellationToken));
        await finished.Task;

        if (string.IsNullOrWhiteSpace(assistantMessage))
        {
            throw new InvalidOperationException("No assistant response received from Copilot session.");
        }

        return assistantMessage;
    }

    private static async Task<string> LoadAgentInstructionsAsync(string mindRoot, string agentName, CancellationToken cancellationToken)
    {
        var agentFile = Path.Combine(mindRoot, ".github", "agents", $"{agentName}.agent.md");
        if (!File.Exists(agentFile))
        {
            throw new FileNotFoundException($"Agent file not found: {agentFile}");
        }

        var content = await File.ReadAllTextAsync(agentFile, cancellationToken);

        // Strip YAML frontmatter — the SDK doesn't parse it
        if (content.StartsWith("---"))
        {
            var endIndex = content.IndexOf("---", 3, StringComparison.Ordinal);
            if (endIndex > 0)
            {
                content = content[(endIndex + 3)..].TrimStart();
            }
        }

        return content;
    }

    private static string BuildPrompt(IReadOnlyList<SessionMessage> messages)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Continue the conversation. Full history:");

        foreach (var message in messages)
        {
            builder.AppendLine($"[{message.Role}] {message.Content}");
        }

        return builder.ToString();
    }
}
