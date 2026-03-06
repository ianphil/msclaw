using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MsClaw.Gateway.Commands;
using MsClaw.Gateway.Hosting;
using Xunit;

namespace MsClaw.Gateway.Tests;

public class StartCommandHealthTests
{
    [Fact]
    public async Task BuildLivenessResult_NotReady_ReturnsHealthy200()
    {
        var hostedService = new StubGatewayHostedService
        {
            State = GatewayState.Failed,
            IsReady = false,
            Error = "Validation failed"
        };

        var result = StartCommand.BuildLivenessResult();
        var (statusCode, body) = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.Contains("Healthy", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildReadinessResult_Ready_ReturnsHealthy200()
    {
        var hostedService = new StubGatewayHostedService
        {
            State = GatewayState.Ready,
            IsReady = true
        };

        var result = StartCommand.BuildReadinessResult(hostedService);
        var (statusCode, body) = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.Contains("Healthy", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildReadinessResult_NotReady_ReturnsUnhealthy503WithHostedServiceComponent()
    {
        var hostedService = new StubGatewayHostedService
        {
            State = GatewayState.Failed,
            IsReady = false,
            Error = "Validation failed"
        };

        var result = StartCommand.BuildReadinessResult(hostedService);
        var (statusCode, body) = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, statusCode);
        Assert.Contains("Unhealthy", body, StringComparison.Ordinal);
        Assert.Contains("hosted-service", body, StringComparison.Ordinal);
        Assert.Contains("Validation failed", body, StringComparison.Ordinal);
    }

    [Fact]
    public void MapEndpoints_MapsHealthAndReadyEndpoints()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSignalR();
        var app = builder.Build();

        StartCommand.MapEndpoints(app);

        var routePatterns = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(static endpoint => endpoint.RoutePattern.RawText)
            .ToArray();

        Assert.Contains("/health", routePatterns, StringComparer.Ordinal);
        Assert.Contains("/health/ready", routePatterns, StringComparer.Ordinal);
    }

    [Fact]
    public void MapEndpoints_DoesNotMapLegacyHealthzEndpoint()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSignalR();
        var app = builder.Build();

        StartCommand.MapEndpoints(app);

        var routePatterns = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(static endpoint => endpoint.RoutePattern.RawText)
            .ToArray();

        Assert.DoesNotContain("/healthz", routePatterns, StringComparer.Ordinal);
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
