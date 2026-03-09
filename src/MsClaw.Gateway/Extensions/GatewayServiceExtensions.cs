using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using MsClaw.Core;
using MsClaw.Gateway.Auth;
using MsClaw.Gateway.Hosting;
using MsClaw.Gateway.Hubs;
using MsClaw.Gateway.Services;
using MsClaw.Gateway.Services.Cron;
using MsClaw.Gateway.Services.Tools;
using MsClaw.OpenResponses;
using MsClaw.Tunnel;

namespace MsClaw.Gateway.Extensions;

/// <summary>
/// Registers all gateway services into the DI container.
/// </summary>
public static class GatewayServiceExtensions
{
    /// <summary>
    /// Adds authentication, authorization, SignalR, and all gateway-specific services
    /// required to run the MsClaw gateway host.
    /// </summary>
    public static IServiceCollection AddGatewayServices(this IServiceCollection services, IConfiguration configuration, GatewayOptions options)
    {
        var azureAdSection = configuration.GetSection("AzureAd");
        var clientId = azureAdSection["ClientId"];
        if (string.IsNullOrWhiteSpace(clientId) is false)
        {
            services.AddAuthentication()
                .AddMicrosoftIdentityWebApi(azureAdSection);
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
        }
        else
        {
            services.AddAuthentication();
        }
        services.AddAuthorization();
        services.AddSignalR();
        services.AddSingleton(options);
        services.AddSingleton<IUserConfigLoader, UserConfigLoader>();
        services.AddSingleton<IMindValidator, MindValidator>();
        services.AddSingleton<IIdentityLoader, IdentityLoader>();
        services.AddSingleton<IMindScaffold, MindScaffold>();
        services.AddSingleton<IMindReader>(_ => new MindReader(options.MindPath));
        services.AddSingleton<IMsalPublicClientFactory, MsalPublicClientFactory>();
        services.AddSingleton<ITokenRefresher, MsalSilentTokenRefresher>();
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
        services.AddSingleton<ToolBridge>();
        services.AddSingleton<IToolCatalog>(serviceProvider => serviceProvider.GetRequiredService<ToolBridge>());
        services.AddSingleton<IToolRegistrar>(serviceProvider => serviceProvider.GetRequiredService<ToolBridge>());
        services.AddSingleton<ToolBridgeHostedService>();
        services.AddSingleton<GatewayHostedService>();
        services.AddSingleton<GatewayTunnelHostedService>();
        services.AddSingleton<GatewayClientProxy>();
        services.AddSingleton<IGatewayClient>(serviceProvider => serviceProvider.GetRequiredService<GatewayClientProxy>());
        services.AddSingleton<IGatewayHostedService>(serviceProvider => serviceProvider.GetRequiredService<GatewayHostedService>());
        services.AddSingleton<IToolExpander, ToolExpander>();
        services.AddSingleton<IToolProvider, EchoToolProvider>();
        services.AddSingleton<CronJobStore>();
        services.AddSingleton<ICronJobStore>(serviceProvider => serviceProvider.GetRequiredService<CronJobStore>());
        services.AddSingleton<ICronRunHistoryStore>(serviceProvider => serviceProvider.GetRequiredService<CronJobStore>());
        services.AddSingleton<ICronErrorClassifier, DefaultCronErrorClassifier>();
        services.AddSingleton<ICronOutputSink, SignalRCronOutputSink>();
        services.AddSingleton<ICronJobExecutor, PromptJobExecutor>();
        services.AddSingleton<ICronJobExecutor, CommandJobExecutor>();
        services.AddSingleton<CronEngine>();
        services.AddSingleton<ICronEngine>(serviceProvider => serviceProvider.GetRequiredService<CronEngine>());
        services.AddSingleton<IToolProvider, CronToolProvider>();
        services.AddSingleton<AgentMessageService>();
        services.AddSingleton<IOpenResponseService, GatewayOpenResponseService>();
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<ToolBridgeHostedService>());
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<GatewayHostedService>());
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<GatewayTunnelHostedService>());
        services.AddHostedService<TokenRefreshService>();
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<CronEngine>());

        return services;
    }
}
