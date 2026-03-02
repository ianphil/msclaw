using Microsoft.Extensions.DependencyInjection;
using MsClaw.Core;
using MsClaw.Models;
using NSubstitute;
using System.Collections;
using System.Reflection;
using System.Text.Json;
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
    public async Task Initialize_LoadsCoreExtensionsAndNoBuiltInTools()
    {
        var sut = CreateSut();

        await sut.InitializeAsync();

        var loaded = sut.GetLoadedExtensions();
        Assert.Contains(loaded, x => x.Id == "msclaw.core.runtime-control" && x.Tier == ExtensionTier.Core && x.Started);
        Assert.Empty(sut.GetTools());
    }

    [Fact]
    public async Task Initialize_CalledTwice_DoesNotDuplicateCoreExtensions()
    {
        var sut = CreateSut();

        await sut.InitializeAsync();
        await sut.InitializeAsync();

        var loaded = sut.GetLoadedExtensions();
        Assert.Single(loaded);
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
        Assert.Contains("msclaw.core.runtime-control", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReloadExternalAsync_WithNoExternalExtensions_KeepsCoreLoaded()
    {
        var sut = CreateSut();
        await sut.InitializeAsync();

        await sut.ReloadExternalAsync();

        var loaded = sut.GetLoadedExtensions();
        Assert.Single(loaded);
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
    public async Task ReloadExternalAsync_CyclesSessions_WhenSessionControlAvailable()
    {
        var sessionControl = new TestSessionControl();
        var sut = CreateSut(sessionControl: sessionControl);
        await sut.InitializeAsync();

        await sut.ReloadExternalAsync();

        Assert.Equal(1, sessionControl.CallCount);
    }

    [Fact]
    public void DiscoverExternalManifests_OnlyReadsImmediateExtensionDirectories()
    {
        var extensionsRoot = Path.Combine(_mindRoot, "extensions");
        WriteManifest(Path.Combine(extensionsRoot, "first"), "ext.first");
        WriteManifest(Path.Combine(extensionsRoot, "first", "node_modules", "ignored"), "ext.nested");
        WriteManifest(Path.Combine(extensionsRoot, "second"), "ext.second");

        var sut = (ExtensionManager)CreateSut();
        var discovered = (IEnumerable)InvokePrivate(sut, "DiscoverExternalManifests");
        var ids = discovered.Cast<object>().Select(GetId).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

        Assert.Equal(["ext.first", "ext.second"], ids);
    }

    [Fact]
    public void ResolveExternalLoadOrder_AllowsDependenciesResolvedInPriorPass()
    {
        var extensionsRoot = Path.Combine(_mindRoot, "extensions");
        WriteManifest(Path.Combine(extensionsRoot, "dep-b"), "ext.dep.b");
        WriteManifest(
            Path.Combine(extensionsRoot, "dep-a"),
            "ext.dep.a",
            dependencies: [new ManifestDependency("ext.dep.b", "1.0.0")]);

        var sut = (ExtensionManager)CreateSut();
        var discovered = InvokePrivate(sut, "DiscoverExternalManifests");
        var ordered = (IEnumerable)InvokePrivate(
            sut,
            "ResolveExternalLoadOrder",
            discovered,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var ids = ordered.Cast<object>().Select(GetId).ToArray();

        Assert.Equal(["ext.dep.b", "ext.dep.a"], ids);
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

    private static object InvokePrivate(object target, string methodName, params object[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method '{methodName}' not found.");
        return method.Invoke(target, args)
            ?? throw new InvalidOperationException($"Method '{methodName}' returned null.");
    }

    private void WriteManifest(string extensionDir, string id, IReadOnlyList<ManifestDependency>? dependencies = null)
    {
        Directory.CreateDirectory(extensionDir);
        var payload = new
        {
            id,
            name = id,
            version = "1.0.0",
            entryAssembly = typeof(ExtensionManagerTests).Assembly.Location,
            entryType = "Test.Extension",
            dependencies = (dependencies ?? []).Select(d => new { id = d.Id, versionRange = d.VersionRange }).ToArray()
        };

        var json = JsonSerializer.Serialize(payload);
        File.WriteAllText(Path.Combine(extensionDir, "plugin.json"), json);
    }

    private static string GetId(object descriptor)
    {
        return (string)(descriptor.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(descriptor)
            ?? throw new InvalidOperationException("Descriptor Id not found."));
    }

    private sealed record ManifestDependency(string Id, string VersionRange);

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
