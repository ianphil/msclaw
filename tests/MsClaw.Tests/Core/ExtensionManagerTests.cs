using Microsoft.Extensions.DependencyInjection;
using MsClaw.Core;
using MsClaw.Models;
using NSubstitute;
using Xunit;

namespace MsClaw.Tests.Core;

public sealed class ExtensionManagerTests : IDisposable
{
    private readonly string _mindRoot;
    private readonly List<ServiceProvider> _providers = [];

    public ExtensionManagerTests()
    {
        _mindRoot = Path.Combine(Path.GetTempPath(), $"msclaw-ext-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_mindRoot);
    }

    [Fact]
    public async Task Initialize_LoadsCoreExtensionsAndTools()
    {
        var sut = CreateSut();

        await sut.InitializeAsync();

        var loaded = sut.GetLoadedExtensions();
        Assert.Contains(loaded, x => x.Id == "msclaw.core.mind-reader" && x.Tier == ExtensionTier.Core && x.Started);
        Assert.Contains(loaded, x => x.Id == "msclaw.core.runtime-control" && x.Tier == ExtensionTier.Core && x.Started);
        Assert.True(sut.GetTools().Count >= 2);
    }

    [Fact]
    public async Task Initialize_CalledTwice_DoesNotDuplicateCoreExtensions()
    {
        var sut = CreateSut();

        await sut.InitializeAsync();
        await sut.InitializeAsync();

        var loaded = sut.GetLoadedExtensions();
        Assert.Equal(2, loaded.Count);
    }

    [Fact]
    public async Task TryExecuteCommandAsync_NonCommand_ReturnsNull()
    {
        var sut = CreateSut();
        await sut.InitializeAsync();

        var result = await sut.TryExecuteCommandAsync("hello world", sessionId: null);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryExecuteCommandAsync_UnknownCommand_ReturnsHelpfulMessage()
    {
        var sut = CreateSut();
        await sut.InitializeAsync();

        var result = await sut.TryExecuteCommandAsync("/missing-cmd", sessionId: null);

        Assert.Equal("Unknown command: /missing-cmd", result);
    }

    [Fact]
    public async Task TryExecuteCommandAsync_ExtensionsCommand_ListsCoreExtensions()
    {
        var sut = CreateSut();
        await sut.InitializeAsync();

        var result = await sut.TryExecuteCommandAsync("/extensions", sessionId: null);

        Assert.NotNull(result);
        Assert.Contains("msclaw.core.mind-reader", result, StringComparison.Ordinal);
        Assert.Contains("msclaw.core.runtime-control", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReloadExternalAsync_WithNoExternalExtensions_KeepsCoreLoaded()
    {
        var sut = CreateSut();
        await sut.InitializeAsync();

        await sut.ReloadExternalAsync();

        var loaded = sut.GetLoadedExtensions();
        Assert.Equal(2, loaded.Count);
        Assert.All(loaded, x => Assert.Equal(ExtensionTier.Core, x.Tier));
    }

    [Fact]
    public async Task ReloadCommand_CyclesSessions_WhenSessionControlAvailable()
    {
        var sessionControl = new TestSessionControl();
        var sut = CreateSut(sessionControl: sessionControl);
        await sut.InitializeAsync();

        var result = await sut.TryExecuteCommandAsync("/reload", sessionId: null);

        Assert.Equal("External extensions reloaded.", result);
        Assert.Equal(1, sessionControl.CallCount);
    }

    [Fact]
    public async Task ShutdownAsync_ClearsLoadedExtensionsAndTools()
    {
        var sut = CreateSut();
        await sut.InitializeAsync();

        await sut.ShutdownAsync();

        Assert.Empty(sut.GetLoadedExtensions());
        Assert.Empty(sut.GetTools());
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        if (Directory.Exists(_mindRoot))
        {
            Directory.Delete(_mindRoot, recursive: true);
        }
    }

    private IExtensionManager CreateSut(ISessionControl? sessionControl = null)
    {
        var config = Substitute.For<IConfigPersistence>();
        config.Load().Returns(new MsClawConfig { MindRoot = _mindRoot });

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton<IConfigPersistence>(config);
        services.AddSingleton(Substitute.For<IMindReader>());
        services.AddOptions<MsClawOptions>().Configure(opts => opts.MindRoot = _mindRoot);

        if (sessionControl is not null)
        {
            services.AddSingleton(sessionControl);
            services.AddSingleton<ISessionControl>(sessionControl);
        }

        services.AddLogging();
        services.AddSingleton<IExtensionManager, ExtensionManager>();

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider.GetRequiredService<IExtensionManager>();
    }

    private sealed class TestSessionControl : ISessionControl
    {
        public int CallCount { get; private set; }

        public Task CycleSessionsAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }
}
