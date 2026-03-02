using GitHub.Copilot.SDK;
using MsClaw.Models;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace MsClaw.Core;

public sealed class CopilotRuntimeClient : ICopilotRuntimeClient
{
    private readonly CopilotClient _client;
    private readonly MsClawOptions _options;
    private readonly IIdentityLoader _identityLoader;
    private readonly ConcurrentDictionary<string, CopilotSession> _sessions = new();

    public CopilotRuntimeClient(
        CopilotClient client,
        IOptions<MsClawOptions> options,
        IIdentityLoader identityLoader)
    {
        _client = client;
        _options = options.Value;
        _identityLoader = identityLoader;
    }

    public async Task<string> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        var mindRoot = Path.GetFullPath(_options.MindRoot);
        var systemMessage = await _identityLoader.LoadSystemMessageAsync(mindRoot, cancellationToken);

        var bootstrapPath = Path.Combine(mindRoot, "bootstrap.md");
        if (File.Exists(bootstrapPath))
        {
            var bootstrapInstructions = await File.ReadAllTextAsync(bootstrapPath, cancellationToken);
            systemMessage = bootstrapInstructions + "\n\n---\n\n" + systemMessage;
        }

        var session = await _client.CreateSessionAsync(new SessionConfig
        {
            Model = _options.Model,
            InfiniteSessions = new InfiniteSessionConfig { Enabled = true },
            OnPermissionRequest = PermissionHandler.ApproveAll,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Replace,
                Content = systemMessage
            }
        }, cancellationToken);

        _sessions[session.SessionId] = session;
        return session.SessionId;
    }

    public async Task<string> SendMessageAsync(
        string sessionId,
        string message,
        CancellationToken cancellationToken = default)
    {
        var session = await GetOrResumeSessionAsync(sessionId, cancellationToken);

        var response = await session.SendAndWaitAsync(
            new MessageOptions { Prompt = message },
            timeout: TimeSpan.FromSeconds(120),
            cancellationToken: cancellationToken);

        return response?.Data?.Content
            ?? throw new InvalidOperationException("No assistant response received from Copilot session.");
    }

    private async Task<CopilotSession> GetOrResumeSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            return session;
        }

        var resumedSession = await _client.ResumeSessionAsync(sessionId, new ResumeSessionConfig
        {
            OnPermissionRequest = PermissionHandler.ApproveAll
        }, cancellationToken);

        return _sessions.GetOrAdd(sessionId, resumedSession);
    }
}
