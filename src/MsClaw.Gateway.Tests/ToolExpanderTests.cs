using System.Text.Json;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;
using MsClaw.Gateway.Hosting;
using MsClaw.Gateway.Services.Tools;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class ToolExpanderTests
{
    [Fact]
    public void CreateExpandToolsFunction_ReturnsFunctionNamedExpandTools()
    {
        var sut = new ToolExpander(new StubToolCatalog(), new StubGatewayClient());

        var function = sut.CreateExpandToolsFunction(new SessionHolder(), []);

        Assert.Equal("expand_tools", function.Name);
    }

    [Fact]
    public async Task CreateExpandToolsFunction_NamesMode_AppendsToolsToSessionList()
    {
        var catalog = new StubToolCatalog();
        var tool = CreateFunction("tool_a", "Tool A");
        catalog.AddDescriptor(new ToolDescriptor
        {
            Function = tool,
            ProviderName = "provider-a",
            Tier = ToolSourceTier.Bundled
        });
        var gatewayClient = new StubGatewayClient();
        var sessionHolder = new SessionHolder();
        var currentTools = new List<AIFunction> { CreateFunction("base_tool", "Base Tool") };
        var sut = new ToolExpander(catalog, gatewayClient);
        var function = sut.CreateExpandToolsFunction(sessionHolder, currentTools);
        sessionHolder.Bind(new StubGatewaySession("session-1"));

        var result = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["names"] = new[] { "tool_a" } }),
            CancellationToken.None);

        Assert.Equal(["base_tool", "tool_a"], currentTools.Select(static toolEntry => toolEntry.Name));

        var json = Assert.IsType<JsonElement>(result);
        Assert.Equal(1, GetProperty(json, "Count").GetInt32());
        Assert.Equal("tool_a", GetProperty(json, "Enabled")[0].GetString());
        Assert.Equal(0, GetProperty(json, "Skipped").GetArrayLength());
    }

    [Fact]
    public async Task CreateExpandToolsFunction_ProviderName_LoadsAllProviderTools()
    {
        var catalog = new StubToolCatalog();
        catalog.AddDescriptor(new ToolDescriptor
        {
            Function = CreateFunction("tool_a", "Tool A"),
            ProviderName = "provider-a",
            Tier = ToolSourceTier.Bundled
        });
        catalog.AddDescriptor(new ToolDescriptor
        {
            Function = CreateFunction("tool_b", "Tool B"),
            ProviderName = "provider-a",
            Tier = ToolSourceTier.Bundled
        });
        var gatewayClient = new StubGatewayClient();
        var sessionHolder = new SessionHolder();
        var currentTools = new List<AIFunction>();
        var sut = new ToolExpander(catalog, gatewayClient);
        var function = sut.CreateExpandToolsFunction(sessionHolder, currentTools);
        sessionHolder.Bind(new StubGatewaySession("session-2"));

        var result = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["names"] = new[] { "provider-a" } }),
            CancellationToken.None);

        Assert.Equal(0, gatewayClient.ResumeSessionCallCount);
        Assert.Equal(["tool_a", "tool_b"], currentTools.Select(static tool => tool.Name));

        var json = Assert.IsType<JsonElement>(result);
        Assert.Equal(2, GetProperty(json, "Count").GetInt32());
    }

    [Fact]
    public async Task CreateExpandToolsFunction_QueryMode_ReturnsMatchesWithoutResumingSession()
    {
        var catalog = new StubToolCatalog
        {
            SearchResults = ["teams_post_message", "teams_pin_message"]
        };
        var gatewayClient = new StubGatewayClient();
        var currentTools = new List<AIFunction> { CreateFunction("base_tool", "Base Tool") };
        var sut = new ToolExpander(catalog, gatewayClient);
        var function = sut.CreateExpandToolsFunction(new SessionHolder(), currentTools);

        var result = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["query"] = "post message" }),
            CancellationToken.None);

        Assert.Equal(0, gatewayClient.ResumeSessionCallCount);
        Assert.Equal(["base_tool"], currentTools.Select(static tool => tool.Name));

        var json = Assert.IsType<JsonElement>(result);
        Assert.Equal(2, GetProperty(json, "Count").GetInt32());
        Assert.Equal("teams_post_message", GetProperty(json, "Matches")[0].GetString());
    }

    [Fact]
    public async Task CreateExpandToolsFunction_UnboundSession_StillAppendsTools()
    {
        var catalog = new StubToolCatalog();
        catalog.AddDescriptor(new ToolDescriptor
        {
            Function = CreateFunction("tool_a", "Tool A"),
            ProviderName = "provider-a",
            Tier = ToolSourceTier.Bundled
        });
        var gatewayClient = new StubGatewayClient();
        var currentTools = new List<AIFunction>();
        var sut = new ToolExpander(catalog, gatewayClient, TimeSpan.FromMilliseconds(25));
        var function = sut.CreateExpandToolsFunction(new SessionHolder(), currentTools);

        var result = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["names"] = new[] { "tool_a" } }),
            CancellationToken.None);

        Assert.Equal(["tool_a"], currentTools.Select(static tool => tool.Name));

        var json = Assert.IsType<JsonElement>(result);
        Assert.Equal(1, GetProperty(json, "Count").GetInt32());
    }

    [Fact]
    public async Task CreateExpandToolsFunction_UnknownToolName_ReturnsSkippedWithoutThrowing()
    {
        var gatewayClient = new StubGatewayClient();
        var sessionHolder = new SessionHolder();
        var sut = new ToolExpander(new StubToolCatalog(), gatewayClient);
        var function = sut.CreateExpandToolsFunction(sessionHolder, []);
        sessionHolder.Bind(new StubGatewaySession("session-3"));

        var result = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["names"] = new[] { "missing_tool" } }),
            CancellationToken.None);

        Assert.Equal(0, gatewayClient.ResumeSessionCallCount);

        var json = Assert.IsType<JsonElement>(result);
        Assert.Equal(0, GetProperty(json, "Count").GetInt32());
        Assert.Equal("missing_tool", GetProperty(json, "Skipped")[0].GetString());
    }

    private static AIFunction CreateFunction(string name, string description)
    {
        return AIFunctionFactory.Create(
            (string input) => input,
            name,
            description);
    }

    private static JsonElement GetProperty(JsonElement json, string propertyName)
    {
        if (json.TryGetProperty(propertyName, out var exactMatch))
        {
            return exactMatch;
        }

        var camelCaseName = char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
        if (json.TryGetProperty(camelCaseName, out var camelCaseMatch))
        {
            return camelCaseMatch;
        }

        throw new KeyNotFoundException($"Property '{propertyName}' was not found in {json.GetRawText()}.");
    }

    private sealed class StubToolCatalog : IToolCatalog
    {
        private readonly Dictionary<string, ToolDescriptor> descriptors = new(StringComparer.Ordinal);

        public IReadOnlyList<string> SearchResults { get; set; } = [];

        public void AddDescriptor(ToolDescriptor descriptor)
        {
            descriptors[descriptor.Function.Name] = descriptor;
        }

        public IReadOnlyList<AIFunction> GetDefaultTools()
        {
            return descriptors.Values
                .Where(static descriptor => descriptor.AlwaysVisible)
                .Select(static descriptor => descriptor.Function)
                .ToArray();
        }

        public IReadOnlyList<AIFunction> GetToolsByName(IEnumerable<string> names)
        {
            return names
                .Select(name => descriptors.TryGetValue(name, out var descriptor) ? descriptor.Function : null)
                .OfType<AIFunction>()
                .ToArray();
        }

        public IReadOnlyList<string> GetCatalogToolNames()
        {
            return descriptors.Keys.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        }

        public IReadOnlyList<string> GetToolNamesByProvider(string providerName)
        {
            return descriptors.Values
                .Where(descriptor => descriptor.ProviderName == providerName)
                .OrderBy(static descriptor => descriptor.Function.Name, StringComparer.Ordinal)
                .Select(static descriptor => descriptor.Function.Name)
                .ToArray();
        }

        public IReadOnlyList<string> SearchTools(string query)
        {
            return SearchResults;
        }

        public ToolDescriptor? GetDescriptor(string toolName)
        {
            return descriptors.TryGetValue(toolName, out var descriptor) ? descriptor : null;
        }
    }

    private sealed class StubGatewayClient : IGatewayClient
    {
        public int ResumeSessionCallCount { get; private set; }

        public string? LastResumedSessionId { get; private set; }

        public ResumeSessionConfig? LastResumeSessionConfig { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IGatewaySession> CreateSessionAsync(SessionConfig? config = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IGatewaySession>(new StubGatewaySession("created"));
        }

        public Task<IGatewaySession> ResumeSessionAsync(string sessionId, ResumeSessionConfig? config = null, CancellationToken cancellationToken = default)
        {
            ResumeSessionCallCount++;
            LastResumedSessionId = sessionId;
            LastResumeSessionConfig = config;

            return Task.FromResult<IGatewaySession>(new StubGatewaySession(sessionId));
        }

        public Task<IReadOnlyList<SessionMetadata>> ListSessionsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SessionMetadata>>([]);
        }

        public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubGatewaySession(string sessionId) : IGatewaySession
    {
        public string SessionId { get; } = sessionId;

        public IDisposable On(Action<SessionEvent> handler)
        {
            return new StubDisposable();
        }

        public Task SendAsync(MessageOptions options, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task AbortAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SessionEvent>> GetMessagesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SessionEvent>>([]);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
