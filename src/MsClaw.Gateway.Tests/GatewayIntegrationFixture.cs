using System.Collections.Concurrent;
using GitHub.Copilot.SDK;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using MsClaw.Gateway.Commands;
using MsClaw.Gateway.Hosting;
using MsClaw.Gateway.Services;
using MsClaw.OpenResponses;
using Xunit;

namespace MsClaw.Gateway.Tests;

/// <summary>
/// Starts a real ASP.NET Core server with stub gateway services for integration testing.
/// </summary>
public sealed class GatewayIntegrationFixture : IAsyncLifetime
{
    private WebApplication? app;
    private string baseUrl = string.Empty;
    private int sessionCounter;

    public StubIntegrationHostedService HostedService { get; } = new();

    public StubIntegrationGatewayClient GatewayClient { get; } = new();

    public string BaseUrl => baseUrl;

    public HttpClient CreateHttpClient() => new() { BaseAddress = new Uri(baseUrl) };

    public HubConnection CreateHubConnection(string hubPath = "/gateway")
    {
        return new HubConnectionBuilder()
            .WithUrl($"{baseUrl}{hubPath}")
            .Build();
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();

        builder.Services.AddSignalR(options =>
        {
            options.MaximumParallelInvocationsPerClient = 2;
        });
        builder.Services.AddSingleton(new GatewayOptions
        {
            MindPath = Path.GetTempPath(),
            Host = "127.0.0.1",
            Port = 0
        });
        builder.Services.AddSingleton<CallerRegistry>();
        builder.Services.AddSingleton<IConcurrencyGate>(sp => sp.GetRequiredService<CallerRegistry>());
        builder.Services.AddSingleton<ISessionMap>(sp => sp.GetRequiredService<CallerRegistry>());
        builder.Services.AddSingleton<IGatewayClient>(GatewayClient);
        builder.Services.AddSingleton<IGatewayHostedService>(HostedService);
        builder.Services.AddSingleton<AgentMessageService>();
        builder.Services.AddSingleton<IOpenResponseService, GatewayOpenResponseService>();

        app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        StartCommand.MapEndpoints(app);
        await app.StartAsync();

        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()!;
        baseUrl = addresses.Addresses.First();
    }

    public async Task DisposeAsync()
    {
        if (app is not null)
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    public string NextSessionId()
    {
        return $"test-session-{Interlocked.Increment(ref sessionCounter)}";
    }
}

/// <summary>
/// Stub hosted service that reports a configurable gateway state.
/// </summary>
public sealed class StubIntegrationHostedService : IGatewayHostedService
{
    public string? SystemMessage { get; set; } = "You are a test agent.";

    public GatewayState State { get; set; } = GatewayState.Ready;

    public string? Error { get; set; }

    public bool IsReady => State is GatewayState.Ready;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Stub gateway client that returns configurable sessions for integration tests.
/// </summary>
public sealed class StubIntegrationGatewayClient : IGatewayClient
{
    private int sessionCounter;

    /// <summary>
    /// Factory called for each <see cref="CreateSessionAsync" /> invocation.
    /// Defaults to returning a session that emits a single assistant reply.
    /// </summary>
    public Func<string, StubIntegrationGatewaySession>? SessionFactory { get; set; }

    public IReadOnlyList<SessionMetadata> ListedSessions { get; set; } = [];

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IGatewaySession> CreateSessionAsync(SessionConfig? config = null, CancellationToken cancellationToken = default)
    {
        var id = $"integration-session-{Interlocked.Increment(ref sessionCounter)}";
        var session = SessionFactory?.Invoke(id) ?? StubIntegrationGatewaySession.CreateDefault(id);

        return Task.FromResult<IGatewaySession>(session);
    }

    public Task<IGatewaySession> ResumeSessionAsync(string sessionId, ResumeSessionConfig? config = null, CancellationToken cancellationToken = default)
    {
        var session = SessionFactory?.Invoke(sessionId) ?? StubIntegrationGatewaySession.CreateDefault(sessionId);

        return Task.FromResult<IGatewaySession>(session);
    }

    public Task<IReadOnlyList<SessionMetadata>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ListedSessions);
    }

    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Stub session that emits configurable events when <see cref="SendAsync" /> is called.
/// </summary>
public sealed class StubIntegrationGatewaySession(string sessionId) : IGatewaySession
{
    private readonly List<Action<SessionEvent>> handlers = [];

    /// <summary>
    /// Delay before emitting events. Used to test concurrency rejection.
    /// </summary>
    public TimeSpan SendDelay { get; init; }

    /// <summary>
    /// Text emitted as assistant response.
    /// </summary>
    public string AssistantResponse { get; init; } = "Hello from integration test!";

    public string SessionId { get; } = sessionId;

    public IDisposable On(Action<SessionEvent> handler)
    {
        handlers.Add(handler);

        return new Subscription(handlers, handler);
    }

    public async Task SendAsync(MessageOptions options, CancellationToken cancellationToken = default)
    {
        if (SendDelay > TimeSpan.Zero)
        {
            await Task.Delay(SendDelay, cancellationToken);
        }

        Emit(new AssistantMessageDeltaEvent
        {
            Data = new AssistantMessageDeltaData { MessageId = "msg-1", DeltaContent = AssistantResponse }
        });
        Emit(new AssistantMessageEvent
        {
            Data = new AssistantMessageData { MessageId = "msg-1", Content = AssistantResponse }
        });
        Emit(new SessionIdleEvent
        {
            Data = new SessionIdleData()
        });
    }

    public Task AbortAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<SessionEvent>> GetMessagesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<SessionEvent>>([]);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static StubIntegrationGatewaySession CreateDefault(string sessionId)
    {
        return new StubIntegrationGatewaySession(sessionId);
    }

    /// <summary>
    /// Creates a session with a configurable delay for concurrency testing.
    /// </summary>
    public static StubIntegrationGatewaySession CreateDelayed(string sessionId, TimeSpan delay)
    {
        return new StubIntegrationGatewaySession(sessionId) { SendDelay = delay };
    }

    private void Emit(SessionEvent sessionEvent)
    {
        foreach (var handler in handlers.ToArray())
        {
            handler(sessionEvent);
        }
    }

    private sealed class Subscription(List<Action<SessionEvent>> handlers, Action<SessionEvent> handler) : IDisposable
    {
        public void Dispose()
        {
            _ = handlers.Remove(handler);
        }
    }
}

[CollectionDefinition("Gateway Integration")]
public class GatewayIntegrationCollection : ICollectionFixture<GatewayIntegrationFixture>;
