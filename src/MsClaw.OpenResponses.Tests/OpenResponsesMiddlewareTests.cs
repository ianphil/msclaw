using System.Text;
using System.Runtime.CompilerServices;
using GitHub.Copilot.SDK;
using Microsoft.AspNetCore.Http;
using MsClaw.OpenResponses;
using MsClaw.OpenResponses.Models;
using Xunit;

namespace MsClaw.OpenResponses.Tests;

public class OpenResponsesMiddlewareTests
{
    [Fact]
    public async Task HandleAsync_StreamFalse_ReturnsCompleteResponseObject()
    {
        var context = CreateHttpContext(
            """
            {
              "model": "gpt-5",
              "input": "hello"
            }
            """);
        var service = new StubOpenResponseService(
        [
            new AssistantMessageEvent
            {
                Data = new AssistantMessageData
                {
                    MessageId = "message-1",
                    Content = "hello back"
                }
            },
            new SessionIdleEvent
            {
                Data = new SessionIdleData()
            }
        ]);

        await OpenResponsesMiddleware.HandleAsync(context, service, CancellationToken.None);

        var body = await ReadBodyAsync(context);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Contains("\"object\":\"response\"", body, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"completed\"", body, StringComparison.Ordinal);
        Assert.Contains("\"text\":\"hello back\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_StreamTrue_WritesSseEventStream()
    {
        var context = CreateHttpContext(
            """
            {
              "model": "gpt-5",
              "input": "hello",
              "stream": true
            }
            """);
        var service = new StubOpenResponseService(
        [
            new AssistantMessageDeltaEvent
            {
                Data = new AssistantMessageDeltaData
                {
                    MessageId = "message-1",
                    DeltaContent = "hel"
                }
            },
            new AssistantMessageEvent
            {
                Data = new AssistantMessageData
                {
                    MessageId = "message-1",
                    Content = "hello"
                }
            },
            new SessionIdleEvent
            {
                Data = new SessionIdleData()
            }
        ]);

        await OpenResponsesMiddleware.HandleAsync(context, service, CancellationToken.None);

        var body = await ReadBodyAsync(context);
        Assert.Equal("text/event-stream", context.Response.ContentType);
        Assert.Contains("event: response.created", body, StringComparison.Ordinal);
        Assert.Contains("event: response.output_text.delta", body, StringComparison.Ordinal);
        Assert.Contains("event: response.completed", body, StringComparison.Ordinal);
        Assert.Contains("data: [DONE]", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_ConcurrentCaller_ReturnsConflictError()
    {
        var context = CreateHttpContext(
            """
            {
              "model": "gpt-5",
              "input": "hello"
            }
            """);
        var service = new StubOpenResponseService(
            [],
            "Caller 'caller-1' already has an active run.");

        await OpenResponsesMiddleware.HandleAsync(context, service, CancellationToken.None);

        var body = await ReadBodyAsync(context);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Contains("\"code\":\"conflict\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_MalformedRequest_ReturnsBadRequest()
    {
        var context = CreateHttpContext("{ not-json");
        var service = new StubOpenResponseService([]);

        await OpenResponsesMiddleware.HandleAsync(context, service, CancellationToken.None);

        var body = await ReadBodyAsync(context);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Contains("\"code\":\"invalid_request\"", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates an HTTP context with the specified JSON request body.
    /// </summary>
    private static DefaultHttpContext CreateHttpContext(string requestBody)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));
        context.Request.ContentType = "application/json";
        context.Response.Body = new MemoryStream();
        context.TraceIdentifier = "req_123";

        return context;
    }

    /// <summary>
    /// Reads the full response body content as a string.
    /// </summary>
    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);

        return await reader.ReadToEndAsync();
    }

    private sealed class StubOpenResponseService(
        IReadOnlyList<SessionEvent> sessionEvents,
        string? invalidOperationMessage = null) : IOpenResponseService
    {
        public async IAsyncEnumerable<SessionEvent> SendAsync(
            HttpContext httpContext,
            ResponseRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(invalidOperationMessage) is false)
            {
                throw new InvalidOperationException(invalidOperationMessage);
            }

            foreach (var sessionEvent in sessionEvents)
            {
                yield return sessionEvent;
                await Task.Yield();
            }
        }
    }
}
