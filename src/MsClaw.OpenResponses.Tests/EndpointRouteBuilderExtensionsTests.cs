using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MsClaw.OpenResponses;
using MsClaw.OpenResponses.Models;
using System.Runtime.CompilerServices;
using Xunit;

namespace MsClaw.OpenResponses.Tests;

public class EndpointRouteBuilderExtensionsTests
{
    [Fact]
    public void MapOpenResponses_RegistersResponsesEndpoint()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IOpenResponseService, StubOpenResponseService>();
        var app = builder.Build();

        app.MapOpenResponses();

        var routePatterns = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(static endpoint => endpoint.RoutePattern.RawText)
            .ToArray();

        Assert.Contains("/v1/responses", routePatterns, StringComparer.Ordinal);
    }

    private sealed class StubOpenResponseService : IOpenResponseService
    {
        public async IAsyncEnumerable<GitHub.Copilot.SDK.SessionEvent> SendAsync(
            HttpContext httpContext,
            ResponseRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield break;
        }
    }
}
