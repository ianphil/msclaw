using System.Text.Json;
using GitHub.Copilot.SDK;
using Microsoft.AspNetCore.Http;
using MsClaw.OpenResponses.Infrastructure;
using MsClaw.OpenResponses.Models;

namespace MsClaw.OpenResponses;

/// <summary>
/// Handles OpenResponses HTTP requests and translates gateway events into JSON or SSE output.
/// </summary>
public static class OpenResponsesMiddleware
{
    /// <summary>
    /// Handles POST /v1/responses requests using the registered OpenResponses service.
    /// </summary>
    public static async Task HandleAsync(
        HttpContext httpContext,
        IOpenResponseService openResponseService,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(openResponseService);

        var requestId = GetRequestId(httpContext);
        var request = await DeserializeRequestAsync(httpContext, requestId, cancellationToken);
        if (request is null)
        {
            return;
        }

        var validationError = request.Validate();
        if (validationError is not null)
        {
            await WriteJsonAsync(
                httpContext,
                StatusCodes.Status400BadRequest,
                ErrorResponse.Create("invalid_request", validationError, requestId),
                cancellationToken);

            return;
        }

        try
        {
            if (request.Stream)
            {
                await WriteStreamingResponseAsync(httpContext, openResponseService, request, requestId, cancellationToken);

                return;
            }

            await WriteNonStreamingResponseAsync(httpContext, openResponseService, request, requestId, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already has an active run", StringComparison.Ordinal))
        {
            await WriteJsonAsync(
                httpContext,
                StatusCodes.Status409Conflict,
                ErrorResponse.Create("conflict", ex.Message, requestId),
                cancellationToken);
        }
    }

    /// <summary>
    /// Deserializes the incoming request body and writes a 400 response for malformed JSON.
    /// </summary>
    private static async Task<ResponseRequest?> DeserializeRequestAsync(
        HttpContext httpContext,
        string requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<ResponseRequest>(
                httpContext.Request.Body,
                ResponseRequest.SerializerOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            await WriteJsonAsync(
                httpContext,
                StatusCodes.Status400BadRequest,
                ErrorResponse.Create("invalid_request", "The request body was not valid JSON.", requestId),
                cancellationToken);

            return null;
        }
    }

    /// <summary>
    /// Writes the full response body for non-streaming requests.
    /// </summary>
    private static async Task WriteNonStreamingResponseAsync(
        HttpContext httpContext,
        IOpenResponseService openResponseService,
        ResponseRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var responseId = CreateResponseId();
        string assistantText = string.Empty;

        await foreach (var sessionEvent in openResponseService.SendAsync(httpContext, request, cancellationToken).WithCancellation(cancellationToken))
        {
            switch (sessionEvent)
            {
                case AssistantMessageEvent assistantMessageEvent:
                    assistantText = assistantMessageEvent.Data.Content ?? string.Empty;
                    break;
                case SessionErrorEvent sessionErrorEvent:
                    await WriteJsonAsync(
                        httpContext,
                        StatusCodes.Status500InternalServerError,
                        ErrorResponse.Create(
                            "runtime_error",
                            sessionErrorEvent.Data.Message ?? "The response failed.",
                            requestId),
                        cancellationToken);

                    return;
            }
        }

        await WriteJsonAsync(
            httpContext,
            StatusCodes.Status200OK,
            ResponseObject.CreateCompleted(responseId, assistantText),
            cancellationToken);
    }

    /// <summary>
    /// Writes an SSE stream for streaming requests.
    /// </summary>
    private static async Task WriteStreamingResponseAsync(
        HttpContext httpContext,
        IOpenResponseService openResponseService,
        ResponseRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var responseId = CreateResponseId();
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";

        await WriteSseFrameAsync(httpContext.Response, OpenResponseEventMapper.CreateResponseCreatedFrame(responseId), cancellationToken);
        await foreach (var sessionEvent in openResponseService.SendAsync(httpContext, request, cancellationToken).WithCancellation(cancellationToken))
        {
            foreach (var frame in OpenResponseEventMapper.Map(responseId, sessionEvent, requestId))
            {
                await WriteSseFrameAsync(httpContext.Response, frame, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Writes a JSON response payload with the shared serializer settings.
    /// </summary>
    private static async Task WriteJsonAsync(
        HttpContext httpContext,
        int statusCode,
        object payload,
        CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(httpContext.Response.Body, payload, ResponseRequest.SerializerOptions, cancellationToken);
    }

    /// <summary>
    /// Writes a single SSE frame to the response stream and flushes immediately.
    /// </summary>
    private static async Task WriteSseFrameAsync(
        HttpResponse response,
        OpenResponseSseFrame frame,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(frame.EventName) is false)
        {
            await response.WriteAsync($"event: {frame.EventName}\n", cancellationToken);
        }

        await response.WriteAsync($"data: {frame.Data}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Returns the request identifier used in error payloads.
    /// </summary>
    private static string GetRequestId(HttpContext httpContext)
    {
        return string.IsNullOrWhiteSpace(httpContext.TraceIdentifier)
            ? CreateRequestId()
            : httpContext.TraceIdentifier;
    }

    /// <summary>
    /// Creates a unique OpenResponses response identifier.
    /// </summary>
    private static string CreateResponseId()
    {
        return $"resp_{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Creates a fallback request identifier when ASP.NET has not assigned one yet.
    /// </summary>
    private static string CreateRequestId()
    {
        return $"req_{Guid.NewGuid():N}";
    }
}
