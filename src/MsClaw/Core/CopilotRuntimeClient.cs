using GitHub.Copilot.SDK;
using MsClaw.Models;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace MsClaw.Core;

public sealed class CopilotRuntimeClient : ICopilotRuntimeClient, ISessionControl
{
    private readonly CopilotClient _client;
    private readonly MsClawOptions _options;
    private readonly IIdentityLoader _identityLoader;
    private readonly IExtensionManager _extensionManager;
    private readonly ConcurrentDictionary<string, CopilotSession> _sessions = new();

    public CopilotRuntimeClient(
        CopilotClient client,
        IOptions<MsClawOptions> options,
        IIdentityLoader identityLoader,
        IExtensionManager extensionManager)
    {
        _client = client;
        _options = options.Value;
        _identityLoader = identityLoader;
        _extensionManager = extensionManager;
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
            Tools = _extensionManager.GetTools().ToArray(),
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Replace,
                Content = systemMessage
            }
        }, cancellationToken);

        _sessions[session.SessionId] = session;
        await _extensionManager.FireHookAsync(
            ExtensionEvents.SessionCreate,
            new ExtensionHookContext
            {
                EventName = ExtensionEvents.SessionCreate,
                SessionId = session.SessionId
            },
            cancellationToken);

        return session.SessionId;
    }

    public async Task<string> SendMessageAsync(
        string sessionId,
        string message,
        CancellationToken cancellationToken = default)
    {
        var session = await GetOrResumeSessionAsync(sessionId, cancellationToken);
        await _extensionManager.FireHookAsync(
            ExtensionEvents.MessageReceived,
            new ExtensionHookContext
            {
                EventName = ExtensionEvents.MessageReceived,
                SessionId = sessionId,
                Message = message
            },
            cancellationToken);

        var response = await session.SendAndWaitAsync(
            new MessageOptions { Prompt = message },
            timeout: TimeSpan.FromSeconds(120),
            cancellationToken: cancellationToken);

        var responseText = response?.Data?.Content
            ?? throw new InvalidOperationException("No assistant response received from Copilot session.");

        await _extensionManager.FireHookAsync(
            ExtensionEvents.MessageSent,
            new ExtensionHookContext
            {
                EventName = ExtensionEvents.MessageSent,
                SessionId = sessionId,
                Message = message,
                Response = responseText
            },
            cancellationToken);

        return responseText;
    }

    public async Task CycleSessionsAsync(CancellationToken cancellationToken = default)
    {
        var sessionIds = _sessions.Keys.ToArray();

        foreach (var sessionId in sessionIds)
        {
            await _extensionManager.FireHookAsync(
                ExtensionEvents.SessionEnd,
                new ExtensionHookContext
                {
                    EventName = ExtensionEvents.SessionEnd,
                    SessionId = sessionId
                },
                cancellationToken);
        }

        _sessions.Clear();
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

        await _extensionManager.FireHookAsync(
            ExtensionEvents.SessionResume,
            new ExtensionHookContext
            {
                EventName = ExtensionEvents.SessionResume,
                SessionId = sessionId
            },
            cancellationToken);

        return _sessions.GetOrAdd(sessionId, resumedSession);
    }
}
