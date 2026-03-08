using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using MsClaw.Core;
using MsClaw.Gateway.Hosting;
using MsClaw.Gateway.Hubs;
using MsClaw.OpenResponses;
using MsClaw.Tunnel;

namespace MsClaw.Gateway.Extensions;

/// <summary>
/// Maps gateway HTTP endpoints and configures the middleware pipeline.
/// </summary>
public static class GatewayEndpointExtensions
{
    /// <summary>
    /// Maps health, tunnel, auth, SignalR, and OpenResponses endpoints onto the gateway.
    /// </summary>
    public static IEndpointRouteBuilder MapGatewayEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => BuildLivenessResult());
        endpoints.MapGet("/health/ready", ([FromServices] IGatewayHostedService hostedService) => BuildReadinessResult(hostedService));
        endpoints.MapGet("/api/tunnel/status", ([FromServices] ITunnelManager tunnelManager) => BuildTunnelStatusResult(tunnelManager));
        endpoints.MapGet("/api/auth/context", ([FromServices] IUserConfigLoader userConfigLoader) => BuildAuthContextResult(userConfigLoader));
        endpoints.MapHub<GatewayHub>("/gateway");
        endpoints.MapOpenResponses();

        return endpoints;
    }

    /// <summary>
    /// Configures middleware required to serve the gateway's static chat assets.
    /// Splitting this from endpoint mapping keeps the pipeline testable without starting the full host.
    /// </summary>
    public static IApplicationBuilder UseGatewayPipeline(this IApplicationBuilder application)
    {
        application.UseDefaultFiles();
        application.UseStaticFiles();
        application.UseAuthentication();
        application.UseAuthorization();

        return application;
    }

    /// <summary>
    /// Returns a liveness probe result indicating the gateway process is running.
    /// </summary>
    public static IResult BuildLivenessResult()
    {
        return Results.Json(new { status = "Healthy" }, statusCode: StatusCodes.Status200OK);
    }

    /// <summary>
    /// Returns a readiness probe result reflecting the hosted service state.
    /// </summary>
    public static IResult BuildReadinessResult(IGatewayHostedService hostedService)
    {
        return hostedService.IsReady
            ? Results.Json(new { status = "Healthy" }, statusCode: StatusCodes.Status200OK)
            : Results.Json(
                new { status = "Unhealthy", component = "hosted-service", error = hostedService.Error },
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>
    /// Returns the current dev tunnel state as a JSON payload.
    /// </summary>
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

    /// <summary>
    /// Builds an auth-context response used by the browser UI to bootstrap bearer-authenticated calls.
    /// </summary>
    public static IResult BuildAuthContextResult(IUserConfigLoader userConfigLoader)
    {
        ArgumentNullException.ThrowIfNull(userConfigLoader);

        var config = userConfigLoader.Load();
        if (TryGetValidAuth(config, DateTimeOffset.UtcNow, out var authConfig) is false)
        {
            return Results.Json(
                new
                {
                    authenticated = false,
                    message = "No active login session. Run `msclaw auth login` and restart the gateway."
                },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return Results.Json(
            new
            {
                authenticated = true,
                username = authConfig!.Username,
                accessToken = authConfig.AccessToken,
                expiresAtUtc = authConfig.ExpiresAtUtc
            },
            statusCode: StatusCodes.Status200OK);
    }

    /// <summary>
    /// Validates that the user config contains an unexpired auth token.
    /// </summary>
    public static bool TryGetValidAuth(UserConfig config, DateTimeOffset nowUtc, out UserAuthConfig? auth)
    {
        ArgumentNullException.ThrowIfNull(config);

        auth = config.Auth;
        if (auth is null
            || string.IsNullOrWhiteSpace(auth.AccessToken)
            || auth.ExpiresAtUtc is null
            || auth.ExpiresAtUtc <= nowUtc.AddMinutes(1))
        {
            auth = null;
            return false;
        }

        return true;
    }
}
