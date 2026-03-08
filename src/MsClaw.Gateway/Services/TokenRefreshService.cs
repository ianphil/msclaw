using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client;
using MsClaw.Core;
using MsClaw.Gateway.Auth;
using MsClaw.Gateway.Hubs;

namespace MsClaw.Gateway.Services;

/// <summary>
/// Refreshes Entra access tokens in the background and propagates refreshed auth context to connected clients.
/// </summary>
public sealed class TokenRefreshService : BackgroundService
{
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MissingConfigDelay = TimeSpan.FromMinutes(2);
    private readonly IUserConfigLoader userConfigLoader;
    private readonly ITokenRefresher tokenRefresher;
    private readonly IHubContext<GatewayHub, IGatewayHubClient> hubContext;
    private readonly ILogger<TokenRefreshService> logger;

    /// <summary>
    /// Creates the hosted service responsible for periodic silent token refresh.
    /// </summary>
    public TokenRefreshService(
        IUserConfigLoader userConfigLoader,
        ITokenRefresher tokenRefresher,
        IHubContext<GatewayHub, IGatewayHubClient> hubContext,
        ILogger<TokenRefreshService>? logger = null)
    {
        this.userConfigLoader = userConfigLoader;
        this.tokenRefresher = tokenRefresher;
        this.hubContext = hubContext;
        this.logger = logger ?? NullLogger<TokenRefreshService>.Instance;
    }

    /// <summary>
    /// Runs the periodic refresh loop until shutdown.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (stoppingToken.IsCancellationRequested is false)
        {
            var nextDelay = await RunRefreshIterationAsync(stoppingToken);
            if (nextDelay > TimeSpan.Zero)
            {
                await Task.Delay(nextDelay, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Executes one refresh iteration and returns the delay before the next iteration.
    /// </summary>
    public async Task<TimeSpan> RunRefreshIterationAsync(CancellationToken cancellationToken)
    {
        var config = userConfigLoader.Load();
        var auth = config.Auth;
        if (auth is null)
        {
            return MissingConfigDelay;
        }

        var expiresAtUtc = auth.ExpiresAtUtc;
        if (expiresAtUtc is null)
        {
            logger.LogWarning("Skipping token refresh because auth expiry is not configured.");
            return MissingConfigDelay;
        }

        var refreshAtUtc = expiresAtUtc.Value - RefreshWindow;
        var nowUtc = DateTimeOffset.UtcNow;
        if (refreshAtUtc > nowUtc)
        {
            return refreshAtUtc - nowUtc;
        }

        try
        {
            var refreshed = await tokenRefresher.RefreshAsync(auth, cancellationToken);
            auth.Username = refreshed.Username;
            auth.AccessToken = refreshed.AccessToken;
            auth.ExpiresAtUtc = refreshed.ExpiresAtUtc;
            config.Auth = auth;
            userConfigLoader.Save(config);

            await hubContext.Clients.All.ReceiveAuthContext(new GatewayAuthContext(
                Authenticated: true,
                Username: auth.Username,
                AccessToken: auth.AccessToken,
                ExpiresAtUtc: auth.ExpiresAtUtc));

            logger.LogInformation("Refreshed Entra token for {Username}; expires at {ExpiresAtUtc:O}.", auth.Username, auth.ExpiresAtUtc);
            return TimeSpan.FromMinutes(10);
        }
        catch (MsalUiRequiredException ex)
        {
            logger.LogWarning(ex, "Silent token refresh requires user interaction. Run `msclaw auth login`.");
            return RetryDelay;
        }
        catch (MsalServiceException ex)
        {
            logger.LogWarning(ex, "Silent token refresh failed due to Entra service error.");
            return RetryDelay;
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Silent token refresh is not possible with the current auth state.");
            return RetryDelay;
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Silent token refresh failed while reading or writing local auth files.");
            return RetryDelay;
        }
    }
}

/// <summary>
/// Performs silent token refresh using MSAL and the persisted token cache.
/// </summary>
public interface ITokenRefresher
{
    /// <summary>
    /// Refreshes the user token represented by the provided auth config.
    /// </summary>
    Task<RefreshedToken> RefreshAsync(UserAuthConfig authConfig, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents refreshed token material returned from silent refresh.
/// </summary>
/// <param name="Username">The username associated with the refreshed token.</param>
/// <param name="AccessToken">The refreshed bearer access token.</param>
/// <param name="ExpiresAtUtc">The refreshed token expiration time.</param>
public sealed record RefreshedToken(string Username, string AccessToken, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Uses MSAL's <c>AcquireTokenSilent</c> flow with persisted cache to refresh Entra tokens.
/// </summary>
public sealed class MsalSilentTokenRefresher(IMsalPublicClientFactory msalClientFactory) : ITokenRefresher
{
    /// <inheritdoc />
    public async Task<RefreshedToken> RefreshAsync(UserAuthConfig authConfig, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authConfig);

        var tenantId = authConfig.TenantId;
        var clientId = authConfig.ClientId;
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("Auth config is missing TenantId and/or ClientId.");
        }

        var app = await msalClientFactory.CreateAsync(tenantId, clientId, cancellationToken);
        var accounts = await app.GetAccountsAsync();
        var username = authConfig.Username;
        IAccount? account = null;
        if (string.IsNullOrWhiteSpace(username) is false)
        {
            account = accounts.FirstOrDefault(candidate => string.Equals(candidate.Username, username, StringComparison.OrdinalIgnoreCase));
        }

        account ??= accounts.FirstOrDefault();
        if (account is null)
        {
            throw new InvalidOperationException("No cached MSAL account was found for silent refresh.");
        }

        var authResult = await app.AcquireTokenSilent(AuthScopes.Build(clientId), account)
            .ExecuteAsync(cancellationToken);

        return new RefreshedToken(
            authResult.Account?.Username ?? username ?? "unknown",
            authResult.AccessToken,
            authResult.ExpiresOn.UtcDateTime);
    }
}
