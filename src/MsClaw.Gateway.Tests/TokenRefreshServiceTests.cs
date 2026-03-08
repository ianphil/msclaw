using GitHub.Copilot.SDK;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Identity.Client;
using MsClaw.Core;
using MsClaw.Gateway.Hubs;
using MsClaw.Gateway.Services;
using Xunit;

namespace MsClaw.Gateway.Tests;

public sealed class TokenRefreshServiceTests
{
    [Fact]
    public async Task RunRefreshIterationAsync_NoAuthConfig_ReturnsMissingConfigDelay()
    {
        var loader = new StubUserConfigLoader { Stored = new UserConfig() };
        var refresher = new StubTokenRefresher();
        var notifier = new StubGatewayHubClient();
        var hubContext = new StubHubContext(notifier);
        var sut = new TokenRefreshService(loader, refresher, hubContext);

        var delay = await sut.RunRefreshIterationAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(2), delay);
        Assert.False(refresher.Called);
        Assert.Empty(notifier.AuthUpdates);
    }

    [Fact]
    public async Task RunRefreshIterationAsync_ExpiringToken_RefreshesSavesAndBroadcasts()
    {
        var loader = new StubUserConfigLoader
        {
            Stored = new UserConfig
            {
                Auth = new UserAuthConfig
                {
                    TenantId = "tenant-id",
                    ClientId = "client-id",
                    Username = "user@example.com",
                    AccessToken = "old-token",
                    ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(1)
                }
            }
        };
        var refresher = new StubTokenRefresher
        {
            NextResult = new RefreshedToken("user@example.com", "new-token", DateTimeOffset.UtcNow.AddMinutes(55))
        };
        var notifier = new StubGatewayHubClient();
        var hubContext = new StubHubContext(notifier);
        var sut = new TokenRefreshService(loader, refresher, hubContext);

        var delay = await sut.RunRefreshIterationAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(10), delay);
        Assert.True(refresher.Called);
        Assert.NotNull(loader.Stored.Auth);
        Assert.Equal("new-token", loader.Stored.Auth.AccessToken);
        Assert.Single(notifier.AuthUpdates);
        Assert.Equal("new-token", notifier.AuthUpdates[0].AccessToken);
    }

    [Fact]
    public async Task RunRefreshIterationAsync_WhenUiRequired_ReturnsRetryDelayWithoutBroadcast()
    {
        var loader = new StubUserConfigLoader
        {
            Stored = new UserConfig
            {
                Auth = new UserAuthConfig
                {
                    TenantId = "tenant-id",
                    ClientId = "client-id",
                    Username = "user@example.com",
                    AccessToken = "old-token",
                    ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(1)
                }
            }
        };
        var refresher = new StubTokenRefresher
        {
            NextException = new MsalUiRequiredException("ui_required", "reauth required")
        };
        var notifier = new StubGatewayHubClient();
        var hubContext = new StubHubContext(notifier);
        var sut = new TokenRefreshService(loader, refresher, hubContext);

        var delay = await sut.RunRefreshIterationAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(1), delay);
        Assert.Equal("old-token", loader.Stored.Auth?.AccessToken);
        Assert.Empty(notifier.AuthUpdates);
    }

    private sealed class StubUserConfigLoader : IUserConfigLoader
    {
        public UserConfig Stored { get; set; } = new();

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

    private sealed class StubTokenRefresher : ITokenRefresher
    {
        public RefreshedToken? NextResult { get; set; }

        public Exception? NextException { get; set; }

        public bool Called { get; private set; }

        public Task<RefreshedToken> RefreshAsync(UserAuthConfig authConfig, CancellationToken cancellationToken = default)
        {
            Called = true;
            if (NextException is not null)
            {
                throw NextException;
            }

            return Task.FromResult(NextResult ?? new RefreshedToken("user@example.com", "token", DateTimeOffset.UtcNow.AddMinutes(30)));
        }
    }

    private sealed class StubGatewayHubClient : IGatewayHubClient
    {
        public List<GatewayAuthContext> AuthUpdates { get; } = [];

        public Task ReceiveEvent(SessionEvent sessionEvent)
        {
            return Task.CompletedTask;
        }

        public Task ReceivePresence(PresenceSnapshot presence)
        {
            return Task.CompletedTask;
        }

        public Task ReceiveAuthContext(GatewayAuthContext authContext)
        {
            AuthUpdates.Add(authContext);
            return Task.CompletedTask;
        }
    }

    private sealed class StubHubContext(IGatewayHubClient client) : IHubContext<GatewayHub, IGatewayHubClient>
    {
        public IHubClients<IGatewayHubClient> Clients { get; } = new StubHubClients(client);

        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class StubHubClients(IGatewayHubClient client) : IHubClients<IGatewayHubClient>
    {
        public IGatewayHubClient All => client;

        public IGatewayHubClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => client;

        public IGatewayHubClient Client(string connectionId) => client;

        public IGatewayHubClient Clients(IReadOnlyList<string> connectionIds) => client;

        public IGatewayHubClient Group(string groupName) => client;

        public IGatewayHubClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => client;

        public IGatewayHubClient Groups(IReadOnlyList<string> groupNames) => client;

        public IGatewayHubClient User(string userId) => client;

        public IGatewayHubClient Users(IReadOnlyList<string> userIds) => client;
    }
}
