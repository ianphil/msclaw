using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MsClaw.Core;

namespace MsClaw.Gateway.Hosting;

/// <summary>
/// Validates the configured mind, starts the gateway client, and exposes gateway runtime services.
/// </summary>
public sealed class GatewayHostedService : IGatewayHostedService, IGatewayClient
{
    private readonly IMindValidator mindValidator;
    private readonly IIdentityLoader identityLoader;
    private readonly GatewayOptions options;
    private readonly Func<string, IGatewayClient> clientFactory;
    private readonly ILogger<GatewayHostedService> logger;
    private IGatewayClient? client;

    public GatewayHostedService(
        IMindValidator mindValidator,
        IIdentityLoader identityLoader,
        GatewayOptions options,
        Func<string, IGatewayClient>? clientFactory = null,
        ILogger<GatewayHostedService>? logger = null)
    {
        this.mindValidator = mindValidator;
        this.identityLoader = identityLoader;
        this.options = options;
        this.clientFactory = clientFactory ?? CreateGatewayClient;
        this.logger = logger ?? NullLogger<GatewayHostedService>.Instance;
        State = GatewayState.Starting;
    }

    public GatewayState State { get; private set; }

    public string? SystemMessage { get; private set; }

    public string? Error { get; private set; }

    public bool IsReady => State is GatewayState.Ready;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        State = GatewayState.Validating;
        var validation = mindValidator.Validate(options.MindPath);
        if (validation.Errors.Count > 0)
        {
            Error = string.Join(Environment.NewLine, validation.Errors);
            State = GatewayState.Failed;
            logger.LogError("Mind validation failed: {ValidationErrors}", Error);

            return;
        }

        SystemMessage = await identityLoader.LoadSystemMessageAsync(options.MindPath, cancellationToken);

        try
        {
            client = clientFactory(options.MindPath);
            await client.StartAsync(cancellationToken);
            Error = null;
            State = GatewayState.Ready;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            State = GatewayState.Failed;
            logger.LogError(ex, "Failed to start gateway client.");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        State = GatewayState.Stopping;
        if (client is not null)
        {
            await DisposeAsync();
        }

        State = GatewayState.Stopped;
    }

    /// <summary>
    /// Creates a new session by delegating to the started gateway client.
    /// </summary>
    public Task<IGatewaySession> CreateSessionAsync(SessionConfig? config = null, CancellationToken cancellationToken = default)
    {
        return GetClientOrThrow().CreateSessionAsync(config, cancellationToken);
    }

    /// <summary>
    /// Resumes an existing session by delegating to the started gateway client.
    /// </summary>
    public Task<IGatewaySession> ResumeSessionAsync(string sessionId, ResumeSessionConfig? config = null, CancellationToken cancellationToken = default)
    {
        return GetClientOrThrow().ResumeSessionAsync(sessionId, config, cancellationToken);
    }

    /// <summary>
    /// Lists sessions by delegating to the started gateway client.
    /// </summary>
    public Task<IReadOnlyList<SessionMetadata>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        return GetClientOrThrow().ListSessionsAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes a session by delegating to the started gateway client.
    /// </summary>
    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return GetClientOrThrow().DeleteSessionAsync(sessionId, cancellationToken);
    }

    /// <summary>
    /// Disposes the started gateway client if one exists.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (client is null)
        {
            return;
        }

        await client.DisposeAsync();
        client = null;
    }

    private static IGatewayClient CreateGatewayClient(string mindPath)
    {
        return new CopilotGatewayClient(MsClawClientFactory.Create(mindPath));
    }

    /// <summary>
    /// Gets the started gateway client or throws when the hosted service is not ready.
    /// </summary>
    private IGatewayClient GetClientOrThrow()
    {
        return client ?? throw new InvalidOperationException("The gateway client is not available before startup completes.");
    }
}
