using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GitHub.Copilot.SDK;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace MsClaw.Gateway.Tests;

[Collection("Gateway Integration")]
public class GatewayIntegrationTests : IAsyncLifetime
{
    private readonly GatewayIntegrationFixture fixture;
    private HubConnection? hubConnection;

    public GatewayIntegrationTests(GatewayIntegrationFixture fixture)
    {
        this.fixture = fixture;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (hubConnection is not null)
        {
            await hubConnection.DisposeAsync();
        }
    }

    // ───────────────────────────────────────
    //  T079: Hub Streaming
    // ───────────────────────────────────────

    [Fact]
    public async Task SendMessage_ViaHub_StreamsEventsToClient()
    {
        hubConnection = fixture.CreateHubConnection();
        await hubConnection.StartAsync();

        var events = new List<SessionEvent>();
        await foreach (var sessionEvent in hubConnection.StreamAsync<SessionEvent>("SendMessage", "hello", CancellationToken.None))
        {
            events.Add(sessionEvent);
        }

        Assert.NotEmpty(events);
        Assert.Contains(events, static e => e is AssistantMessageDeltaEvent);
        Assert.Contains(events, static e => e is AssistantMessageEvent);
        Assert.Contains(events, static e => e is SessionIdleEvent);
    }

    // ───────────────────────────────────────
    //  T081: Concurrency Rejection
    // ───────────────────────────────────────

    [Fact]
    public async Task PostResponses_ConcurrentSameCaller_ReturnsConflict()
    {
        var callerKey = $"concurrent-user-{Guid.NewGuid():N}";

        fixture.GatewayClient.SessionFactory = id =>
            StubIntegrationGatewaySession.CreateDelayed(id, TimeSpan.FromSeconds(3));

        using var httpClient = fixture.CreateHttpClient();

        var request1 = CreateOpenResponseRequest("hello", callerKey);
        var request2 = CreateOpenResponseRequest("world", callerKey);

        var task1 = httpClient.SendAsync(request1);
        await Task.Delay(100);
        var task2 = httpClient.SendAsync(request2);

        var response2 = await task2;

        Assert.Equal(HttpStatusCode.Conflict, response2.StatusCode);
        var body = await response2.Content.ReadAsStringAsync();
        Assert.Contains("conflict", body, StringComparison.OrdinalIgnoreCase);

        await task1;

        fixture.GatewayClient.SessionFactory = null;
    }

    // ───────────────────────────────────────
    //  T082: OpenResponses JSON (non-streaming)
    // ───────────────────────────────────────

    [Fact]
    public async Task PostResponses_NonStreaming_ReturnsResponseObject()
    {
        using var httpClient = fixture.CreateHttpClient();
        var request = CreateOpenResponseRequest("What is 2+2?", user: $"json-user-{Guid.NewGuid():N}");

        var response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("response", json.GetProperty("object").GetString());
        Assert.Equal("completed", json.GetProperty("status").GetString());

        var output = json.GetProperty("output");
        Assert.True(output.GetArrayLength() > 0);

        var firstItem = output[0];
        Assert.Equal("message", firstItem.GetProperty("type").GetString());
        Assert.Equal("assistant", firstItem.GetProperty("role").GetString());

        var content = firstItem.GetProperty("content");
        Assert.True(content.GetArrayLength() > 0);
        Assert.Equal("output_text", content[0].GetProperty("type").GetString());
        Assert.Contains("integration test", content[0].GetProperty("text").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    // ───────────────────────────────────────
    //  T083: OpenResponses SSE (streaming)
    // ───────────────────────────────────────

    [Fact]
    public async Task PostResponses_Streaming_ReturnsSseEvents()
    {
        using var httpClient = fixture.CreateHttpClient();

        var body = JsonSerializer.Serialize(new { model = "test-model", input = "tell me a story", stream = true, user = $"sse-user-{Guid.NewGuid():N}" });
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var sseText = await response.Content.ReadAsStringAsync();
        Assert.Contains("event: response.created", sseText, StringComparison.Ordinal);
        Assert.Contains("event: response.output_text.delta", sseText, StringComparison.Ordinal);
        Assert.Contains("event: response.output_text.done", sseText, StringComparison.Ordinal);
        Assert.Contains("event: response.completed", sseText, StringComparison.Ordinal);
        Assert.Contains("data: [DONE]", sseText, StringComparison.Ordinal);
    }

    // ───────────────────────────────────────
    //  T084: Health Probes
    // ───────────────────────────────────────

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        using var httpClient = fixture.CreateHttpClient();

        var response = await httpClient.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task HealthReady_WhenReady_ReturnsHealthy()
    {
        fixture.HostedService.State = GatewayState.Ready;
        using var httpClient = fixture.CreateHttpClient();

        var response = await httpClient.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task HealthReady_WhenNotReady_ReturnsServiceUnavailable()
    {
        var previousState = fixture.HostedService.State;
        var previousError = fixture.HostedService.Error;
        try
        {
            fixture.HostedService.State = GatewayState.Failed;
            fixture.HostedService.Error = "CopilotClient not started";
            using var httpClient = fixture.CreateHttpClient();

            var response = await httpClient.GetAsync("/health/ready");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Unhealthy", json.GetProperty("status").GetString());
            Assert.Equal("hosted-service", json.GetProperty("component").GetString());
        }
        finally
        {
            fixture.HostedService.State = previousState;
            fixture.HostedService.Error = previousError;
        }
    }

    [Fact]
    public async Task TunnelStatus_ReturnsStatusPayload()
    {
        using var httpClient = fixture.CreateHttpClient();

        var response = await httpClient.GetAsync("/api/tunnel/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("enabled").GetBoolean());
        Assert.False(json.GetProperty("running").GetBoolean());
    }

    private static HttpRequestMessage CreateOpenResponseRequest(string prompt, string? user = null)
    {
        var body = JsonSerializer.Serialize(new { model = "test-model", input = prompt, stream = false, user });

        return new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
