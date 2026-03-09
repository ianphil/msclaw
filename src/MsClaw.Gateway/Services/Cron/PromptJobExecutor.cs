using System.Diagnostics;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;
using MsClaw.Gateway.Hosting;
using MsClaw.Gateway.Services;
using MsClaw.Gateway.Services.Tools;

namespace MsClaw.Gateway.Services.Cron;

/// <summary>
/// Executes prompt payloads in isolated gateway sessions.
/// </summary>
public sealed class PromptJobExecutor : ICronJobExecutor
{
    private readonly ISessionPool sessionPool;
    private readonly IGatewayClient client;
    private readonly IGatewayHostedService hostedService;
    private readonly IToolCatalog toolCatalog;

    /// <summary>
    /// Creates a prompt executor using the shared gateway session infrastructure.
    /// </summary>
    public PromptJobExecutor(
        ISessionPool sessionPool,
        IGatewayClient client,
        IGatewayHostedService hostedService,
        IToolCatalog toolCatalog)
    {
        this.sessionPool = sessionPool;
        this.client = client;
        this.hostedService = hostedService;
        this.toolCatalog = toolCatalog;
    }

    /// <inheritdoc />
    public Type PayloadType => typeof(PromptPayload);

    /// <inheritdoc />
    public async Task<CronRunResult> ExecuteAsync(CronJob job, string runId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        if (job.Payload is not PromptPayload payload)
        {
            throw new ArgumentException($"Job payload must be a {nameof(PromptPayload)}.", nameof(job));
        }

        var callerKey = $"cron:{job.Id}:{runId}";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var session = await sessionPool.GetOrCreateAsync(
                callerKey,
                ct => CreateSessionAsync(payload, ct),
                cancellationToken);
            var completion = new TaskCompletionSource<PromptCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
            string? assistantContent = null;

            using var subscription = session.On(sessionEvent =>
            {
                switch (sessionEvent)
                {
                    case AssistantMessageEvent assistantMessageEvent:
                        assistantContent = assistantMessageEvent.Data.Content;
                        break;
                    case SessionErrorEvent sessionErrorEvent:
                        completion.TrySetResult(new PromptCompletion(assistantContent, sessionErrorEvent.Data.Message));
                        break;
                    case SessionIdleEvent:
                        completion.TrySetResult(new PromptCompletion(assistantContent, null));
                        break;
                }
            });

            await session.SendAsync(new MessageOptions { Prompt = payload.Prompt }, cancellationToken);
            var promptCompletion = await completion.Task.WaitAsync(cancellationToken);
            stopwatch.Stop();

            return promptCompletion.ErrorMessage is null
                ? new CronRunResult(promptCompletion.Content ?? string.Empty, CronRunOutcome.Success, null, stopwatch.ElapsedMilliseconds, false)
                : new CronRunResult(promptCompletion.Content ?? string.Empty, CronRunOutcome.Failure, promptCompletion.ErrorMessage, stopwatch.ElapsedMilliseconds, false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            return new CronRunResult(string.Empty, CronRunOutcome.Failure, ex.Message, stopwatch.ElapsedMilliseconds, false);
        }
        finally
        {
            await sessionPool.RemoveAsync(callerKey);
        }
    }

    /// <summary>
    /// Creates a new isolated session configured with the default and preloaded tools for the payload.
    /// </summary>
    /// <param name="payload">The prompt payload being executed.</param>
    /// <param name="cancellationToken">Cancels session creation.</param>
    private Task<IGatewaySession> CreateSessionAsync(PromptPayload payload, CancellationToken cancellationToken)
    {
        var tools = new List<AIFunction>(toolCatalog.GetDefaultTools());
        if (payload.PreloadToolNames is { Length: > 0 })
        {
            tools.AddRange(toolCatalog.GetToolsByName(payload.PreloadToolNames));
        }

        var sessionConfig = new SessionConfig
        {
            Model = payload.Model,
            Streaming = true,
            Tools = tools
        };
        if (string.IsNullOrWhiteSpace(hostedService.SystemMessage) is false)
        {
            sessionConfig.SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = hostedService.SystemMessage
            };
        }

        return client.CreateSessionAsync(sessionConfig, cancellationToken);
    }

    /// <summary>
    /// Captures the terminal assistant content and any session-level error message.
    /// </summary>
    /// <param name="Content">The last assistant message content received.</param>
    /// <param name="ErrorMessage">The session error message, when present.</param>
    private sealed record PromptCompletion(string? Content, string? ErrorMessage);
}
