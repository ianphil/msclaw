using System.CommandLine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MsClaw.Core;
using MsClaw.Gateway.Hosting;
using MsClaw.Gateway.Hubs;
using MsClaw.Gateway.Services;

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
        command.Add(mindOption);
        command.Add(newMindOption);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var mindPath = parseResult.GetValue(mindOption);
            var newMindPath = parseResult.GetValue(newMindOption);
            var scaffold = new MindScaffold();

            return await ExecuteStartAsync(mindPath, newMindPath, RunGatewayAsync, scaffold, cancellationToken);
        });

        return command;
    }

    public static void ConfigureServices(IServiceCollection services, GatewayOptions options)
    {
        services.AddSignalR();
        services.AddSingleton(options);
        services.AddSingleton<IMindValidator, MindValidator>();
        services.AddSingleton<IIdentityLoader, IdentityLoader>();
        services.AddSingleton<IMindScaffold, MindScaffold>();
        services.AddSingleton<IMindReader>(_ => new MindReader(options.MindPath));
        services.AddSingleton<CallerRegistry>();
        services.AddSingleton<IConcurrencyGate>(serviceProvider => serviceProvider.GetRequiredService<CallerRegistry>());
        services.AddSingleton<ISessionMap>(serviceProvider => serviceProvider.GetRequiredService<CallerRegistry>());
        services.AddSingleton<GatewayHostedService>();
        services.AddSingleton<IGatewayClient>(serviceProvider => serviceProvider.GetRequiredService<GatewayHostedService>());
        services.AddSingleton<IGatewayHostedService>(serviceProvider => serviceProvider.GetRequiredService<GatewayHostedService>());
        services.AddSingleton<AgentMessageService>();
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<GatewayHostedService>());
    }

    public static IResult BuildHealthResult(IGatewayHostedService hostedService)
    {
        return hostedService.IsReady
            ? Results.Json(new { status = "Healthy" }, statusCode: StatusCodes.Status200OK)
            : Results.Json(new { status = "Unhealthy", error = hostedService.Error }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/healthz", (IGatewayHostedService hostedService) => BuildHealthResult(hostedService));
        endpoints.MapHub<GatewayHub>("/gateway");
    }

    public static async Task<int> ExecuteStartAsync(
        string? mindPath,
        string? newMindPath,
        Func<GatewayOptions, CancellationToken, Task<int>> runGatewayAsync,
        IMindScaffold mindScaffold,
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

        var options = new GatewayOptions
        {
            MindPath = Path.GetFullPath(resolvedMindPath),
            Host = "127.0.0.1",
            Port = 18789
        };

        return await runGatewayAsync(options, cancellationToken);
    }

    public static async Task<int> RunGatewayAsync(GatewayOptions options, CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://{options.Host}:{options.Port}");
        ConfigureServices(builder.Services, options);
        var app = builder.Build();
        MapEndpoints(app);
        await app.RunAsync(cancellationToken);

        return 0;
    }
}
