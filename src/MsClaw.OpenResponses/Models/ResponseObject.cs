using System.Text.Json.Serialization;

namespace MsClaw.OpenResponses.Models;

/// <summary>
/// Represents an OpenResponses response payload.
/// </summary>
public sealed record ResponseObject
{
    /// <summary>
    /// Gets the OpenResponses object discriminator.
    /// </summary>
    [JsonPropertyName("object")]
    public string Object { get; init; } = "response";

    /// <summary>
    /// Gets the unique response identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Gets the response lifecycle status.
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// Gets the assistant output items.
    /// </summary>
    [JsonPropertyName("output")]
    public required IReadOnlyList<OutputItem> Output { get; init; }

    /// <summary>
    /// Creates a completed response with a single assistant text message.
    /// </summary>
    public static ResponseObject CreateCompleted(string id, string text)
    {
        return new ResponseObject
        {
            Id = id,
            Status = "completed",
            Output =
            [
                OutputItem.CreateText(text)
            ]
        };
    }

    /// <summary>
    /// Creates an in-progress response shell for SSE startup events.
    /// </summary>
    public static ResponseObject CreateInProgress(string id)
    {
        return new ResponseObject
        {
            Id = id,
            Status = "in_progress",
            Output = []
        };
    }
}

/// <summary>
/// Represents a single assistant output item.
/// </summary>
public sealed record OutputItem
{
    /// <summary>
    /// Gets the OpenResponses item type.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "message";

    /// <summary>
    /// Gets the assistant role.
    /// </summary>
    [JsonPropertyName("role")]
    public string Role { get; init; } = "assistant";

    /// <summary>
    /// Gets the content parts emitted by the assistant.
    /// </summary>
    [JsonPropertyName("content")]
    public required IReadOnlyList<ContentPart> Content { get; init; }

    /// <summary>
    /// Creates an assistant message with a single text part.
    /// </summary>
    public static OutputItem CreateText(string text)
    {
        return new OutputItem
        {
            Content =
            [
                new ContentPart
                {
                    Text = text
                }
            ]
        };
    }
}

/// <summary>
/// Represents a text content part inside an output item.
/// </summary>
public sealed record ContentPart
{
    /// <summary>
    /// Gets the OpenResponses content part type.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "output_text";

    /// <summary>
    /// Gets the output text content.
    /// </summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

/// <summary>
/// Represents an OpenResponses error envelope.
/// </summary>
public sealed record ErrorResponse
{
    /// <summary>
    /// Gets the error detail.
    /// </summary>
    [JsonPropertyName("error")]
    public required ErrorDetail Error { get; init; }

    /// <summary>
    /// Creates a new error response envelope.
    /// </summary>
    public static ErrorResponse Create(string code, string message, string requestId)
    {
        return new ErrorResponse
        {
            Error = new ErrorDetail
            {
                Code = code,
                Message = message,
                RequestId = requestId
            }
        };
    }
}

/// <summary>
/// Represents the error detail returned to OpenResponses clients.
/// </summary>
public sealed record ErrorDetail
{
    /// <summary>
    /// Gets the machine-readable error code.
    /// </summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    /// <summary>
    /// Gets the human-readable error message.
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>
    /// Gets the request identifier for correlation.
    /// </summary>
    [JsonPropertyName("request_id")]
    public required string RequestId { get; init; }
}
