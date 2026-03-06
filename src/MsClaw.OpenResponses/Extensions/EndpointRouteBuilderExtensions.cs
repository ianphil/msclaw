using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace MsClaw.OpenResponses;

/// <summary>
/// Registers the OpenResponses HTTP surface on an endpoint route builder.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps POST /v1/responses to the OpenResponses request handler.
    /// </summary>
    public static IEndpointConventionBuilder MapOpenResponses(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapPost("/v1/responses", OpenResponsesMiddleware.HandleAsync);
    }
}
