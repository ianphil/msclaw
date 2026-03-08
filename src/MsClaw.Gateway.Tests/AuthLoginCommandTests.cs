using Microsoft.Extensions.Configuration;
using MsClaw.Core;
using MsClaw.Gateway.Commands.Auth;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class AuthLoginCommandTests
{
    [Fact]
    public async Task ExecuteLoginAsync_ValidConfiguration_PersistsAuthSession()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:TenantId"] = "tenant-id",
                ["AzureAd:ClientId"] = "client-id"
            })
            .Build();
        var loader = new StubUserConfigLoader();
        var authenticator = new StubInteractiveAuthenticator
        {
            Result = new LoginResult
            {
                Username = "user@example.com",
                AccessToken = "token-1",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30)
            }
        };
        using var writer = new StringWriter();

        var exitCode = await LoginCommand.ExecuteLoginAsync(
            configuration,
            loader,
            authenticator,
            writer,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.NotNull(loader.Stored.Auth);
        Assert.Equal("tenant-id", loader.Stored.Auth.TenantId);
        Assert.Equal("client-id", loader.Stored.Auth.ClientId);
        Assert.Equal("user@example.com", loader.Stored.Auth.Username);
        Assert.Equal("token-1", loader.Stored.Auth.AccessToken);
        Assert.Contains("Saved auth session", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("api://client-id/access_as_user", authenticator.ReceivedScopes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ExecuteLoginAsync_MissingAzureAdSettings_ReturnsError()
    {
        var configuration = new ConfigurationBuilder().Build();
        var loader = new StubUserConfigLoader();
        var authenticator = new StubInteractiveAuthenticator();
        using var writer = new StringWriter();

        var exitCode = await LoginCommand.ExecuteLoginAsync(
            configuration,
            loader,
            authenticator,
            writer,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Null(loader.Stored.Auth);
        Assert.Contains("TenantId/ClientId", writer.ToString(), StringComparison.Ordinal);
    }

    private sealed class StubUserConfigLoader : IUserConfigLoader
    {
        public UserConfig Stored { get; private set; } = new();

        public UserConfig Load()
        {
            return Stored;
        }

        public void Save(UserConfig config)
        {
            Stored = config;
        }

        public string GetConfigPath()
        {
            return "C:\\temp\\config.json";
        }
    }

    private sealed class StubInteractiveAuthenticator : IInteractiveAuthenticator
    {
        public LoginResult? Result { get; set; }

        public IReadOnlyList<string> ReceivedScopes { get; private set; } = [];

        public Task<LoginResult> LoginAsync(
            string tenantId,
            string clientId,
            IReadOnlyList<string> scopes,
            Func<string, Task> onStatusMessage,
            CancellationToken cancellationToken)
        {
            ReceivedScopes = scopes.ToArray();
            return Task.FromResult(Result ?? new LoginResult
            {
                Username = "user@example.com",
                AccessToken = "token",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
            });
        }
    }
}
