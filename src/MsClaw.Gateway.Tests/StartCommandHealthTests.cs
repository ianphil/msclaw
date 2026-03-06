using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
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
        Assert.Contains("/v1/responses", routePatterns, StringComparer.Ordinal);
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

    [Fact]
    public async Task ConfigurePipeline_IncludesDefaultFilesAndStaticFiles()
    {
        var webRootPath = CreateWebRoot(new Dictionary<string, string>
        {
            ["index.html"] = "<html><body>chat</body></html>",
            ["css\\site.css"] = "body { color: red; }"
        });

        try
        {
            using var services = new ServiceCollection()
                .AddLogging()
                .AddRouting()
                .AddSingleton<IWebHostEnvironment>(new StubWebHostEnvironment(webRootPath))
                .BuildServiceProvider();
            var builder = new ApplicationBuilder(services);

            StartCommand.ConfigurePipeline(builder);
            builder.Run(static context =>
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;

                return Task.CompletedTask;
            });

            var application = builder.Build();
            var rootResponse = await ExecuteRequestAsync(application, "/");
            var stylesheetResponse = await ExecuteRequestAsync(application, "/css/site.css");

            Assert.Equal(StatusCodes.Status200OK, rootResponse.StatusCode);
            Assert.Contains("chat", rootResponse.Body, StringComparison.Ordinal);
            Assert.Equal(StatusCodes.Status200OK, stylesheetResponse.StatusCode);
            Assert.Contains("color: red", stylesheetResponse.Body, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(webRootPath, recursive: true);
        }
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

    private static async Task<(int StatusCode, string Body)> ExecuteRequestAsync(RequestDelegate application, string path)
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(new MemoryStream()));
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        await application(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        return (context.Response.StatusCode, body);
    }

    private static string CreateWebRoot(IReadOnlyDictionary<string, string> files)
    {
        var webRootPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(webRootPath);

        foreach (var file in files)
        {
            var filePath = Path.Combine(webRootPath, file.Key);
            var directoryPath = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directoryPath) is false)
            {
                Directory.CreateDirectory(directoryPath);
            }

            File.WriteAllText(filePath, file.Value);
        }

        return webRootPath;
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

    private sealed class StubWebHostEnvironment(string webRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "MsClaw.Gateway.Tests";

        public IFileProvider WebRootFileProvider { get; set; } = new PhysicalFileProvider(webRootPath);

        public string WebRootPath { get; set; } = webRootPath;

        public string EnvironmentName { get; set; } = Environments.Development;

        public string ContentRootPath { get; set; } = webRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(webRootPath);
    }
}
