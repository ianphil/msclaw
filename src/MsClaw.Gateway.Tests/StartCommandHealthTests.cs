using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MsClaw.Gateway.Commands;
using MsClaw.Gateway.Hosting;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class StartCommandHealthTests
{
    [Fact]
    public async Task BuildHealthResult_Ready_ReturnsHealthy200()
    {
        var hostedService = new StubGatewayHostedService
        {
            State = GatewayState.Ready,
            IsReady = true
        };

        var result = StartCommand.BuildHealthResult(hostedService);
        var (statusCode, body) = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.Contains("Healthy", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildHealthResult_NotReady_ReturnsUnhealthy503()
    {
        var hostedService = new StubGatewayHostedService
        {
            State = GatewayState.Failed,
            IsReady = false,
            Error = "Validation failed"
        };

        var result = StartCommand.BuildHealthResult(hostedService);
        var (statusCode, body) = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, statusCode);
        Assert.Contains("Unhealthy", body, StringComparison.Ordinal);
    }

    private static async Task<(int StatusCode, string Body)> ExecuteResultAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        using var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        context.Response.Body = new MemoryStream();
        context.RequestServices = services;

        await result.ExecuteAsync(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        return (context.Response.StatusCode, body);
    }

    private sealed class StubGatewayHostedService : IGatewayHostedService
    {
        public string? SystemMessage { get; set; }

        public GatewayState State { get; set; }
        public string? Error { get; set; }
        public bool IsReady { get; set; }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
