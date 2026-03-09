using Microsoft.Extensions.AI;
using MsClaw.Gateway.Services.Tools;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class ToolBridgeTests
{
    [Fact]
    public async Task RegisterProviderAsync_DiscoveredTools_AreAddedToTheStore()
    {
        var store = new ToolCatalogStore();
        var sut = new ToolBridge(store);
        var provider = new StubToolProvider(
            "provider-a",
            ToolSourceTier.Bundled,
            [
                CreateDescriptor("tool_a", "Tool A", "provider-a", ToolSourceTier.Bundled),
                CreateDescriptor("tool_b", "Tool B", "provider-a", ToolSourceTier.Bundled)
            ]);

        await sut.RegisterProviderAsync(provider, CancellationToken.None);

        Assert.Collection(
            store.GetAll(),
            descriptor => Assert.Equal("tool_a", descriptor.Function.Name),
            descriptor => Assert.Equal("tool_b", descriptor.Function.Name));
    }

    [Fact]
    public async Task UnregisterProviderAsync_RegisteredProvider_RemovesToolsAndDisposesProvider()
    {
        var store = new ToolCatalogStore();
        var sut = new ToolBridge(store);
        var provider = new StubToolProvider(
            "provider-a",
            ToolSourceTier.Bundled,
            [CreateDescriptor("tool_a", "Tool A", "provider-a", ToolSourceTier.Bundled)]);
        await sut.RegisterProviderAsync(provider, CancellationToken.None);

        await sut.UnregisterProviderAsync("provider-a", CancellationToken.None);

        Assert.Empty(store.GetAll());
        Assert.True(provider.Disposed);
    }

    [Fact]
    public async Task RefreshProviderAsync_DiscoveryChanges_UpdatesToolSet()
    {
        var store = new ToolCatalogStore();
        var sut = new ToolBridge(store);
        var provider = new StubToolProvider(
            "provider-a",
            ToolSourceTier.Bundled,
            [CreateDescriptor("tool_a", "Tool A", "provider-a", ToolSourceTier.Bundled)]);
        await sut.RegisterProviderAsync(provider, CancellationToken.None);
        provider.Descriptors =
        [
            CreateDescriptor("tool_b", "Tool B", "provider-a", ToolSourceTier.Bundled)
        ];

        await sut.RefreshProviderAsync("provider-a", CancellationToken.None);

        Assert.Null(store.TryGet("tool_a"));
        Assert.NotNull(store.TryGet("tool_b"));
    }

    [Fact]
    public async Task RegisterProviderAsync_SameTierCollision_ThrowsInvalidOperationException()
    {
        var sut = new ToolBridge(new ToolCatalogStore());
        await sut.RegisterProviderAsync(
            new StubToolProvider(
                "provider-a",
                ToolSourceTier.Bundled,
                [CreateDescriptor("tool_a", "Tool A", "provider-a", ToolSourceTier.Bundled)]),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.RegisterProviderAsync(
                new StubToolProvider(
                    "provider-b",
                    ToolSourceTier.Bundled,
                    [CreateDescriptor("tool_a", "Tool A", "provider-b", ToolSourceTier.Bundled)]),
                CancellationToken.None));

        Assert.Contains("provider-a", exception.Message, StringComparison.Ordinal);
        Assert.Contains("provider-b", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisterProviderAsync_CrossTierCollision_KeepsHigherPriorityTool()
    {
        var store = new ToolCatalogStore();
        var sut = new ToolBridge(store);
        await sut.RegisterProviderAsync(
            new StubToolProvider(
                "workspace-provider",
                ToolSourceTier.Workspace,
                [CreateDescriptor("tool_a", "Workspace Tool", "workspace-provider", ToolSourceTier.Workspace)]),
            CancellationToken.None);

        await sut.RegisterProviderAsync(
            new StubToolProvider(
                "bundled-provider",
                ToolSourceTier.Bundled,
                [CreateDescriptor("tool_a", "Bundled Tool", "bundled-provider", ToolSourceTier.Bundled)]),
            CancellationToken.None);

        var descriptor = Assert.IsType<ToolDescriptor>(store.TryGet("tool_a"));

        Assert.Equal("bundled-provider", descriptor.ProviderName);
        Assert.Single(store.GetAll());
    }

    [Fact]
    public void GetDefaultTools_OnlyReturnsAlwaysVisibleReadyTools()
    {
        var store = new ToolCatalogStore();
        var visibleReady = CreateDescriptor("tool_a", "Tool A", "provider-a", ToolSourceTier.Bundled, alwaysVisible: true);
        var hiddenReady = CreateDescriptor("tool_b", "Tool B", "provider-a", ToolSourceTier.Bundled);
        var visibleDegraded = CreateDescriptor("tool_c", "Tool C", "provider-a", ToolSourceTier.Bundled, alwaysVisible: true);
        store.Add(visibleReady, ToolStatus.Ready);
        store.Add(hiddenReady, ToolStatus.Ready);
        store.Add(visibleDegraded, ToolStatus.Degraded);
        var sut = new ToolBridge(store);

        var result = sut.GetDefaultTools();

        Assert.Collection(result, function => Assert.Equal("tool_a", function.Name));
    }

    [Fact]
    public void GetToolsByName_MatchingNames_ReturnsReadyToolsAndSkipsUnknownNames()
    {
        var store = new ToolCatalogStore();
        store.Add(CreateDescriptor("tool_a", "Tool A", "provider-a", ToolSourceTier.Bundled), ToolStatus.Ready);
        store.Add(CreateDescriptor("tool_b", "Tool B", "provider-a", ToolSourceTier.Bundled), ToolStatus.Degraded);
        var sut = new ToolBridge(store);

        var result = sut.GetToolsByName(["tool_a", "missing", "tool_b"]);

        Assert.Collection(result, function => Assert.Equal("tool_a", function.Name));
    }

    [Fact]
    public void GetToolNamesByProvider_KnownProvider_ReturnsOwnedToolNames()
    {
        var store = new ToolCatalogStore();
        store.Add(CreateDescriptor("tool_a", "Tool A", "provider-a", ToolSourceTier.Bundled), ToolStatus.Ready);
        store.Add(CreateDescriptor("tool_b", "Tool B", "provider-a", ToolSourceTier.Bundled), ToolStatus.Ready);
        store.Add(CreateDescriptor("tool_c", "Tool C", "provider-b", ToolSourceTier.Bundled), ToolStatus.Ready);
        var sut = new ToolBridge(store);

        var result = sut.GetToolNamesByProvider("provider-a");

        Assert.Equal(["tool_a", "tool_b"], result);
    }

    [Fact]
    public void SearchTools_QueryKeywords_MatchesToolNamesAndDescriptionsCaseInsensitively()
    {
        var store = new ToolCatalogStore();
        store.Add(CreateDescriptor("teams_post_message", "Post a message to Teams channels", "provider-a", ToolSourceTier.Bundled), ToolStatus.Ready);
        store.Add(CreateDescriptor("calendar_lookup", "Read the calendar", "provider-a", ToolSourceTier.Bundled), ToolStatus.Ready);
        var sut = new ToolBridge(store);

        var result = sut.SearchTools("post message");

        Assert.Equal(["teams_post_message"], result);
    }

    [Fact]
    public void GetDescriptor_ExistingName_ReturnsDescriptorAndMissingNameReturnsNull()
    {
        var store = new ToolCatalogStore();
        var descriptor = CreateDescriptor("tool_a", "Tool A", "provider-a", ToolSourceTier.Bundled);
        store.Add(descriptor, ToolStatus.Ready);
        var sut = new ToolBridge(store);

        var found = sut.GetDescriptor("tool_a");
        var missing = sut.GetDescriptor("missing");

        Assert.Same(descriptor, found);
        Assert.Null(missing);
    }

    private static ToolDescriptor CreateDescriptor(string name, string description, string providerName, ToolSourceTier tier, bool alwaysVisible = false)
    {
        return new ToolDescriptor
        {
            Function = AIFunctionFactory.Create(
                (string input) => input,
                name,
                description),
            ProviderName = providerName,
            Tier = tier,
            AlwaysVisible = alwaysVisible
        };
    }

    private sealed class StubToolProvider(string name, ToolSourceTier tier, IReadOnlyList<ToolDescriptor> descriptors) : IToolProvider
    {
        public IReadOnlyList<ToolDescriptor> Descriptors { get; set; } = descriptors;

        public bool Disposed { get; private set; }

        public string Name { get; } = name;

        public ToolSourceTier Tier { get; } = tier;

        public Task<IReadOnlyList<ToolDescriptor>> DiscoverAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Descriptors);
        }

        public Task WaitForSurfaceChangeAsync(CancellationToken cancellationToken)
        {
            return Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;

            return ValueTask.CompletedTask;
        }
    }
}
