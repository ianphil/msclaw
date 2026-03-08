using MsClaw.Core;
using Xunit;

namespace MsClaw.Tunnel.Tests;

public sealed class TunnelManagerTests
{
    [Fact]
    public async Task StartAsync_EnabledWithoutConfig_CreatesTunnelAndPersistsId()
    {
        var configLoader = new FakeUserConfigLoader();
        var executor = new FakeDevTunnelExecutor();
        executor.RunResults.Enqueue(new DevTunnelCommandResult(0, "{}", "")); // user show
        executor.RunResults.Enqueue(new DevTunnelCommandResult(0, "{\"tunnelId\":\"alpha-tunnel\"}", "")); // create
        executor.RunResults.Enqueue(new DevTunnelCommandResult(0, "ok", "")); // access create
        executor.RunResults.Enqueue(new DevTunnelCommandResult(0, "ok", "")); // port create
        executor.HostOutputLines.Enqueue("Connected at https://alpha-tunnel.devtunnels.ms");
        var sut = new TunnelManager(
            new FakeDevTunnelLocator(),
            configLoader,
            new TunnelManagerOptions { Enabled = true, LocalPort = 18789 },
            executor);

        await sut.StartAsync();
        var status = sut.GetStatus();

        Assert.True(status.IsRunning);
        Assert.Equal("alpha-tunnel", status.TunnelId);
        Assert.Equal("https://alpha-tunnel.devtunnels.ms", status.PublicUrl);
        Assert.Equal("alpha-tunnel", configLoader.StoredConfig.TunnelId);
    }

    [Fact]
    public async Task StartAsync_WithConfiguredTunnelId_SkipsCreateCommand()
    {
        var configLoader = new FakeUserConfigLoader { StoredConfig = new UserConfig { TunnelId = "existing-id" } };
        var executor = new FakeDevTunnelExecutor();
        executor.RunResults.Enqueue(new DevTunnelCommandResult(0, "{}", "")); // user show
        executor.RunResults.Enqueue(new DevTunnelCommandResult(0, "ok", "")); // access create
        executor.RunResults.Enqueue(new DevTunnelCommandResult(0, "ok", "")); // port create
        executor.HostOutputLines.Enqueue("Connected at https://existing-id.devtunnels.ms");
        var sut = new TunnelManager(
            new FakeDevTunnelLocator(),
            configLoader,
            new TunnelManagerOptions { Enabled = true, LocalPort = 18789 },
            executor);

        await sut.StartAsync();

        Assert.DoesNotContain(executor.Commands, static command => command.StartsWith("create", StringComparison.Ordinal));
        Assert.Contains("host existing-id", executor.Commands, StringComparer.Ordinal);
        Assert.Equal("existing-id", sut.GetStatus().TunnelId);
    }

    [Fact]
    public async Task StartAsync_WhenPortAlreadyExists_ContinuesToHostTunnel()
    {
        var configLoader = new FakeUserConfigLoader { StoredConfig = new UserConfig { TunnelId = "existing-id" } };
        var executor = new FakeDevTunnelExecutor();
        executor.RunResults.Enqueue(new DevTunnelCommandResult(0, "{}", "")); // user show
        executor.RunResults.Enqueue(new DevTunnelCommandResult(0, "ok", "")); // access create
        executor.RunResults.Enqueue(new DevTunnelCommandResult(
            1,
            string.Empty,
            "Tunnel service error: Conflict with existing entity. Tunnel port number conflicts with an existing port in the tunnel.")); // port create already exists
        executor.HostOutputLines.Enqueue("Connected at https://existing-id.devtunnels.ms");
        var sut = new TunnelManager(
            new FakeDevTunnelLocator(),
            configLoader,
            new TunnelManagerOptions { Enabled = true, LocalPort = 18789 },
            executor);

        await sut.StartAsync();
        var status = sut.GetStatus();

        Assert.True(status.IsRunning);
        Assert.Equal("https://existing-id.devtunnels.ms", status.PublicUrl);
    }

    [Fact]
    public async Task StartAsync_WhenDisabled_DoesNothing()
    {
        var configLoader = new FakeUserConfigLoader();
        var executor = new FakeDevTunnelExecutor();
        var sut = new TunnelManager(
            new FakeDevTunnelLocator(),
            configLoader,
            new TunnelManagerOptions { Enabled = false },
            executor);

        await sut.StartAsync();
        var status = sut.GetStatus();

        Assert.False(status.IsRunning);
        Assert.Empty(executor.Commands);
    }

    [Fact]
    public async Task StopAsync_WhenRunning_StopsHostAndClearsPublicUrl()
    {
        var configLoader = new FakeUserConfigLoader();
        var executor = new FakeDevTunnelExecutor();
        executor.RunResults.Enqueue(new DevTunnelCommandResult(0, "{}", "")); // user show
        executor.RunResults.Enqueue(new DevTunnelCommandResult(0, "{\"tunnelId\":\"alpha-tunnel\"}", "")); // create
        executor.RunResults.Enqueue(new DevTunnelCommandResult(0, "ok", "")); // access create
        executor.RunResults.Enqueue(new DevTunnelCommandResult(0, "ok", "")); // port create
        executor.HostOutputLines.Enqueue("Connected at https://alpha-tunnel.devtunnels.ms");
        var sut = new TunnelManager(
            new FakeDevTunnelLocator(),
            configLoader,
            new TunnelManagerOptions { Enabled = true, LocalPort = 18789 },
            executor);
        await sut.StartAsync();

        await sut.StopAsync();
        var status = sut.GetStatus();

        Assert.False(status.IsRunning);
        Assert.Null(status.PublicUrl);
        Assert.True(executor.HostHandle.StopCalled);
    }

    private sealed class FakeDevTunnelLocator : IDevTunnelLocator
    {
        public string ResolveDevTunnelCliPath()
        {
            return "devtunnel";
        }
    }

    private sealed class FakeUserConfigLoader : IUserConfigLoader
    {
        public UserConfig StoredConfig { get; set; } = new();

        public UserConfig Load()
        {
            return new UserConfig { TunnelId = StoredConfig.TunnelId };
        }

        public void Save(UserConfig config)
        {
            StoredConfig = new UserConfig { TunnelId = config.TunnelId };
        }

        public string GetConfigPath()
        {
            return "C:\\temp\\config.json";
        }
    }

    private sealed class FakeDevTunnelExecutor : IDevTunnelExecutor
    {
        public Queue<DevTunnelCommandResult> RunResults { get; } = new();

        public Queue<string> HostOutputLines { get; } = new();

        public List<string> Commands { get; } = [];

        public FakeHostHandle HostHandle { get; } = new();

        public Task<DevTunnelCommandResult> RunAsync(string cliPath, string arguments, CancellationToken cancellationToken = default)
        {
            Commands.Add(arguments);
            return Task.FromResult(RunResults.Dequeue());
        }

        public IDevTunnelHostHandle CreateHost(string cliPath, string arguments)
        {
            Commands.Add(arguments);
            HostHandle.SetOutputLines(HostOutputLines.ToArray());
            return HostHandle;
        }
    }

    private sealed class FakeHostHandle : IDevTunnelHostHandle
    {
        private IReadOnlyList<string> outputLines = [];

        public event Action<string>? OutputLine;

        public event Action<string>? ErrorLine;

        public bool HasExited { get; private set; }

        public int? ExitCode => HasExited ? 0 : null;

        public bool StopCalled { get; private set; }

        public void SetOutputLines(IReadOnlyList<string> lines)
        {
            outputLines = lines;
        }

        public void Start()
        {
            foreach (var line in outputLines)
            {
                OutputLine?.Invoke(line);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalled = true;
            HasExited = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            HasExited = true;
            return ValueTask.CompletedTask;
        }
    }
}
