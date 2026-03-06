using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MsClaw.Core;

namespace MsClaw.Gateway.Hosting;

public sealed class GatewayHostedService : IGatewayHostedService
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
            await client.DisposeAsync();
            client = null;
        }

        State = GatewayState.Stopped;
    }

    private static IGatewayClient CreateGatewayClient(string mindPath)
    {
        return new CopilotGatewayClient(MsClawClientFactory.Create(mindPath));
    }
}
