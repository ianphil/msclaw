using MsClaw.Core;
using MsClaw.Gateway.Commands;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class StartCommandHandlerTests
{
    [Fact]
    public async Task ExecuteStartAsync_NewMind_ScaffoldsBeforeRunningGateway()
    {
        var calls = new List<string>();
        var scaffold = new StubMindScaffold(() => calls.Add("scaffold"));

        var exitCode = await StartCommand.ExecuteStartAsync(
            null,
            "C:\\mind",
            (options, cancellationToken) =>
            {
                calls.Add("run");

                return Task.FromResult(0);
            },
            scaffold,
            cancellationToken: CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(["scaffold", "run"], calls);
    }

    [Fact]
    public async Task ExecuteStartAsync_TunnelEnabled_ResolvesTunnelIdFromLoader()
    {
        GatewayOptions? captured = null;
        var scaffold = new StubMindScaffold(static () => { });
        var loader = new StubUserConfigLoader(
            "loader-tunnel-id",
            new UserAuthConfig
            {
                AccessToken = "token",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
            });

        _ = await StartCommand.ExecuteStartAsync(
            "C:\\mind",
            null,
            (options, cancellationToken) =>
            {
                captured = options;
                return Task.FromResult(0);
            },
            scaffold,
            tunnelEnabled: true,
            tunnelId: null,
            userConfigLoader: loader,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(captured);
        Assert.True(captured.TunnelEnabled);
        Assert.Equal("loader-tunnel-id", captured.TunnelId);
    }

    [Fact]
    public async Task ExecuteStartAsync_TunnelEnabledWithoutLogin_Throws()
    {
        var scaffold = new StubMindScaffold(static () => { });
        var loader = new StubUserConfigLoader("loader-tunnel-id", auth: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StartCommand.ExecuteStartAsync(
                "C:\\mind",
                null,
                (options, cancellationToken) => Task.FromResult(0),
                scaffold,
                tunnelEnabled: true,
                tunnelId: null,
                userConfigLoader: loader,
                cancellationToken: CancellationToken.None));

        Assert.Contains("msclaw auth login", ex.Message, StringComparison.Ordinal);
    }

    private sealed class StubMindScaffold(Action onScaffold) : IMindScaffold
    {
        public void Scaffold(string mindRoot)
        {
            onScaffold();
        }
    }

    private sealed class StubUserConfigLoader(string? tunnelId, UserAuthConfig? auth) : IUserConfigLoader
    {
        public UserConfig Load() => new() { TunnelId = tunnelId, Auth = auth };

        public void Save(UserConfig config)
        {
        }

        public string GetConfigPath() => "C:\\temp\\config.json";
    }
}
