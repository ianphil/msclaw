using System.CommandLine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MsClaw.Core;
using MsClaw.Gateway.Extensions;
using MsClaw.Tunnel;

namespace MsClaw.Gateway.Commands;

/// <summary>
/// Defines the <c>start</c> CLI command and orchestrates gateway startup.
/// </summary>
public static class StartCommand
{
    /// <summary>
    /// Creates the <c>start</c> command with its CLI options.
    /// </summary>
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

    /// <summary>
    /// Validates CLI inputs, resolves tunnel settings, and delegates to the gateway runner.
    /// </summary>
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
        EnsureTunnelLogin(tunnelRequested, userConfigLoader);
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

    /// <summary>
    /// Builds and runs the ASP.NET Core WebApplication that hosts the gateway.
    /// </summary>
    public static async Task<int> RunGatewayAsync(GatewayOptions options, CancellationToken cancellationToken = default)
    {
        var assemblyDir = Path.GetDirectoryName(typeof(StartCommand).Assembly.Location)!;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            WebRootPath = Path.Combine(assemblyDir, "wwwroot")
        });
        builder.WebHost.UseUrls($"http://{options.Host}:{options.Port}");
        builder.Services.AddGatewayServices(builder.Configuration, options);
        var app = builder.Build();
        app.UseGatewayPipeline();
        app.MapGatewayEndpoints();
        await app.StartAsync(cancellationToken);
        var tunnelManager = app.Services.GetRequiredService<ITunnelManager>();
        Console.WriteLine(GatewayBannerBuilder.BuildAccessBanner(options, tunnelManager.GetStatus()));
        await app.WaitForShutdownAsync(cancellationToken);

        return 0;
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

    private static void EnsureTunnelLogin(bool tunnelRequested, IUserConfigLoader? userConfigLoader)
    {
        if (tunnelRequested is false)
        {
            return;
        }

        if (userConfigLoader is null)
        {
            throw new InvalidOperationException("Tunnel mode requires user config access. Run `msclaw auth login` and retry.");
        }

        var config = userConfigLoader.Load();
        if (GatewayEndpointExtensions.TryGetValidAuth(config, DateTimeOffset.UtcNow, out _) is false)
        {
            throw new InvalidOperationException("Tunnel mode requires an active login. Run `msclaw auth login` and retry.");
        }
    }
}
