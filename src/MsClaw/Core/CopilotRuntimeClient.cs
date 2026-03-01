using System.Text;
using GitHub.Copilot.SDK;
using MsClaw.Models;
using Microsoft.Extensions.Options;

namespace MsClaw.Core;

public sealed class CopilotRuntimeClient : ICopilotRuntimeClient
{
    private readonly MsClawOptions _options;
    private readonly IIdentityLoader _identityLoader;

    public CopilotRuntimeClient(IOptions<MsClawOptions> options, IIdentityLoader identityLoader)
    {
        _options = options.Value;
        _identityLoader = identityLoader;
    }

    public async Task<string> GetAssistantResponseAsync(IReadOnlyList<SessionMessage> messages, CancellationToken cancellationToken = default)
    {
        var mindRoot = Path.GetFullPath(_options.MindRoot);
        var systemMessage = await _identityLoader.LoadSystemMessageAsync(mindRoot, cancellationToken);
        var bootstrapPath = Path.Combine(mindRoot, "bootstrap.md");
        if (File.Exists(bootstrapPath))
        {
            var bootstrapInstructions = await File.ReadAllTextAsync(bootstrapPath, cancellationToken);
            systemMessage = bootstrapInstructions + "\n\n---\n\n" + systemMessage;
        }

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
                Content = systemMessage
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
