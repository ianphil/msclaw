using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;

namespace MsClaw.Core;

public interface IExtension : IAsyncDisposable
{
    void Register(IMsClawPluginApi api);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public abstract class ExtensionBase : IExtension
{
    public abstract void Register(IMsClawPluginApi api);

    public virtual Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public virtual Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public interface IMsClawPluginApi
{
    string ExtensionId { get; }
    JsonElement? Config { get; }

    void RegisterTool(AIFunction tool);
    void RegisterHook(string eventName, ExtensionHookHandler handler);
    void RegisterService(IHostedService service);
    void RegisterCommand(string command, ExtensionCommandHandler handler);
    void RegisterHttpRoute(Action<IEndpointRouteBuilder> mapRoute);
}

public delegate Task ExtensionHookHandler(ExtensionHookContext context, CancellationToken cancellationToken);

public delegate Task<string> ExtensionCommandHandler(ExtensionCommandContext context, CancellationToken cancellationToken);

public sealed class ExtensionHookContext
{
    public required string EventName { get; init; }
    public string? SessionId { get; init; }
    public string? Message { get; init; }
    public string? Response { get; init; }
}

public sealed class ExtensionCommandContext
{
    public required string Command { get; init; }
    public string Arguments { get; init; } = "";
    public required string RawInput { get; init; }
    public string? SessionId { get; init; }
}

public static class ExtensionEvents
{
    public const string SessionCreate = "session:create";
    public const string SessionResume = "session:resume";
    public const string SessionEnd = "session:end";
    public const string MessageReceived = "message:received";
    public const string MessageSent = "message:sent";
    public const string AgentBootstrap = "agent:bootstrap";
    public const string ExtensionLoaded = "extension:loaded";
}

public enum ExtensionTier
{
    Core,
    External
}

public sealed class LoadedExtensionInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required ExtensionTier Tier { get; init; }
    public bool Started { get; init; }
    public bool Failed { get; init; }
}

public sealed class PluginManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("entryAssembly")]
    public string EntryAssembly { get; set; } = "";

    [JsonPropertyName("entryType")]
    public string EntryType { get; set; } = "";

    [JsonPropertyName("dependencies")]
    public List<PluginDependency> Dependencies { get; set; } = [];

    [JsonPropertyName("config")]
    public JsonElement? Config { get; set; }
}

public sealed class PluginDependency
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("versionRange")]
    public string? VersionRange { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    public string Range => !string.IsNullOrWhiteSpace(VersionRange)
        ? VersionRange!
        : string.IsNullOrWhiteSpace(Version) ? "*" : Version!;
}

public interface IExtensionManager
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
    Task ReloadExternalAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<AIFunction> GetTools();
    IReadOnlyList<LoadedExtensionInfo> GetLoadedExtensions();
    void MapRoutes(IEndpointRouteBuilder endpointRouteBuilder);

    Task FireHookAsync(string eventName, ExtensionHookContext context, CancellationToken cancellationToken = default);
    Task<string?> TryExecuteCommandAsync(string input, string? sessionId, CancellationToken cancellationToken = default);
}
