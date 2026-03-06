using System.Text.Json;
using System.Text.Json.Serialization;

namespace MsClaw.OpenResponses.Models;

/// <summary>
/// Represents a POST /v1/responses request body.
/// </summary>
public sealed record ResponseRequest
{
    /// <summary>
    /// Gets the shared JSON serializer options for OpenResponses payloads.
    /// </summary>
    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Gets the requested model identifier.
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// Gets the raw input payload as either a string or message array.
    /// </summary>
    [JsonPropertyName("input")]
    public JsonElement Input { get; init; }

    /// <summary>
    /// Gets a value indicating whether the response should stream as SSE.
    /// </summary>
    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    /// <summary>
    /// Gets the optional stable caller key used for session routing.
    /// </summary>
    [JsonPropertyName("user")]
    public string? User { get; init; }

    /// <summary>
    /// Validates the request and returns a user-facing error when it is invalid.
    /// </summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Model))
        {
            return "The 'model' field is required.";
        }

        return TryGetPrompt(out _)
            ? null
            : "The 'input' field must be a non-empty string or non-empty array.";
    }

    /// <summary>
    /// Extracts the request input into a prompt string suitable for the gateway runtime.
    /// </summary>
    public bool TryGetPrompt(out string prompt)
    {
        var segments = ExtractTextSegments(Input)
            .Where(static segment => string.IsNullOrWhiteSpace(segment) is false)
            .ToArray();

        if (segments.Length is 0)
        {
            prompt = string.Empty;

            return false;
        }

        prompt = string.Join(Environment.NewLine, segments);

        return true;
    }

    /// <summary>
    /// Extracts the prompt or throws when the request has not been validated.
    /// </summary>
    public string GetRequiredPrompt()
    {
        if (TryGetPrompt(out var prompt))
        {
            return prompt;
        }

        throw new InvalidOperationException("The request input must be validated before retrieving the prompt.");
    }

    /// <summary>
    /// Extracts text content from the supported OpenResponses input shapes.
    /// </summary>
    private static IEnumerable<string> ExtractTextSegments(JsonElement input)
    {
        switch (input.ValueKind)
        {
            case JsonValueKind.String:
                yield return input.GetString() ?? string.Empty;
                yield break;

            case JsonValueKind.Array:
                foreach (var item in input.EnumerateArray())
                {
                    switch (item.ValueKind)
                    {
                        case JsonValueKind.String:
                            yield return item.GetString() ?? string.Empty;
                            break;

                        case JsonValueKind.Object:
                            foreach (var segment in ExtractObjectContent(item))
                            {
                                yield return segment;
                            }

                            break;
                    }
                }

                yield break;

            default:
                yield break;
        }
    }

    /// <summary>
    /// Extracts text content from a message object.
    /// </summary>
    private static IEnumerable<string> ExtractObjectContent(JsonElement item)
    {
        if (item.TryGetProperty("content", out var content) is false)
        {
            yield break;
        }

        switch (content.ValueKind)
        {
            case JsonValueKind.String:
                yield return content.GetString() ?? string.Empty;
                yield break;

            case JsonValueKind.Array:
                foreach (var contentPart in content.EnumerateArray())
                {
                    if (contentPart.ValueKind is JsonValueKind.String)
                    {
                        yield return contentPart.GetString() ?? string.Empty;
                        continue;
                    }

                    if (contentPart.ValueKind is JsonValueKind.Object &&
                        contentPart.TryGetProperty("text", out var textElement) &&
                        textElement.ValueKind is JsonValueKind.String)
                    {
                        yield return textElement.GetString() ?? string.Empty;
                    }
                }

                yield break;
        }
    }
}
