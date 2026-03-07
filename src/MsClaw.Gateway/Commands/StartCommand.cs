using System.CommandLine;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using MsClaw.Core;
using MsClaw.Gateway.Hosting;
using MsClaw.Gateway.Hubs;
using MsClaw.Gateway.Services;
using MsClaw.OpenResponses;
using MsClaw.Tunnel;

namespace MsClaw.Gateway.Commands;

public static class StartCommand
{
    public static Command Create()
    {
        var command = new Command("start", "Start the gateway daemon");
        var mindOption = new Option<string?>("--mind")
        {
            Description = "Path to an existing mind directory"
        };
        var newMindOption = new Option<string?>("--new-mind")
        {
            Description = "Path to scaffold and start a new mind directory"
        };
        var tunnelOption = new Option<bool>("--tunnel")
        {
            Description = "Enable dev tunnel hosting for remote access"
        };
        var tunnelIdOption = new Option<string?>("--tunnel-id")
        {
            Description = "Use the specified persistent dev tunnel ID"
        };
        command.Add(mindOption);
        command.Add(newMindOption);
        command.Add(tunnelOption);
        command.Add(tunnelIdOption);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var mindPath = parseResult.GetValue(mindOption);
            var newMindPath = parseResult.GetValue(newMindOption);
            var tunnelEnabled = parseResult.GetValue(tunnelOption);
            var tunnelId = parseResult.GetValue(tunnelIdOption);
            var scaffold = new MindScaffold();
            var userConfigLoader = new UserConfigLoader();

            return await ExecuteStartAsync(
                mindPath,
                newMindPath,
                RunGatewayAsync,
                scaffold,
                tunnelEnabled,
                tunnelId,
                userConfigLoader,
                cancellationToken);
        });

        return command;
    }

    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration, GatewayOptions options)
    {
        services.AddAuthentication()
            .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"));
        services.Configure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>("Bearer", jwtOptions =>
        {
            var existingOnMessageReceived = jwtOptions.Events?.OnMessageReceived;
            jwtOptions.Events ??= new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents();
            jwtOptions.Events.OnMessageReceived = async context =>
            {
                if (existingOnMessageReceived is not null)
                {
                    await existingOnMessageReceived(context);
                }

                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/gateway"))
                {
                    context.Token = accessToken;
                }
            };
        });
        services.AddAuthorization();
        services.AddSignalR();
        services.AddSingleton(options);
        services.AddSingleton<IUserConfigLoader, UserConfigLoader>();
        services.AddSingleton<IMindValidator, MindValidator>();
        services.AddSingleton<IIdentityLoader, IdentityLoader>();
        services.AddSingleton<IMindScaffold, MindScaffold>();
        services.AddSingleton<IMindReader>(_ => new MindReader(options.MindPath));
        services.AddSingleton<IDevTunnelLocator, DevTunnelLocator>();
        services.AddSingleton<ITunnelManager>(serviceProvider =>
            new TunnelManager(
                serviceProvider.GetRequiredService<IDevTunnelLocator>(),
                serviceProvider.GetRequiredService<IUserConfigLoader>(),
                new TunnelManagerOptions
                {
                    Enabled = options.TunnelEnabled,
                    LocalPort = options.Port,
                    TunnelId = options.TunnelId
                }));
        services.AddSingleton<CallerRegistry>();
        services.AddSingleton<IConcurrencyGate>(serviceProvider => serviceProvider.GetRequiredService<CallerRegistry>());
        services.AddSingleton<ISessionPool, SessionPool>();
        services.AddSingleton<GatewayHostedService>();
        services.AddSingleton<GatewayTunnelHostedService>();
        services.AddSingleton<IGatewayClient>(serviceProvider => serviceProvider.GetRequiredService<GatewayHostedService>());
        services.AddSingleton<IGatewayHostedService>(serviceProvider => serviceProvider.GetRequiredService<GatewayHostedService>());
        services.AddSingleton<AgentMessageService>();
        services.AddSingleton<IOpenResponseService, GatewayOpenResponseService>();
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<GatewayHostedService>());
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<GatewayTunnelHostedService>());
    }

    public static IResult BuildLivenessResult()
    {
        return Results.Json(new { status = "Healthy" }, statusCode: StatusCodes.Status200OK);
    }

    public static IResult BuildReadinessResult(IGatewayHostedService hostedService)
    {
        return hostedService.IsReady
            ? Results.Json(new { status = "Healthy" }, statusCode: StatusCodes.Status200OK)
            : Results.Json(
                new { status = "Unhealthy", component = "hosted-service", error = hostedService.Error },
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    public static IResult BuildTunnelStatusResult(ITunnelManager tunnelManager)
    {
        var status = tunnelManager.GetStatus();
        return Results.Json(new
        {
            enabled = status.Enabled,
            running = status.IsRunning,
            tunnelId = status.TunnelId,
            publicUrl = status.PublicUrl,
            error = status.Error
        }, statusCode: StatusCodes.Status200OK);
    }

    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => BuildLivenessResult());
        endpoints.MapGet("/health/ready", ([FromServices] IGatewayHostedService hostedService) => BuildReadinessResult(hostedService));
        endpoints.MapGet("/api/tunnel/status", ([FromServices] ITunnelManager tunnelManager) => BuildTunnelStatusResult(tunnelManager));
        endpoints.MapHub<GatewayHub>("/gateway");
        endpoints.MapOpenResponses();
    }

    /// <summary>
    /// Configures middleware required to serve the gateway's static chat assets.
    /// Splitting this from endpoint mapping keeps the pipeline testable without starting the full host.
    /// </summary>
    public static void ConfigurePipeline(IApplicationBuilder application)
    {
        application.UseDefaultFiles();
        application.UseStaticFiles();
        application.UseAuthentication();
        application.UseAuthorization();
    }

    public static async Task<int> ExecuteStartAsync(
        string? mindPath,
        string? newMindPath,
        Func<GatewayOptions, CancellationToken, Task<int>> runGatewayAsync,
        IMindScaffold mindScaffold,
        bool tunnelEnabled = false,
        string? tunnelId = null,
        IUserConfigLoader? userConfigLoader = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedMindPath = newMindPath ?? mindPath;
        if (string.IsNullOrWhiteSpace(resolvedMindPath))
        {
            throw new ArgumentException("Either --mind or --new-mind must be specified.");
        }

        if (string.IsNullOrWhiteSpace(newMindPath) is false)
        {
            mindScaffold.Scaffold(resolvedMindPath);
        }

        var tunnelRequested = tunnelEnabled || IsTunnelEnabledFromEnvironment() || string.IsNullOrWhiteSpace(tunnelId) is false;
        var resolvedTunnelId = tunnelRequested ? ResolveTunnelId(tunnelId, userConfigLoader) : null;
        var options = new GatewayOptions
        {
            MindPath = Path.GetFullPath(resolvedMindPath),
            Host = "127.0.0.1",
            Port = 18789,
            TunnelEnabled = tunnelRequested,
            TunnelId = resolvedTunnelId
        };

        return await runGatewayAsync(options, cancellationToken);
    }

    private static bool IsTunnelEnabledFromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable("MSCLAW_TUNNEL");
        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveTunnelId(string? tunnelIdFromCli, IUserConfigLoader? userConfigLoader)
    {
        if (string.IsNullOrWhiteSpace(tunnelIdFromCli) is false)
        {
            return tunnelIdFromCli;
        }

        var tunnelIdFromEnvironment = Environment.GetEnvironmentVariable("MSCLAW_TUNNEL_ID");
        if (string.IsNullOrWhiteSpace(tunnelIdFromEnvironment) is false)
        {
            return tunnelIdFromEnvironment;
        }

        if (userConfigLoader is null)
        {
            return null;
        }

        return userConfigLoader.Load().TunnelId;
    }

    public static async Task<int> RunGatewayAsync(GatewayOptions options, CancellationToken cancellationToken = default)
    {
        var assemblyDir = Path.GetDirectoryName(typeof(StartCommand).Assembly.Location)!;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            WebRootPath = Path.Combine(assemblyDir, "wwwroot")
        });
        builder.WebHost.UseUrls($"http://{options.Host}:{options.Port}");
        ConfigureServices(builder.Services, builder.Configuration, options);
        var app = builder.Build();
        ConfigurePipeline(app);
        MapEndpoints(app);
        await app.StartAsync(cancellationToken);
        var tunnelManager = app.Services.GetRequiredService<ITunnelManager>();
        Console.WriteLine(BuildAccessBanner(options, tunnelManager.GetStatus()));
        await app.WaitForShutdownAsync(cancellationToken);

        return 0;
    }

    /// <summary>
    /// Builds a startup banner that lists local and remote gateway access endpoints.
    /// </summary>
    public static string BuildAccessBanner(GatewayOptions options, TunnelStatus tunnelStatus)
    {
        var localBaseUrl = $"http://{options.Host}:{options.Port}";
        var buffer = new StringBuilder();
        buffer.AppendLine("MSCLAW GATEWAY READY");
        buffer.AppendLine("===================");
        buffer.AppendLine();
        buffer.AppendLine("LOCAL ACCESS");
        AppendAccessLine(buffer, "UI (browser)", $"{localBaseUrl}/", "Local chat interface");
        AppendAccessLine(buffer, "OpenResponses API", $"{localBaseUrl}/v1/responses", "HTTP JSON/SSE endpoint");
        AppendAccessLine(buffer, "SignalR Hub", $"{localBaseUrl}/gateway", "Realtime streaming channel");
        AppendAccessLine(buffer, "Health", $"{localBaseUrl}/health", "Liveness probe");
        AppendAccessLine(buffer, "Readiness", $"{localBaseUrl}/health/ready", "Runtime readiness");
        AppendAccessLine(buffer, "Tunnel Status", $"{localBaseUrl}/api/tunnel/status", "Remote URL + tunnel state");
        buffer.AppendLine();
        buffer.AppendLine("REMOTE ACCESS (Dev Tunnel)");

        if (tunnelStatus.Enabled && string.IsNullOrWhiteSpace(tunnelStatus.PublicUrl) is false)
        {
            var remoteBaseUrl = tunnelStatus.PublicUrl.TrimEnd('/');
            AppendAccessLine(buffer, "Tunnel URL", remoteBaseUrl, "Public HTTPS entrypoint");
            AppendAccessLine(buffer, "Remote UI", $"{remoteBaseUrl}/", "Browser UI through tunnel");
            AppendAccessLine(buffer, "Remote API", $"{remoteBaseUrl}/v1/responses", "OpenResponses endpoint through tunnel");
            AppendAccessLine(buffer, "Remote SignalR", $"{remoteBaseUrl}/gateway", "SignalR endpoint through tunnel");
        }
        else
        {
            AppendAccessLine(buffer, "Tunnel URL", "(disabled)", "Start with --tunnel to enable remote access");
        }

        buffer.AppendLine();
        buffer.AppendLine("NOTES");
        buffer.AppendLine("  - Tunnel auth: Entra tenant access + gateway JWT auth.");
        buffer.AppendLine("  - Press Ctrl+C to stop gateway and devtunnel.");

        return buffer.ToString();
    }

    private static void AppendAccessLine(StringBuilder buffer, string label, string endpoint, string description)
    {
        buffer.Append("  ");
        buffer.Append(label.PadRight(18, ' '));
        buffer.Append(" ");
        buffer.Append(endpoint.PadRight(40, ' '));
        buffer.Append(" ");
        buffer.AppendLine(description);
    }
}
