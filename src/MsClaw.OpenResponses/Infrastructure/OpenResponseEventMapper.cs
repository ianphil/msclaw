using System.Text.Json;
using GitHub.Copilot.SDK;
using MsClaw.OpenResponses.Models;

namespace MsClaw.OpenResponses.Infrastructure;

/// <summary>
/// Maps Copilot SDK session events into OpenResponses SSE frames.
/// </summary>
internal static class OpenResponseEventMapper
{
    /// <summary>
    /// Creates the initial response.created frame for a new stream.
    /// </summary>
    public static OpenResponseSseFrame CreateResponseCreatedFrame(string responseId)
    {
        return new OpenResponseSseFrame(
            "response.created",
            JsonSerializer.Serialize(ResponseObject.CreateInProgress(responseId), ResponseRequest.SerializerOptions));
    }

    /// <summary>
    /// Maps a single SDK session event into one or more SSE frames.
    /// </summary>
    public static IReadOnlyList<OpenResponseSseFrame> Map(string responseId, SessionEvent sessionEvent, string requestId = "")
    {
        return sessionEvent switch
        {
            AssistantMessageDeltaEvent assistantMessageDeltaEvent =>
            [
                new OpenResponseSseFrame(
                    "response.output_text.delta",
                    JsonSerializer.Serialize(
                        new OutputTextDelta(assistantMessageDeltaEvent.Data.DeltaContent ?? string.Empty),
                        ResponseRequest.SerializerOptions))
            ],
            AssistantMessageEvent assistantMessageEvent =>
            [
                new OpenResponseSseFrame(
                    "response.output_text.done",
                    JsonSerializer.Serialize(
                        new OutputTextDone(assistantMessageEvent.Data.Content ?? string.Empty),
                        ResponseRequest.SerializerOptions)),
                new OpenResponseSseFrame(
                    "response.completed",
                    JsonSerializer.Serialize(
                        ResponseObject.CreateCompleted(responseId, assistantMessageEvent.Data.Content ?? string.Empty),
                        ResponseRequest.SerializerOptions))
            ],
            SessionIdleEvent =>
            [
                OpenResponseSseFrame.Done
            ],
            SessionErrorEvent sessionErrorEvent =>
            [
                new OpenResponseSseFrame(
                    "response.failed",
                    JsonSerializer.Serialize(
                        ErrorResponse.Create(
                            "runtime_error",
                            sessionErrorEvent.Data.Message ?? "The response failed.",
                            string.IsNullOrWhiteSpace(requestId) ? responseId : requestId),
                        ResponseRequest.SerializerOptions)),
                OpenResponseSseFrame.Done
            ],
            _ => []
        };
    }

    private sealed record OutputTextDelta(string Delta)
    {
        public string Type { get; init; } = "output_text";
    }

    private sealed record OutputTextDone(string Text)
    {
        public string Type { get; init; } = "output_text";
    }
}
