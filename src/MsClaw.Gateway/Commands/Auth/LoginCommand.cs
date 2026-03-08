using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;
using MsClaw.Core;
using MsClaw.Gateway.Auth;

namespace MsClaw.Gateway.Commands.Auth;

/// <summary>
/// Implements interactive browser login and persists the resulting auth session in user config.
/// </summary>
public static class LoginCommand
{
    /// <summary>
    /// Creates the <c>msclaw auth login</c> command.
    /// </summary>
    public static Command Create()
    {
        var command = new Command("login", "Sign in via Entra interactive browser and cache an access token");
        command.SetAction(async (_, cancellationToken) =>
        {
            return await ExecuteLoginAsync(
                BuildConfiguration(),
                new UserConfigLoader(),
                new MsalInteractiveBrowserAuthenticator(new MsalPublicClientFactory()),
                Console.Out,
                cancellationToken);
        });

        return command;
    }

    /// <summary>
    /// Executes interactive browser login and persists auth material under <c>~/.msclaw/config.json</c>.
    /// </summary>
    public static async Task<int> ExecuteLoginAsync(
        IConfiguration configuration,
        IUserConfigLoader userConfigLoader,
        IInteractiveAuthenticator authenticator,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(userConfigLoader);
        ArgumentNullException.ThrowIfNull(authenticator);
        ArgumentNullException.ThrowIfNull(output);

        var tenantId = configuration["AzureAd:TenantId"];
        var clientId = configuration["AzureAd:ClientId"];
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId))
        {
            await output.WriteLineAsync("Auth login failed: AzureAd TenantId/ClientId are not configured.");

            return 1;
        }

        var scopes = AuthScopes.Build(clientId);

        try
        {
            await output.WriteLineAsync("Opening browser for sign-in...");
            var loginResult = await authenticator.LoginAsync(
                tenantId,
                clientId,
                scopes,
                message => output.WriteLineAsync(message),
                cancellationToken);

            var config = userConfigLoader.Load();
            config.Auth = new UserAuthConfig
            {
                TenantId = tenantId,
                ClientId = clientId,
                Username = loginResult.Username,
                AccessToken = loginResult.AccessToken,
                ExpiresAtUtc = loginResult.ExpiresAtUtc
            };
            userConfigLoader.Save(config);

            await output.WriteLineAsync($"Signed in as {loginResult.Username}.");
            await output.WriteLineAsync($"Token expires at {loginResult.ExpiresAtUtc:O}.");
            await output.WriteLineAsync($"Saved auth session to {userConfigLoader.GetConfigPath()}.");

            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync("Auth login cancelled.");

            return 1;
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Auth login failed: {ex.Message}");

            return 1;
        }
    }

    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
    }
}

/// <summary>
/// Abstracts interactive authentication to keep login command behavior unit-testable.
/// </summary>
public interface IInteractiveAuthenticator
{
    /// <summary>
    /// Performs an interactive browser login and returns issued token data.
    /// </summary>
    Task<LoginResult> LoginAsync(
        string tenantId,
        string clientId,
        IReadOnlyList<string> scopes,
        Func<string, Task> onStatusMessage,
        CancellationToken cancellationToken);
}

/// <summary>
/// Login result material returned by <see cref="IInteractiveAuthenticator" />.
/// </summary>
public sealed class LoginResult
{
    /// <summary>
    /// Gets or sets the signed-in account username/email.
    /// </summary>
    public required string Username { get; set; }

    /// <summary>
    /// Gets or sets the access token returned from Entra.
    /// </summary>
    public required string AccessToken { get; set; }

    /// <summary>
    /// Gets or sets the UTC access-token expiration instant.
    /// </summary>
    public required DateTimeOffset ExpiresAtUtc { get; set; }
}

internal sealed class MsalInteractiveBrowserAuthenticator : IInteractiveAuthenticator
{
    private readonly IMsalPublicClientFactory msalClientFactory;

    /// <summary>
    /// Creates the interactive authenticator using the provided MSAL client factory.
    /// </summary>
    public MsalInteractiveBrowserAuthenticator(IMsalPublicClientFactory msalClientFactory)
    {
        this.msalClientFactory = msalClientFactory;
    }

    /// <summary>
    /// Opens the system browser for Entra interactive login with localhost loopback redirect.
    /// This satisfies Conditional Access device-compliance policies that block device-code flow.
    /// </summary>
    public async Task<LoginResult> LoginAsync(
        string tenantId,
        string clientId,
        IReadOnlyList<string> scopes,
        Func<string, Task> onStatusMessage,
        CancellationToken cancellationToken)
    {
        var app = await msalClientFactory.CreateAsync(tenantId, clientId, cancellationToken);

        var authResult = await app.AcquireTokenInteractive(scopes)
            .WithUseEmbeddedWebView(false)
            .WithSystemWebViewOptions(new SystemWebViewOptions
            {
                HtmlMessageSuccess = "<h2>Authentication successful!</h2><p>You can close this tab.</p>",
                HtmlMessageError = "<h2>Authentication failed</h2><p>Check the terminal for details.</p>"
            })
            .ExecuteAsync(cancellationToken);

        return new LoginResult
        {
            Username = authResult.Account?.Username ?? "unknown",
            AccessToken = authResult.AccessToken,
            ExpiresAtUtc = authResult.ExpiresOn.UtcDateTime
        };
    }
}
